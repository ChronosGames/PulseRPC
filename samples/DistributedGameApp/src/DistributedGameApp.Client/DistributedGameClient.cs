using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Messages;
using DistributedGameApp.Shared.Receivers;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Transport;

namespace DistributedGameApp.Client;

/// <summary>
/// 分布式游戏客户端 - 演示如何使用 PulseRPC 客户端功能
/// 支持多 GameServer 和 BattleServer 连接
/// </summary>
[PulseClientGeneration(typeof(IPlayerHub))]
[PulseClientGeneration(typeof(IChatRoomHub))]
[PulseClientGeneration(typeof(IBattleHub))]
[PulseClientGeneration(typeof(IGameHub))]
[PulseClientGeneration(typeof(IPlayerReceiver))]
[PulseClientGeneration(typeof(IChatRoomReceiver))]
[PulseClientGeneration(typeof(IBattleReceiver))]
[PulseClientGeneration(typeof(IGameReceiver))]
public class DistributedGameClient
{
    private readonly ILogger<DistributedGameClient> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ServerConnectionManager _connectionManager;
    private CancellationTokenSource? _cts;

    // 客户端状态
    private string _currentPlayerId = string.Empty;
    private string _currentPlayerName = string.Empty;
    private string? _currentRoomId;
    private string? _currentBattleId;
    private CharacterInfo? _currentCharacter;

    public DistributedGameClient(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<DistributedGameClient>();
        _connectionManager = new ServerConnectionManager(loggerFactory);
    }

    /// <summary>
    /// 获取当前连接
    /// </summary>
    public ServerConnection? CurrentConnection => _connectionManager.CurrentConnection;

    /// <summary>
    /// 获取所有连接
    /// </summary>
    public IReadOnlyDictionary<string, ServerConnection> AllConnections => _connectionManager.AllConnections;

    /// <summary>
    /// 初始化客户端并连接到 GameServer
    /// </summary>
    public async Task InitializeAsync(string host, int port, string serverId = "GameServer01")
    {
        _logger.LogInformation("正在初始化分布式游戏客户端...");
        _logger.LogInformation("连接地址: {Host}:{Port}", host, port);

        _cts = new CancellationTokenSource();

        try
        {
            // 连接到第一个 GameServer
            await _connectionManager.ConnectToGameServerAsync(
                serverId,
                serverId,
                host,
                port,
                _cts.Token);

            // 注册事件监听器
            var eventHandler = new GameEventHandler(this, _loggerFactory.CreateLogger<GameEventHandler>());
            await _connectionManager.RegisterEventListenerAsync(eventHandler);

            _logger.LogInformation("客户端初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "客户端初始化失败: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// 连接到额外的 GameServer
    /// </summary>
    public async Task ConnectToGameServerAsync(string serverId, string serverName, string host, int port)
    {
        try
        {
            await _connectionManager.ConnectToGameServerAsync(serverId, serverName, host, port, _cts?.Token ?? default);
            _logger.LogInformation("成功连接到 GameServer: {ServerName}", serverName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接到 GameServer 失败: {ServerName}", serverName);
            throw;
        }
    }

    /// <summary>
    /// 连接到 BattleServer
    /// </summary>
    public async Task ConnectToBattleServerAsync(string battleId, string host, int port)
    {
        try
        {
            var serverId = $"BattleServer-{battleId}";
            var connection = await _connectionManager.ConnectToBattleServerAsync(
                serverId,
                battleId,
                host,
                port,
                _cts?.Token ?? default);

            // 切换到 BattleServer
            _connectionManager.SwitchToServer(serverId);

            // 注册事件监听器
            var eventHandler = new GameEventHandler(this, _loggerFactory.CreateLogger<GameEventHandler>());
            await _connectionManager.RegisterEventListenerAsync(eventHandler);

            _logger.LogInformation("成功连接到 BattleServer: {BattleId}", battleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接到 BattleServer 失败: {BattleId}", battleId);
            throw;
        }
    }

    /// <summary>
    /// 切换到指定的服务器
    /// </summary>
    public bool SwitchServer(string serverId)
    {
        return _connectionManager.SwitchToServer(serverId);
    }

    /// <summary>
    /// 列出所有已连接的服务器
    /// </summary>
    public void ListServers()
    {
        Console.WriteLine("\n=== 已连接的服务器 ===");

        if (!_connectionManager.AllConnections.Any())
        {
            Console.WriteLine("暂无已连接的服务器");
            return;
        }

        foreach (var (serverId, connection) in _connectionManager.AllConnections)
        {
            var current = serverId == _connectionManager.CurrentServerId ? " [当前]" : "";
            var connectedTime = DateTime.UtcNow - connection.ConnectedAt;

            Console.WriteLine($"[{connection.ServerType}] {connection.ServerName}{current}");
            Console.WriteLine($"  ID: {serverId}");
            Console.WriteLine($"  地址: {connection.Host}:{connection.Port}");
            Console.WriteLine($"  状态: {(connection.IsConnected ? "已连接" : "未连接")}");
            Console.WriteLine($"  连接时长: {connectedTime.TotalMinutes:F1} 分钟");

            if (connection.BattleId != null)
            {
                Console.WriteLine($"  战斗ID: {connection.BattleId}");
            }

            Console.WriteLine();
        }
    }

    /// <summary>
    /// 注册事件处理器到当前连接
    /// </summary>
    public async Task RegisterEventHandlerAsync<T>(T eventHandler)
        where T : class, IPulseReceiver
    {
        await _connectionManager.RegisterEventListenerAsync(eventHandler);
    }

    /// <summary>
    /// 断开指定服务器连接
    /// </summary>
    public async Task DisconnectServerAsync(string serverId)
    {
        await _connectionManager.DisconnectServerAsync(serverId);
    }

    /// <summary>
    /// 登录
    /// </summary>
    public async Task<bool> LoginAsync(string account, string password)
    {
        var gameHub = _connectionManager.CurrentConnection?.GameHub;
        if (gameHub == null)
        {
            _logger.LogWarning("未连接到 GameServer");
            return false;
        }

        try
        {
            _logger.LogInformation("正在登录: {Account}", account);

            var request = new LoginRequest
            {
                Account = account,
                Password = password,
                DeviceId = Environment.MachineName,
                ClientVersion = "1.0.0"
            };

            var response = await gameHub.LoginAsync(request);

            if (response.Success)
            {
                _currentPlayerId = response.PlayerId;
                _logger.LogInformation("登录成功! 玩家ID: {PlayerId}", response.PlayerId);
                return true;
            }
            else
            {
                _logger.LogWarning("登录失败: {ErrorMessage}", response.ErrorMessage);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "登录过程中发生错误");
            return false;
        }
    }

    /// <summary>
    /// 创建角色
    /// </summary>
    public async Task<CharacterInfo?> CreateCharacterAsync(string name, CharacterClass characterClass, Gender gender)
    {
        var gameHub = _connectionManager.CurrentConnection?.GameHub;
        if (gameHub == null)
        {
            _logger.LogWarning("未连接到 GameServer");
            return null;
        }

        try
        {
            _logger.LogInformation("正在创建角色: {Name}, 职业: {Class}, 性别: {Gender}", name, characterClass, gender);

            var request = new CreateCharacterRequest
            {
                CharacterName = name,
                Class = characterClass,
                Gender = gender
            };

            var character = await gameHub.CreateCharacterAsync(request);

            if (character != null)
            {
                _currentCharacter = character;
                _currentPlayerName = character.CharacterName;
                _logger.LogInformation("角色创建成功! ID: {CharacterId}", character.CharacterId);
            }

            return character;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建角色失败");
            return null;
        }
    }

    /// <summary>
    /// 获取玩家信息 - 已移除，请使用 GameHub 的方法
    /// </summary>
    [Obsolete("IPlayerHub 已废弃，请使用 IGameHub")]
    public Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        _logger.LogWarning("IPlayerHub 已废弃，此方法不再可用");
        return Task.FromResult<PlayerInfo?>(null);
    }

    /// <summary>
    /// 移动玩家 - 已移除，请使用 GameHub 的方法
    /// </summary>
    [Obsolete("IPlayerHub 已废弃，请使用 IGameHub")]
    public Task<bool> MoveAsync(float x, float y, float z, float speed = 5.0f)
    {
        _logger.LogWarning("IPlayerHub 已废弃，此方法不再可用");
        return Task.FromResult(false);
    }

    /// <summary>
    /// 加入聊天室 - 已移除，请使用 GameHub 的聊天方法
    /// </summary>
    [Obsolete("IChatRoomHub 已废弃，请使用 IGameHub 的聊天功能")]
    public Task<bool> JoinChatRoomAsync(string roomId, string playerName)
    {
        _logger.LogWarning("IChatRoomHub 已废弃，此方法不再可用");
        return Task.FromResult(false);
    }

    /// <summary>
    /// 发送聊天消息 - 已移除，请使用 GameHub 的聊天方法
    /// </summary>
    [Obsolete("IChatRoomHub 已废弃，请使用 IGameHub 的聊天功能")]
    public Task<bool> SendMessageAsync(string content)
    {
        _logger.LogWarning("IChatRoomHub 已废弃，此方法不再可用");
        return Task.FromResult(false);
    }

    /// <summary>
    /// 开始匹配
    /// </summary>
    public async Task<bool> RequestMatchAsync(MatchMode mode, int teamSize = 1)
    {
        var gameHub = _connectionManager.CurrentConnection?.GameHub;
        if (gameHub == null)
        {
            _logger.LogWarning("未连接到 GameServer");
            return false;
        }

        try
        {
            _logger.LogInformation("开始匹配: 模式={Mode}, 队伍大小={TeamSize}", mode, teamSize);

            var request = new MatchmakingRequest
            {
                Mode = mode,
                TeamSize = teamSize,
                IsPartyMatch = false
            };

            var response = await gameHub.RequestMatchAsync(request);

            if (response.Success)
            {
                _logger.LogInformation("匹配成功! 票据ID: {TicketId}, 预计等待: {Wait}秒",
                    response.TicketId, response.EstimatedWaitTime);
                return true;
            }
            else
            {
                _logger.LogWarning("匹配失败: {ErrorMessage}", response.ErrorMessage);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "开始匹配失败");
            return false;
        }
    }

    /// <summary>
    /// 加入战斗
    /// </summary>
    public async Task<BattleInfo?> JoinBattleAsync(string battleId)
    {
        var battleHub = _connectionManager.CurrentConnection?.BattleHub;
        if (battleHub == null)
        {
            _logger.LogWarning("未连接到 BattleServer");
            return null;
        }

        try
        {
            _logger.LogInformation("正在加入战斗: {BattleId}", battleId);

            var request = new JoinBattleRequest
            {
                BattleId = battleId
            };

            var battleInfo = await battleHub.JoinBattleAsync(request);

            if (battleInfo != null)
            {
                _currentBattleId = battleId;
                _logger.LogInformation("成功加入战斗! 状态: {Status}", battleInfo.Status);
            }

            return battleInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加入战斗失败");
            return null;
        }
    }

    /// <summary>
    /// 战斗准备
    /// </summary>
    public async Task<bool> BattleReadyAsync()
    {
        var battleHub = _connectionManager.CurrentConnection?.BattleHub;
        if (battleHub == null || _currentBattleId == null)
        {
            _logger.LogWarning("未在战斗中");
            return false;
        }

        try
        {
            var success = await battleHub.ReadyAsync();
            if (success)
            {
                _logger.LogInformation("已准备就绪");
            }
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "准备失败");
            return false;
        }
    }

    /// <summary>
    /// 执行战斗行动（攻击）
    /// </summary>
    public async Task<BattleActionResult?> PerformAttackAsync(string targetCharacterId)
    {
        var battleHub = _connectionManager.CurrentConnection?.BattleHub;
        if (battleHub == null || _currentCharacter == null)
        {
            _logger.LogWarning("未在战斗中或未选择角色");
            return null;
        }

        try
        {
            var action = new BattleAction
            {
                ActionId = Guid.NewGuid().ToString(),
                Type = ActionType.Attack,
                CharacterId = _currentCharacter.CharacterId,
                TargetIds = new List<string> { targetCharacterId },
                Timestamp = DateTime.UtcNow
            };

            var result = await battleHub.PerformActionAsync(action);

            if (result.Success)
            {
                var totalDamage = result.DamageRecords.Sum(d => d.Damage);
                _logger.LogInformation("攻击成功! 伤害: {Damage}", totalDamage);
            }
            else
            {
                _logger.LogWarning("攻击失败: {Message}", result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行攻击失败");
            return null;
        }
    }

    /// <summary>
    /// 使用技能
    /// </summary>
    public async Task<BattleActionResult?> UseSkillAsync(string skillId, string targetCharacterId)
    {
        var battleHub = _connectionManager.CurrentConnection?.BattleHub;
        if (battleHub == null || _currentCharacter == null)
        {
            _logger.LogWarning("未在战斗中或未选择角色");
            return null;
        }

        try
        {
            var action = new BattleAction
            {
                ActionId = Guid.NewGuid().ToString(),
                Type = ActionType.Skill,
                CharacterId = _currentCharacter.CharacterId,
                SkillId = skillId,
                TargetIds = new List<string> { targetCharacterId },
                Timestamp = DateTime.UtcNow
            };

            var result = await battleHub.PerformActionAsync(action);

            if (result.Success)
            {
                _logger.LogInformation("技能释放成功! 技能: {SkillId}", skillId);
            }
            else
            {
                _logger.LogWarning("技能释放失败: {Message}", result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "使用技能失败");
            return null;
        }
    }

    /// <summary>
    /// 离开战斗
    /// </summary>
    public async Task<bool> LeaveBattleAsync()
    {
        var battleHub = _connectionManager.CurrentConnection?.BattleHub;
        if (battleHub == null)
        {
            _logger.LogWarning("未连接到 BattleServer");
            return false;
        }

        try
        {
            _logger.LogInformation("正在离开战斗...");
            var success = await battleHub.LeaveBattleAsync();

            if (success)
            {
                _currentBattleId = null;
                _logger.LogInformation("已离开战斗");
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "离开战斗失败");
            return false;
        }
    }

    /// <summary>
    /// 显示状态
    /// </summary>
    public void DisplayStatus()
    {
        Console.WriteLine("\n=== 客户端状态 ===");

        var currentConnection = _connectionManager.CurrentConnection;
        if (currentConnection != null)
        {
            Console.WriteLine($"当前服务器: {currentConnection.ServerName} ({currentConnection.ServerType})");
            Console.WriteLine($"服务器地址: {currentConnection.Host}:{currentConnection.Port}");
        }
        else
        {
            Console.WriteLine("当前服务器: 未连接");
        }

        Console.WriteLine($"已连接服务器数: {_connectionManager.AllConnections.Count}");
        Console.WriteLine($"玩家ID: {_currentPlayerId}");
        Console.WriteLine($"玩家名称: {_currentPlayerName}");
        Console.WriteLine($"当前房间: {_currentRoomId ?? "未加入"}");
        Console.WriteLine($"当前战斗: {_currentBattleId ?? "未在战斗中"}");

        if (_currentCharacter != null)
        {
            Console.WriteLine($"\n角色信息:");
            Console.WriteLine($"  名称: {_currentCharacter.CharacterName}");
            Console.WriteLine($"  职业: {_currentCharacter.Class}");
            Console.WriteLine($"  等级: {_currentCharacter.Level}");
            Console.WriteLine($"  HP: {_currentCharacter.Hp}/{_currentCharacter.MaxHp}");
            Console.WriteLine($"  攻击: {_currentCharacter.Attack} | 防御: {_currentCharacter.Defense}");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// 关闭客户端
    /// </summary>
    public async Task ShutdownAsync()
    {
        _logger.LogInformation("正在关闭客户端...");

        // 取消操作
        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts?.Dispose();
            _cts = null;
        }

        // 释放连接管理器
        _connectionManager?.Dispose();

        _logger.LogInformation("客户端已关闭");
    }

    /// <summary>
    /// 游戏事件处理器
    /// </summary>
    private class GameEventHandler : IPlayerReceiver, IChatRoomReceiver, IBattleReceiver, IGameReceiver
    {
        private readonly DistributedGameClient _client;
        private readonly ILogger<GameEventHandler> _logger;

        public GameEventHandler(DistributedGameClient client, ILogger<GameEventHandler> logger)
        {
            _client = client;
            _logger = logger;
        }

        // IPlayerReceiver 实现
        public Task OnPlayerInfoUpdatedAsync(PlayerInfo playerInfo)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n[玩家] 信息更新: {playerInfo.PlayerName}, Level: {playerInfo.Level}\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnPlayerLevelUpAsync(PlayerInfo playerInfo)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n[玩家] 恭喜升级! 当前等级: {playerInfo.Level}\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnNearbyPlayerMovedAsync(string playerId, float x, float y, float z)
        {
            _logger.LogDebug("附近玩家移动: {PlayerId} -> ({X}, {Y}, {Z})", playerId, x, y, z);
            return Task.CompletedTask;
        }

        // IChatRoomReceiver 实现
        public Task OnMessageReceivedAsync(ChatMessage message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[聊天] {message.SenderName}: {message.Content}\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnPlayerJoinedAsync(string playerId, string playerName)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[聊天室] {playerName} 加入了房间\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnPlayerLeftAsync(string playerId, string playerName)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n[聊天室] {playerName} 离开了房间\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnRoomInfoUpdatedAsync(RoomInfo roomInfo)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n[聊天室] 房间信息更新: {roomInfo.RoomName}, 成员: {roomInfo.MemberCount}/{roomInfo.MaxMembers}\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        // IBattleReceiver 实现
        public Task OnBattleStartedAsync(BattleInfo battleInfo)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[战斗] 战斗开始! ID: {battleInfo.BattleId}\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnBattleEndedAsync(BattleResult result)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"\n[战斗] 战斗结束! 胜利者: {result.WinnerTeam}\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnPlayerJoinedBattleAsync(string playerId, string playerName, int team)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n[战斗] {playerName} 加入战斗 (队伍{team})\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnPlayerReadyAsync(string playerId, bool isReady)
        {
            _logger.LogDebug("玩家准备状态: {PlayerId} -> {IsReady}", playerId, isReady);
            return Task.CompletedTask;
        }

        public Task OnBattleActionAsync(BattleAction action)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"\n[战斗] 动作: {action.Type}, 来自: {action.CharacterId}\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnPlayerLeftBattleAsync(string characterId, string reason)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n[战斗] 玩家 {characterId} 离开战斗: {reason}\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnActionPerformedAsync(BattleAction action, BattleActionResult result)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"\n[战斗] 动作执行: {action.Type}, 成功: {result.Success}\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnRoundStartedAsync(int roundNumber)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n[战斗] 回合 {roundNumber} 开始\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnRoundEndedAsync(int roundNumber)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n[战斗] 回合 {roundNumber} 结束\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnPlayerDiedAsync(string characterId, string killerId)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[战斗] {characterId} 被 {killerId} 击杀\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnPlayerRespawnedAsync(string characterId)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[战斗] {characterId} 复活了\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnCountdownAsync(int seconds)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n[战斗] 倒计时: {seconds} 秒\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnPlayerDisconnectedAsync(string characterId)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"\n[战斗] {characterId} 断线了\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnPlayerReconnectedAsync(string characterId)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[战斗] {characterId} 重新连接\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        // IGameReceiver 实现
        public Task OnMatchFoundAsync(MatchFoundNotification notification)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[匹配] 找到对手! 战斗ID: {notification.BattleId}");
            Console.WriteLine($"       服务器: {notification.BattleServerAddress}:{notification.BattleServerPort}");
            Console.WriteLine($"       倒计时: {notification.Countdown}秒\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnServerNotificationAsync(string message)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n[系统] {message}\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnMatchCancelledAsync(string reason)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n[匹配] 匹配已取消: {reason}\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnMatchProgressAsync(int estimatedWaitTime, int queuePosition)
        {
            _logger.LogInformation("匹配进度: 队列位置={Position}, 预计等待={Wait}秒", queuePosition, estimatedWaitTime);
            return Task.CompletedTask;
        }

        public Task OnCharacterLevelUpAsync(CharacterInfo characterInfo)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"\n[游戏] 恭喜! {characterInfo.CharacterName} 升到 {characterInfo.Level} 级!\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnKickedAsync(string reason)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[系统] 您已被强制下线: {reason}\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnSystemAnnouncementAsync(string announcement)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n[公告] {announcement}\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnFriendOnlineAsync(string friendId, string friendName)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[好友] {friendName} 上线了\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }

        public Task OnFriendOfflineAsync(string friendId, string friendName)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"\n[好友] {friendName} 下线了\n> ");
            Console.ResetColor();
            return Task.CompletedTask;
        }
    }
}
