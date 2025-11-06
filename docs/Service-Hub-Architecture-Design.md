# PulseRPC.Server 服务架构设计

## 执行摘要

本文档定义了 PulseRPC.Server 中有状态服务（IPulseService）与无状态通信契约（IPulseHub）的关系架构，提供了生产级的解决方案，解决"多个无状态 Hub 共享同一个有状态 Service 实例"的设计挑战。

**核心方案**：Service as State Container + ServiceFactory

**设计原则**：
- ✅ 职责分离：Service 负责状态，Hub 负责通信契约
- ✅ 自动化管理：ServiceFactory 负责生命周期
- ✅ 线程安全：线程亲和性由 ThreadAffinityManager 保证
- ✅ 故障隔离：服务间故障不相互影响
- ✅ 易于测试：清晰的依赖关系

---

## 1. 架构概览

### 1.1 组件关系图

```
┌─────────────────────────────────────────────────────────────────┐
│                        客户端请求                                  │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                     ServiceRegistry                              │
│  • 根据 ServiceName + ServiceId 路由请求                          │
│  • 管理 Hub 注册信息                                               │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                  ThreadAffinityManager                           │
│  • 为每个 ServiceId 分配固定线程                                   │
│  • 使用一致性哈希保证线程亲和性                                     │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                         IPulseHub                                │
│  • 无状态的通信契约（gRPC Service Definition）                     │
│  • 定义客户端可调用的方法                                          │
│  • 可以有多个 Hub 对应同一个 Service                               │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                   IPulseServiceFactory                           │
│  • 管理 Service 实例的创建和销毁                                   │
│  • 实现缓存和 LRU 驱逐策略                                         │
│  • 调用生命周期钩子                                                │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                      IPulseService                               │
│  • 有状态的服务实例（Actor）                                       │
│  • 拥有 ServiceName + ServiceId                                  │
│  • 存储业务状态（无需加锁，线程安全保证）                           │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 核心概念

| 概念 | 定义 | 职责 | 生命周期 |
|------|------|------|---------|
| **IPulseHub** | 通信契约接口 | 定义 RPC 方法签名 | 单例（Singleton） |
| **IPulseService** | 状态容器接口 | 管理业务状态 | 按需创建，LRU 驱逐 |
| **ServiceFactory** | 实例工厂 | 创建、缓存、销毁 Service | 单例 |
| **ServiceName** | 服务类型标识 | 区分不同类型的服务 | 静态配置 |
| **ServiceId** | 服务实例标识 | 区分同类型的不同实例 | 动态生成 |

---

## 2. 设计方案详解

### 2.1 方案：Service as State Container

#### 核心思想

- **IPulseService**：负责状态管理和业务逻辑
- **IPulseHub**：负责定义通信契约
- **ServiceFactory**：负责实例生命周期管理
- **关系**：多个 Hub → 1 个 Service（通过 DI 注入）

#### 架构图

```
┌──────────────────────────────────────────────────────────────┐
│                      ChatRoomService                          │
│                    (IPulseService)                            │
│                                                               │
│  + ServiceName: "ChatRoom"                                   │
│  + ServiceId: "ChatRoom:room-123"                            │
│                                                               │
│  - _messages: List<Message>  ◄─────── 业务状态                │
│  - _users: HashSet<User>                                     │
│  - _bannedUsers: HashSet<string>                             │
│                                                               │
│  + AddMessage(msg)                                           │
│  + IsBanned(userId): bool                                    │
└───────────────┬──────────────────────────────────────────────┘
                │
                │ 依赖注入
                │
    ┌───────────┴───────────┬───────────────────────┐
    │                       │                       │
    ▼                       ▼                       ▼
┌─────────────┐      ┌─────────────┐      ┌─────────────┐
│ UserHub     │      │ AdminHub    │      │ QueryHub    │
│             │      │             │      │             │
│ + SendMsg() │      │ + BanUser() │      │ + GetMsgs() │
└─────────────┘      └─────────────┘      └─────────────┘
      │                     │                     │
      └─────────────────────┴─────────────────────┘
                            │
                    访问同一个 Service 实例
```

### 2.2 职责分离

#### IPulseService 的职责

```csharp
public interface IPulseService
{
    /// <summary>服务类型名称（如 "ChatRoom"）</summary>
    string ServiceName { get; }

    /// <summary>服务实例唯一标识（如 "ChatRoom:room-123"）</summary>
    string ServiceId { get; }
}
```

**职责**：
1. 定义服务身份（ServiceName + ServiceId）
2. 存储业务状态
3. 实现业务逻辑
4. 可选实现 `IServiceLifecycle` 接口

**不负责**：
- ❌ 不负责消息序列化/反序列化
- ❌ 不负责网络通信
- ❌ 不负责线程调度

#### IPulseHub 的职责

```csharp
public interface IPulseHub
{
    // 标记接口，触发源代码生成器
}

// 具体契约
public interface IChatRoomUserHub : IPulseHub
{
    Task<Message[]> GetMessagesAsync(string roomId);
    Task<Message> SendMessageAsync(string roomId, string text);
}
```

**职责**：
1. 定义客户端可调用的 RPC 方法
2. 定义参数和返回值类型
3. 触发源代码生成器

**不负责**：
- ❌ 不存储状态
- ❌ 不实现业务逻辑

#### IPulseServiceFactory 的职责

```csharp
public interface IPulseServiceFactory<TService>
    where TService : IPulseService
{
    ValueTask<TService> GetOrCreateAsync(string serviceId, ...);
    ValueTask<bool> RemoveAsync(string serviceId, ...);
    // ...
}
```

**职责**：
1. 按需创建 Service 实例
2. 缓存实例（避免重复创建）
3. 空闲超时清理
4. LRU 驱逐策略
5. 调用生命周期钩子（OnActivate/OnDeactivate/OnHealthCheck）

**不负责**：
- ❌ 不负责线程调度
- ❌ 不负责消息路由

---

## 3. 实现模式

### 3.1 基本模式（推荐）

#### 场景
- 多个 Hub 接口对应同一个 Service
- 需要权限分离（用户 Hub vs 管理员 Hub）
- Service 有复杂的业务状态

#### 实现示例

```csharp
// 1. 定义 Service（状态容器）
public class ChatRoomService : IPulseService, IServiceLifecycle
{
    public string ServiceName => "ChatRoom";
    public string ServiceId { get; }

    // 业务状态
    private readonly List<Message> _messages = new();
    private readonly HashSet<string> _bannedUsers = new();

    // 依赖注入
    private readonly ILogger<ChatRoomService> _logger;
    private readonly IChatRepository _repository;

    public ChatRoomService(
        string roomId,
        ILogger<ChatRoomService> logger,
        IChatRepository repository)
    {
        ServiceId = $"ChatRoom:{roomId}";
        _logger = logger;
        _repository = repository;
    }

    // 状态访问方法（供 Hub 调用）
    public IReadOnlyList<Message> GetMessages() => _messages;

    public void AddMessage(Message msg)
    {
        if (_bannedUsers.Contains(msg.UserId))
            throw new InvalidOperationException("User is banned");

        _messages.Add(msg);
        _logger.LogInformation("Message added: {MessageId}", msg.Id);
    }

    public void BanUser(string userId)
    {
        _bannedUsers.Add(userId);
        _logger.LogWarning("User banned: {UserId}", userId);
    }

    // 生命周期钩子
    public async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Activating ChatRoom: {ServiceId}", ServiceId);

        // 从数据库加载历史消息
        var messages = await _repository.LoadMessagesAsync(ServiceId, cancellationToken);
        _messages.AddRange(messages);

        _logger.LogInformation("Loaded {Count} messages", messages.Count);
    }

    public async Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deactivating ChatRoom: {ServiceId}", ServiceId);

        // 保存消息到数据库
        await _repository.SaveMessagesAsync(ServiceId, _messages, cancellationToken);
    }

    public Task<bool> OnHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        // 检查内存使用
        var isHealthy = _messages.Count < 10000;
        return Task.FromResult(isHealthy);
    }
}

// 2. 定义多个 Hub（通信契约）
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
        return service.GetMessages().ToArray();
    }

    public async Task<Message> SendMessageAsync(string roomId, string text, string userId)
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");

        var message = new Message
        {
            Id = Guid.NewGuid().ToString(),
            Text = text,
            UserId = userId,
            Timestamp = DateTimeOffset.UtcNow
        };

        service.AddMessage(message);
        return message;
    }
}

public class ChatRoomAdminHub : IPulseHub
{
    private readonly IPulseServiceFactory<ChatRoomService> _factory;

    public ChatRoomAdminHub(IPulseServiceFactory<ChatRoomService> factory)
    {
        _factory = factory;
    }

    public async Task BanUserAsync(string roomId, string userId)
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        service.BanUser(userId);
    }

    public async Task<Message[]> GetAllMessagesAsync(string roomId)
    {
        // 管理员可以看到所有消息（包括已删除的）
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        return service.GetMessages().ToArray();
    }
}

// 3. DI 注册
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChatRoomServices(this IServiceCollection services)
    {
        // 注册 Repository
        services.AddSingleton<IChatRepository, ChatRepository>();

        // 注册 ServiceFactory
        services.AddPulseServiceFactory<ChatRoomService>(
            (sp, serviceId) =>
            {
                var roomId = serviceId.Split(':')[1];
                return new ChatRoomService(
                    roomId,
                    sp.GetRequiredService<ILogger<ChatRoomService>>(),
                    sp.GetRequiredService<IChatRepository>());
            },
            options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(10);
                options.MaxCachedInstances = 5000;
            });

        // 注册 Hub
        services.AddSingleton<ChatRoomUserHub>();
        services.AddSingleton<ChatRoomAdminHub>();

        return services;
    }
}
```

### 3.2 读写分离模式（高级）

#### 场景
- 需要更细粒度的权限控制
- 读写操作性能要求不同
- 需要实现 CQRS 模式

#### 实现示例

```csharp
// 1. 定义状态访问接口
public interface IGameStateReader
{
    IReadOnlyList<Player> Players { get; }
    GamePhase CurrentPhase { get; }
    int RoundNumber { get; }
}

public interface IGameStateWriter : IGameStateReader
{
    void AddPlayer(Player player);
    void RemovePlayer(string playerId);
    void ChangePhase(GamePhase phase);
    void NextRound();
}

// 2. Service 实现读写接口
public class GameRoomService : IPulseService, IGameStateWriter, IServiceLifecycle
{
    public string ServiceName => "GameRoom";
    public string ServiceId { get; }

    private readonly List<Player> _players = new();
    private GamePhase _currentPhase = GamePhase.Waiting;
    private int _roundNumber = 0;

    // 只读属性
    public IReadOnlyList<Player> Players => _players;
    public GamePhase CurrentPhase => _currentPhase;
    public int RoundNumber => _roundNumber;

    // 写入方法
    public void AddPlayer(Player player)
    {
        if (_currentPhase != GamePhase.Waiting)
            throw new InvalidOperationException("Cannot add player after game started");

        _players.Add(player);
    }

    public void RemovePlayer(string playerId)
    {
        _players.RemoveAll(p => p.Id == playerId);
    }

    public void ChangePhase(GamePhase phase)
    {
        _currentPhase = phase;
    }

    public void NextRound()
    {
        _roundNumber++;
    }

    // ... 生命周期方法
}

// 3. 查询 Hub（只读访问）
public class GameQueryHub : IPulseHub
{
    private readonly IPulseServiceFactory<GameRoomService> _factory;

    public GameQueryHub(IPulseServiceFactory<GameRoomService> factory)
    {
        _factory = factory;
    }

    public async Task<Player[]> GetPlayersAsync(string gameId)
    {
        var service = await _factory.GetOrCreateAsync($"GameRoom:{gameId}");
        IGameStateReader reader = service; // 只读访问
        return reader.Players.ToArray();
    }

    public async Task<GameStatus> GetStatusAsync(string gameId)
    {
        var service = await _factory.GetOrCreateAsync($"GameRoom:{gameId}");
        IGameStateReader reader = service;
        return new GameStatus
        {
            Phase = reader.CurrentPhase,
            RoundNumber = reader.RoundNumber,
            PlayerCount = reader.Players.Count
        };
    }
}

// 4. 命令 Hub（读写访问）
public class GameCommandHub : IPulseHub
{
    private readonly IPulseServiceFactory<GameRoomService> _factory;

    public GameCommandHub(IPulseServiceFactory<GameRoomService> factory)
    {
        _factory = factory;
    }

    public async Task JoinGameAsync(string gameId, Player player)
    {
        var service = await _factory.GetOrCreateAsync($"GameRoom:{gameId}");
        IGameStateWriter writer = service; // 读写访问
        writer.AddPlayer(player);
    }

    public async Task StartGameAsync(string gameId)
    {
        var service = await _factory.GetOrCreateAsync($"GameRoom:{gameId}");
        IGameStateWriter writer = service;
        writer.ChangePhase(GamePhase.Playing);
    }
}
```

### 3.3 简化模式（适合简单场景）

#### 场景
- Service 逻辑简单
- 不需要多个 Hub
- 快速原型开发

#### 实现示例

```csharp
// Service 直接实现 Hub 接口
public class CounterService : IPulseService, ICounterHub, IServiceLifecycle
{
    public string ServiceName => "Counter";
    public string ServiceId { get; }

    private int _count = 0;

    public CounterService(string counterId)
    {
        ServiceId = $"Counter:{counterId}";
    }

    // ICounterHub 实现
    public Task<int> GetCountAsync()
    {
        return Task.FromResult(_count);
    }

    public Task<int> IncrementAsync()
    {
        return Task.FromResult(++_count);
    }

    // 生命周期
    public Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // 从持久化存储加载
        return Task.CompletedTask;
    }

    public Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        // 保存到持久化存储
        return Task.CompletedTask;
    }
}

// DI 注册
services.AddPulseServiceFactory<CounterService>();
services.AddSingleton<ICounterHub>(sp =>
{
    // 需要一个默认的 Hub 实例，转发到 Factory
    return new CounterHubProxy(sp.GetRequiredService<IPulseServiceFactory<CounterService>>());
});
```

---

## 4. 线程安全保证

### 4.1 ThreadAffinityManager

**保证**：同一个 ServiceId 的所有请求在同一个线程上执行

```csharp
// 内部实现（简化版）
public class ThreadAffinityManager
{
    private readonly Dictionary<int, ServiceSchedulingWorkerThread> _workerThreads;
    private readonly ConsistentHash<int> _consistentHash;

    public void ScheduleTask(string serviceId, Func<Task> task)
    {
        // 1. 计算 ServiceId 的哈希值
        var hash = serviceId.GetHashCode();

        // 2. 使用一致性哈希找到对应的线程
        var threadId = _consistentHash.GetNode(hash);

        // 3. 将任务调度到该线程
        var thread = _workerThreads[threadId];
        thread.EnqueueTask(task);
    }
}
```

### 4.2 无锁设计

由于线程亲和性保证，Service 内部的状态访问**无需加锁**：

```csharp
public class ChatRoomService : IPulseService
{
    // ✅ 无需 lock，因为同一个 ServiceId 的请求在同一线程执行
    private readonly List<Message> _messages = new();

    public void AddMessage(Message msg)
    {
        // ✅ 线程安全，无需加锁
        _messages.Add(msg);
    }
}
```

### 4.3 跨 Service 通信

如果需要跨 Service 通信，使用消息传递：

```csharp
public class OrderService : IPulseService
{
    private readonly IPulseServiceFactory<InventoryService> _inventoryFactory;

    public async Task PlaceOrderAsync(Order order)
    {
        // 1. 在当前 Service 线程执行
        _orders.Add(order);

        // 2. 调用另一个 Service（会切换到另一个线程）
        var inventory = await _inventoryFactory.GetOrCreateAsync("Inventory:default");
        await inventory.ReserveItemsAsync(order.Items);

        // 3. 回到当前 Service 线程
        order.Status = OrderStatus.Confirmed;
    }
}
```

---

## 5. 生命周期管理

### 5.1 完整生命周期

```
┌───────────────┐
│  未创建状态     │
└───────┬───────┘
        │
        │ GetOrCreateAsync
        ▼
┌───────────────┐
│  创建实例      │  ◄── serviceFactory(serviceId)
└───────┬───────┘
        │
        │ OnActivateAsync
        ▼
┌───────────────┐
│  活跃状态      │  ◄── 处理请求，更新 LastAccessTime
│  (Cached)     │
└───────┬───────┘
        │
        │ 空闲超时 / 健康检查失败 / LRU 驱逐
        ▼
┌───────────────┐
│  停用中       │  ◄── OnDeactivateAsync
└───────┬───────┘
        │
        │ Dispose (if IDisposable)
        ▼
┌───────────────┐
│  已销毁       │
└───────────────┘
```

### 5.2 生命周期钩子

```csharp
public interface IServiceLifecycle
{
    /// <summary>
    /// 实例首次创建后调用
    /// </summary>
    /// <remarks>
    /// 用途：
    /// - 加载持久化状态
    /// - 建立数据库连接
    /// - 初始化外部资源
    /// </remarks>
    Task OnActivateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 实例从缓存移除前调用
    /// </summary>
    /// <remarks>
    /// 用途：
    /// - 保存状态到数据库
    /// - 释放外部资源
    /// - 清理临时文件
    /// </remarks>
    Task OnDeactivateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 定期健康检查调用
    /// </summary>
    /// <remarks>
    /// 用途：
    /// - 检查内存使用
    /// - 检查数据库连接
    /// - 检查业务逻辑约束
    /// 返回 false 将触发实例移除
    /// </remarks>
    Task<bool> OnHealthCheckAsync(CancellationToken cancellationToken = default);
}
```

### 5.3 实现示例

```csharp
public class ChatRoomService : IPulseService, IServiceLifecycle
{
    private readonly IChatRepository _repository;
    private readonly ILogger _logger;
    private readonly List<Message> _messages = new();

    public async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Activating {ServiceId}", ServiceId);

        // 从数据库加载状态
        var messages = await _repository.LoadMessagesAsync(ServiceId, cancellationToken);
        _messages.AddRange(messages);

        _logger.LogInformation("Loaded {Count} messages", messages.Count);
    }

    public async Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deactivating {ServiceId}", ServiceId);

        // 保存状态到数据库
        await _repository.SaveMessagesAsync(ServiceId, _messages, cancellationToken);

        _logger.LogInformation("Saved {Count} messages", _messages.Count);
    }

    public Task<bool> OnHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        // 检查消息数量是否超过限制
        var isHealthy = _messages.Count < 10000;

        if (!isHealthy)
        {
            _logger.LogWarning("Health check failed: too many messages ({Count})", _messages.Count);
        }

        return Task.FromResult(isHealthy);
    }
}
```

---

## 6. 配置与调优

### 6.1 ServiceFactory 配置

```csharp
services.AddPulseServiceFactory<ChatRoomService>(
    serviceFactory: (sp, serviceId) => /* ... */,
    configureOptions: options =>
    {
        // 空闲超时时间
        options.IdleTimeout = TimeSpan.FromMinutes(10);

        // 最大缓存实例数
        options.MaxCachedInstances = 5000;

        // 清理间隔
        options.CleanupInterval = TimeSpan.FromMinutes(1);

        // 健康检查
        options.EnableHealthCheck = true;
        options.HealthCheckInterval = TimeSpan.FromSeconds(30);
    });
```

### 6.2 ThreadAffinity 配置

```csharp
services.AddIPulseServiceScheduling(
    configureScheduling: options =>
    {
        // Worker 线程数量
        options.WorkerThreadCount = Environment.ProcessorCount;

        // 虚拟节点数（影响负载均衡）
        options.VirtualNodesPerThread = 150;

        // 实例空闲超时
        options.IdleInstanceTimeout = TimeSpan.FromMinutes(5);
    });
```

### 6.3 调优建议

| 场景 | 配置建议 | 理由 |
|------|---------|------|
| **低流量** | WorkerThreadCount = 2-4<br>MaxCachedInstances = 1000 | 减少资源占用 |
| **高流量** | WorkerThreadCount = CPU 核数<br>MaxCachedInstances = 10000+ | 提高并发性能 |
| **内存敏感** | IdleTimeout = 2 分钟<br>MaxCachedInstances = 5000 | 快速释放内存 |
| **IO 密集** | WorkerThreadCount = CPU 核数 × 2 | 提高 IO 并发 |
| **CPU 密集** | WorkerThreadCount = CPU 核数 | 避免上下文切换 |

---

## 7. 监控与可观测性

### 7.1 关键指标

```csharp
// ServiceFactory 指标
public interface IPulseServiceFactoryMetrics
{
    int ActiveInstances { get; }        // 当前活跃实例数
    long TotalCreated { get; }          // 总创建次数
    long TotalRemoved { get; }          // 总移除次数
    double CacheHitRate { get; }        // 缓存命中率
    long EvictionCount { get; }         // 驱逐次数
}

// 使用示例
var metrics = serviceProvider.GetRequiredService<IPulseServiceFactoryMetrics>();
Console.WriteLine($"Cache Hit Rate: {metrics.CacheHitRate:P2}");
Console.WriteLine($"Active Instances: {metrics.ActiveInstances}");
```

### 7.2 日志记录

```csharp
// ServiceFactory 自动记录关键事件
_logger.LogInformation(
    "Created service instance: ServiceId={ServiceId}, Type={ServiceType}",
    serviceId, typeof(TService).Name);

_logger.LogWarning(
    "Evicted {Count} least recently used instances (cache limit exceeded)",
    toEvict.Count);

_logger.LogError(ex,
    "Service activation failed: ServiceId={ServiceId}", serviceId);
```

### 7.3 分布式追踪

```csharp
// 集成 OpenTelemetry（未来计划）
public async ValueTask<TService> GetOrCreateAsync(
    string serviceId,
    CancellationToken cancellationToken = default)
{
    using var activity = ActivitySource.StartActivity("ServiceFactory.GetOrCreate");
    activity?.SetTag("service.id", serviceId);
    activity?.SetTag("service.type", typeof(TService).Name);

    // ...
}
```

---

## 8. 故障处理

### 8.1 创建失败

```csharp
try
{
    var service = await factory.GetOrCreateAsync("ChatRoom:123");
}
catch (ServiceCreationException ex)
{
    _logger.LogError(ex, "Failed to create service: {ServiceId}", ex.ServiceId);
    // 重试或降级处理
}
```

### 8.2 激活失败

```csharp
public async Task OnActivateAsync(CancellationToken cancellationToken)
{
    // ❌ 错误：不处理异常
    var data = await _repository.LoadAsync(ServiceId);

    // ✅ 正确：处理异常并提供降级
    try
    {
        var data = await _repository.LoadAsync(ServiceId, cancellationToken);
        _state = data;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to load state, using default");
        _state = GetDefaultState(); // 降级到默认状态
    }
}
```

### 8.3 健康检查失败

```csharp
public Task<bool> OnHealthCheckAsync(CancellationToken cancellationToken)
{
    try
    {
        // 检查多个维度
        var memoryOk = _messages.Count < 10000;
        var stateOk = ValidateState();
        var connectionOk = CheckDatabaseConnection();

        var isHealthy = memoryOk && stateOk && connectionOk;

        if (!isHealthy)
        {
            _logger.LogWarning(
                "Health check failed: Memory={MemoryOk}, State={StateOk}, Connection={ConnectionOk}",
                memoryOk, stateOk, connectionOk);
        }

        return Task.FromResult(isHealthy);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Health check exception");
        return Task.FromResult(false); // 异常视为不健康
    }
}
```

---

## 9. 测试策略

### 9.1 单元测试

```csharp
[Fact]
public async Task Service_Should_AddMessage_When_UserNotBanned()
{
    // Arrange
    var service = new ChatRoomService(
        roomId: "test-room",
        logger: Mock.Of<ILogger<ChatRoomService>>(),
        repository: Mock.Of<IChatRepository>());

    var message = new Message { UserId = "user1", Text = "Hello" };

    // Act
    service.AddMessage(message);

    // Assert
    service.GetMessages().Should().Contain(message);
}

[Fact]
public async Task Factory_Should_ReturnSameInstance_ForSameServiceId()
{
    // Arrange
    var factory = CreateFactory();

    // Act
    var service1 = await factory.GetOrCreateAsync("ChatRoom:123");
    var service2 = await factory.GetOrCreateAsync("ChatRoom:123");

    // Assert
    service1.Should().BeSameAs(service2);
}
```

### 9.2 集成测试

```csharp
[Fact]
public async Task Hub_Should_ShareState_AcrossMultipleHubs()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddChatRoomServices();
    var sp = services.BuildServiceProvider();

    var userHub = sp.GetRequiredService<ChatRoomUserHub>();
    var adminHub = sp.GetRequiredService<ChatRoomAdminHub>();

    // Act
    await userHub.SendMessageAsync("room1", "Hello", "user1");
    var messages = await adminHub.GetAllMessagesAsync("room1");

    // Assert
    messages.Should().HaveCount(1);
    messages[0].Text.Should().Be("Hello");
}
```

---

## 10. 迁移指南

### 10.1 从旧架构迁移

**旧架构**（Service 直接实现 Hub）：

```csharp
// 旧方式
public class ChatRoomService : IPulseService, IChatRoomHub
{
    public Task<Message> SendMessageAsync(string text)
    {
        // ...
    }
}
```

**新架构**（Service + Factory + Hub）：

```csharp
// 1. Service 只负责状态
public class ChatRoomService : IPulseService
{
    public void AddMessage(Message msg) { /* ... */ }
}

// 2. Hub 通过 Factory 访问 Service
public class ChatRoomHub : IPulseHub
{
    private readonly IPulseServiceFactory<ChatRoomService> _factory;

    public async Task<Message> SendMessageAsync(string roomId, string text)
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        // ...
    }
}
```

### 10.2 迁移步骤

1. **创建 Service 类**（移动状态字段）
2. **创建 Hub 类**（移动 RPC 方法）
3. **注册 ServiceFactory**
4. **更新 DI 注册**
5. **测试验证**

---

## 11. 最佳实践总结

### ✅ 推荐做法

1. **ServiceId 命名规范**：`{ServiceName}:{BusinessId}`
   - ✅ `"ChatRoom:room-123"`
   - ✅ `"GameRoom:game-456"`
   - ❌ `"ChatRoom"` （所有实例相同，失去隔离）

2. **状态存储在 Service**：所有可变状态放在 Service 中
   - ✅ `private List<Message> _messages;`
   - ❌ `static List<Message> _messages;` （跨实例共享）

3. **Hub 无状态**：Hub 只负责转发请求
   - ✅ `await _factory.GetOrCreateAsync(...)`
   - ❌ `private ChatRoomService _service;` （违反无状态原则）

4. **实现生命周期钩子**：充分利用激活/停用钩子
   - ✅ `OnActivateAsync` 加载持久化状态
   - ✅ `OnDeactivateAsync` 保存状态
   - ✅ `OnHealthCheckAsync` 检查健康状态

5. **合理配置超时**：根据业务特性配置
   - 频繁访问的服务：`IdleTimeout = 30 分钟`
   - 偶尔访问的服务：`IdleTimeout = 5 分钟`

### ❌ 避免做法

1. ❌ **在 Hub 中存储状态**
2. ❌ **在 Service 中实现网络通信**
3. ❌ **在 Service 中手动加锁**（线程亲和性已保证）
4. ❌ **在激活钩子中抛出异常**（会导致实例创建失败）
5. ❌ **在健康检查中执行耗时操作**（会阻塞清理线程）

---

## 12. 常见问题

### Q1: Service 和 Hub 的生命周期是什么？

**A**:
- **Hub**：单例（Singleton），与应用程序同生命周期
- **Service**：按需创建，空闲后自动销毁（LRU 策略）

### Q2: 如何保证线程安全？

**A**: ThreadAffinityManager 保证同一个 ServiceId 的请求在同一线程执行，因此 Service 内部无需加锁。

### Q3: 如何处理跨 Service 通信？

**A**: 通过 Factory 获取另一个 Service 实例，使用异步调用（会自动切换线程）。

### Q4: Factory 缓存会占用多少内存？

**A**: 每个 Entry 约 64 字节 + Service 实例大小。10,000 实例约占 640 KB（不含 Service）。

### Q5: 健康检查失败后会怎样？

**A**: 实例会被自动移除，下次访问时重新创建。

---

## 13. 版本历史

- **v1.0** (2025-01-10) - 初始设计
  - 方案 1：Service as State Container
  - ServiceFactory 实现
  - 生命周期钩子
  - LRU 驱逐策略

---

## 14. 参考资料

- [ServiceFactory 设计文档](ServiceFactory-Design.md)
- [ServiceFactory 实现示例](ServiceFactory-Implementation-Example.cs)
- [最佳实践指南](Service-Hub-Best-Practices.md)
- [Orleans Actor 模型](https://learn.microsoft.com/en-us/dotnet/orleans/)
- [Akka.NET Actor 系统](https://getakka.net/)

---

**文档维护者**：PulseRPC Team
**最后更新**：2025-01-10
**状态**：生产就绪 (Production Ready)
