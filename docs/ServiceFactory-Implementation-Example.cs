// ============================================================================
// IPulseServiceFactory 完整实现示例
// ============================================================================
// 这个文件包含 ServiceFactory 的完整实现，可以直接集成到 PulseRPC.Server 项目中
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Abstractions;

namespace PulseRPC.Server.ServiceManagement
{
    // ========================================================================
    // 核心接口
    // ========================================================================

    /// <summary>
    /// 服务实例工厂接口
    /// </summary>
    /// <typeparam name="TService">服务类型，必须实现 IPulseService</typeparam>
    public interface IPulseServiceFactory<TService> where TService : IPulseService
    {
        /// <summary>
        /// 获取或创建服务实例
        /// </summary>
        ValueTask<TService> GetOrCreateAsync(
            string serviceId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 尝试获取已存在的服务实例
        /// </summary>
        bool TryGet(string serviceId, [NotNullWhen(true)] out TService? service);

        /// <summary>
        /// 移除服务实例
        /// </summary>
        ValueTask<bool> RemoveAsync(
            string serviceId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取所有活跃的服务实例 ID
        /// </summary>
        IReadOnlyCollection<string> GetActiveServiceIds();

        /// <summary>
        /// 获取当前活跃实例数量
        /// </summary>
        int ActiveCount { get; }
    }

    /// <summary>
    /// 服务实例生命周期接口
    /// </summary>
    public interface IServiceLifecycle
    {
        /// <summary>
        /// 服务实例激活时调用
        /// </summary>
        Task OnActivateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 服务实例停用时调用
        /// </summary>
        Task OnDeactivateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 健康检查时调用（可选）
        /// </summary>
        Task<bool> OnHealthCheckAsync(CancellationToken cancellationToken = default);
    }

    // ========================================================================
    // 配置选项
    // ========================================================================

    public class PulseServiceFactoryOptions
    {
        /// <summary>
        /// 实例空闲超时时间（默认 5 分钟）
        /// </summary>
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 清理任务执行间隔（默认 1 分钟）
        /// </summary>
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 最大缓存实例数（默认 10000）
        /// </summary>
        public int MaxCachedInstances { get; set; } = 10000;

        /// <summary>
        /// 是否启用健康检查（默认 true）
        /// </summary>
        public bool EnableHealthCheck { get; set; } = true;

        /// <summary>
        /// 健康检查间隔（默认 30 秒）
        /// </summary>
        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 是否启用指标收集（默认 true）
        /// </summary>
        public bool EnableMetrics { get; set; } = true;
    }

    // ========================================================================
    // 异常类型
    // ========================================================================

    public class ServiceCreationException : Exception
    {
        public string ServiceId { get; }

        public ServiceCreationException(string serviceId, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            ServiceId = serviceId;
        }
    }

    public class ServiceActivationException : Exception
    {
        public string ServiceId { get; }

        public ServiceActivationException(string serviceId, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            ServiceId = serviceId;
        }
    }

    // ========================================================================
    // 指标接口
    // ========================================================================

    public interface IPulseServiceFactoryMetrics
    {
        /// <summary>当前活跃实例数</summary>
        int ActiveInstances { get; }

        /// <summary>总创建次数</summary>
        long TotalCreated { get; }

        /// <summary>总移除次数</summary>
        long TotalRemoved { get; }

        /// <summary>缓存命中次数</summary>
        long CacheHits { get; }

        /// <summary>缓存未命中次数</summary>
        long CacheMisses { get; }

        /// <summary>缓存命中率</summary>
        double CacheHitRate { get; }

        /// <summary>驱逐次数</summary>
        long EvictionCount { get; }
    }

    // ========================================================================
    // 默认实现
    // ========================================================================

    public class PulseServiceFactory<TService> : IPulseServiceFactory<TService>, IPulseServiceFactoryMetrics, IDisposable
        where TService : IPulseService
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

        // ====================================================================
        // IPulseServiceFactory 实现
        // ====================================================================

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
                Interlocked.Increment(ref _cacheHits);
                return entry.Service;
            }

            Interlocked.Increment(ref _cacheMisses);

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
                Interlocked.Increment(ref _totalCreated);

                _logger.LogInformation(
                    "Created service instance: ServiceId={ServiceId}, Type={ServiceType}",
                    serviceId, typeof(TService).Name);

                // 调用生命周期钩子
                if (service is IServiceLifecycle lifecycle)
                {
                    try
                    {
                        await lifecycle.OnActivateAsync(cancellationToken);

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
                        Interlocked.Decrement(ref _totalCreated);

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

        public async ValueTask<bool> RemoveAsync(
            string serviceId,
            CancellationToken cancellationToken = default)
        {
            if (!_instances.TryRemove(serviceId, out var entry))
                return false;

            Interlocked.Increment(ref _totalRemoved);

            _logger.LogInformation(
                "Removing service instance: ServiceId={ServiceId}, AccessCount={AccessCount}, Lifetime={Lifetime}",
                serviceId, entry.AccessCount, DateTimeOffset.UtcNow - entry.CreatedTime);

            // 调用停用钩子
            if (entry.Service is IServiceLifecycle lifecycle)
            {
                try
                {
                    await lifecycle.OnDeactivateAsync(cancellationToken);
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

        public IReadOnlyCollection<string> GetActiveServiceIds()
        {
            return _instances.Keys.ToList();
        }

        public int ActiveCount => _instances.Count;

        // ====================================================================
        // IPulseServiceFactoryMetrics 实现
        // ====================================================================

        public int ActiveInstances => _instances.Count;
        public long TotalCreated => Interlocked.Read(ref _totalCreated);
        public long TotalRemoved => Interlocked.Read(ref _totalRemoved);
        public long CacheHits => Interlocked.Read(ref _cacheHits);
        public long CacheMisses => Interlocked.Read(ref _cacheMisses);
        public long EvictionCount => Interlocked.Read(ref _evictionCount);

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

        // ====================================================================
        // 内部方法
        // ====================================================================

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
                    Interlocked.Increment(ref _evictionCount);
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
                    if (entry.Service is IServiceLifecycle lifecycle)
                    {
                        try
                        {
                            var isHealthy = await lifecycle.OnHealthCheckAsync(_disposeCts.Token);
                            if (!isHealthy)
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

        // ====================================================================
        // IDisposable 实现
        // ====================================================================

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

        // ====================================================================
        // 内部类
        // ====================================================================

        private class ServiceInstanceEntry
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
}

// ============================================================================
// DI 扩展
// ============================================================================

namespace Microsoft.Extensions.DependencyInjection
{
    using PulseRPC.Server.Abstractions;
    using PulseRPC.Server.ServiceManagement;

    public static class PulseServiceFactoryExtensions
    {
        /// <summary>
        /// 注册服务工厂（使用自定义工厂函数）
        /// </summary>
        public static IServiceCollection AddPulseServiceFactory<TService>(
            this IServiceCollection services,
            Func<IServiceProvider, string, TService> serviceFactory,
            Action<PulseServiceFactoryOptions>? configureOptions = null)
            where TService : IPulseService
        {
            // 注册选项
            if (configureOptions != null)
            {
                services.Configure(configureOptions);
            }
            else
            {
                services.Configure<PulseServiceFactoryOptions>(_ => { });
            }

            // 注册工厂
            services.AddSingleton<IPulseServiceFactory<TService>>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<PulseServiceFactoryOptions>>().Value;
                var logger = sp.GetRequiredService<ILogger<PulseServiceFactory<TService>>>();

                return new PulseServiceFactory<TService>(
                    serviceId => serviceFactory(sp, serviceId),
                    options,
                    logger);
            });

            // 同时注册指标接口
            services.AddSingleton<IPulseServiceFactoryMetrics>(sp =>
                (IPulseServiceFactoryMetrics)sp.GetRequiredService<IPulseServiceFactory<TService>>());

            return services;
        }

        /// <summary>
        /// 注册服务工厂（使用 ActivatorUtilities 自动创建）
        /// </summary>
        /// <remarks>
        /// TService 必须有一个接受 string serviceId 参数的构造函数
        /// </remarks>
        public static IServiceCollection AddPulseServiceFactory<TService>(
            this IServiceCollection services,
            Action<PulseServiceFactoryOptions>? configureOptions = null)
            where TService : IPulseService
        {
            return services.AddPulseServiceFactory<TService>(
                (sp, serviceId) => ActivatorUtilities.CreateInstance<TService>(sp, serviceId),
                configureOptions);
        }
    }
}
