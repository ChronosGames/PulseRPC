using System.Numerics;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Transport;
using PulseRPC.Client;
using PulseRPC.Client.Core;
using ISubscriptionToken = PulseRPC.ISubscriptionToken;

namespace ChatApp.Console;

/// <summary>
/// 游戏控制台客户端
/// </summary>
[PulseClientGeneration(typeof(IPlayerHub))]
[PulseClientGeneration(typeof(IPlayerLoginEvents))]
[PulseClientGeneration(typeof(IPlayerMovementEvents))]
public class GameConsoleClient(ILoggerFactory loggerFactory)
{
    private readonly ILogger<GameConsoleClient> _logger = loggerFactory.CreateLogger<GameConsoleClient>();
    private IPulseClient? _client;
    private IPlayerHub? _playerService;
    private ISubscriptionToken? _eventsSubscription;
    private CancellationTokenSource? _cts;
    private bool _isLoggedIn;
    private PlayerInfo? _playerInfo;
    private Vector3 _position = Vector3.Zero;

    // 用于存储其他玩家位置的字典
    private readonly Dictionary<Guid, PlayerData> _otherPlayers = new Dictionary<Guid, PlayerData>();

    /// <summary>
    /// 初始化客户端 - 使用新的客户端 API
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInformation("正在初始化游戏客户端...");

        _cts = new CancellationTokenSource();

        // 使用 PulseRPC 客户端构建器 API
        _client = new PulseClientBuilder()
            .AddConnection(ConnectionDescriptor.CreateTcp("ChatApp001", "ChatApp", "localhost", 7000, ConnectionStrategy.Persistent))
            .WithTransportOptions(TransportType.Tcp, new TcpTransportOptions
            {
                ConnectionTimeout = 30000,
                NoDelay = true,
                SendBufferSize = 8192,
                RecvBufferSize = 8192,
            })
            .Build();

        try
        {
            // 初始化客户端
            await _client.InitializeAsync(_cts.Token);

            // 获取服务代理
            _playerService = _client.CreatePlayerHubProxy();

            // 注册事件监听器
            _eventsSubscription = await _client.RegisterSmartEventListenerAsync<IPlayerLoginEvents>(new PlayerEventsHandler(this));
            _eventsSubscription =
                await _client.RegisterSmartEventListenerAsync<IPlayerMovementEvents>(new PlayerEventsHandler(this));

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
    public async Task LoginAsync(string username, string password)
    {
        _logger.LogInformation("正在登录，用户名: {Username}, 密码: {Password}", username, password);

        try
        {
            var request = new LoginRequest { Username = username, Password = password };
            var response = await _playerService!.LoginAsync(request);
            if (response.Success)
            {
                _isLoggedIn = true;
                _playerInfo = response.Player;

                _logger.LogInformation("登录成功: {Username} (ID: {PlayerId})", _playerInfo!.Username, _playerInfo.Id);
            }
            else
            {
                _logger.LogWarning("登录失败: {ErrorMessage}", response.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "登录过程中发生错误: {ErrorMessage}", ex.Message);
            if (ex.InnerException != null)
            {
                _logger.LogError("内部错误: {InnerError}", ex.InnerException.Message);
            }
            throw;
        }
    }

    /// <summary>
    /// 移动玩家
    /// </summary>
    public async Task MoveAsync(float x, float y, float z)
    {
        if (!_isLoggedIn)
        {
            _logger.LogWarning("尚未登录，无法移动");
            return;
        }

        try
        {
            // 更新本地位置
            _position.X = x;
            _position.Y = y;
            _position.Z = z;

            // 发送移动请求
            await _playerService!.MoveAsync(new MoveRequest { X = x, Y = y, Z = z });

            _logger.LogDebug("已发送移动请求: ({X}, {Y}, {Z})", x, y, z);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送移动请求失败");
        }
    }

    /// <summary>
    /// 启动客户端循环
    /// </summary>
    public async Task RunAsync()
    {
        // 显示欢迎消息
        System.Console.Clear();
        System.Console.WriteLine("==============================");
        System.Console.WriteLine("    PulseRPC 游戏客户端示例");
        System.Console.WriteLine("==============================");
        System.Console.WriteLine();
        System.Console.WriteLine("命令列表:");
        System.Console.WriteLine("  login <用户名> <密码> - 登录游戏");
        System.Console.WriteLine("  move <x> <y> <z>      - 移动角色");
        System.Console.WriteLine("  players               - 显示在线玩家");
        System.Console.WriteLine("  help                  - 显示帮助");
        System.Console.WriteLine("  exit                  - 退出游戏");
        System.Console.WriteLine();

        // 处理用户输入
        var running = true;
        while (running && !_cts!.IsCancellationRequested)
        {
            System.Console.Write("> ");
            var input = System.Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            var parts = input.Split(' ');
            var command = parts[0].ToLower();

            try
            {
                switch (command)
                {
                    case "login":
                        if (parts.Length < 3)
                        {
                            System.Console.WriteLine("用法: login <用户名> <密码>");
                        }
                        else
                        {
                            await LoginAsync(parts[1], parts[2]);
                            if (_isLoggedIn)
                                System.Console.WriteLine($"欢迎, {_playerInfo!.Username}!");
                        }

                        break;

                    case "move":
                        if (parts.Length < 4)
                        {
                            System.Console.WriteLine("用法: move <x> <y> <z>");
                        }
                        else if (_isLoggedIn)
                        {
                            var x = float.Parse(parts[1]);
                            var y = float.Parse(parts[2]);
                            var z = float.Parse(parts[3]);
                            await MoveAsync(x, y, z);
                            System.Console.WriteLine($"已移动到 ({x}, {y}, {z})");
                        }
                        else
                        {
                            System.Console.WriteLine("请先登录");
                        }

                        break;

                    case "players":
                        DisplayPlayers();
                        break;

                    case "help":
                        System.Console.WriteLine("命令列表:");
                        System.Console.WriteLine("  login <用户名> <密码> - 登录游戏");
                        System.Console.WriteLine("  move <x> <y> <z>      - 移动角色");
                        System.Console.WriteLine("  players               - 显示在线玩家");
                        System.Console.WriteLine("  help                  - 显示帮助");
                        System.Console.WriteLine("  exit                  - 退出游戏");
                        break;

                    case "exit":
                        running = false;
                        break;

                    default:
                        System.Console.WriteLine($"未知命令: {command}，输入 help 查看帮助");
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine($"错误: {ex.Message}");
                System.Console.ResetColor();
            }
        }
    }

    /// <summary>
    /// 显示在线玩家
    /// </summary>
    private void DisplayPlayers()
    {
        if (!_isLoggedIn)
        {
            System.Console.WriteLine("请先登录");
            return;
        }

        System.Console.WriteLine("\n在线玩家列表:");
        System.Console.WriteLine($"* {_playerInfo!.Username} (你) - 位置: ({_position.X}, {_position.Y}, {_position.Z})");

        foreach (var player in _otherPlayers.Values)
        {
            System.Console.WriteLine(
                $"* {player.Name} - 位置: ({player.Position.X}, {player.Position.Y}, {player.Position.Z})");
        }

        System.Console.WriteLine();
    }

    /// <summary>
    /// 添加玩家
    /// </summary>
    internal void AddPlayer(Guid playerId, string playerName, Vector3 position)
    {
        if (_playerInfo != null && playerId == _playerInfo.Id)
            return; // 忽略自己

        _otherPlayers[playerId] = new PlayerData { Id = playerId, Name = playerName, Position = position };

        System.Console.ForegroundColor = ConsoleColor.Green;
        System.Console.WriteLine($"\n玩家 {playerName} 加入了游戏\n> ");
        System.Console.ResetColor();
    }

    /// <summary>
    /// 移除玩家
    /// </summary>
    internal void RemovePlayer(Guid playerId, string reason)
    {
        if (_otherPlayers.TryGetValue(playerId, out var player))
        {
            _otherPlayers.Remove(playerId);

            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine($"\n玩家 {player.Name} 离开了游戏 ({reason})\n> ");
            System.Console.ResetColor();
        }
    }

    /// <summary>
    /// 更新玩家位置
    /// </summary>
    internal void UpdatePlayerPosition(Guid playerId, Vector3 position)
    {
        if (_playerInfo != null && playerId == _playerInfo.Id)
            return; // 忽略自己

        if (_otherPlayers.TryGetValue(playerId, out var player))
        {
            player.Position = position;
        }
    }

    /// <summary>
    /// 关闭客户端
    /// </summary>
    public async Task ShutdownAsync()
    {
        _logger.LogInformation("正在关闭客户端...");

        // 取消订阅
        _eventsSubscription?.Dispose();

        // 取消操作
        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts = null;
        }

        // 释放资源
        _client?.Dispose();
        _cts?.Dispose();

        _logger.LogInformation("客户端已关闭");
    }

    /// <summary>
    /// 玩家数据
    /// </summary>
    internal class PlayerData
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Vector3 Position { get; set; } = Vector3.Zero;
    }

    /// <summary>
    /// 简单的组合订阅令牌
    /// </summary>
    internal class CompositeSubscriptionToken : ISubscriptionToken
    {
        private readonly ISubscriptionToken[] _tokens;
        private bool _isDisposed;

        public Guid Id { get; } = Guid.NewGuid();
        public bool IsActive => !_isDisposed;
        public bool IsUnsubscribed => _isDisposed;

        public CompositeSubscriptionToken(ISubscriptionToken[] tokens)
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        }

        public void Unsubscribe()
        {
            if (_isDisposed) return;

            foreach (var token in _tokens)
            {
                try
                {
                    token.Unsubscribe();
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"Error unsubscribing token {token.Id}: {ex.Message}");
                }
            }

            _isDisposed = true;
        }

        public void Dispose()
        {
            Unsubscribe();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// 玩家事件处理器
    /// </summary>
    private class PlayerEventsHandler : IPlayerLoginEvents, IPlayerMovementEvents
    {
        private readonly GameConsoleClient _client;

        public PlayerEventsHandler(GameConsoleClient client)
        {
            _client = client;
        }

        public void OnPlayerJoined(PlayerJoinedEvent eventData)
        {
            var position = eventData.Position != Vector3.Zero
                ? eventData.Position
                : new Vector3(eventData.X, eventData.Y, eventData.Z);
            _client.AddPlayer(eventData.PlayerId, eventData.PlayerName, position);
        }

        public void OnPlayerLeft(PlayerLeftEvent eventData)
        {
            _client.RemovePlayer(eventData.PlayerId, eventData.Reason);
        }

        public void OnPlayerMoved(PlayerMovedEvent eventData)
        {
            _client.UpdatePlayerPosition(eventData.PlayerId,
                new Vector3(eventData.X, eventData.Y, eventData.Z));
        }

        public void OnPlayersMovedBatch(PlayerMovedEvent[] eventData)
        {
            foreach (var update in eventData)
            {
                OnPlayerMoved(update);
            }
        }

        public void OnPlayersMovedBatch(PlayersBatchMovedEvent eventData)
        {
            if (eventData.Updates == null)
            {
                return;
            }

            foreach (var update in eventData.Updates)
            {
                OnPlayerMoved(update);
            }
        }
    }
}
