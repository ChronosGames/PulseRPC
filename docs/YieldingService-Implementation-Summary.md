# YieldingService 实现总结

## ✅ 已完成实现

### 核心组件

#### 1. ServiceSynchronizationContext.cs
自定义同步上下文，拦截 `await` 的延续并重新排队。

**核心机制**：
```csharp
public override void Post(SendOrPostCallback d, object? state)
{
    // ✅ 关键：将 await 完成后的延续重新排队到消息队列
    _continuationWriter.TryWrite((d, state));
}
```

#### 2. YieldingServiceMessageQueue.cs
统一处理消息和延续的队列。

**核心设计**：
- 统一队列：`Channel<QueueItem>`
- 两种队列项：`MessageQueueItem`（新消息）和 `ContinuationQueueItem`（延续）
- 自动设置 `SynchronizationContext`

#### 3. YieldingService.cs
支持让出的 Service 基类。

**核心特性**：
- 继承此类即可获得自动让出能力
- 提供 `NoYieldAsync` 方法用于原子操作
- 完整的生命周期管理

---

## 🎯 工作原理

### 执行流程图

```
┌─────────────────────────────────────────────────────────────┐
│  客户端调用: GameHub.LoginAsync(request)                     │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│  步骤 1: 消息入队                                            │
│  MethodInvocationMessage → YieldingServiceMessageQueue      │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│  步骤 2: 队列线程取出消息                                    │
│  设置 SynchronizationContext = ServiceSynchronizationContext│
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│  步骤 3: 执行 LoginAsync                                     │
│  public async Task<LoginResponse> LoginAsync(...)           │
│  {                                                           │
│      // 在队列线程执行                                       │
│      Logger.Log("开始登录...");                              │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│  步骤 4: 遇到 await，自动让出队列                            │
│      var account = await _repository.GetByUserIdAsync(...); │
│                                                              │
│  → CLR 调用 SynchronizationContext.Post                     │
│  → 延续（await 后的代码）被排队                              │
│  → 队列线程释放，可以处理其他消息                            │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│  步骤 5: 队列处理其他消息                                    │
│  - HeartbeatAsync 立即执行                                   │
│  - GetCharacterListAsync 立即执行                            │
│  - ... 其他消息                                              │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│  步骤 6: IO 完成，延续被取出                                 │
│  ContinuationQueueItem 从队列取出                            │
│  执行延续（await 后的代码）                                  │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│  步骤 7: 继续执行 LoginAsync                                 │
│      // 回到队列线程，线程安全                                │
│      _playerStates[account.UserId] = new PlayerState();     │
│      return new LoginResponse { Success = true };           │
│  }                                                           │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│  步骤 8: 返回结果给客户端                                    │
└─────────────────────────────────────────────────────────────┘
```

---

## 💡 使用示例

### 示例 1：基础使用（自动让出）

```csharp
using DistributedGameApp.Infrastructure.MongoDB.Repositories;
using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Messages;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;

namespace DistributedGameApp.GameServer.Services;

/// <summary>
/// 游戏 Hub - 使用 YieldingService 自动让出队列
/// </summary>
public class GameHub : YieldingService, IGameHub
{
    private readonly AccountRepository _accountRepository;
    private readonly CharacterRepository _characterRepository;

    // 内部状态 - 只在队列线程访问，无需加锁
    private readonly Dictionary<string, PlayerState> _playerStates = new();

    public GameHub(
        AccountRepository accountRepository,
        CharacterRepository characterRepository,
        ILogger<GameHub> logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
        : base(logger, authenticationService, permissionValidator)
    {
        _accountRepository = accountRepository;
        _characterRepository = characterRepository;
    }

    /// <summary>
    /// 登录 - await 自动让出队列
    /// </summary>
    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        Logger.LogInformation("处理登录请求: {Account}", request.Account);

        // ✅ 第一个 await - 让出队列
        // 队列可以处理其他消息：HeartbeatAsync, GetCharacterListAsync 等
        var account = await _accountRepository.GetByUserIdAsync(request.Account);

        // ✅ IO 完成后自动回到队列线程
        if (account == null)
        {
            Logger.LogWarning("账号不存在: {Account}", request.Account);
            return new LoginResponse
            {
                Success = false,
                ErrorCode = 1001,
                ErrorMessage = "账号不存在"
            };
        }

        // ✅ 第二个 await - 再次让出队列
        var existingState = await LoadPlayerStateAsync(account.UserId);

        // ✅ 修改内部状态 - 线程安全（在队列线程）
        _playerStates[account.UserId] = new PlayerState
        {
            PlayerId = account.UserId,
            LoginTime = DateTimeOffset.UtcNow,
            Status = PlayerStatus.Online,
            PreviousState = existingState
        };

        Logger.LogInformation("玩家登录成功: {PlayerId}", account.UserId);

        return new LoginResponse
        {
            Success = true,
            PlayerId = account.UserId,
            AccessToken = GenerateToken(account),
            TokenExpireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        };
    }

    /// <summary>
    /// 获取角色列表 - await 自动让出
    /// </summary>
    public async Task<CharacterInfo[]> GetCharacterListAsync()
    {
        var caller = GetCurrentCaller();

        Logger.LogDebug("获取角色列表: {UserId}", caller.UserId);

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
        // 访问内部状态，线程安全（在队列线程）
        var count = _playerStates.Count;
        Logger.LogDebug("当前在线人数: {Count}", count);
        return Task.FromResult(count);
    }

    private async Task<PlayerState?> LoadPlayerStateAsync(string userId)
    {
        // 模拟从持久化存储加载状态
        await Task.Delay(10);
        return null;
    }

    private string GenerateToken(object account)
    {
        return "temp_token_" + Guid.NewGuid().ToString("N");
    }

    private CharacterInfo MapToCharacterInfo(object character)
    {
        // 映射逻辑
        return new CharacterInfo();
    }
}
```

### 示例 2：复杂业务流程（多步骤 await）

```csharp
/// <summary>
/// 创建角色 - 多步骤操作，每个 await 都让出队列
/// </summary>
public async Task<CharacterInfo> CreateCharacterAsync(CreateCharacterRequest request)
{
    var caller = GetCurrentCaller();

    Logger.LogInformation("创建角色: {CharacterName}, UserId: {UserId}",
        request.CharacterName, caller.UserId);

    // 步骤 1：验证账号（await 让出）
    var account = await _accountRepository.GetByUserIdAsync(caller.UserId!);
    if (account == null)
    {
        Logger.LogWarning("账号不存在: {UserId}", caller.UserId);
        throw new InvalidOperationException("账号不存在");
    }

    // 步骤 2：检查角色数量（await 让出）
    var existingCount = await _characterRepository.CountByUserIdAsync(caller.UserId!);
    if (existingCount >= 5)
    {
        Logger.LogWarning("角色数量已达上限: {UserId}, Count: {Count}",
            caller.UserId, existingCount);
        throw new InvalidOperationException("角色数量已达上限");
    }

    // 步骤 3：检查名称是否重复（await 让出）
    var nameExists = await _characterRepository.NameExistsAsync(request.CharacterName);
    if (nameExists)
    {
        Logger.LogWarning("角色名称已存在: {CharacterName}", request.CharacterName);
        throw new InvalidOperationException("角色名称已存在");
    }

    // 步骤 4：创建角色对象（队列线程，无 await）
    var character = new Character
    {
        CharacterId = Guid.NewGuid().ToString(),
        UserId = account.UserId,
        Name = request.CharacterName,
        Class = request.Class,
        Level = 1,
        CreatedAt = DateTime.UtcNow
    };

    // 步骤 5：分配初始属性（队列线程）
    character.Attributes = CalculateInitialAttributes(request.Class);

    // 步骤 6：保存到数据库（await 让出）
    await _characterRepository.InsertAsync(character);

    Logger.LogInformation("角色创建成功: {CharacterId}, Name: {CharacterName}",
        character.CharacterId, character.Name);

    // 步骤 7：返回结果（队列线程）
    return MapToCharacterInfo(character);
}

private CharacterAttributes CalculateInitialAttributes(CharacterClass characterClass)
{
    return characterClass switch
    {
        CharacterClass.Warrior => new CharacterAttributes { Hp = 150, Attack = 20, Defense = 15 },
        CharacterClass.Mage => new CharacterAttributes { Hp = 100, Attack = 30, Defense = 5 },
        CharacterClass.Archer => new CharacterAttributes { Hp = 120, Attack = 25, Defense = 10 },
        _ => new CharacterAttributes { Hp = 100, Attack = 15, Defense = 10 }
    };
}
```

### 示例 3：批量操作（并发 await）

```csharp
/// <summary>
/// 获取玩家完整信息 - 并发加载多个数据源
/// </summary>
public async Task<PlayerFullInfo> GetPlayerFullInfoAsync(string playerId)
{
    Logger.LogDebug("获取玩家完整信息: {PlayerId}", playerId);

    // ✅ 启动多个并发 IO 操作（每个都让出队列）
    var accountTask = _accountRepository.GetByUserIdAsync(playerId);
    var charactersTask = _characterRepository.GetByUserIdAsync(playerId);
    var friendsTask = _friendRepository.GetFriendsAsync(playerId);
    var mailCountTask = _mailRepository.GetUnreadCountAsync(playerId);

    // ✅ 等待所有任务完成（让出队列）
    // 在等待期间，队列可以处理其他消息
    await Task.WhenAll(accountTask, charactersTask, friendsTask, mailCountTask);

    // ✅ 恢复执行，组装结果（队列线程）
    var fullInfo = new PlayerFullInfo
    {
        Account = await accountTask,
        Characters = await charactersTask,
        Friends = await friendsTask,
        UnreadMailCount = await mailCountTask
    };

    Logger.LogDebug("玩家完整信息加载完成: {PlayerId}, CharacterCount: {Count}",
        playerId, fullInfo.Characters.Length);

    return fullInfo;
}
```

### 示例 4：原子操作（不让出队列）

```csharp
/// <summary>
/// 转账 - 必须原子执行，不能被打断
/// </summary>
public async Task<TransferResult> TransferGoldAsync(
    string fromPlayerId,
    string toPlayerId,
    int amount)
{
    Logger.LogInformation("转账: From={From}, To={To}, Amount={Amount}",
        fromPlayerId, toPlayerId, amount);

    // ✅ 使用 NoYieldAsync 确保原子执行
    // 警告：这会阻塞队列，谨慎使用
    return await NoYieldAsync(async () =>
    {
        // 在这个闭包内，所有 await 都不会让出队列

        // 步骤 1：扣除金币（不让出）
        var fromPlayer = await _playerRepository.GetAsync(fromPlayerId);
        if (fromPlayer.Gold < amount)
        {
            Logger.LogWarning("金币不足: PlayerId={PlayerId}, Gold={Gold}, Required={Amount}",
                fromPlayerId, fromPlayer.Gold, amount);
            return new TransferResult { Success = false, ErrorMessage = "金币不足" };
        }

        fromPlayer.Gold -= amount;
        await _playerRepository.UpdateAsync(fromPlayer);

        // 步骤 2：增加金币（不让出）
        var toPlayer = await _playerRepository.GetAsync(toPlayerId);
        toPlayer.Gold += amount;
        await _playerRepository.UpdateAsync(toPlayer);

        Logger.LogInformation("转账成功: From={From}, To={To}, Amount={Amount}",
            fromPlayerId, toPlayerId, amount);

        return new TransferResult
        {
            Success = true,
            FromPlayerNewGold = fromPlayer.Gold,
            ToPlayerNewGold = toPlayer.Gold
        };
    });
}
```

---

## 📊 性能对比

### 测试场景：混合负载

**10 个请求**：
- 3x LoginAsync (50ms IO)
- 3x GetCharacterListAsync (30ms IO)
- 4x HeartbeatAsync (<1ms)

| 指标 | BaseService | ExecuteIOAsync | YieldingService |
|------|-------------|----------------|-----------------|
| **总耗时** | 244ms | 54ms | 54ms |
| **吞吐量** | 41 req/s | 185 req/s | 185 req/s |
| **开发心智负担** | 低 | 中 | **极低** ⭐ |
| **线程安全** | ✅ | ✅ | ✅ |
| **代码复杂度** | 简单 | 需要包装 | **简单** ⭐ |

**结论**：YieldingService = 性能最优 + 开发体验最佳 🎯

---

## ⚡ 优缺点分析

### 优点 ✅

1. **零心智负担**：开发者按正常 async/await 写代码，无需手动包装
2. **自动优化**：框架自动处理让出和恢复，无需 ExecuteIOAsync
3. **线程安全**：恢复后仍在队列线程，保证 Actor 模型
4. **完全控制**：提供 NoYieldAsync 用于原子操作
5. **性能最优**：IO 期间队列不阻塞，吞吐量提升 4.3x

### 缺点 ⚠️

1. **实现复杂**：需要自定义 SynchronizationContext
2. **调试难度**：延续可能在不同时间点执行，调试时需要理解机制
3. **顺序不确定**：多个 await 的恢复顺序取决于 IO 完成时间
4. **兼容性要求**：必须使用 `ConfigureAwait(true)`（默认）

### 适用场景

| 场景 | 推荐方案 | 原因 |
|------|---------|------|
| 纯游戏逻辑（无 IO） | BaseService | 简单，无 IO 无需让出 |
| IO 密集 + 需要手动控制 | ExecuteIOAsync | 灵活，可选择性包装 |
| **IO 密集 + 零心智负担** | **YieldingService** ⭐ | **自动优化，最佳选择** |
| 有状态 + 大量 IO | **YieldingService** ⭐ | **线程安全 + 高吞吐** |

---

## 🚀 迁移指南

### Step 1：更换基类

```csharp
// ❌ 修改前
public class GameHub : BaseService, IGameHub
{
    public GameHub(
        ILogger<GameHub> logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
        : base(logger, authenticationService, permissionValidator)
    {
    }
}

// ✅ 修改后
public class GameHub : YieldingService, IGameHub  // ← 改为 YieldingService
{
    public GameHub(
        ILogger<GameHub> logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
        : base(logger, authenticationService, permissionValidator)
    {
    }
}
```

### Step 2：移除 ExecuteIOAsync 包装

```csharp
// ❌ 修改前 - 使用 ExecuteIOAsync 手动包装
public async Task<LoginResponse> LoginAsync(LoginRequest request)
{
    var account = await ExecuteIOAsync(() =>
        _accountRepository.GetByUserIdAsync(request.Account));

    _playerStates[account.UserId] = new PlayerState();
    return new LoginResponse { Success = true };
}

// ✅ 修改后 - 直接使用 await（自动让出）
public async Task<LoginResponse> LoginAsync(LoginRequest request)
{
    var account = await _accountRepository.GetByUserIdAsync(request.Account);

    _playerStates[account.UserId] = new PlayerState();
    return new LoginResponse { Success = true };
}
```

### Step 3：验证功能

```bash
# 运行测试
dotnet test

# 检查日志
# 确认延续正常处理
[Trace] Posting continuation #1 - Service: GameHub
[Trace] Processing continuation - Service: GameHub
[Trace] Continuation processed - Service: GameHub
```

### Step 4：性能对比

```bash
# 压力测试
dotnet run --project perf/BenchmarkApp

# 对比指标
- 吞吐量（req/s）：预期提升 2-5x
- P50/P95/P99 延迟：预期降低 40-70%
- 队列深度：预期显著降低
```

---

## 🎉 总结

### 核心价值

**YieldingService 是 PulseRPC.Server 的终极形态**：

1. ✅ **自动让出**：await 时自动让出队列执行权
2. ✅ **自动恢复**：IO 完成后自动回到队列线程
3. ✅ **线程安全**：恢复后仍在队列线程，保证 Actor 模型
4. ✅ **零心智负担**：开发者按正常 async/await 写代码
5. ✅ **完全控制**：可选的不让出机制（NoYieldAsync）

### 实现状态

- ✅ ServiceSynchronizationContext 实现完成
- ✅ YieldingServiceMessageQueue 实现完成
- ✅ YieldingService 基类实现完成
- ✅ NoYieldAsync 辅助方法实现完成
- ✅ 完整的日志和监控

### 推荐使用

**新项目**：直接使用 YieldingService

**现有项目**：
1. 短期（1 周）：使用 ExecuteIOAsync 快速优化热点
2. 中期（1 月）：逐步迁移到 YieldingService
3. 长期（标准）：所有 IO 密集 Service 使用 YieldingService

**YieldingService = 性能最优 + 开发体验最佳！** 🎯
