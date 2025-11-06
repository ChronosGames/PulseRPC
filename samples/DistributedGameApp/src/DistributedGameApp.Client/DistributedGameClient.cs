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
    private IPulseClient? _client;
    private IPlayerHub? _playerHub;
    private IChatRoomHub? _chatRoomHub;
    private IBattleHub? _battleHub;
    private IGameHub? _gameHub;
    private ISubscriptionToken? _eventSubscription;
    private CancellationTokenSource? _cts;

    // 客户端状态
    private bool _isConnected;
    private string _currentPlayerId = string.Empty;
    private string _currentPlayerName = string.Empty;
    private string? _currentRoomId;
    private string? _currentBattleId;
    private CharacterInfo? _currentCharacter;

    public DistributedGameClient(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<DistributedGameClient>();
    }

    /// <summary>
    /// 初始化客户端并连接到服务器
    /// </summary>
    public async Task InitializeAsync(string host = "localhost", int port = 8080)
    {
        _logger.LogInformation("正在初始化分布式游戏客户端...");
        _logger.LogInformation("连接地址: {Host}:{Port}", host, port);

        _cts = new CancellationTokenSource();

        try
        {
            // 使用 PulseRPC 客户端构建器 API
            _client = new PulseClientBuilder()
                .AddConnection(ConnectionDescriptor.CreateTcp(
                    "GameServer01",
                    "DistributedGameApp",
                    host,
                    port,
                    ConnectionStrategy.Persistent))
                .WithTransportOptions(TransportType.TCP, new TcpTransportOptions
                {
                    ConnectionTimeout = 30000,
                    NoDelay = true,
                    SendBufferSize = 8192,
                    RecvBufferSize = 8192,
                })
                .Build();

            // 初始化客户端
            await _client.InitializeAsync(_cts.Token);

            // 获取服务代理
            _playerHub = await _client.GetServiceAsync<IPlayerHub>("GameServer01");
            _chatRoomHub = await _client.GetServiceAsync<IChatRoomHub>("GameServer01");
            _battleHub = await _client.GetServiceAsync<IBattleHub>("GameServer01");
            _gameHub = await _client.GetServiceAsync<IGameHub>("GameServer01");

            // 注册事件监听器
            var eventHandler = new GameEventHandler(this, _loggerFactory.CreateLogger<GameEventHandler>());
            _eventSubscription = await _client.RegisterEventListenerAsync(eventHandler);

            _isConnected = true;
            _logger.LogInformation("客户端初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "客户端初始化失败: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// 登录
    /// </summary>
    public async Task<bool> LoginAsync(string account, string password)
    {
        if (!_isConnected || _gameHub == null)
        {
            _logger.LogWarning("客户端未连接");
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

            var response = await _gameHub.LoginAsync(request);

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
        if (!_isConnected || _gameHub == null)
        {
            _logger.LogWarning("客户端未连接");
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

            var character = await _gameHub.CreateCharacterAsync(request);

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
    /// 获取玩家信息
    /// </summary>
    public async Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        if (!_isConnected || _playerHub == null)
        {
            _logger.LogWarning("客户端未连接");
            return null;
        }

        try
        {
            var info = await _playerHub.GetPlayerInfoAsync();
            if (info != null)
            {
                _logger.LogInformation("玩家信息: {Name}, Level: {Level}, Exp: {Exp}",
                    info.PlayerName, info.Level, info.Exp);
            }
            return info;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取玩家信息失败");
            return null;
        }
    }

    /// <summary>
    /// 移动玩家
    /// </summary>
    public async Task<bool> MoveAsync(float x, float y, float z, float speed = 5.0f)
    {
        if (!_isConnected || _playerHub == null)
        {
            _logger.LogWarning("客户端未连接");
            return false;
        }

        try
        {
            var request = new MoveRequest
            {
                TargetX = x,
                TargetY = y,
                TargetZ = z,
                Speed = speed
            };

            var result = await _playerHub.MoveAsync(request);

            if (result.Success)
            {
                _logger.LogInformation("移动成功: ({X}, {Y}, {Z})", result.CurrentX, result.CurrentY, result.CurrentZ);
                return true;
            }
            else
            {
                _logger.LogWarning("移动失败: {ErrorMessage}", result.ErrorMessage);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "移动失败");
            return false;
        }
    }

    /// <summary>
    /// 加入聊天室
    /// </summary>
    public async Task<bool> JoinChatRoomAsync(string roomId, string playerName)
    {
        if (!_isConnected || _chatRoomHub == null)
        {
            _logger.LogWarning("客户端未连接");
            return false;
        }

        try
        {
            _logger.LogInformation("正在加入聊天室: {RoomId}", roomId);

            var success = await _chatRoomHub.JoinRoomAsync(_currentPlayerId, playerName);

            if (success)
            {
                _currentRoomId = roomId;
                _currentPlayerName = playerName;
                _logger.LogInformation("成功加入聊天室");
                return true;
            }
            else
            {
                _logger.LogWarning("加入聊天室失败");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加入聊天室失败");
            return false;
        }
    }

    /// <summary>
    /// 发送聊天消息
    /// </summary>
    public async Task<bool> SendMessageAsync(string content)
    {
        if (!_isConnected || _chatRoomHub == null || _currentRoomId == null)
        {
            _logger.LogWarning("未加入聊天室");
            return false;
        }

        try
        {
            var request = new SendMessageRequest
            {
                Content = content,
                Channel = ChatChannel.Room
            };

            var result = await _chatRoomHub.SendMessageAsync(request);

            if (result.Success)
            {
                _logger.LogDebug("消息发送成功: {MessageId}", result.MessageId);
                return true;
            }
            else
            {
                _logger.LogWarning("消息发送失败: {ErrorMessage}", result.ErrorMessage);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送消息失败");
            return false;
        }
    }

    /// <summary>
    /// 开始匹配
    /// </summary>
    public async Task<bool> RequestMatchAsync(MatchMode mode, int teamSize = 1)
    {
        if (!_isConnected || _gameHub == null)
        {
            _logger.LogWarning("客户端未连接");
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

            var response = await _gameHub.RequestMatchAsync(request);

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
        if (!_isConnected || _battleHub == null)
        {
            _logger.LogWarning("客户端未连接");
            return null;
        }

        try
        {
            _logger.LogInformation("正在加入战斗: {BattleId}", battleId);

            var request = new JoinBattleRequest
            {
                BattleId = battleId
            };

            var battleInfo = await _battleHub.JoinBattleAsync(request);

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
        if (!_isConnected || _battleHub == null || _currentBattleId == null)
        {
            _logger.LogWarning("未在战斗中");
            return false;
        }

        try
        {
            var success = await _battleHub.ReadyAsync();
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
    /// 显示状态
    /// </summary>
    public void DisplayStatus()
    {
        Console.WriteLine("\n=== 客户端状态 ===");
        Console.WriteLine($"连接状态: {(_isConnected ? "已连接" : "未连接")}");
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

        // 取消订阅
        _eventSubscription?.Dispose();

        // 取消操作
        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts?.Dispose();
            _cts = null;
        }

        // 释放资源
        _client?.Dispose();

        _isConnected = false;
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