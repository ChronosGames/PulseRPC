# Service 队列让出机制设计方案

> 文档状态：历史设计方案。当前服务执行模型以 `PulseServiceBase`、`ServiceExecutionOptions` 和 `Processing/Engine` 为准；本文中的 `BaseService`/`YieldingService` 对比保留为早期设计讨论。

## 🎯 核心理念

**让 Service 在 await 时自动让出队列执行权，IO 完成后自动重新获得执行权**

### 当前问题

```csharp
// ❌ 当前：await 阻塞整个队列
public async Task<LoginResponse> LoginAsync(LoginRequest request)
{
    // await 时，队列被阻塞
    var account = await accountRepository.GetByUserIdAsync(request.Account);
    // 其他消息（HeartbeatAsync）必须等待

    _playerStates[account.UserId] = new PlayerState();
    return new LoginResponse { Success = true };
}
```

**问题**：
- 即使使用 `async/await`，方法仍然占用消息队列线程
- 后续消息无法处理，吞吐量低

### 期望行为

```csharp
// ✅ 期望：await 自动让出队列
public async Task<LoginResponse> LoginAsync(LoginRequest request)
{
    // 步骤 1：开始执行（在队列线程）

    // 步骤 2：await 时自动让出队列执行权
    var account = await accountRepository.GetByUserIdAsync(request.Account);
    // 队列可以处理其他消息：HeartbeatAsync, GetCharacterListAsync 等

    // 步骤 3：IO 完成后，自动重新排队，恢复执行
    _playerStates[account.UserId] = new PlayerState();  // 仍然是线程安全的
    return new LoginResponse { Success = true };
}
```

**优势**：
- ✅ **自动让出**：无需手动调用 `ExecuteIOAsync`
- ✅ **自动恢复**：IO 完成后自动回到队列线程
- ✅ **线程安全**：恢复后仍在同一逻辑线程
- ✅ **零心智负担**：开发者按正常 async/await 写代码

---

## 🏗️ 实现方案

### 方案 1：自定义 SynchronizationContext（推荐）⭐

#### 核心原理

通过自定义 `SynchronizationContext`，拦截 `await` 的延续（continuation），将其重新排队到消息队列。

#### 实现

```csharp
/// <summary>
/// Service 同步上下文 - 让 await 自动让出队列执行权
/// </summary>
public class ServiceSynchronizationContext : SynchronizationContext
{
    private readonly Channel<(SendOrPostCallback Callback, object? State)> _continuationQueue;
    private readonly string _serviceName;
    private readonly PID _servicePID;
    private readonly ILogger _logger;

    public ServiceSynchronizationContext(
        Channel<(SendOrPostCallback, object?)> continuationQueue,
        string serviceName,
        PID servicePID,
        ILogger logger)
    {
        _continuationQueue = continuationQueue;
        _serviceName = serviceName;
        _servicePID = servicePID;
        _logger = logger;
    }

    /// <summary>
    /// Post 方法会在 await 完成后被调用，用于调度延续
    /// </summary>
    public override void Post(SendOrPostCallback d, object? state)
    {
        // ✅ 关键：将延续重新排队到消息队列
        if (!_continuationQueue.Writer.TryWrite((d, state)))
        {
            _logger.LogWarning(
                "Failed to enqueue continuation - Service: {ServiceName}, PID: {PID}",
                _serviceName, _servicePID);

            // 回退到线程池执行
            ThreadPool.QueueUserWorkItem(_ => d(state), null);
        }
    }

    /// <summary>
    /// Send 方法（同步执行）- 直接在当前线程执行
    /// </summary>
    public override void Send(SendOrPostCallback d, object? state)
    {
        d(state);
    }

    /// <summary>
    /// 创建副本
    /// </summary>
    public override SynchronizationContext CreateCopy()
    {
        return new ServiceSynchronizationContext(
            _continuationQueue,
            _serviceName,
            _servicePID,
            _logger);
    }
}
```

#### 消息队列集成

```csharp
/// <summary>
/// 支持让出的消息队列
/// </summary>
public class YieldingServiceMessageQueue
{
    private readonly Channel<ServiceMessage> _messageQueue;
    private readonly Channel<(SendOrPostCallback, object?)> _continuationQueue;
    private readonly ServiceSynchronizationContext _syncContext;
    private readonly ILogger _logger;

    public YieldingServiceMessageQueue(
        string serviceName,
        PID servicePID,
        ILogger logger)
    {
        _logger = logger;

        // 主消息队列
        _messageQueue = Channel.CreateUnbounded<ServiceMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        // 延续队列（await 完成后的回调）
        _continuationQueue = Channel.CreateUnbounded<(SendOrPostCallback, object?)>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        // 创建自定义同步上下文
        _syncContext = new ServiceSynchronizationContext(
            _continuationQueue,
            serviceName,
            servicePID,
            logger);
    }

    /// <summary>
    /// 启动消息处理循环
    /// </summary>
    public void Start(Func<ServiceMessage, Task> messageHandler, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            // ✅ 设置同步上下文
            SynchronizationContext.SetSynchronizationContext(_syncContext);

            await ProcessMessagesAsync(messageHandler, cancellationToken);
        }, cancellationToken);
    }

    private async Task ProcessMessagesAsync(
        Func<ServiceMessage, Task> messageHandler,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // ✅ 使用 Select 同时等待消息和延续
            var selectedChannel = await Task.WhenAny(
                WaitForMessageAsync(cancellationToken),
                WaitForContinuationAsync(cancellationToken)
            );

            if (selectedChannel == await WaitForMessageAsync(cancellationToken))
            {
                // 处理新消息
                if (_messageQueue.Reader.TryRead(out var message))
                {
                    await ProcessMessageAsync(message, messageHandler);
                }
            }
            else
            {
                // 处理延续（await 完成后的回调）
                if (_continuationQueue.Reader.TryRead(out var continuation))
                {
                    ProcessContinuation(continuation);
                }
            }
        }
    }

    private Task<bool> WaitForMessageAsync(CancellationToken cancellationToken)
    {
        return _messageQueue.Reader.WaitToReadAsync(cancellationToken).AsTask();
    }

    private Task<bool> WaitForContinuationAsync(CancellationToken cancellationToken)
    {
        return _continuationQueue.Reader.WaitToReadAsync(cancellationToken).AsTask();
    }

    private async Task ProcessMessageAsync(ServiceMessage message, Func<ServiceMessage, Task> handler)
    {
        try
        {
            _logger.LogDebug("Processing message: {MessageType}", message.GetType().Name);

            // ✅ 在设置了 SynchronizationContext 的环境下执行
            await handler(message);

            _logger.LogDebug("Message processed: {MessageType}", message.GetType().Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message: {MessageType}", message.GetType().Name);
        }
    }

    private void ProcessContinuation((SendOrPostCallback Callback, object? State) continuation)
    {
        try
        {
            _logger.LogDebug("Processing continuation");

            // ✅ 执行延续（await 完成后的代码）
            continuation.Callback(continuation.State);

            _logger.LogDebug("Continuation processed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing continuation");
        }
    }

    public ValueTask<bool> SendMessageAsync(ServiceMessage message)
    {
        return _messageQueue.Writer.WriteAsync(message);
    }
}
```

#### YieldingService 基类

```csharp
/// <summary>
/// 支持让出的 Service 基类
/// </summary>
public abstract class YieldingService : IService, IPulseHub
{
    public PID ServicePID { get; private set; }
    protected readonly ILogger Logger;

    private YieldingServiceMessageQueue? _messageQueue;
    private readonly IAuthenticationService _authenticationService;
    private readonly PermissionValidator _permissionValidator;
    private string? _serviceSecret;

    protected YieldingService(
        ILogger logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
    {
        Logger = logger;
        _authenticationService = authenticationService;
        _permissionValidator = permissionValidator;
    }

    internal void SetPID(PID pid)
    {
        ServicePID = pid;
        _serviceSecret = _authenticationService.GenerateServiceSecret(pid);

        // 创建支持让出的消息队列
        _messageQueue = new YieldingServiceMessageQueue(
            GetType().Name,
            pid,
            Logger);

        _messageQueue.Start(ProcessMessageAsync, CancellationToken.None);

        Logger.LogInformation(
            "YieldingService initialized - Service: {ServiceName}, PID: {PID}",
            GetType().Name, pid);
    }

    private async Task ProcessMessageAsync(ServiceMessage message)
    {
        if (message is MethodInvocationMessage methodMsg)
        {
            await ProcessMethodInvocationAsync(methodMsg);
        }
    }

    private async Task ProcessMethodInvocationAsync(MethodInvocationMessage message)
    {
        try
        {
            var methodInfo = GetMethodInfo(message.ProtocolId);
            if (methodInfo == null)
            {
                throw new InvalidOperationException(
                    $"Method not found for ProtocolId {message.ProtocolId}");
            }

            // ✅ 执行方法（await 会自动让出队列）
            var result = await CompiledAsyncMethodInvoker.InvokeAsync(
                this, methodInfo, message.Arguments);

            message.CompletionSource.TrySetResult(result);
        }
        catch (Exception ex)
        {
            message.CompletionSource.TrySetException(ex);
        }
    }

    // 其他方法...
}
```

---

## 💡 使用示例

### 示例 1：基础使用（自动让出）

```csharp
/// <summary>
/// 游戏 Hub - 继承 YieldingService
/// </summary>
public class GameHub : YieldingService, IGameHub
{
    private readonly AccountRepository _accountRepository;
    private readonly CharacterRepository _characterRepository;

    // 内部状态（只在队列线程访问）
    private readonly Dictionary<string, PlayerState> _playerStates = new();

    /// <summary>
    /// 登录 - await 自动让出队列
    /// </summary>
    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        Logger.LogInformation("处理登录请求: {Account}", request.Account);

        // ✅ await 时自动让出队列执行权
        // 队列可以处理其他消息：HeartbeatAsync, GetCharacterListAsync 等
        var account = await _accountRepository.GetByUserIdAsync(request.Account);

        // ✅ IO 完成后，自动回到队列线程，继续执行
        // 此时仍然是线程安全的，可以安全修改 _playerStates
        if (account == null)
        {
            Logger.LogWarning("账号不存在: {Account}", request.Account);
            return new LoginResponse
            {
                Success = false,
                ErrorMessage = "账号不存在"
            };
        }

        // ✅ 修改内部状态 - 线程安全
        _playerStates[account.UserId] = new PlayerState
        {
            PlayerId = account.UserId,
            LoginTime = DateTimeOffset.UtcNow,
            Status = PlayerStatus.Online
        };

        Logger.LogInformation("玩家登录成功: {PlayerId}", account.UserId);

        return new LoginResponse
        {
            Success = true,
            PlayerId = account.UserId,
            AccessToken = GenerateToken(account)
        };
    }

    /// <summary>
    /// 获取角色列表 - await 自动让出
    /// </summary>
    public async Task<CharacterInfo[]> GetCharacterListAsync()
    {
        var caller = GetCurrentCaller();

        // ✅ 数据库查询时让出队列
        var characters = await _characterRepository.GetByUserIdAsync(caller.UserId!);

        // ✅ 恢复后处理结果
        return characters.Select(MapToCharacterInfo).ToArray();
    }

    /// <summary>
    /// 心跳 - 纯内存操作，不需要 await
    /// </summary>
    public Task<long> HeartbeatAsync()
    {
        // 纯内存操作，立即返回
        return Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    /// <summary>
    /// 获取在线人数 - 读取内部状态
    /// </summary>
    public Task<int> GetOnlineCountAsync()
    {
        // 访问内部状态，线程安全
        return Task.FromResult(_playerStates.Count);
    }
}
```

### 示例 2：多步骤操作（自动协调）

```csharp
public class GameHub : YieldingService
{
    /// <summary>
    /// 复杂业务流程 - 多次 await，每次都让出队列
    /// </summary>
    public async Task<CharacterInfo> CreateCharacterAsync(CreateCharacterRequest request)
    {
        var caller = GetCurrentCaller();

        // 步骤 1：验证账号（await 让出）
        var account = await _accountRepository.GetByUserIdAsync(caller.UserId!);
        if (account == null)
            throw new InvalidOperationException("账号不存在");

        // 步骤 2：检查角色数量（await 让出）
        var existingCount = await _characterRepository.CountByUserIdAsync(caller.UserId!);
        if (existingCount >= 5)
            throw new InvalidOperationException("角色数量已达上限");

        // 步骤 3：创建角色对象（队列线程，无 await）
        var character = new Character
        {
            CharacterId = Guid.NewGuid().ToString(),
            UserId = account.UserId,
            Name = request.CharacterName,
            Class = request.Class,
            Level = 1
        };

        // 步骤 4：保存到数据库（await 让出）
        await _characterRepository.InsertAsync(character);

        // 步骤 5：返回结果（队列线程）
        Logger.LogInformation("角色创建成功: {CharacterId}", character.CharacterId);
        return MapToCharacterInfo(character);
    }
}
```

### 示例 3：批量操作（多个 await）

```csharp
public class GameHub : YieldingService
{
    /// <summary>
    /// 获取玩家完整信息 - 多个 await，每个都让出
    /// </summary>
    public async Task<PlayerFullInfo> GetPlayerFullInfoAsync(string playerId)
    {
        // 并发加载多个数据源（每个 await 都让出）
        var accountTask = _accountRepository.GetByUserIdAsync(playerId);
        var charactersTask = _characterRepository.GetByUserIdAsync(playerId);
        var friendsTask = _friendRepository.GetFriendsAsync(playerId);
        var mailCountTask = _mailRepository.GetUnreadCountAsync(playerId);

        // 等待所有任务完成（让出队列，直到全部完成）
        await Task.WhenAll(accountTask, charactersTask, friendsTask, mailCountTask);

        // 恢复执行，组装结果
        return new PlayerFullInfo
        {
            Account = await accountTask,
            Characters = await charactersTask,
            Friends = await friendsTask,
            UnreadMailCount = await mailCountTask
        };
    }
}
```

---

## 🔒 不让出队列的机制

### 场景：原子操作（不能被打断）

```csharp
/// <summary>
/// 原子操作 - 不让出队列
/// </summary>
public async Task<bool> TransferGoldAsync(string fromPlayerId, string toPlayerId, int amount)
{
    // ⚠️ 这个操作必须原子执行，不能被打断

    // 方案 1：使用 ConfigureAwait(false) - 但这会失去同步上下文
    // ❌ 不推荐：会在线程池线程执行，失去线程安全保证

    // 方案 2：使用自定义 Awaiter
    await NoYieldAsync(async () =>
    {
        // 扣除金币（不让出）
        var fromPlayer = await _playerRepository.GetAsync(fromPlayerId);
        fromPlayer.Gold -= amount;
        await _playerRepository.UpdateAsync(fromPlayer);

        // 增加金币（不让出）
        var toPlayer = await _playerRepository.GetAsync(toPlayerId);
        toPlayer.Gold += amount;
        await _playerRepository.UpdateAsync(toPlayer);
    });

    return true;
}

/// <summary>
/// 不让出队列的辅助方法
/// </summary>
protected async Task<T> NoYieldAsync<T>(Func<Task<T>> operation)
{
    // 临时切换到不让出的同步上下文
    var previousContext = SynchronizationContext.Current;

    try
    {
        // 使用线程池的同步上下文（不会重新排队）
        SynchronizationContext.SetSynchronizationContext(null);

        return await operation();
    }
    finally
    {
        // 恢复原来的同步上下文
        SynchronizationContext.SetSynchronizationContext(previousContext);
    }
}
```

### 方案 2：特性标记（声明式）

```csharp
/// <summary>
/// 原子执行特性 - 整个方法不让出队列
/// </summary>
[AtomicExecution]
public async Task<bool> TransferGoldAsync(string fromPlayerId, string toPlayerId, int amount)
{
    // Source Generator 会生成包装代码，确保整个方法在 NoYieldAsync 中执行

    var fromPlayer = await _playerRepository.GetAsync(fromPlayerId);
    fromPlayer.Gold -= amount;
    await _playerRepository.UpdateAsync(fromPlayer);

    var toPlayer = await _playerRepository.GetAsync(toPlayerId);
    toPlayer.Gold += amount;
    await _playerRepository.UpdateAsync(toPlayer);

    return true;
}
```

---

## 📊 性能对比

### 场景：混合负载（LoginAsync + HeartbeatAsync）

#### 优化前（BaseService - 阻塞队列）

```
时间线：
0ms:   LoginAsync 开始执行
0ms:   await 数据库查询（阻塞队列 50ms）
50ms:  LoginAsync 完成
50ms:  HeartbeatAsync 开始执行
51ms:  HeartbeatAsync 完成

总耗时: 51ms
吞吐量: 2 req / 0.051s = 39.2 req/s
```

#### 使用 ExecuteIOAsync（手动转发）

```
时间线：
0ms:   LoginAsync 开始执行
0ms:   ExecuteIOAsync 转到线程池
0ms:   HeartbeatAsync 立即执行
1ms:   HeartbeatAsync 完成
50ms:  LoginAsync 完成

总耗时: 50ms
吞吐量: 2 req / 0.05s = 40 req/s
```

#### 使用 YieldingService（自动让出）⭐

```
时间线：
0ms:   LoginAsync 开始执行
0ms:   await 数据库查询（自动让出队列）
0ms:   HeartbeatAsync 立即执行（队列未阻塞）
1ms:   HeartbeatAsync 完成
50ms:  数据库查询完成，LoginAsync 延续排队
50ms:  LoginAsync 恢复执行并完成

总耗时: 50ms
吞吐量: 2 req / 0.05s = 40 req/s
线程安全: ✅ 保证（LoginAsync 恢复后仍在队列线程）
```

### 复杂场景：10 个混合请求

| 请求类型 | 数量 | BaseService | ExecuteIOAsync | YieldingService |
|---------|------|------------|----------------|-----------------|
| LoginAsync (50ms) | 3 | 150ms | 50ms | 50ms |
| GetCharacterListAsync (30ms) | 3 | 90ms | 30ms | 30ms |
| HeartbeatAsync (<1ms) | 4 | 4ms | 4ms | 4ms |

**总耗时**：
- BaseService: 244ms（串行）
- ExecuteIOAsync: 54ms（IO 并发，但需要手动包装）
- YieldingService: 54ms（IO 自动并发，无需手动操作）⭐

**开发体验**：
- BaseService: 简单，但性能差
- ExecuteIOAsync: 需要手动包装每个 IO
- YieldingService: **最佳**，自动优化，开发者无感知

---

## ⚡ 优缺点分析

### 优点 ✅

1. **零心智负担**：开发者按正常 async/await 写代码
2. **自动优化**：框架自动处理让出和恢复
3. **线程安全**：恢复后仍在队列线程，保证 Actor 模型
4. **完全控制**：可选的不让出机制（原子操作）
5. **性能最优**：IO 期间队列不阻塞

### 缺点 ⚠️

1. **实现复杂**：需要自定义 SynchronizationContext
2. **调试困难**：延续可能在不同时间点执行
3. **顺序不确定**：多个 await 的恢复顺序取决于 IO 完成时间
4. **兼容性**：需要确保所有 await 都使用 `ConfigureAwait(true)`（默认）

### 适用场景

| 场景 | 推荐方案 |
|------|---------|
| 纯游戏逻辑（无 IO） | BaseService |
| IO 密集 + 需要手动控制 | ExecuteIOAsync |
| **IO 密集 + 零心智负担** | **YieldingService** ⭐ |
| 有状态 + 大量 IO | **YieldingService** ⭐ |

---

## 🚀 实施计划

### Phase 1：核心实现（1-2 周）

- [ ] 实现 `ServiceSynchronizationContext`
- [ ] 实现 `YieldingServiceMessageQueue`
- [ ] 实现 `YieldingService` 基类
- [ ] 单元测试验证

### Phase 2：辅助功能（1 周）

- [ ] 实现 `NoYieldAsync` 不让出机制
- [ ] 实现 `[AtomicExecution]` 特性
- [ ] 性能监控和日志

### Phase 3：示例和文档（1 周）

- [ ] 更新 DistributedGameApp 示例
- [ ] 编写迁移指南
- [ ] 性能基准测试

### Phase 4：生产就绪（1 周）

- [ ] 完整测试覆盖
- [ ] 边界情况处理
- [ ] 性能优化

---

## 🎉 总结

### 核心价值

**YieldingService 是最优雅的解决方案**：

1. ✅ **自动让出**：await 时自动让出队列执行权
2. ✅ **自动恢复**：IO 完成后自动回到队列线程
3. ✅ **线程安全**：恢复后仍在队列线程，保证 Actor 模型
4. ✅ **零心智负担**：开发者按正常 async/await 写代码
5. ✅ **完全控制**：可选的不让出机制（原子操作）

### 与其他方案对比

| 方案 | 心智负担 | 性能 | 线程安全 | 灵活性 | 推荐度 |
|------|---------|------|---------|--------|--------|
| BaseService | 低 | 差 | ✅ | 低 | ⭐⭐ |
| ExecuteIOAsync | 中 | 好 | ✅ | 高 | ⭐⭐⭐⭐ |
| **YieldingService** | **极低** | **最优** | **✅** | **高** | **⭐⭐⭐⭐⭐** |

### 推荐使用

**新项目**：直接使用 YieldingService
**现有项目**：
1. 短期：使用 ExecuteIOAsync 快速优化
2. 长期：迁移到 YieldingService

**YieldingService 是 PulseRPC.Server 的终极形态！** 🎯
