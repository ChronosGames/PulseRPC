using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Messages;
using DistributedGameApp.Shared.Receivers;
using Microsoft.Extensions.Logging;
using MongoDB.Driver.Core.Servers;
using PulseRPC;

namespace DistributedGameApp.Client;

/// <summary>
/// 统一的游戏客户端 - 整合 HTTP 登录和 PulseRPC 游戏连接
/// </summary>
/// <remarks>
/// 此类提供完整的游戏客户端功能，包括：
/// 1. 连接 LoginServer 进行登录认证
/// 2. 选择区服（GameServer）
/// 3. 创建/列表/选择角色进入 GameServer
/// 4. 维护 GameServer 连接
/// 5. 进入战斗服（BattleServer）
/// 6. 在战斗期间维持连接
/// </remarks>
[PulseClientGeneration(typeof(IPlayerHub))]
[PulseClientGeneration(typeof(IChatRoomHub))]
[PulseClientGeneration(typeof(IBattleHub))]
[PulseClientGeneration(typeof(IGameHub))]
[PulseClientGeneration(typeof(IPlayerReceiver))]
[PulseClientGeneration(typeof(IChatRoomReceiver))]
[PulseClientGeneration(typeof(IBattleReceiver))]
[PulseClientGeneration(typeof(IGameReceiver))]
public class GameClient : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<GameClient> _logger;
    private readonly LoginServerClient _loginClient;
    private readonly ServerConnectionManager _connectionManager;

    // 客户端状态
    private string? _userId;
    private string? _username;
    private string? _currentCharacterId;
    private CharacterInfo? _currentCharacter;
    private List<CharacterInfo> _characterList = new();
    private ServerInfo? _currentGameServer;
    private ServerInfo? _currentBattleServer;

    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>
    /// 初始化游戏客户端
    /// </summary>
    /// <param name="loginServerUrl">LoginServer 的 URL，例如 "http://localhost:5000"</param>
    /// <param name="loggerFactory">日志工厂</param>
    public GameClient(string loginServerUrl, ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<GameClient>();

        _loginClient = new LoginServerClient(loginServerUrl, loggerFactory.CreateLogger<LoginServerClient>());
        _connectionManager = new ServerConnectionManager(loggerFactory);

        _logger.LogInformation("游戏客户端已初始化，LoginServer: {Url}", loginServerUrl);
    }

    #region 属性

    /// <summary>
    /// 当前用户ID
    /// </summary>
    public string? UserId => _userId;

    /// <summary>
    /// 当前用户名
    /// </summary>
    public string? Username => _username;

    /// <summary>
    /// 当前访问令牌
    /// </summary>
    public string? AccessToken => _loginClient.AccessToken;

    /// <summary>
    /// 是否已登录
    /// </summary>
    public bool IsLoggedIn => _loginClient.IsAuthenticated;

    /// <summary>
    /// 当前角色
    /// </summary>
    public CharacterInfo? CurrentCharacter => _currentCharacter;

    /// <summary>
    /// 当前游戏服务器
    /// </summary>
    public ServerInfo? CurrentGameServer => _currentGameServer;

    /// <summary>
    /// 当前战斗服务器
    /// </summary>
    public ServerInfo? CurrentBattleServer => _currentBattleServer;

    /// <summary>
    /// 是否已连接到游戏服务器
    /// </summary>
    public bool IsConnectedToGameServer
    {
        get
        {
            if (_currentGameServer == null)
            {
                return false;
            }

            return _connectionManager.AllConnections.Any(x => x.Value.IsConnected && x.Value.ServerId == _currentGameServer.ServerId);
        }
    }

    /// <summary>
    /// 是否在战斗中
    /// </summary>
    public bool IsInBattle
    {
        get
        {
            if (_currentBattleServer == null)
            {
                return false;
            }

            return _connectionManager.AllConnections.Any(x => x.Value.IsConnected && x.Value.ServerId == _currentBattleServer.ServerId);
        }
    }

    #endregion

    #region 1. 登录认证流程

    /// <summary>
    /// 注册新账号
    /// </summary>
    /// <param name="username">用户名</param>
    /// <param name="password">密码</param>
    /// <param name="email">邮箱</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    public async Task<bool> RegisterAsync(
        string username,
        string password,
        string email,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _loginClient.RegisterAsync(username, password, email, cancellationToken);

            _userId = response.UserId;
            _username = response.Username;

            _logger.LogInformation("注册成功: {Username} ({UserId})", _username, _userId);
            return true;
        }
        catch (LoginServerException ex)
        {
            _logger.LogError(ex, "注册失败");
            return false;
        }
    }

    /// <summary>
    /// 登录到 LoginServer
    /// </summary>
    /// <param name="usernameOrEmail">用户名或邮箱</param>
    /// <param name="password">密码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    public async Task<bool> LoginAsync(
        string usernameOrEmail,
        string password,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _loginClient.LoginAsync(usernameOrEmail, password, cancellationToken);

            _userId = response.UserId;
            _username = response.Username;

            _logger.LogInformation("登录成功: {Username} ({UserId})", _username, _userId);
            return true;
        }
        catch (LoginServerException ex)
        {
            _logger.LogError(ex, "登录失败");
            return false;
        }
    }

    /// <summary>
    /// 刷新访问令牌
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    public async Task<bool> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _loginClient.RefreshTokenAsync(cancellationToken);

            _userId = response.UserId;
            _username = response.Username;

            _logger.LogInformation("令牌刷新成功: {Username} ({UserId})", _username, _userId);
            return true;
        }
        catch (LoginServerException ex)
        {
            _logger.LogError(ex, "令牌刷新失败");
            return false;
        }
    }

    #endregion

    #region 2. 选择区服流程

    /// <summary>
    /// 获取所有可用的游戏服务器列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务器列表</returns>
    public async Task<List<ServerInfo>> GetGameServerListAsync(CancellationToken cancellationToken = default)
    {
        EnsureLoggedIn();

        try
        {
            var servers = await _loginClient.GetGameServersAsync(cancellationToken);
            _logger.LogInformation("获取到 {Count} 个游戏服务器", servers.Count);
            return servers;
        }
        catch (LoginServerException ex)
        {
            _logger.LogError(ex, "获取服务器列表失败");
            return new List<ServerInfo>();
        }
    }

    /// <summary>
    /// 获取推荐的游戏服务器
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>推荐的服务器，如果没有则返回 null</returns>
    public async Task<ServerInfo?> GetRecommendedGameServerAsync(CancellationToken cancellationToken = default)
    {
        EnsureLoggedIn();

        try
        {
            var server = await _loginClient.GetRecommendedGameServerAsync(cancellationToken);
            _logger.LogInformation("推荐服务器: {ServerName}", server.ServerName);
            return server;
        }
        catch (LoginServerException ex)
        {
            _logger.LogError(ex, "获取推荐服务器失败");
            return null;
        }
    }

    /// <summary>
    /// 连接到指定的游戏服务器
    /// </summary>
    /// <param name="server">服务器信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    public async Task<bool> ConnectToGameServerAsync(
        ServerInfo server,
        CancellationToken cancellationToken = default)
    {
        EnsureLoggedIn();

        try
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _logger.LogInformation("正在连接到游戏服务器: {ServerName} ({Host}:{Port})",
                server.ServerName, server.Host, server.TcpPort);

            // 连接到 GameServer
            await _connectionManager.ConnectToServerAsync(
                server.ServerId,
                server.ServerName,
                server.Host,
                server.TcpPort,
                cancellationToken: _cts.Token);

            // ✅ 使用 Ticket 在 GameServer 上进行身份验证
            var gameHub = await _connectionManager.GetHubAsync<IGameHub>(server.ServerId, cancellationToken: _cts.Token);

            // 优先使用 Ticket（从 LoginServer 获取的 AccessToken）
            var loginRequest = new Shared.Messages.LoginRequest
            {
                DeviceId = Environment.MachineName,
                Ticket = AccessToken!,
            };

            var loginResult = await gameHub.LoginAsync(loginRequest);

            if (!loginResult.Success)
            {
                _logger.LogError("GameServer 身份验证失败: {ErrorCode} - {ErrorMessage}",
                    loginResult.ErrorCode, loginResult.ErrorMessage);
                return false;
            }

            _logger.LogInformation("GameServer 身份验证成功: {PlayerId} (使用 {Method})",
                loginResult.PlayerId,
                !string.IsNullOrEmpty(loginRequest.Ticket) ? "Ticket" : "Account");

            // 注册事件监听器（GameEventHandler 实现了多个 Receiver 接口）
            // 新的双向 RPC 架构要求为每个接口单独注册
            var eventHandler = new GameEventHandler(this, _loggerFactory.CreateLogger<GameEventHandler>());
            await _connectionManager.RegisterEventListenerAsync<IPlayerReceiver>(eventHandler);
            await _connectionManager.RegisterEventListenerAsync<IChatRoomReceiver>(eventHandler);
            await _connectionManager.RegisterEventListenerAsync<IBattleReceiver>(eventHandler);
            await _connectionManager.RegisterEventListenerAsync<IGameReceiver>(eventHandler);

            _currentGameServer = server;

            _logger.LogInformation("成功连接到游戏服务器: {ServerName}", server.ServerName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接到游戏服务器失败");
            return false;
        }
    }

    #endregion

    #region 3. 角色管理流程

    /// <summary>
    /// 获取角色列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>角色列表</returns>
    public async Task<List<CharacterInfo>> GetCharacterListAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnectedToGameServer();

        var gameHub = await _connectionManager.GetHubAsync<IGameHub>(_currentGameServer!.ServerId, cancellationToken: cancellationToken);

        try
        {
            var characters = await gameHub.GetCharacterListAsync();
            _characterList = characters.ToList();

            _logger.LogInformation("获取到 {Count} 个角色", _characterList.Count);
            return _characterList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取角色列表失败");
            return new List<CharacterInfo>();
        }
    }

    /// <summary>
    /// 创建新角色
    /// </summary>
    /// <param name="name">角色名称</param>
    /// <param name="characterClass">职业</param>
    /// <param name="gender">性别</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建的角色，失败则返回 null</returns>
    public async Task<CharacterInfo?> CreateCharacterAsync(
        string name,
        CharacterClass characterClass,
        Gender gender,
        CancellationToken cancellationToken = default)
    {
        EnsureConnectedToGameServer();

        var gameHub = await _connectionManager.GetHubAsync<IGameHub>(_currentGameServer!.ServerId, cancellationToken: cancellationToken);

        try
        {
            var request = new CreateCharacterRequest
            {
                CharacterName = name,
                Class = characterClass,
                Gender = gender
            };

            var character = await gameHub.CreateCharacterAsync(request);

            _logger.LogInformation("成功创建角色: {CharacterName} ({CharacterId})",
                character.CharacterName, character.CharacterId);

            // 添加到角色列表
            _characterList.Add(character);

            return character;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建角色失败");
            return null;
        }
    }

    /// <summary>
    /// 选择角色进入游戏
    /// </summary>
    /// <param name="characterId">角色ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    public async Task<bool> SelectCharacterAsync(
        string characterId,
        CancellationToken cancellationToken = default)
    {
        EnsureConnectedToGameServer();

        var gameHub = await _connectionManager.GetHubAsync<IGameHub>(_currentGameServer!.ServerId, cancellationToken: cancellationToken);

        try
        {
            var character = await gameHub.GetCharacterAsync(characterId);

            if (character == null)
            {
                _logger.LogWarning("角色不存在: {CharacterId}", characterId);
                return false;
            }

            _currentCharacter = character;
            _currentCharacterId = characterId;

            _logger.LogInformation("已选择角色: {CharacterName} (等级 {Level})",
                character.CharacterName, character.Level);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "选择角色失败");
            return false;
        }
    }

    /// <summary>
    /// 删除角色
    /// </summary>
    /// <param name="characterId">角色ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    public async Task<bool> DeleteCharacterAsync(
        string characterId,
        CancellationToken cancellationToken = default)
    {
        EnsureConnectedToGameServer();

        var gameHub = await _connectionManager.GetHubAsync<IGameHub>(_currentGameServer!.ServerId, cancellationToken: cancellationToken);

        try
        {
            var success = await gameHub.DeleteCharacterAsync(characterId);

            if (success)
            {
                _characterList.RemoveAll(c => c.CharacterId == characterId);

                if (_currentCharacterId == characterId)
                {
                    _currentCharacterId = null;
                    _currentCharacter = null;
                }

                _logger.LogInformation("成功删除角色: {CharacterId}", characterId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除角色失败");
            return false;
        }
    }

    #endregion

    #region 4. 匹配与战斗流程

    /// <summary>
    /// 请求匹配
    /// </summary>
    /// <param name="mode">匹配模式</param>
    /// <param name="teamSize">队伍大小</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配票据ID，失败则返回 null</returns>
    public async Task<string?> RequestMatchAsync(
        MatchMode mode,
        int teamSize = 1,
        CancellationToken cancellationToken = default)
    {
        EnsureConnectedToGameServer();

        var gameHub = await _connectionManager.GetHubAsync<IGameHub>(_currentGameServer!.ServerId, cancellationToken: cancellationToken);

        try
        {
            var request = new MatchmakingRequest
            {
                Mode = mode,
                TeamSize = teamSize,
                IsPartyMatch = false
            };

            var response = await gameHub.RequestMatchAsync(request);

            if (response.Success)
            {
                _logger.LogInformation("匹配请求成功，票据ID: {TicketId}, 预计等待: {Wait}秒",
                    response.TicketId, response.EstimatedWaitTime);
                return response.TicketId;
            }
            else
            {
                _logger.LogWarning("匹配请求失败: {ErrorMessage}", response.ErrorMessage);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "请求匹配失败");
            return null;
        }
    }

    /// <summary>
    /// 取消匹配
    /// </summary>
    /// <param name="ticketId">匹配票据ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    public async Task<bool> CancelMatchAsync(
        string ticketId,
        CancellationToken cancellationToken = default)
    {
        EnsureConnectedToGameServer();

        var gameHub = await _connectionManager.GetHubAsync<IGameHub>(_currentGameServer!.ServerId, cancellationToken: cancellationToken);

        try
        {
            var success = await gameHub.CancelMatchAsync(ticketId);

            if (success)
            {
                _logger.LogInformation("已取消匹配: {TicketId}", ticketId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消匹配失败");
            return false;
        }
    }

    /// <summary>
    /// 连接到战斗服务器
    /// </summary>
    /// <param name="battleId">战斗ID</param>
    /// <param name="host">战斗服务器地址</param>
    /// <param name="port">战斗服务器端口</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    public async Task<bool> ConnectToBattleServerAsync(
        string battleId,
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("正在连接到战斗服务器: {BattleId} ({Host}:{Port})",
                battleId, host, port);

            var serverId = $"BattleServer-{battleId}";

            // 连接到 BattleServer
            await _connectionManager.ConnectToServerAsync(
                serverId,
                battleId,
                host,
                port,
                cancellationToken: cancellationToken);

            // 注册事件监听器（GameEventHandler 实现了多个 Receiver 接口）
            // 新的双向 RPC 架构要求为每个接口单独注册
            var eventHandler = new GameEventHandler(this, _loggerFactory.CreateLogger<GameEventHandler>());
            await _connectionManager.RegisterEventListenerAsync<IPlayerReceiver>(eventHandler);
            await _connectionManager.RegisterEventListenerAsync<IChatRoomReceiver>(eventHandler);
            await _connectionManager.RegisterEventListenerAsync<IBattleReceiver>(eventHandler);
            await _connectionManager.RegisterEventListenerAsync<IGameReceiver>(eventHandler);

            _currentBattleServer = new ServerInfo
            {
                ServerId = serverId,
                ServerName = $"BattleServer-{battleId}",
                Host = host,
                TcpPort = port
            };

            _logger.LogInformation("成功连接到战斗服务器: {BattleId}", battleId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接到战斗服务器失败");
            return false;
        }
    }

    /// <summary>
    /// 加入战斗
    /// </summary>
    /// <param name="battleId">战斗ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>战斗信息，失败则返回 null</returns>
    public async Task<BattleInfo?> JoinBattleAsync(
        string battleId,
        CancellationToken cancellationToken = default)
    {
        var battleHub = await _connectionManager.GetHubAsync<IBattleHub>(_currentBattleServer!.ServerId, cancellationToken: cancellationToken);
        if (battleHub == null)
        {
            _logger.LogWarning("未连接至战斗服");
            return null;
        }

        try
        {
            var request = new JoinBattleRequest
            {
                BattleId = battleId
            };

            var battleInfo = await battleHub.JoinBattleAsync(request);

            _logger.LogInformation("成功加入战斗: {BattleId}, 状态: {Status}",
                battleId, battleInfo.Status);

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
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    public async Task<bool> BattleReadyAsync(CancellationToken cancellationToken = default)
    {
        var battleHub = await _connectionManager.GetHubAsync<IBattleHub>(_currentBattleServer!.ServerId, cancellationToken: cancellationToken);
        if (battleHub == null)
        {
            _logger.LogWarning("未连接到战斗服务器");
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
    /// 离开战斗
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    public async Task<bool> LeaveBattleAsync(CancellationToken cancellationToken = default)
    {
        var battleHub = await _connectionManager.GetHubAsync<IBattleHub>(_currentBattleServer!.ServerId, cancellationToken: cancellationToken);
        if (battleHub == null)
        {
            _logger.LogWarning("未连接到战斗服务器");
            return false;
        }

        try
        {
            var success = await battleHub.LeaveBattleAsync();

            if (success)
            {
                _logger.LogInformation("已离开战斗");

                // 断开战斗服务器连接
                if (_currentBattleServer != null)
                {
                    await _connectionManager.DisconnectServerAsync(_currentBattleServer.ServerId);
                    _currentBattleServer = null;
                }

                // 切换回游戏服务器
                if (_currentGameServer != null)
                {
                    _connectionManager.SwitchToServer(_currentGameServer.ServerId);
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "离开战斗失败");
            return false;
        }
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 确保已登录
    /// </summary>
    private void EnsureLoggedIn()
    {
        if (!IsLoggedIn)
        {
            throw new InvalidOperationException("未登录，请先调用 LoginAsync");
        }
    }

    /// <summary>
    /// 确保已连接到游戏服务器
    /// </summary>
    private void EnsureConnectedToGameServer()
    {
        if (!IsConnectedToGameServer)
        {
            throw new InvalidOperationException("未连接到游戏服务器，请先调用 ConnectToGameServerAsync");
        }
    }

    /// <summary>
    /// 显示客户端状态
    /// </summary>
    public void DisplayStatus()
    {
        Console.WriteLine("\n=== 游戏客户端状态 ===");
        Console.WriteLine($"用户: {_username} ({_userId})");
        Console.WriteLine($"已登录: {IsLoggedIn}");

        if (_currentGameServer != null)
        {
            Console.WriteLine($"\n当前游戏服务器: {_currentGameServer.ServerName}");
            Console.WriteLine($"  地址: {_currentGameServer.Host}:{_currentGameServer.TcpPort}");
            Console.WriteLine($"  状态: {(IsConnectedToGameServer ? "已连接" : "未连接")}");
            Console.WriteLine($"  负载: {_currentGameServer.CurrentPlayers}/{_currentGameServer.MaxPlayers} ({_currentGameServer.LoadPercentage}%)");
        }

        if (_currentCharacter != null)
        {
            Console.WriteLine($"\n当前角色: {_currentCharacter.CharacterName}");
            Console.WriteLine($"  职业: {_currentCharacter.Class}");
            Console.WriteLine($"  等级: {_currentCharacter.Level}");
            Console.WriteLine($"  HP: {_currentCharacter.Hp}/{_currentCharacter.MaxHp}");
            Console.WriteLine($"  攻击: {_currentCharacter.Attack} | 防御: {_currentCharacter.Defense}");
        }

        if (_currentBattleServer != null)
        {
            Console.WriteLine($"\n当前战斗服务器: {_currentBattleServer.ServerName}");
            Console.WriteLine($"  地址: {_currentBattleServer.Host}:{_currentBattleServer.TcpPort}");
            Console.WriteLine($"  状态: {(IsInBattle ? "战斗中" : "未在战斗中")}");
        }

        Console.WriteLine($"\n已连接服务器数: {_connectionManager.AllConnections.Count}");
        Console.WriteLine();
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        _cts?.Cancel();
        _cts?.Dispose();

        _connectionManager?.Dispose();
        _loginClient?.Dispose();

        _disposed = true;

        GC.SuppressFinalize(this);
    }

    #endregion

    #region 内部事件处理器

    /// <summary>
    /// 游戏事件处理器
    /// </summary>
    private class GameEventHandler : IPlayerReceiver, IChatRoomReceiver, IBattleReceiver, IGameReceiver
    {
        private readonly GameClient _client;
        private readonly ILogger<GameEventHandler> _logger;

        public GameEventHandler(GameClient client, ILogger<GameEventHandler> logger)
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

            // 自动连接到战斗服务器
            _ = _client.ConnectToBattleServerAsync(
                notification.BattleId,
                notification.BattleServerAddress,
                notification.BattleServerPort);

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

            // 更新当前角色信息
            if (_client._currentCharacter?.CharacterId == characterInfo.CharacterId)
            {
                _client._currentCharacter = characterInfo;
            }

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

    #endregion
}
