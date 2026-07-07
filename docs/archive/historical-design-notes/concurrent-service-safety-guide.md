# 并发服务安全指南

> 文档状态：历史安全指南。当前源码中不再保留独立 `ConcurrentServiceBase`/`BaseService` 公共基类，服务执行配置已收敛到 `PulseServiceBase` 与 `ServiceExecutionOptions`。本文保留并发安全原则，类型名称请按当前实现映射理解。

## 文档信息

- **版本**: 1.0
- **日期**: 2025-11-06
- **状态**: ✅ 已实现
- **相关**: [PulseRPC.Server 架构设计深度分析](../../concepts/server-runtime.md)

## 概述

`ConcurrentServiceBase` 允许 Service 内部并发处理多个消息，适合 IO 密集型场景。本指南介绍如何安全使用并发服务，避免常见的并发问题。

## 何时使用并发服务

### ✅ 适合使用 ConcurrentServiceBase

1. **IO 密集型操作**
   - 数据库查询服务
   - HTTP API 网关
   - 文件读写服务
   - 网络请求代理

2. **无状态查询**
   - 纯查询操作，不修改状态
   - 缓存读取
   - 只读数据访问

3. **独立任务处理**
   - 每个消息处理互不依赖
   - 无共享状态
   - 可并行执行

### ❌ 不适合使用 ConcurrentServiceBase

1. **有状态业务逻辑**
   - 聊天室消息顺序
   - 游戏状态机
   - 订单处理流程

2. **需要严格顺序**
   - 必须按顺序处理的消息
   - 有前后依赖关系的操作

3. **共享可变状态**
   - 频繁修改共享变量
   - 复杂的状态管理

**推荐**: 对于这些场景，使用 `BaseService`（单线程 Actor 模型）更安全。

---

## 基本用法

### 1. 创建并发服务

```csharp
using PulseRPC.Server;

/// <summary>
/// 玩家数据查询服务 - IO 密集型，适合并发
/// </summary>
public class PlayerDataService : ConcurrentServiceBase
{
    private readonly IDatabase _database;

    public PlayerDataService(
        ILogger<PlayerDataService> logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator,
        IDatabase database,
        ConcurrentServiceOptions? options = null)
        : base(logger, authenticationService, permissionValidator, options)
    {
        _database = database;
    }

    /// <summary>
    /// 查询玩家信息 - 并发处理
    /// </summary>
    public async Task<PlayerInfo> GetPlayerAsync(long playerId)
    {
        // ✅ 纯查询操作，无共享状态，可以并发
        return await _database.QueryPlayerAsync(playerId);
    }

    /// <summary>
    /// 批量查询 - 并发处理
    /// </summary>
    public async Task<List<PlayerInfo>> GetPlayersAsync(List<long> playerIds)
    {
        // ✅ 每个查询独立，可以并发
        var tasks = playerIds.Select(id => _database.QueryPlayerAsync(id));
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }
}
```

### 2. 配置并发度

```csharp
// 默认配置（4 并发）
var service1 = new PlayerDataService(
    logger, authService, permissionValidator, database);

// IO 密集型配置（16 并发）
var service2 = new PlayerDataService(
    logger, authService, permissionValidator, database,
    ConcurrentServiceOptions.ForIOIntensive);

// CPU 密集型配置（CPU 核心数并发）
var service3 = new PlayerDataService(
    logger, authService, permissionValidator, database,
    ConcurrentServiceOptions.ForCPUIntensive);

// 自定义配置
var service4 = new PlayerDataService(
    logger, authService, permissionValidator, database,
    new ConcurrentServiceOptions
    {
        MaxConcurrency = 10,
        QueueCapacity = 5000
    });
```

---

## 并发安全模式

### 模式 1: 无共享状态（最安全）✅

```csharp
public class QueryService : ConcurrentServiceBase
{
    private readonly IDatabase _database; // 只读依赖

    // ✅ 每个请求独立，无共享状态
    public async Task<User> GetUserAsync(long userId)
    {
        return await _database.QueryAsync<User>(userId);
    }
}
```

### 模式 2: 只读共享状态✅

```csharp
public class ConfigService : ConcurrentServiceBase
{
    private readonly IConfiguration _config; // 只读

    // ✅ 只读取配置，不修改，线程安全
    public Task<string> GetSettingAsync(string key)
    {
        return Task.FromResult(_config[key]);
    }
}
```

### 模式 3: 线程安全集合✅

```csharp
public class CacheService : ConcurrentServiceBase
{
    private readonly ConcurrentDictionary<string, object> _cache = new(); // 线程安全集合

    // ✅ ConcurrentDictionary 自带线程安全
    public Task<object?> GetAsync(string key)
    {
        _cache.TryGetValue(key, out var value);
        return Task.FromResult(value);
    }

    public Task SetAsync(string key, object value)
    {
        _cache[key] = value; // 线程安全
        return Task.CompletedTask;
    }
}
```

### 模式 4: Interlocked 原子操作✅

```csharp
public class StatsService : ConcurrentServiceBase
{
    private long _requestCount; // long 类型，使用 Interlocked

    public Task IncrementCounterAsync()
    {
        // ✅ Interlocked.Increment 原子操作
        Interlocked.Increment(ref _requestCount);
        return Task.CompletedTask;
    }

    public Task<long> GetCountAsync()
    {
        // ✅ Interlocked.Read 原子读取
        return Task.FromResult(Interlocked.Read(ref _requestCount));
    }
}
```

### 模式 5: SemaphoreSlim 保护临界区✅

```csharp
public class ResourceService : ConcurrentServiceBase
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _sharedState; // 需要保护的状态

    public async Task UpdateStateAsync(int newValue)
    {
        await _lock.WaitAsync();
        try
        {
            // ✅ 临界区，同一时刻只有一个线程执行
            _sharedState = newValue;
            await Task.Delay(10); // 模拟耗时操作
        }
        finally
        {
            _lock.Release();
        }
    }
}
```

---

## 常见错误和修复

### ❌ 错误 1: 共享可变状态无保护

```csharp
public class BadService : ConcurrentServiceBase
{
    private int _counter; // ❌ 竞态条件

    public Task IncrementAsync()
    {
        _counter++; // ❌ 并发不安全
        return Task.CompletedTask;
    }
}
```

**修复**: 使用 `Interlocked`

```csharp
public class GoodService : ConcurrentServiceBase
{
    private long _counter; // ✅ 改为 long

    public Task IncrementAsync()
    {
        Interlocked.Increment(ref _counter); // ✅ 原子操作
        return Task.CompletedTask;
    }
}
```

### ❌ 错误 2: List 并发修改

```csharp
public class BadService : ConcurrentServiceBase
{
    private readonly List<string> _items = new(); // ❌ 不是线程安全的

    public Task AddItemAsync(string item)
    {
        _items.Add(item); // ❌ 可能抛出异常或丢失数据
        return Task.CompletedTask;
    }
}
```

**修复**: 使用 `ConcurrentBag` 或加锁

```csharp
public class GoodService : ConcurrentServiceBase
{
    private readonly ConcurrentBag<string> _items = new(); // ✅ 线程安全集合

    public Task AddItemAsync(string item)
    {
        _items.Add(item); // ✅ 线程安全
        return Task.CompletedTask;
    }
}
```

**或使用锁保护**:

```csharp
public class GoodService : ConcurrentServiceBase
{
    private readonly List<string> _items = new();
    private readonly object _lock = new();

    public Task AddItemAsync(string item)
    {
        lock (_lock)
        {
            _items.Add(item); // ✅ 锁保护
        }
        return Task.CompletedTask;
    }
}
```

### ❌ 错误 3: 延迟初始化无保护

```csharp
public class BadService : ConcurrentServiceBase
{
    private HeavyObject? _instance;

    public async Task<HeavyObject> GetInstanceAsync()
    {
        if (_instance == null) // ❌ 竞态条件
        {
            _instance = await CreateHeavyObjectAsync(); // 可能创建多次
        }
        return _instance;
    }
}
```

**修复**: 使用 `Lazy<T>` 或 `SemaphoreSlim`

```csharp
public class GoodService : ConcurrentServiceBase
{
    private readonly Lazy<Task<HeavyObject>> _instance;

    public GoodService(...)
    {
        _instance = new Lazy<Task<HeavyObject>>(
            () => CreateHeavyObjectAsync()); // ✅ 线程安全的延迟初始化
    }

    public Task<HeavyObject> GetInstanceAsync()
    {
        return _instance.Value; // ✅ 只创建一次
    }
}
```

---

## 性能优化建议

### 1. 选择合适的并发度

```csharp
// ❌ 过高：资源浪费，上下文切换开销
new ConcurrentServiceOptions { MaxConcurrency = 100 };

// ✅ 合理：IO 密集型使用 8-16
new ConcurrentServiceOptions { MaxConcurrency = 12 };

// ✅ 合理：CPU 密集型使用 CPU 核心数
ConcurrentServiceOptions.ForCPUIntensive;
```

### 2. 避免锁竞争

```csharp
// ❌ 粗粒度锁：所有操作都锁
private readonly object _lock = new();
public async Task Operation1Async()
{
    lock (_lock) { /* 大量代码 */ }
}

// ✅ 细粒度锁：只锁必要部分
private readonly object _lock = new();
public async Task Operation1Async()
{
    await DoSomeWorkAsync(); // 不需要锁

    lock (_lock) {
        _state = newValue; // 只锁临界区
    }
}
```

### 3. 使用异步 IO

```csharp
// ❌ 同步 IO：阻塞线程
public Task<Data> LoadDataAsync()
{
    return Task.FromResult(File.ReadAllText("data.json")); // 阻塞
}

// ✅ 异步 IO：释放线程
public async Task<Data> LoadDataAsync()
{
    return await File.ReadAllTextAsync("data.json"); // 异步
}
```

---

## 调试和监控

### 1. 日志输出

```csharp
public class MonitoredService : ConcurrentServiceBase
{
    public async Task ProcessAsync(int id)
    {
        Logger.LogInformation("Processing started - ID: {Id}, Thread: {ThreadId}",
            id, Environment.CurrentManagedThreadId);

        try
        {
            await DoWorkAsync(id);
            Logger.LogInformation("Processing completed - ID: {Id}", id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Processing failed - ID: {Id}", id);
            throw;
        }
    }
}
```

### 2. 统计信息

```csharp
public class StatsService : ConcurrentServiceBase
{
    private long _totalRequests;
    private long _concurrentRequests;

    public async Task<Response> HandleRequestAsync(Request req)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _concurrentRequests);

        try
        {
            return await ProcessRequestAsync(req);
        }
        finally
        {
            Interlocked.Decrement(ref _concurrentRequests);
        }
    }

    public Task<(long Total, long Concurrent)> GetStatsAsync()
    {
        return Task.FromResult((
            Interlocked.Read(ref _totalRequests),
            Interlocked.Read(ref _concurrentRequests)
        ));
    }
}
```

---

## BaseService vs ConcurrentServiceBase 对比

| 特性 | BaseService | ConcurrentServiceBase |
|------|-------------|----------------------|
| 处理模型 | 单线程串行 | 多线程并发 |
| 消息顺序 | 严格 FIFO（同优先级） | 不保证顺序 |
| 线程安全 | 无需加锁 | 需要保护共享状态 |
| 吞吐量 | 低（单线程） | 高（多线程） |
| 适用场景 | 有状态业务逻辑 | IO 密集型查询 |
| 状态管理 | 简单 | 复杂 |
| 调试难度 | 低 | 中到高 |

**推荐策略**:
- 默认使用 `BaseService`（更安全）
- IO 密集型场景才使用 `ConcurrentServiceBase`
- 有疑问时选择 `BaseService`

---

## 迁移检查清单

从 `BaseService` 迁移到 `ConcurrentServiceBase` 前，确认：

- [ ] 服务是 IO 密集型（数据库、网络、文件）
- [ ] 消息处理互不依赖，可并行执行
- [ ] 没有共享可变状态，或已用锁保护
- [ ] 所有集合使用线程安全版本（`Concurrent*`）
- [ ] 计数器使用 `Interlocked` 操作
- [ ] 已添加日志监控并发执行
- [ ] 已编写并发安全测试

---

## 测试并发安全

```csharp
[Fact]
public async Task ConcurrentService_ShouldBe_ThreadSafe()
{
    var service = CreateService();
    var tasks = new List<Task>();

    // 100 个并发请求
    for (int i = 0; i < 100; i++)
    {
        tasks.Add(Task.Run(async () =>
        {
            for (int j = 0; j < 10; j++)
            {
                await service.IncrementCounterAsync();
            }
        }));
    }

    await Task.WhenAll(tasks);

    // 验证结果正确性
    var count = await service.GetCountAsync();
    Assert.Equal(1000, count); // 应该正好是 1000
}
```

---

## 相关文档

- [PulseRPC.Server 架构设计深度分析](../../concepts/server-runtime.md)
- Service 方法优先级使用指南 - 旧独立文档当前仓库未提供

## 参考资料

- [Thread Safety in C#](https://docs.microsoft.com/en-us/dotnet/standard/threading/thread-safety)
- [Concurrent Collections](https://docs.microsoft.com/en-us/dotnet/standard/collections/thread-safe/)
- [Interlocked Operations](https://docs.microsoft.com/en-us/dotnet/api/system.threading.interlocked)
