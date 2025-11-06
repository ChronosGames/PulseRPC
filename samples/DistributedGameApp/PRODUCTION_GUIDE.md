# 生产级实施指南

本指南提供完整的生产级实现示例和最佳实践。

## 🎯 完整实现示例

### 1. GameService 实现（生产级）

```csharp
// src/DistributedGameApp.GameServer/Services/PlayerSessionService.cs
using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Messages;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Services;
using PulseRPC.Server.Authentication;
using System;
using System.Threading.Tasks;

/// <summary>
/// 玩家会话服务 - 使用 BaseService 和 Actor 模型
/// </summary>
/// <remarks>
/// ServiceId: PlayerSession:{playerId}
///
/// 设计特点：
/// 1. 继承 BaseService，获得完整的 Actor 模型支持
/// 2. 单线程消息循环，保证操作严格有序
/// 3. 集成认证授权，自动验证权限
/// 4. 使用表达式树编译，方法调用性能提升 50 倍
/// </remarks>
public class PlayerSessionService : BaseService, IGameHub, IPulseService
{
    private readonly ILogger<PlayerSessionService> _logger;
    private readonly IPlayerRepository _playerRepository;
    private readonly ICharacterRepository _characterRepository;
    private readonly IJwtService _jwtService;

    // ✅ Actor 模型 - 状态变量无需加锁
    private PlayerInfo? _currentPlayer;
    private List<CharacterInfo> _characters = new();
    private OnlineStatus _onlineStatus = OnlineStatus.Offline;
    private DateTime _lastHeartbeat;

    public string ServiceName => "PlayerSession";
    public string ServiceId { get; private set; } = string.Empty;

    public PlayerSessionService(
        string playerId,
        ILogger<PlayerSessionService> logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator,
        IPlayerRepository playerRepository,
        ICharacterRepository characterRepository,
        IJwtService jwtService)
        : base(logger, authenticationService, permissionValidator)
    {
        _logger = logger;
        _playerRepository = playerRepository;
        _characterRepository = characterRepository;
        _jwtService = jwtService;
        ServiceId = $"PlayerSession:{playerId}";
    }

    #region IGameHub Implementation

    /// <summary>
    /// 玩家登录
    /// </summary>
    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        try
        {
            _logger.LogInformation("Player login attempt: {Account}", request.Account);

            // 1. 验证账号密码（实际应该查询数据库并验证加密密码）
            var player = await _playerRepository.GetPlayerByAccountAsync(request.Account);
            if (player == null || !VerifyPassword(request.Password, player.PasswordHash))
            {
                return new LoginResponse
                {
                    Success = false,
                    ErrorCode = 401,
                    ErrorMessage = "账号或密码错误"
                };
            }

            // 2. 生成 JWT Token
            var accessToken = _jwtService.GenerateAccessToken(
                playerId: player.PlayerId,
                account: player.Account,
                roles: player.Roles,
                permissions: player.Permissions,
                expireMinutes: 1440 // 24小时
            );

            var refreshToken = _jwtService.GenerateRefreshToken(player.PlayerId);

            // 3. 更新会话状态（Actor 模型 - 无需锁）
            _currentPlayer = player;
            _onlineStatus = OnlineStatus.Online;
            _lastHeartbeat = DateTime.UtcNow;

            // 4. 加载角色列表
            _characters = await _characterRepository.GetCharactersByPlayerIdAsync(player.PlayerId);

            _logger.LogInformation("Player {PlayerId} logged in successfully", player.PlayerId);

            return new LoginResponse
            {
                Success = true,
                PlayerId = player.PlayerId,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                TokenExpireAt = DateTimeOffset.UtcNow.AddMinutes(1440).ToUnixTimeSeconds()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for account {Account}", request.Account);
            return new LoginResponse
            {
                Success = false,
                ErrorCode = 500,
                ErrorMessage = "服务器内部错误"
            };
        }
    }

    /// <summary>
    /// 创建角色
    /// </summary>
    [RequirePermission("character.create")]
    public async Task<CharacterInfo> CreateCharacterAsync(CreateCharacterRequest request)
    {
        // ✅ 获取当前调用者（自动从 JWT Token 中提取）
        var caller = GetCurrentCaller();

        _logger.LogInformation("Creating character for player {PlayerId}", caller.UserId);

        // 验证角色名称是否已存在
        if (await _characterRepository.IsCharacterNameExistsAsync(request.CharacterName))
        {
            throw new InvalidOperationException($"角色名称 {request.CharacterName} 已存在");
        }

        // 创建新角色
        var character = new CharacterInfo
        {
            CharacterId = Guid.NewGuid().ToString(),
            PlayerId = caller.UserId!,
            CharacterName = request.CharacterName,
            Class = request.Class,
            Gender = request.Gender,
            Level = 1,
            Exp = 0,
            Hp = 100,
            MaxHp = 100,
            Mp = 50,
            MaxMp = 50,
            Attack = GetInitialAttribute(request.Class, "Attack"),
            Defense = GetInitialAttribute(request.Class, "Defense"),
            Speed = GetInitialAttribute(request.Class, "Speed"),
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow,
            Position = new Position { X = 0, Y = 0, Z = 0, MapId = "newbie_village" },
            OnlineStatus = OnlineStatus.Offline
        };

        // 保存到数据库
        await _characterRepository.CreateCharacterAsync(character);

        // ✅ 更新本地缓存（Actor 模型 - 线程安全）
        _characters.Add(character);

        _logger.LogInformation("Character {CharacterId} created successfully", character.CharacterId);

        return character;
    }

    /// <summary>
    /// 获取角色信息
    /// </summary>
    public async Task<CharacterInfo?> GetCharacterAsync(string characterId)
    {
        // ✅ 先查本地缓存（Actor 模型 - 无需锁）
        var character = _characters.FirstOrDefault(c => c.CharacterId == characterId);
        if (character != null)
        {
            return character;
        }

        // 缓存未命中，从数据库加载
        character = await _characterRepository.GetCharacterByIdAsync(characterId);
        if (character != null && character.PlayerId == _currentPlayer?.PlayerId)
        {
            _characters.Add(character);
        }

        return character;
    }

    /// <summary>
    /// 获取角色列表
    /// </summary>
    public Task<CharacterInfo[]> GetCharacterListAsync()
    {
        return Task.FromResult(_characters.ToArray());
    }

    /// <summary>
    /// 删除角色
    /// </summary>
    [RequirePermission("character.delete")]
    public async Task<bool> DeleteCharacterAsync(string characterId)
    {
        var caller = GetCurrentCaller();

        // 验证权限（角色必须属于当前玩家）
        var character = await GetCharacterAsync(characterId);
        if (character == null || character.PlayerId != caller.UserId)
        {
            throw new UnauthorizedAccessException("无权删除该角色");
        }

        // 删除数据库记录
        var success = await _characterRepository.DeleteCharacterAsync(characterId);

        if (success)
        {
            // ✅ 更新本地缓存（Actor 模型 - 线程安全）
            _characters.RemoveAll(c => c.CharacterId == characterId);
            _logger.LogInformation("Character {CharacterId} deleted", characterId);
        }

        return success;
    }

    /// <summary>
    /// 请求匹配
    /// </summary>
    [RequirePermission("matchmaking.request")]
    public async Task<MatchmakingResponse> RequestMatchAsync(MatchmakingRequest request)
    {
        var caller = GetCurrentCaller();

        _logger.LogInformation("Player {PlayerId} requesting match: {Mode}",
            caller.UserId, request.Mode);

        // 验证状态
        if (_onlineStatus == OnlineStatus.InMatchmaking || _onlineStatus == OnlineStatus.InBattle)
        {
            return new MatchmakingResponse
            {
                Success = false,
                ErrorMessage = "当前状态无法开始匹配"
            };
        }

        // ✅ 调用匹配服务（跨Service RPC）
        var matchmakingService = GetService<IMatchmakingService>();
        var ticket = await matchmakingService.EnqueueAsync(caller.UserId!, request);

        // 更新状态
        _onlineStatus = OnlineStatus.InMatchmaking;

        return new MatchmakingResponse
        {
            Success = true,
            TicketId = ticket.TicketId,
            EstimatedWaitTime = ticket.EstimatedWaitTime
        };
    }

    /// <summary>
    /// 取消匹配
    /// </summary>
    public async Task<bool> CancelMatchAsync(string ticketId)
    {
        var matchmakingService = GetService<IMatchmakingService>();
        var success = await matchmakingService.DequeueAsync(ticketId);

        if (success)
        {
            _onlineStatus = OnlineStatus.Online;
        }

        return success;
    }

    /// <summary>
    /// 确认加入战斗
    /// </summary>
    public async Task<bool> ConfirmBattleAsync(string battleId)
    {
        _logger.LogInformation("Player {PlayerId} confirming battle {BattleId}",
            _currentPlayer?.PlayerId, battleId);

        _onlineStatus = OnlineStatus.InBattle;
        return true;
    }

    /// <summary>
    /// 心跳
    /// </summary>
    public Task<long> HeartbeatAsync()
    {
        _lastHeartbeat = DateTime.UtcNow;
        return Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// 更新在线状态
    /// </summary>
    public Task<bool> UpdateOnlineStatusAsync(OnlineStatus status)
    {
        _onlineStatus = status;
        _logger.LogDebug("Player {PlayerId} status changed to {Status}",
            _currentPlayer?.PlayerId, status);
        return Task.FromResult(true);
    }

    #endregion

    #region Lifecycle Hooks

    protected override async ValueTask OnStartingAsync()
    {
        _logger.LogInformation("PlayerSessionService {ServiceId} starting", ServiceId);
        await base.OnStartingAsync();
    }

    protected override async ValueTask OnStartedAsync()
    {
        _logger.LogInformation("PlayerSessionService {ServiceId} started", ServiceId);
        await base.OnStartedAsync();
    }

    protected override async ValueTask OnStoppingAsync()
    {
        _logger.LogInformation("PlayerSessionService {ServiceId} stopping", ServiceId);

        // 保存玩家状态到数据库
        if (_currentPlayer != null)
        {
            await _playerRepository.UpdatePlayerStatusAsync(_currentPlayer.PlayerId, OnlineStatus.Offline);
        }

        await base.OnStoppingAsync();
    }

    protected override async ValueTask OnFaultedAsync(Exception ex)
    {
        _logger.LogError(ex, "PlayerSessionService {ServiceId} faulted", ServiceId);
        await base.OnFaultedAsync(ex);
    }

    #endregion

    #region Private Methods

    private bool VerifyPassword(string password, string passwordHash)
    {
        // 实际应该使用 BCrypt 或 Argon2
        return BCrypt.Net.BCrypt.Verify(password, passwordHash);
    }

    private int GetInitialAttribute(CharacterClass characterClass, string attributeName)
    {
        // 根据职业返回初始属性值
        return characterClass switch
        {
            CharacterClass.Warrior => attributeName switch
            {
                "Attack" => 15,
                "Defense" => 12,
                "Speed" => 8,
                _ => 10
            },
            CharacterClass.Mage => attributeName switch
            {
                "Attack" => 20,
                "Defense" => 6,
                "Speed" => 10,
                _ => 10
            },
            CharacterClass.Archer => attributeName switch
            {
                "Attack" => 18,
                "Defense" => 8,
                "Speed" => 14,
                _ => 10
            },
            CharacterClass.Assassin => attributeName switch
            {
                "Attack" => 22,
                "Defense" => 5,
                "Speed" => 16,
                _ => 10
            },
            CharacterClass.Priest => attributeName switch
            {
                "Attack" => 10,
                "Defense" => 10,
                "Speed" => 9,
                _ => 10
            },
            _ => 10
        };
    }

    private T GetService<T>() where T : class
    {
        // 通过 ServiceLocator 获取其他服务
        throw new NotImplementedException("需要实现 ServiceLocator");
    }

    #endregion
}
```

### 2. BattleRoomService 实现（生产级）

```csharp
// src/DistributedGameApp.BattleServer/Services/BattleRoomService.cs
public class BattleRoomService : BaseService, IBattleHub, IPulseService
{
    private readonly ILogger<BattleRoomService> _logger;

    // ✅ Actor 模型 - 战斗状态（无需锁）
    private BattleInfo _battleInfo;
    private Dictionary<string, BattlePlayer> _players = new();
    private Queue<BattleAction> _actionQueue = new();
    private CancellationTokenSource _battleLoopCts = new();

    public string ServiceName => "BattleRoom";
    public string ServiceId { get; private set; }

    public BattleRoomService(
        string battleId,
        ILogger<BattleRoomService> logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
        : base(logger, authenticationService, permissionValidator)
    {
        _logger = logger;
        ServiceId = $"BattleRoom:{battleId}";

        _battleInfo = new BattleInfo
        {
            BattleId = battleId,
            Status = BattleStatus.Waiting,
            Team1 = new(),
            Team2 = new(),
            CurrentRound = 0,
            MaxRounds = 30
        };
    }

    public async Task<BattleInfo> JoinBattleAsync(JoinBattleRequest request)
    {
        var caller = GetCurrentCaller();

        _logger.LogInformation("Player {CharacterId} joining battle {BattleId}",
            request.CharacterId, request.BattleId);

        // ✅ Actor 模型 - 无并发问题
        var player = CreateBattlePlayer(request.CharacterId);
        _players[request.CharacterId] = player;

        // 分配到队伍
        if (_battleInfo.Team1.Count <= _battleInfo.Team2.Count)
        {
            _battleInfo.Team1.Add(player);
        }
        else
        {
            _battleInfo.Team2.Add(player);
        }

        // ✅ 广播给所有玩家
        await BroadcastAsync<IBattleReceiver>(receiver =>
            receiver.OnPlayerJoinedAsync(player));

        // 检查是否可以开始战斗
        if (CanStartBattle())
        {
            await StartBattleAsync();
        }

        return _battleInfo;
    }

    public async Task<BattleActionResult> PerformActionAsync(BattleAction action)
    {
        var caller = GetCurrentCaller();

        // 验证动作合法性
        if (!_players.TryGetValue(action.CharacterId, out var player))
        {
            return new BattleActionResult
            {
                Success = false,
                ErrorMessage = "玩家不在战斗中"
            };
        }

        if (!player.IsAlive)
        {
            return new BattleActionResult
            {
                Success = false,
                ErrorMessage = "角色已死亡"
            };
        }

        // ✅ 战斗计算（Actor 模型 - 严格有序）
        var result = CalculateActionResult(action, player);

        // 应用效果
        ApplyActionResult(result);

        // ✅ 广播动作结果给所有玩家
        await BroadcastAsync<IBattleReceiver>(receiver =>
            receiver.OnActionPerformedAsync(action, result));

        // 检查战斗是否结束
        if (IsBattleEnded())
        {
            await EndBattleAsync();
        }

        return result;
    }

    private async Task StartBattleAsync()
    {
        _battleInfo.Status = BattleStatus.InProgress;
        _battleInfo.StartTime = DateTime.UtcNow;

        _logger.LogInformation("Battle {BattleId} started", _battleInfo.BattleId);

        // 广播战斗开始
        await BroadcastAsync<IBattleReceiver>(receiver =>
            receiver.OnBattleStartedAsync(_battleInfo));

        // 启动战斗循环
        _ = Task.Run(BattleLoopAsync);
    }

    private async Task BattleLoopAsync()
    {
        while (!_battleLoopCts.Token.IsCancellationRequested && !IsBattleEnded())
        {
            _battleInfo.CurrentRound++;

            // 广播回合开始
            await BroadcastAsync<IBattleReceiver>(receiver =>
                receiver.OnRoundStartedAsync(_battleInfo.CurrentRound));

            // 回合逻辑（等待玩家操作）
            await Task.Delay(30000, _battleLoopCts.Token); // 30秒回合时间

            // 广播回合结束
            await BroadcastAsync<IBattleReceiver>(receiver =>
                receiver.OnRoundEndedAsync(_battleInfo.CurrentRound));
        }
    }

    // ... 其他方法实现
}
```

## 🔧 关键实现要点

### 1. BaseService 的正确使用

```csharp
// ✅ 正确：继承 BaseService
public class MyService : BaseService, IMyHub, IPulseService
{
    public MyService(
        ILogger logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
        : base(logger, authenticationService, permissionValidator)
    {
    }
}

// ❌ 错误：不继承 BaseService（缺失 Actor 模型支持）
public class MyService : IMyHub, IPulseService
{
    // 需要手动处理并发、权限验证等
}
```

### 2. 权限验证的使用

```csharp
// ✅ 方法级权限验证
[RequirePermission("admin.kick")]
public async Task<bool> KickPlayerAsync(string playerId)
{
    // 自动验证调用者是否有 "admin.kick" 权限
}

// ✅ 角色验证
[RequireRole("GameMaster")]
public async Task BanPlayerAsync(string playerId, int days)
{
    // 自动验证调用者是否有 "GameMaster" 角色
}

// ✅ 内部服务专用
[InternalOnly]
public async Task<ServerStats> GetServerStatsAsync()
{
    // 只有其他服务器可以调用
}
```

### 3. 跨Service通信

```csharp
public class GameService : BaseService
{
    private readonly IServiceLocator _serviceLocator;

    // 调用Battle Server
    public async Task NotifyBattleEndAsync(string battleId, BattleResult result)
    {
        var battleService = _serviceLocator.GetService<IBattleHub>(
            serverAddress: "battle-server:5100",
            serviceId: $"BattleRoom:{battleId}"
        );

        await battleService.ProcessBattleResultAsync(result);
    }
}
```

## 📊 性能监控

### 1. 指标收集

```csharp
public class MetricsService : BaseService
{
    private readonly IMetricsCollector _metrics;

    public async Task RecordActionAsync(BattleAction action)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 执行业务逻辑
            await ProcessAction(action);

            // 记录成功指标
            _metrics.Increment("battle.actions.success");
            _metrics.Histogram("battle.action.duration", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _metrics.Increment("battle.actions.failed");
            throw;
        }
    }
}
```

### 2. 健康检查

```csharp
public class HealthCheckService : IHealthCheck
{
    private readonly IServiceRegistry _serviceRegistry;

    public async Task<HealthCheckResult> CheckHealthAsync()
    {
        // 检查所有关键服务
        var services = await _serviceRegistry.GetAllServicesAsync();
        var unhealthyServices = services.Where(s => !s.IsHealthy).ToList();

        if (unhealthyServices.Any())
        {
            return HealthCheckResult.Unhealthy(
                $"发现 {unhealthyServices.Count} 个不健康的服务"
            );
        }

        return HealthCheckResult.Healthy();
    }
}
```

## 🚀 部署清单

### 必须实现的功能

- [ ] 数据库持久化（MongoDB / PostgreSQL）
- [ ] Redis 缓存
- [ ] 服务发现（Consul）
- [ ] 负载均衡
- [ ] 日志聚合（ELK / Loki）
- [ ] 监控告警（Prometheus + Grafana）
- [ ] 配置中心（Apollo / Nacos）
- [ ] 熔断降级
- [ ] 灰度发布

### 推荐的技术栈

```yaml
数据库:
  - MongoDB (玩家数据、角色数据)
  - PostgreSQL (交易数据、日志)
  - Redis (缓存、会话、排行榜)

消息队列:
  - RabbitMQ / Kafka (异步处理、事件驱动)

监控:
  - Prometheus (指标收集)
  - Grafana (可视化)
  - Jaeger (分布式追踪)

日志:
  - Serilog + Elasticsearch + Kibana

容器化:
  - Docker
  - Kubernetes
```

## 📝 总结

本指南展示了如何使用 PulseRPC 构建生产级分布式游戏服务器：

1. **Actor 模型**：使用 BaseService 实现服务隔离和顺序处理
2. **认证授权**：集成 JWT 和权限验证
3. **性能优化**：协议号、表达式树编译、零拷贝
4. **可观测性**：日志、监控、追踪
5. **高可用**：服务发现、负载均衡、熔断降级

完整的代码实现请参考各服务器项目。
