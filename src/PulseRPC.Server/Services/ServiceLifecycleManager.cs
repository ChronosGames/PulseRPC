using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Configuration;

namespace PulseRPC.Server;

/// <summary>
/// Service 生命周期事件类型
/// </summary>
public enum ServiceLifecycleEvent
{
    /// <summary>Service 正在创建</summary>
    Creating,
    /// <summary>Service 已创建</summary>
    Created,
    /// <summary>Service 正在启动</summary>
    Starting,
    /// <summary>Service 已启动</summary>
    Started,
    /// <summary>Service 正在停止</summary>
    Stopping,
    /// <summary>Service 已停止</summary>
    Stopped,
    /// <summary>Service 正在销毁</summary>
    Destroying,
    /// <summary>Service 已销毁</summary>
    Destroyed
}

/// <summary>
/// Service 生命周期事件参数
/// </summary>
public sealed class ServiceLifecycleEventArgs : EventArgs
{
    /// <summary>Service 类型</summary>
    public required Type ServiceType { get; init; }

    /// <summary>实例 ID（可选）</summary>
    public string? InstanceId { get; init; }

    /// <summary>Service ID</summary>
    public required ServiceId ServiceId { get; init; }

    /// <summary>PID</summary>
    public PID? PID { get; init; }

    /// <summary>事件类型</summary>
    public required ServiceLifecycleEvent Event { get; init; }

    /// <summary>时间戳</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>错误信息（如果有）</summary>
    public Exception? Error { get; init; }
}

/// <summary>
/// Service 实例元数据
/// </summary>
public sealed class ServiceInstanceMetadata
{
    /// <summary>Service 实例</summary>
    public required IService Service { get; init; }

    /// <summary>Service ID</summary>
    public required ServiceId ServiceId { get; init; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>最后活跃时间</summary>
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;

    private int _referenceCount;

    /// <summary>引用计数</summary>
    public int ReferenceCount => _referenceCount;

    /// <summary>是否正在销毁</summary>
    public bool IsDestroying { get; set; }

    /// <summary>是否已销毁</summary>
    public bool IsDestroyed { get; set; }

    /// <summary>总处理消息数</summary>
    public long TotalMessagesProcessed { get; set; }

    /// <summary>更新最后活跃时间</summary>
    public void UpdateActivity()
    {
        LastActiveAt = DateTime.UtcNow;
    }

    /// <summary>增加引用计数</summary>
    public int AddReference()
    {
        return Interlocked.Increment(ref _referenceCount);
    }

    /// <summary>减少引用计数</summary>
    public int ReleaseReference()
    {
        return Interlocked.Decrement(ref _referenceCount);
    }

    /// <summary>是否空闲（无引用且超时）</summary>
    public bool IsIdle(TimeSpan idleTimeout)
    {
        if (ReferenceCount > 0)
            return false;

        if (idleTimeout == Timeout.InfiniteTimeSpan)
            return false;

        return DateTime.UtcNow - LastActiveAt > idleTimeout;
    }
}

/// <summary>
/// Service 生命周期管理器
/// </summary>
public sealed class ServiceLifecycleManager : IAsyncDisposable
{
    private readonly ILogger<ServiceLifecycleManager> _logger;
    private readonly ServiceLifecycleOptions _options;
    private readonly ConcurrentDictionary<ServiceId, ServiceInstanceMetadata> _instances = new();
    private readonly Timer? _cleanupTimer;
    private readonly CancellationTokenSource _disposeCts = new();

    /// <summary>
    /// Service 生命周期事件
    /// </summary>
    public event EventHandler<ServiceLifecycleEventArgs>? LifecycleEvent;

    /// <summary>
    /// 当前活跃的 Service 实例数
    /// </summary>
    public int ActiveInstanceCount => _instances.Count;

    /// <summary>
    /// 构造函数
    /// </summary>
    public ServiceLifecycleManager(
        ILogger<ServiceLifecycleManager> logger,
        ServiceLifecycleOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? ServiceLifecycleOptions.Default;
        _options.Validate();

        // 启动清理定时器
        if (_options.EnableAutoDestroy)
        {
            _cleanupTimer = new Timer(
                CleanupIdleServices,
                null,
                _options.DestroyCheckInterval,
                _options.DestroyCheckInterval);

            _logger.LogInformation(
                "ServiceLifecycleManager initialized - IdleTimeout: {IdleTimeout}, CheckInterval: {CheckInterval}",
                _options.IdleTimeout, _options.DestroyCheckInterval);
        }
        else
        {
            _logger.LogInformation("ServiceLifecycleManager initialized - Auto-destroy disabled");
        }
    }

    /// <summary>
    /// 注册 Service 实例
    /// </summary>
    public void RegisterInstance(ServiceId serviceId, IService service)
    {
        var metadata = new ServiceInstanceMetadata
        {
            Service = service,
            ServiceId = serviceId
        };

        if (_instances.TryAdd(serviceId, metadata))
        {
            _logger.LogInformation(
                "Service registered - ServiceId: {ServiceId}, PID: {PID}",
                serviceId, service.ServicePID);

            RaiseEvent(new ServiceLifecycleEventArgs
            {
                ServiceType = service.GetType(),
                InstanceId = serviceId.InstanceId,
                ServiceId = serviceId,
                PID = service.ServicePID,
                Event = ServiceLifecycleEvent.Created
            });
        }
    }

    /// <summary>
    /// 取消注册 Service 实例
    /// </summary>
    public void UnregisterInstance(ServiceId serviceId)
    {
        if (_instances.TryRemove(serviceId, out var metadata))
        {
            metadata.IsDestroyed = true;

            _logger.LogInformation(
                "Service unregistered - ServiceId: {ServiceId}, PID: {PID}, Lifetime: {Lifetime:F2}s",
                serviceId, metadata.Service.ServicePID,
                (DateTime.UtcNow - metadata.CreatedAt).TotalSeconds);

            RaiseEvent(new ServiceLifecycleEventArgs
            {
                ServiceType = metadata.Service.GetType(),
                InstanceId = serviceId.InstanceId,
                ServiceId = serviceId,
                PID = metadata.Service.ServicePID,
                Event = ServiceLifecycleEvent.Destroyed
            });
        }
    }

    /// <summary>
    /// 获取 Service 元数据
    /// </summary>
    public ServiceInstanceMetadata? GetMetadata(ServiceId serviceId)
    {
        return _instances.TryGetValue(serviceId, out var metadata) ? metadata : null;
    }

    /// <summary>
    /// 增加引用计数
    /// </summary>
    public void AddReference(ServiceId serviceId)
    {
        if (_options.EnableReferenceCount && _instances.TryGetValue(serviceId, out var metadata))
        {
            var count = metadata.AddReference();
            _logger.LogDebug(
                "Service reference increased - ServiceId: {ServiceId}, RefCount: {RefCount}",
                serviceId, count);
        }
    }

    /// <summary>
    /// 减少引用计数
    /// </summary>
    public void ReleaseReference(ServiceId serviceId)
    {
        if (_options.EnableReferenceCount && _instances.TryGetValue(serviceId, out var metadata))
        {
            var count = metadata.ReleaseReference();
            _logger.LogDebug(
                "Service reference released - ServiceId: {ServiceId}, RefCount: {RefCount}",
                serviceId, count);
        }
    }

    /// <summary>
    /// 更新活跃时间
    /// </summary>
    public void UpdateActivity(ServiceId serviceId)
    {
        if (_instances.TryGetValue(serviceId, out var metadata))
        {
            metadata.UpdateActivity();
        }
    }

    /// <summary>
    /// 销毁指定的 Service 实例
    /// </summary>
    public async Task<bool> DestroyInstanceAsync(ServiceId serviceId)
    {
        if (!_instances.TryGetValue(serviceId, out var metadata))
            return false;

        // 检查引用计数
        if (_options.EnableReferenceCount && metadata.ReferenceCount > 0)
        {
            _logger.LogWarning(
                "Cannot destroy service with active references - ServiceId: {ServiceId}, RefCount: {RefCount}",
                serviceId, metadata.ReferenceCount);
            return false;
        }

        // 标记为正在销毁
        if (metadata.IsDestroying || metadata.IsDestroyed)
            return false;

        metadata.IsDestroying = true;

        try
        {
            _logger.LogInformation(
                "Destroying service - ServiceId: {ServiceId}, PID: {PID}",
                serviceId, metadata.Service.ServicePID);

            RaiseEvent(new ServiceLifecycleEventArgs
            {
                ServiceType = metadata.Service.GetType(),
                InstanceId = serviceId.InstanceId,
                ServiceId = serviceId,
                PID = metadata.Service.ServicePID,
                Event = ServiceLifecycleEvent.Destroying
            });

            // 优雅停止（等待队列清空）
            using var cts = new CancellationTokenSource(_options.GracefulShutdownTimeout);
            await metadata.Service.StopAsync(cts.Token);

            // 释放资源
            await metadata.Service.DisposeAsync();

            // 取消注册
            UnregisterInstance(serviceId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to destroy service - ServiceId: {ServiceId}",
                serviceId);

            RaiseEvent(new ServiceLifecycleEventArgs
            {
                ServiceType = metadata.Service.GetType(),
                InstanceId = serviceId.InstanceId,
                ServiceId = serviceId,
                PID = metadata.Service.ServicePID,
                Event = ServiceLifecycleEvent.Destroyed,
                Error = ex
            });

            return false;
        }
        finally
        {
            metadata.IsDestroying = false;
        }
    }

    /// <summary>
    /// 清理空闲的 Service 实例（后台定时任务）
    /// </summary>
    private void CleanupIdleServices(object? state)
    {
        if (_disposeCts.IsCancellationRequested)
            return;

        try
        {
            var idleServices = _instances
                .Where(kvp => kvp.Value.IsIdle(_options.IdleTimeout))
                .Select(kvp => kvp.Key)
                .ToList();

            if (idleServices.Count > 0)
            {
                _logger.LogInformation(
                    "Found {Count} idle services to cleanup",
                    idleServices.Count);

                foreach (var serviceId in idleServices)
                {
                    // 异步销毁（不等待）
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await DestroyInstanceAsync(serviceId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Error during idle service cleanup - ServiceId: {ServiceId}",
                                serviceId);
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup cycle");
        }
    }

    /// <summary>
    /// 获取所有 Service 实例的统计信息
    /// </summary>
    public ServiceLifecycleStats GetStats()
    {
        var instances = _instances.Values.ToList();

        return new ServiceLifecycleStats
        {
            TotalInstances = instances.Count,
            ActiveReferences = instances.Sum(m => m.ReferenceCount),
            IdleInstances = instances.Count(m => m.IsIdle(_options.IdleTimeout)),
            AverageIdleTime = instances.Any()
                ? TimeSpan.FromSeconds(instances.Average(m => (DateTime.UtcNow - m.LastActiveAt).TotalSeconds))
                : TimeSpan.Zero,
            OldestInstance = instances.OrderBy(m => m.CreatedAt).FirstOrDefault()?.CreatedAt,
            NewestInstance = instances.OrderByDescending(m => m.CreatedAt).FirstOrDefault()?.CreatedAt
        };
    }

    /// <summary>
    /// 触发生命周期事件
    /// </summary>
    private void RaiseEvent(ServiceLifecycleEventArgs args)
    {
        try
        {
            LifecycleEvent?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error raising lifecycle event - Event: {Event}", args.Event);
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _cleanupTimer?.Dispose();

        // 销毁所有实例
        var instances = _instances.Keys.ToList();
        foreach (var serviceId in instances)
        {
            try
            {
                await DestroyInstanceAsync(serviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error destroying service during disposal - ServiceId: {ServiceId}", serviceId);
            }
        }

        _disposeCts.Dispose();
    }
}

/// <summary>
/// Service 生命周期统计信息
/// </summary>
public sealed class ServiceLifecycleStats
{
    /// <summary>总实例数</summary>
    public required int TotalInstances { get; init; }

    /// <summary>活跃引用数</summary>
    public required int ActiveReferences { get; init; }

    /// <summary>空闲实例数</summary>
    public required int IdleInstances { get; init; }

    /// <summary>平均空闲时间</summary>
    public required TimeSpan AverageIdleTime { get; init; }

    /// <summary>最旧实例创建时间</summary>
    public DateTime? OldestInstance { get; init; }

    /// <summary>最新实例创建时间</summary>
    public DateTime? NewestInstance { get; init; }

    public override string ToString()
    {
        return $"ServiceLifecycleStats[Total={TotalInstances}, ActiveRefs={ActiveReferences}, " +
               $"Idle={IdleInstances}, AvgIdleTime={AverageIdleTime.TotalSeconds:F1}s]";
    }
}
