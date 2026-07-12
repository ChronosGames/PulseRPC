using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Hubs; using PulseRPC.Server.Services; using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Services.Management;

/// <summary>
/// 服务实例工厂默认实现
/// </summary>
/// <typeparam name="TService">服务类型，必须实现 <see cref="IPulseService"/></typeparam>
/// <remarks>
/// <para>
/// 提供完整的服务实例生命周期管理，包括：
/// </para>
/// <list type="bullet">
/// <item><description>按需创建和缓存</description></item>
/// <item><description>空闲超时清理</description></item>
/// <item><description>LRU 驱逐策略</description></item>
/// <item><description>健康检查</description></item>
/// <item><description>指标收集</description></item>
/// </list>
/// </remarks>
internal sealed class PulseServiceFactory<TService> : IPulseServiceFactory<TService>, IPulseServiceFactoryMetrics, IDisposable
    where TService : IPulseService
{
    private readonly ConcurrentDictionary<string, ServiceInstanceEntry> _instances;
    private readonly Func<string, TService> _serviceFactory;
    private readonly ILogger<PulseServiceFactory<TService>> _logger;
    private readonly PulseServiceFactoryOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operationGates = new();
    private readonly object _lifecycleLock = new();
    private readonly CancellationTokenSource _disposeCts;
    private readonly Task _cleanupTask;
    private readonly Task? _healthCheckTask;
    private TaskCompletionSource<bool>? _operationsDrained;
    private int _activeOperations;
    private volatile bool _disposed;

    // 指标
    private long _totalCreated;
    private long _totalRemoved;
    private long _cacheHits;
    private long _cacheMisses;
    private long _evictionCount;

    /// <summary>
    /// 初始化 <see cref="PulseServiceFactory{TService}"/> 类的新实例
    /// </summary>
    /// <param name="serviceFactory">服务实例工厂函数</param>
    /// <param name="options">配置选项</param>
    /// <param name="logger">日志记录器</param>
    public PulseServiceFactory(
        Func<string, TService> serviceFactory,
        PulseServiceFactoryOptions options,
        ILogger<PulseServiceFactory<TService>> logger)
    {
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _instances = new ConcurrentDictionary<string, ServiceInstanceEntry>();
        _disposeCts = new CancellationTokenSource();

        _cleanupTask = RunPeriodicAsync(
            _options.CleanupInterval,
            CleanupIdleInstancesAsync,
            _disposeCts.Token);

        if (_options.EnableHealthCheck)
        {
            _healthCheckTask = RunPeriodicAsync(
                _options.HealthCheckInterval,
                PerformHealthChecksAsync,
                _disposeCts.Token);
        }

        _logger.LogInformation(
            "ServiceFactory initialized: Type={ServiceType}, MaxInstances={MaxInstances}, IdleTimeout={IdleTimeout}",
            typeof(TService).Name, _options.MaxCachedInstances, _options.IdleTimeout);
    }

    // ========================================================================
    // IPulseServiceFactory 实现
    // ========================================================================

    /// <inheritdoc/>
    public async ValueTask<TService> GetOrCreateAsync(
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("ServiceId cannot be null or whitespace", nameof(serviceId));

        var gate = BeginOperation(serviceId);
        TService? result = default;
        var enforceCapacity = false;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCts.Token);
        try
        {
            await gate.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_instances.TryGetValue(serviceId, out var existingEntry))
                {
                    existingEntry.RecordAccess();
                    if (_options.EnableMetrics)
                    {
                        Interlocked.Increment(ref _cacheHits);
                    }

                    result = existingEntry.Service;
                    return result;
                }

                if (_options.EnableMetrics)
                {
                    Interlocked.Increment(ref _cacheMisses);
                }

                TService service;
                try
                {
                    service = _serviceFactory(serviceId) ??
                        throw new InvalidOperationException("Service factory returned null.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to create service instance: ServiceId={ServiceId}, Type={ServiceType}",
                        serviceId, typeof(TService).Name);
                    throw new ServiceCreationException(serviceId,
                        $"Failed to create service instance: {serviceId}", ex);
                }

                try
                {
                    await service.StartAsync(linkedCts.Token).ConfigureAwait(false);
                    ThrowIfDisposed();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Service activation failed: ServiceId={ServiceId}", serviceId);
                    await DisposeServiceAsync(service, stopFirst: false).ConfigureAwait(false);
                    throw new ServiceActivationException(serviceId,
                        $"Failed to activate service: {serviceId}", ex);
                }

                var entry = new ServiceInstanceEntry(service);
                if (!_instances.TryAdd(serviceId, entry))
                {
                    await DisposeServiceAsync(service, stopFirst: true).ConfigureAwait(false);
                    throw new InvalidOperationException($"Service instance was concurrently published: {serviceId}");
                }

                entry.RecordAccess();
                result = service;
                enforceCapacity = _instances.Count > _options.MaxCachedInstances;

                if (_options.EnableMetrics)
                {
                    Interlocked.Increment(ref _totalCreated);
                }

                _logger.LogInformation(
                    "Created and started service instance: ServiceId={ServiceId}, Type={ServiceType}",
                    serviceId, typeof(TService).Name);
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

        if (enforceCapacity)
        {
            await EvictLeastRecentlyUsedAsync(_disposeCts.Token).ConfigureAwait(false);
        }

        return result!;
    }

    /// <inheritdoc/>
    public bool TryGet(string serviceId, [NotNullWhen(true)] out TService? service)
    {
        if (_disposed)
        {
            service = default;
            return false;
        }

        if (_instances.TryGetValue(serviceId, out var entry))
        {
            entry.RecordAccess();
            service = entry.Service;
            return true;
        }

        service = default;
        return false;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> RemoveAsync(
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        var gate = BeginOperation(serviceId);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCts.Token);
        try
        {
            await gate.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            try
            {
                return await RemovePublishedEntryAsync(serviceId, linkedCts.Token).ConfigureAwait(false);
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
        return _instances.Keys.ToList();
    }

    /// <inheritdoc/>
    public int ActiveCount => _instances.Count;

    // ========================================================================
    // IPulseServiceFactoryMetrics 实现
    // ========================================================================

    /// <inheritdoc/>
    public int ActiveInstances => _instances.Count;

    /// <inheritdoc/>
    public long TotalCreated => Interlocked.Read(ref _totalCreated);

    /// <inheritdoc/>
    public long TotalRemoved => Interlocked.Read(ref _totalRemoved);

    /// <inheritdoc/>
    public long CacheHits => Interlocked.Read(ref _cacheHits);

    /// <inheritdoc/>
    public long CacheMisses => Interlocked.Read(ref _cacheMisses);

    /// <inheritdoc/>
    public long EvictionCount => Interlocked.Read(ref _evictionCount);

    /// <inheritdoc/>
    public double CacheHitRate
    {
        get
        {
            var hits = CacheHits;
            var misses = CacheMisses;
            var total = hits + misses;
            return total == 0 ? 0 : (double)hits / total;
        }
    }

    // ========================================================================
    // 内部方法
    // ========================================================================

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

    private async Task RunPeriodicAsync(
        TimeSpan interval,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                await action().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async ValueTask<bool> RemovePublishedEntryAsync(
        string serviceId,
        CancellationToken cancellationToken)
    {
        if (!_instances.TryRemove(serviceId, out var entry))
        {
            return false;
        }

        if (_options.EnableMetrics)
        {
            Interlocked.Increment(ref _totalRemoved);
        }

        _logger.LogInformation(
            "Removing service instance: ServiceId={ServiceId}, AccessCount={AccessCount}, Lifetime={Lifetime}",
            serviceId, entry.AccessCount, DateTimeOffset.UtcNow - entry.CreatedTime);

        await DisposeServiceAsync(entry.Service, stopFirst: true, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async ValueTask DisposeServiceAsync(
        TService service,
        bool stopFirst,
        CancellationToken cancellationToken = default)
    {
        if (stopFirst)
        {
            try
            {
                await service.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Service stop failed: ServiceId={ServiceId}", service.ServiceId);
            }
        }

        try
        {
            await service.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service disposal failed: ServiceId={ServiceId}", service.ServiceId);
        }
    }

    private async Task CleanupIdleInstancesAsync()
    {
        if (_disposed)
            return;

        try
        {
            var now = DateTimeOffset.UtcNow;
            var idleThreshold = now - _options.IdleTimeout;

            var toRemove = _instances
                .Where(kvp => kvp.Value.LastAccessTime < idleThreshold)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var serviceId in toRemove)
            {
                await RemoveAsync(serviceId, _disposeCts.Token);
            }

            if (toRemove.Count > 0)
            {
                _logger.LogInformation(
                    "Cleaned up {Count} idle service instances", toRemove.Count);
            }
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during idle instance cleanup");
        }
    }

    private async Task EvictLeastRecentlyUsedAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 驱逐 10% 的最少使用实例
            var evictCount = Math.Max(1, (int)(_options.MaxCachedInstances * 0.1));

            var toEvict = _instances
                .OrderBy(kvp => kvp.Value.LastAccessTime)
                .Take(evictCount)
                .Select(kvp => kvp.Key)
                .ToList();

            var removedCount = 0;
            foreach (var serviceId in toEvict)
            {
                if (await RemoveAsync(serviceId, cancellationToken).ConfigureAwait(false))
                {
                    removedCount++;
                    if (_options.EnableMetrics)
                    {
                        Interlocked.Increment(ref _evictionCount);
                    }
                }
            }

            _logger.LogWarning(
                "Evicted {Count} least recently used instances (cache limit exceeded)",
                removedCount);
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during LRU eviction");
        }
    }

    private async Task PerformHealthChecksAsync()
    {
        if (_disposed)
            return;

        try
        {
            var unhealthyServices = new List<string>();

            foreach (var (serviceId, entry) in _instances)
            {
                if (entry.Service is IPulseServiceHealthCheck healthCheck)
                {
                    try
                    {
                        var result = await healthCheck.CheckHealthAsync(_disposeCts.Token);
                        if (!result.IsHealthy)
                        {
                            _logger.LogWarning(
                                "Service health check failed: ServiceId={ServiceId}", serviceId);
                            unhealthyServices.Add(serviceId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Health check exception: ServiceId={ServiceId}", serviceId);
                        unhealthyServices.Add(serviceId);
                    }
                }
            }

            foreach (var serviceId in unhealthyServices)
            {
                _logger.LogWarning(
                    "Removing unhealthy service: ServiceId={ServiceId}", serviceId);
                await RemoveAsync(serviceId, _disposeCts.Token);
            }

            if (unhealthyServices.Count > 0)
            {
                _logger.LogWarning(
                    "Removed {Count} unhealthy service instances", unhealthyServices.Count);
            }
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health checks");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    // ========================================================================
    // IDisposable 实现
    // ========================================================================

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
            "Disposing ServiceFactory: Type={ServiceType}, ActiveInstances={ActiveInstances}",
            typeof(TService).Name, _instances.Count);

        _disposeCts.Cancel();
        if (_healthCheckTask is null)
        {
            _cleanupTask.GetAwaiter().GetResult();
        }
        else
        {
            Task.WhenAll(_cleanupTask, _healthCheckTask).GetAwaiter().GetResult();
        }
        operationsDrained.GetAwaiter().GetResult();

        // 此时不再有创建/移除操作，直接按统一 Stop -> Dispose 生命周期排空实例。
        var serviceIds = _instances.Keys.ToList();
        foreach (var serviceId in serviceIds)
        {
            RemovePublishedEntryAsync(serviceId, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        _disposeCts.Dispose();
        foreach (var gate in _operationGates.Values)
        {
            gate.Dispose();
        }

        _logger.LogInformation(
            "ServiceFactory disposed: Type={ServiceType}, TotalCreated={TotalCreated}, TotalRemoved={TotalRemoved}",
            typeof(TService).Name, TotalCreated, TotalRemoved);
    }

    // ========================================================================
    // 内部类
    // ========================================================================

    private sealed class ServiceInstanceEntry
    {
        public TService Service { get; }
        public DateTimeOffset CreatedTime { get; }

        private DateTimeOffset _lastAccessTime;
        private long _accessCount;

        public DateTimeOffset LastAccessTime => _lastAccessTime;
        public long AccessCount => Interlocked.Read(ref _accessCount);

        public ServiceInstanceEntry(TService service)
        {
            Service = service;
            CreatedTime = DateTimeOffset.UtcNow;
            _lastAccessTime = CreatedTime;
            _accessCount = 0;
        }

        public void RecordAccess()
        {
            _lastAccessTime = DateTimeOffset.UtcNow;
            Interlocked.Increment(ref _accessCount);
        }
    }
}
