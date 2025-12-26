using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Abstractions;

namespace PulseRPC.Server.ServiceManagement;

/// <summary>
/// 服务实例工厂默认实现
/// </summary>
/// <typeparam name="TService">服务类型，必须实现 <see cref="IUnifiedPulseService"/></typeparam>
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
    where TService : IUnifiedPulseService
{
    private readonly ConcurrentDictionary<string, ServiceInstanceEntry> _instances;
    private readonly Func<string, TService> _serviceFactory;
    private readonly ILogger<PulseServiceFactory<TService>> _logger;
    private readonly PulseServiceFactoryOptions _options;
    private readonly Timer _cleanupTimer;
    private readonly Timer? _healthCheckTimer;
    private readonly CancellationTokenSource _disposeCts;
    private bool _disposed;

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

        // 启动清理定时器
        _cleanupTimer = new Timer(
            _ => _ = CleanupIdleInstancesAsync(),
            null,
            _options.CleanupInterval,
            _options.CleanupInterval);

        // 启动健康检查定时器
        if (_options.EnableHealthCheck)
        {
            _healthCheckTimer = new Timer(
                _ => _ = PerformHealthChecksAsync(),
                null,
                _options.HealthCheckInterval,
                _options.HealthCheckInterval);
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

        ThrowIfDisposed();

        // 快速路径：实例已存在
        if (_instances.TryGetValue(serviceId, out var entry))
        {
            entry.RecordAccess();
            if (_options.EnableMetrics)
            {
                Interlocked.Increment(ref _cacheHits);
            }
            return entry.Service;
        }

        if (_options.EnableMetrics)
        {
            Interlocked.Increment(ref _cacheMisses);
        }

        // 慢速路径：创建新实例
        TService service;
        try
        {
            service = _serviceFactory(serviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create service instance: ServiceId={ServiceId}, Type={ServiceType}",
                serviceId, typeof(TService).Name);
            throw new ServiceCreationException(serviceId,
                $"Failed to create service instance: {serviceId}", ex);
        }

        var newEntry = new ServiceInstanceEntry(service);

        // 竞态保护：确保只有一个实例被创建
        entry = _instances.GetOrAdd(serviceId, newEntry);

        // 如果是新创建的实例
        if (ReferenceEquals(entry, newEntry))
        {
            if (_options.EnableMetrics)
            {
                Interlocked.Increment(ref _totalCreated);
            }

            _logger.LogInformation(
                "Created service instance: ServiceId={ServiceId}, Type={ServiceType}",
                serviceId, typeof(TService).Name);

            // 调用生命周期钩子
            if (service is IUnifiedServiceLifecycle lifecycle)
            {
                try
                {
                    await lifecycle.OnStartingAsync(cancellationToken);

                    _logger.LogDebug(
                        "Service instance activated: ServiceId={ServiceId}",
                        serviceId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Service activation failed: ServiceId={ServiceId}", serviceId);

                    // 激活失败，移除实例
                    _instances.TryRemove(serviceId, out _);
                    if (_options.EnableMetrics)
                    {
                        Interlocked.Decrement(ref _totalCreated);
                    }

                    throw new ServiceActivationException(serviceId,
                        $"Failed to activate service: {serviceId}", ex);
                }
            }

            // 检查是否超过最大缓存数
            if (_instances.Count > _options.MaxCachedInstances)
            {
                _ = EvictLeastRecentlyUsedAsync(cancellationToken);
            }
        }

        entry.RecordAccess();
        return entry.Service;
    }

    /// <inheritdoc/>
    public bool TryGet(string serviceId, [NotNullWhen(true)] out TService? service)
    {
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
        if (!_instances.TryRemove(serviceId, out var entry))
            return false;

        if (_options.EnableMetrics)
        {
            Interlocked.Increment(ref _totalRemoved);
        }

        _logger.LogInformation(
            "Removing service instance: ServiceId={ServiceId}, AccessCount={AccessCount}, Lifetime={Lifetime}",
            serviceId, entry.AccessCount, DateTimeOffset.UtcNow - entry.CreatedTime);

        // 调用停用钩子
        if (entry.Service is IUnifiedServiceLifecycle lifecycle)
        {
            try
            {
                await lifecycle.OnStoppingAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Service deactivation failed: ServiceId={ServiceId}", serviceId);
                // 继续移除，不抛出异常
            }
        }

        // 如果实现了 IDisposable，调用 Dispose
        if (entry.Service is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Service disposal failed: ServiceId={ServiceId}", serviceId);
            }
        }

        return true;
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

            foreach (var serviceId in toEvict)
            {
                await RemoveAsync(serviceId, cancellationToken);
                if (_options.EnableMetrics)
                {
                    Interlocked.Increment(ref _evictionCount);
                }
            }

            _logger.LogWarning(
                "Evicted {Count} least recently used instances (cache limit exceeded)",
                toEvict.Count);
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
                if (entry.Service is IUnifiedServiceHealthCheck healthCheck)
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
        if (_disposed)
            return;

        _disposed = true;

        _logger.LogInformation(
            "Disposing ServiceFactory: Type={ServiceType}, ActiveInstances={ActiveInstances}",
            typeof(TService).Name, _instances.Count);

        _disposeCts.Cancel();
        _cleanupTimer.Dispose();
        _healthCheckTimer?.Dispose();

        // 移除所有实例
        var serviceIds = _instances.Keys.ToList();
        foreach (var serviceId in serviceIds)
        {
            _ = RemoveAsync(serviceId).AsTask().GetAwaiter().GetResult();
        }

        _disposeCts.Dispose();

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
