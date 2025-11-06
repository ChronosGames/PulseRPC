using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;

namespace PulseRPC.Server.ServiceManagement;

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
public sealed class PulseHubFactory<THub, TService> : IPulseHubFactory<THub, TService>, IDisposable
    where THub : class
    where TService : IPulseService
{
    private readonly IPulseServiceFactory<TService> _serviceFactory;
    private readonly Func<TService, THub> _hubFactory;
    private readonly ILogger<PulseHubFactory<THub, TService>> _logger;
    private readonly ConcurrentDictionary<string, THub> _hubs;
    private bool _disposed;

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

        ThrowIfDisposed();

        // 快速路径：Hub 已存在
        if (_hubs.TryGetValue(serviceId, out var hub))
        {
            return hub;
        }

        // 慢速路径：创建新 Hub
        // 1. 获取或创建 Service 实例
        var service = await _serviceFactory.GetOrCreateAsync(serviceId, cancellationToken);

        // 2. 创建 Hub 实例
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
            throw new InvalidOperationException(
                $"Failed to create hub instance for ServiceId: {serviceId}", ex);
        }

        // 3. 缓存 Hub 实例（竞态保护）
        hub = _hubs.GetOrAdd(serviceId, newHub);

        if (ReferenceEquals(hub, newHub))
        {
            _logger.LogInformation(
                "Created hub instance: ServiceId={ServiceId}, HubType={HubType}",
                serviceId, typeof(THub).Name);
        }

        return hub;
    }

    /// <inheritdoc/>
    public bool TryGet(string serviceId, [NotNullWhen(true)] out THub? hub)
    {
        return _hubs.TryGetValue(serviceId, out hub);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> RemoveAsync(
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        // 1. 移除 Hub 实例
        if (!_hubs.TryRemove(serviceId, out var hub))
            return false;

        _logger.LogInformation(
            "Removing hub instance: ServiceId={ServiceId}, HubType={HubType}",
            serviceId, typeof(THub).Name);

        // 2. 如果 Hub 实现了 IDisposable，调用 Dispose
        if (hub is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Hub disposal failed: ServiceId={ServiceId}", serviceId);
            }
        }

        // 3. 移除对应的 Service 实例
        await _serviceFactory.RemoveAsync(serviceId, cancellationToken);

        return true;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetActiveServiceIds()
    {
        return _hubs.Keys.ToList();
    }

    /// <inheritdoc/>
    public int ActiveCount => _hubs.Count;

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _logger.LogInformation(
            "Disposing HubFactory: HubType={HubType}, ActiveInstances={ActiveInstances}",
            typeof(THub).Name, _hubs.Count);

        // 移除所有 Hub 实例
        var serviceIds = _hubs.Keys.ToList();
        foreach (var serviceId in serviceIds)
        {
            _ = RemoveAsync(serviceId).AsTask().GetAwaiter().GetResult();
        }

        _logger.LogInformation(
            "HubFactory disposed: HubType={HubType}",
            typeof(THub).Name);
    }
}
