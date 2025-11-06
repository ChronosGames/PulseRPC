# IPulseServiceFactory 设计文档

## 概述

`IPulseServiceFactory<TService>` 是 PulseRPC.Server 的核心基础设施组件，用于管理有状态的 `IPulseService` 实例的生命周期。它解决了"多个无状态 IPulseHub 共享同一个有状态 IPulseService 实例"的架构需求。

## 设计目标

### 核心目标
1. **自动化实例管理** - 按需创建、缓存和销毁 Service 实例
2. **线程安全** - 支持高并发场景下的安全访问
3. **生命周期控制** - 提供激活、空闲超时、健康检查等生命周期钩子
4. **可观测性** - 内置指标收集和日志记录
5. **DI 集成** - 与 Microsoft.Extensions.DependencyInjection 深度集成

### 非目标
- ❌ 不负责消息路由（由 ServiceRegistry 负责）
- ❌ 不负责线程调度（由 ThreadAffinityManager 负责）
- ❌ 不负责健康监控（由 ServiceInstanceHealthMonitor 负责）

## 接口设计

### 核心接口

```csharp
namespace PulseRPC.Server.Abstractions;

/// <summary>
/// 服务实例工厂接口
/// </summary>
/// <typeparam name="TService">服务类型，必须实现 IPulseService</typeparam>
public interface IPulseServiceFactory<TService> where TService : IPulseService
{
    /// <summary>
    /// 获取或创建服务实例
    /// </summary>
    /// <param name="serviceId">服务实例的唯一标识符</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务实例</returns>
    /// <exception cref="ServiceCreationException">创建失败时抛出</exception>
    ValueTask<TService> GetOrCreateAsync(
        string serviceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试获取已存在的服务实例
    /// </summary>
    /// <param name="serviceId">服务实例的唯一标识符</param>
    /// <param name="service">输出服务实例</param>
    /// <returns>如果实例存在返回 true，否则返回 false</returns>
    bool TryGet(string serviceId, [NotNullWhen(true)] out TService? service);

    /// <summary>
    /// 移除服务实例
    /// </summary>
    /// <param name="serviceId">服务实例的唯一标识符</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>如果移除成功返回 true，否则返回 false</returns>
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
```

### 生命周期接口

```csharp
namespace PulseRPC.Server.Abstractions;

/// <summary>
/// 服务实例生命周期接口
/// </summary>
public interface IServiceLifecycle
{
    /// <summary>
    /// 服务实例激活时调用
    /// </summary>
    /// <remarks>
    /// 在实例首次创建后、处理第一个请求前调用
    /// 可用于加载持久化状态、建立外部连接等
    /// </remarks>
    Task OnActivateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 服务实例停用时调用
    /// </summary>
    /// <remarks>
    /// 在实例从缓存中移除前调用
    /// 可用于保存状态、释放资源等
    /// </remarks>
    Task OnDeactivateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 健康检查时调用（可选）
    /// </summary>
    /// <remarks>
    /// 定期调用以检查实例健康状态
    /// 返回 false 将触发实例移除
    /// </remarks>
    Task<bool> OnHealthCheckAsync(CancellationToken cancellationToken = default);
}
```

## 默认实现：PulseServiceFactory

### 核心特性

```csharp
namespace PulseRPC.Server.ServiceManagement;

public class PulseServiceFactory<TService> : IPulseServiceFactory<TService>, IDisposable
    where TService : IPulseService
{
    private readonly ConcurrentDictionary<string, ServiceInstanceEntry> _instances;
    private readonly Func<string, TService> _serviceFactory;
    private readonly ILogger<PulseServiceFactory<TService>> _logger;
    private readonly PulseServiceFactoryOptions _options;
    private readonly Timer _cleanupTimer;

    private class ServiceInstanceEntry
    {
        public TService Service { get; }
        public DateTimeOffset LastAccessTime { get; set; }
        public DateTimeOffset CreatedTime { get; }
        public long AccessCount { get; set; }

        public ServiceInstanceEntry(TService service)
        {
            Service = service;
            CreatedTime = DateTimeOffset.UtcNow;
            LastAccessTime = CreatedTime;
            AccessCount = 0;
        }
    }
}
```

### 配置选项

```csharp
public class PulseServiceFactoryOptions
{
    /// <summary>
    /// 实例空闲超时时间
    /// </summary>
    /// <remarks>默认 5 分钟</remarks>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 清理任务执行间隔
    /// </summary>
    /// <remarks>默认 1 分钟</remarks>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 最大缓存实例数
    /// </summary>
    /// <remarks>默认 10000，超过后使用 LRU 策略驱逐</remarks>
    public int MaxCachedInstances { get; set; } = 10000;

    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    /// <remarks>默认 true</remarks>
    public bool EnableHealthCheck { get; set; } = true;

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    /// <remarks>默认 30 秒</remarks>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
}
```

## 关键实现细节

### 1. 实例创建流程

```csharp
public async ValueTask<TService> GetOrCreateAsync(
    string serviceId,
    CancellationToken cancellationToken = default)
{
    // 快速路径：实例已存在
    if (_instances.TryGetValue(serviceId, out var entry))
    {
        entry.LastAccessTime = DateTimeOffset.UtcNow;
        Interlocked.Increment(ref entry.AccessCount);
        return entry.Service;
    }

    // 慢速路径：创建新实例
    var service = _serviceFactory(serviceId);
    var newEntry = new ServiceInstanceEntry(service);

    // 竞态保护：确保只有一个实例被创建
    entry = _instances.GetOrAdd(serviceId, newEntry);

    // 如果是新创建的实例，触发激活
    if (ReferenceEquals(entry, newEntry))
    {
        _logger.LogInformation(
            "Created service instance: ServiceId={ServiceId}, Type={ServiceType}",
            serviceId, typeof(TService).Name);

        // 调用生命周期钩子
        if (service is IServiceLifecycle lifecycle)
        {
            try
            {
                await lifecycle.OnActivateAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Service activation failed: ServiceId={ServiceId}", serviceId);

                // 激活失败，移除实例
                _instances.TryRemove(serviceId, out _);
                throw new ServiceActivationException(
                    $"Failed to activate service: {serviceId}", ex);
            }
        }

        // 检查是否超过最大缓存数
        if (_instances.Count > _options.MaxCachedInstances)
        {
            await EvictLeastRecentlyUsedAsync(cancellationToken);
        }
    }

    entry.LastAccessTime = DateTimeOffset.UtcNow;
    Interlocked.Increment(ref entry.AccessCount);
    return entry.Service;
}
```

### 2. 空闲实例清理

```csharp
private async Task CleanupIdleInstancesAsync()
{
    var now = DateTimeOffset.UtcNow;
    var idleThreshold = now - _options.IdleTimeout;

    var toRemove = _instances
        .Where(kvp => kvp.Value.LastAccessTime < idleThreshold)
        .Select(kvp => kvp.Key)
        .ToList();

    foreach (var serviceId in toRemove)
    {
        await RemoveAsync(serviceId);
    }

    if (toRemove.Count > 0)
    {
        _logger.LogInformation(
            "Cleaned up {Count} idle service instances", toRemove.Count);
    }
}
```

### 3. LRU 驱逐策略

```csharp
private async Task EvictLeastRecentlyUsedAsync(CancellationToken cancellationToken)
{
    // 驱逐 10% 的最少使用实例
    var evictCount = (int)(_options.MaxCachedInstances * 0.1);

    var toEvict = _instances
        .OrderBy(kvp => kvp.Value.LastAccessTime)
        .Take(evictCount)
        .Select(kvp => kvp.Key)
        .ToList();

    foreach (var serviceId in toEvict)
    {
        await RemoveAsync(serviceId, cancellationToken);
    }

    _logger.LogWarning(
        "Evicted {Count} least recently used instances (cache full)",
        toEvict.Count);
}
```

### 4. 健康检查

```csharp
private async Task PerformHealthChecksAsync()
{
    if (!_options.EnableHealthCheck)
        return;

    var unhealthyServices = new List<string>();

    foreach (var (serviceId, entry) in _instances)
    {
        if (entry.Service is IServiceLifecycle lifecycle)
        {
            try
            {
                var isHealthy = await lifecycle.OnHealthCheckAsync();
                if (!isHealthy)
                {
                    unhealthyServices.Add(serviceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Health check failed: ServiceId={ServiceId}", serviceId);
                unhealthyServices.Add(serviceId);
            }
        }
    }

    foreach (var serviceId in unhealthyServices)
    {
        _logger.LogWarning(
            "Removing unhealthy service: ServiceId={ServiceId}", serviceId);
        await RemoveAsync(serviceId);
    }
}
```

## DI 注册扩展

```csharp
namespace Microsoft.Extensions.DependencyInjection;

public static class PulseServiceFactoryExtensions
{
    /// <summary>
    /// 注册服务工厂
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

        return services;
    }

    /// <summary>
    /// 注册简单的服务工厂（使用构造函数注入）
    /// </summary>
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
```

## 使用示例

### 基本用法

```csharp
// 1. 定义服务
public class ChatRoomService : IPulseService, IServiceLifecycle
{
    public string ServiceName => "ChatRoom";
    public string ServiceId { get; }

    private readonly List<Message> _messages = new();

    public ChatRoomService(string roomId, ILogger<ChatRoomService> logger)
    {
        ServiceId = $"ChatRoom:{roomId}";
    }

    public Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // 从数据库加载历史消息
        return Task.CompletedTask;
    }

    public Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        // 保存消息到数据库
        return Task.CompletedTask;
    }

    public Task<bool> OnHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        // 检查内存使用等
        return Task.FromResult(_messages.Count < 10000);
    }
}

// 2. 注册工厂
services.AddPulseServiceFactory<ChatRoomService>(
    (sp, serviceId) =>
    {
        var roomId = serviceId.Split(':')[1];
        return new ChatRoomService(roomId, sp.GetRequiredService<ILogger<ChatRoomService>>());
    },
    options =>
    {
        options.IdleTimeout = TimeSpan.FromMinutes(10);
        options.MaxCachedInstances = 5000;
    });

// 3. 在 Hub 中使用
public class ChatRoomUserHub : IPulseHub
{
    private readonly IPulseServiceFactory<ChatRoomService> _factory;

    public ChatRoomUserHub(IPulseServiceFactory<ChatRoomService> factory)
    {
        _factory = factory;
    }

    public async Task<Message[]> GetMessagesAsync(string roomId)
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        return service.Messages.ToArray();
    }
}
```

## 性能考虑

### 内存占用
- **Entry 大小**：约 64 字节（对象头 + 字段）
- **10,000 实例**：约 640 KB（不含 Service 本身）
- **建议**：根据实际 Service 大小调整 `MaxCachedInstances`

### 并发性能
- **GetOrCreateAsync**：O(1) 平均时间复杂度
- **竞态保护**：使用 ConcurrentDictionary，支持高并发
- **锁竞争**：无全局锁，每个 ServiceId 独立

### 清理开销
- **清理频率**：默认 1 分钟
- **清理复杂度**：O(n)，n 为实例数
- **建议**：高负载场景下增加 `CleanupInterval`

## 监控指标

```csharp
public interface IPulseServiceFactoryMetrics
{
    /// <summary>当前活跃实例数</summary>
    int ActiveInstances { get; }

    /// <summary>总创建次数</summary>
    long TotalCreated { get; }

    /// <summary>总移除次数</summary>
    long TotalRemoved { get; }

    /// <summary>缓存命中率</summary>
    double CacheHitRate { get; }

    /// <summary>平均实例存活时间</summary>
    TimeSpan AverageLifetime { get; }
}
```

## 故障处理

### 创建失败
- **行为**：抛出 `ServiceCreationException`
- **恢复**：下次调用重新尝试创建
- **建议**：在上层实现重试逻辑

### 激活失败
- **行为**：移除实例，抛出 `ServiceActivationException`
- **恢复**：下次调用重新创建和激活
- **建议**：在 `OnActivateAsync` 中实现幂等性

### 健康检查失败
- **行为**：自动移除实例
- **恢复**：下次访问时自动重新创建
- **建议**：在 `OnHealthCheckAsync` 中记录详细日志

## 版本历史

- **v1.0** (2025-01-10) - 初始设计
  - 基础 CRUD 操作
  - 空闲超时清理
  - LRU 驱逐策略
  - 生命周期钩子

## 未来计划

### v1.1
- [ ] 分布式追踪支持（OpenTelemetry）
- [ ] 更细粒度的指标收集
- [ ] 可插拔的驱逐策略

### v2.0
- [ ] 分布式服务工厂（跨节点）
- [ ] 持久化支持（Redis/Database）
- [ ] 预热和预加载机制
