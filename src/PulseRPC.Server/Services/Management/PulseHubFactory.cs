using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Hubs; using PulseRPC.Server.Services; using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Services.Management;

/// <summary>
/// Hub 实例工厂默认实现
/// </summary>
/// <typeparam name="THub">Hub 类型</typeparam>
/// <typeparam name="TService">Service 类型</typeparam>
/// <remarks>
/// <para>
/// 管理 Hub 实例的生命周期，Hub 和 Service 实例一对一绑定。
/// </para>
/// <para>
/// <strong>工作原理</strong>：
/// </para>
/// <list type="number">
/// <item><description>通过 <see cref="IPulseServiceFactory{TService}"/> 获取或创建 Service 实例</description></item>
/// <item><description>为 Service 实例创建对应的 Hub 实例</description></item>
/// <item><description>缓存 Hub 实例以便复用</description></item>
/// <item><description>当 Service 被移除时，对应的 Hub 也会被移除</description></item>
/// </list>
/// </remarks>
internal sealed class PulseHubFactory<THub, TService> : IPulseHubFactory<THub, TService>, IDisposable
    where THub : class
    where TService : IPulseService
{
    private readonly IPulseServiceFactory<TService> _serviceFactory;
    private readonly Func<TService, THub> _hubFactory;
    private readonly ILogger<PulseHubFactory<THub, TService>> _logger;
    private readonly ConcurrentDictionary<string, THub> _hubs;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operationGates = new();
    private readonly object _lifecycleLock = new();
    private TaskCompletionSource<bool>? _operationsDrained;
    private int _activeOperations;
    private volatile bool _disposed;

    /// <summary>
    /// 初始化 <see cref="PulseHubFactory{THub, TService}"/> 类的新实例
    /// </summary>
    /// <param name="serviceFactory">Service 工厂</param>
    /// <param name="hubFactory">Hub 工厂函数，接收 Service 实例，返回 Hub 实例</param>
    /// <param name="logger">日志记录器</param>
    public PulseHubFactory(
        IPulseServiceFactory<TService> serviceFactory,
        Func<TService, THub> hubFactory,
        ILogger<PulseHubFactory<THub, TService>> logger)
    {
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        _hubFactory = hubFactory ?? throw new ArgumentNullException(nameof(hubFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _hubs = new ConcurrentDictionary<string, THub>();

        _logger.LogInformation(
            "HubFactory initialized: HubType={HubType}, ServiceType={ServiceType}",
            typeof(THub).Name, typeof(TService).Name);
    }

    /// <inheritdoc/>
    public async ValueTask<THub> GetOrCreateAsync(
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("ServiceId cannot be null or whitespace", nameof(serviceId));

        var gate = BeginOperation(serviceId);
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_hubs.TryGetValue(serviceId, out var existingHub))
                {
                    return existingHub;
                }

                var service = await _serviceFactory
                    .GetOrCreateAsync(serviceId, cancellationToken)
                    .ConfigureAwait(false);

                THub newHub;
                try
                {
                    newHub = _hubFactory(service);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to create hub instance: ServiceId={ServiceId}, HubType={HubType}",
                        serviceId, typeof(THub).Name);
                    await _serviceFactory.RemoveAsync(serviceId, CancellationToken.None).ConfigureAwait(false);
                    throw new InvalidOperationException(
                        $"Failed to create hub instance for ServiceId: {serviceId}", ex);
                }

                if (_disposed)
                {
                    await DisposeHubAsync(newHub).ConfigureAwait(false);
                    await _serviceFactory.RemoveAsync(serviceId, CancellationToken.None).ConfigureAwait(false);
                    ThrowIfDisposed();
                }

                if (!_hubs.TryAdd(serviceId, newHub))
                {
                    await DisposeHubAsync(newHub).ConfigureAwait(false);
                    throw new InvalidOperationException($"Hub instance was concurrently published: {serviceId}");
                }

                _logger.LogInformation(
                    "Created hub instance: ServiceId={ServiceId}, HubType={HubType}",
                    serviceId, typeof(THub).Name);
                return newHub;
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            EndOperation();
        }
    }

    /// <inheritdoc/>
    public bool TryGet(string serviceId, [NotNullWhen(true)] out THub? hub)
    {
        if (_disposed)
        {
            hub = default;
            return false;
        }

        return _hubs.TryGetValue(serviceId, out hub);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> RemoveAsync(
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        var gate = BeginOperation(serviceId);
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await RemovePublishedHubAsync(serviceId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            EndOperation();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetActiveServiceIds()
    {
        return _hubs.Keys.ToList();
    }

    /// <inheritdoc/>
    public int ActiveCount => _hubs.Count;

    private SemaphoreSlim BeginOperation(string serviceId)
    {
        lock (_lifecycleLock)
        {
            ThrowIfDisposed();
            _activeOperations++;
            return _operationGates.GetOrAdd(serviceId, static _ => new SemaphoreSlim(1, 1));
        }
    }

    private void EndOperation()
    {
        lock (_lifecycleLock)
        {
            _activeOperations--;
            if (_disposed && _activeOperations == 0)
            {
                _operationsDrained?.TrySetResult(true);
            }
        }
    }

    private async ValueTask<bool> RemovePublishedHubAsync(
        string serviceId,
        CancellationToken cancellationToken)
    {
        if (!_hubs.TryRemove(serviceId, out var hub))
        {
            return false;
        }

        _logger.LogInformation(
            "Removing hub instance: ServiceId={ServiceId}, HubType={HubType}",
            serviceId, typeof(THub).Name);

        await DisposeHubAsync(hub).ConfigureAwait(false);
        await _serviceFactory.RemoveAsync(serviceId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask DisposeHubAsync(THub hub)
    {
        try
        {
            if (hub is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (hub is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hub disposal failed: HubType={HubType}", typeof(THub).Name);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Task operationsDrained;
        lock (_lifecycleLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_activeOperations == 0)
            {
                operationsDrained = Task.CompletedTask;
            }
            else
            {
                _operationsDrained = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                operationsDrained = _operationsDrained.Task;
            }
        }

        _logger.LogInformation(
            "Disposing HubFactory: HubType={HubType}, ActiveInstances={ActiveInstances}",
            typeof(THub).Name, _hubs.Count);

        operationsDrained.GetAwaiter().GetResult();

        var serviceIds = _hubs.Keys.ToList();
        foreach (var serviceId in serviceIds)
        {
            RemovePublishedHubAsync(serviceId, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        foreach (var gate in _operationGates.Values)
        {
            gate.Dispose();
        }

        _logger.LogInformation(
            "HubFactory disposed: HubType={HubType}",
            typeof(THub).Name);
    }
}
