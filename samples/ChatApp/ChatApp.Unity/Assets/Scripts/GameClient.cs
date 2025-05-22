using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChatApp.Shared;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Client.Channels;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Transport;
using UnityEngine;

namespace ChatApp.Unity
{
    /// <summary>
    /// 游戏客户端Unity实现
    /// </summary>
    [PulseClientGeneration(typeof(IPlayerService))]
    [PulseClientGeneration(typeof(IPlayerLoginEvents))]
    [PulseClientGeneration(typeof(IPlayerMovementEvents))]
    public class GameClient : MonoBehaviour
    {
        [Header("服务器配置")]
        [SerializeField] private string _host = "localhost";
        [SerializeField] private int _tcpPort = 7000;
        [SerializeField] private int _kcpPort = 7001;

        [Header("游戏设置")]
        [SerializeField] private string _username = "Player";
        [SerializeField] private string _password = "password";

        [Header("场景控制器")]
        [SerializeField] private GameSceneController _sceneController;

        // 状态更新事件
        public event Action<string> OnStatusUpdate;
        public event Action<PlayerInfo> OnLoginSuccess;
        public event Action<string> OnLoginFailed;
        public event Action<Guid, string, PulseRPC.Shared.Vector3> OnPlayerJoined;
        public event Action<Guid, string> OnPlayerLeft;
        public event Action<Guid, PulseRPC.Shared.Vector3> OnPlayerMoved;

        // 日志
        private ILogger _logger;

        // 通道管理
        private IChannelManager _channelManager;
        private TransportFactory _transportFactory;

        // 服务代理
        private IPlayerService _playerService;

        // 事件订阅
        private ISubscriptionToken _eventsSubscription;
        private CancellationTokenSource _cts;

        // 玩家状态
        private bool _isLoggedIn;
        private PlayerInfo _playerInfo;
        private Vector3 _position = new Vector3();

        // 其他玩家信息
        private readonly Dictionary<Guid, PlayerData> _otherPlayers = new Dictionary<Guid, PlayerData>();

        private void Awake()
        {
            _logger = Debug.unityLogger;
            _cts = new CancellationTokenSource();
            DontDestroyOnLoad(gameObject);

            // 查找场景控制器（如果未指定）
            if (_sceneController == null)
                _sceneController = FindObjectOfType<GameSceneController>();
        }

        private async void Start()
        {
            UpdateStatus("正在初始化游戏客户端...");
            await InitializeAsync();

            UpdateStatus("正在连接到服务器...");
            await ConnectAsync(_host, _tcpPort, _kcpPort);

            UpdateStatus("正在登录...");
            await LoginAsync(_username, _password);
        }

        /// <summary>
        /// 初始化客户端
        /// </summary>
        private async Task InitializeAsync()
        {
            _logger.Log(LogType.Log, "GameClient", "正在初始化游戏客户端...");

            // 创建序列化器
            var serializer = new PulseRPCSerializer();

            // 创建传输工厂
            _transportFactory = new TransportFactory();

            // 创建通道管理器
            _channelManager = new ChannelManager();

            // 创建TCP通道
            var tcpOptions = new TransportOptions { NoDelay = true, KeepAlive = true, AutoReconnect = true };

            var tcpTransport = await _transportFactory.CreateClientTransportAsync(
                TransportType.Tcp, tcpOptions);

            var tcpChannel = new TransportChannel(
                "TcpChannel",
                tcpTransport,
                serializer,
                null); // Unity环境中不需要传递Logger

            _channelManager.RegisterChannel("TcpChannel", tcpChannel, true);

            // 创建KCP通道
            var kcpOptions = new TransportOptions
            {
                Kcp = new KcpOptions { NoDelay = 1, Interval = 10, Resend = 2, DisableFlowControl = false }
            };

            var kcpTransport = await _transportFactory.CreateClientTransportAsync(
                TransportType.Kcp, kcpOptions);

            var kcpChannel = new TransportChannel(
                "KcpChannel",
                kcpTransport,
                serializer,
                null); // Unity环境中不需要传递Logger

            _channelManager.RegisterChannel("KcpChannel", kcpChannel);

            try
            {
                // 获取服务代理
                _playerService = _channelManager.GetPlayerService();

                // 创建事件处理器实例
                var eventsHandler = new PlayerEventsHandler(this);

                try
                {
                    // 获取事件处理器 - 直接在通道上注册事件处理程序
                    var tcpMessageChannel = _channelManager.GetChannel("TcpChannel");
                    var kcpMessageChannel = _channelManager.GetChannel("KcpChannel");

                    // 登录事件 (TCP通道)
                    var loginJoinedToken = tcpMessageChannel.SubscribeToEvent<PlayerJoinedEvent>("OnPlayerJoined",
                        (sender, eventData) => eventsHandler.OnPlayerJoined(eventData));
                    var loginLeftToken = tcpMessageChannel.SubscribeToEvent<PlayerLeftEvent>("OnPlayerLeft",
                        (sender, eventData) => eventsHandler.OnPlayerLeft(eventData));

                    // 移动事件 (KCP通道)
                    var moveToken = kcpMessageChannel.SubscribeToEvent<PlayerMovedEvent>("OnPlayerMoved",
                        (sender, eventData) => eventsHandler.OnPlayerMoved(eventData));
                    var moveBatchToken = kcpMessageChannel.SubscribeToEvent<PlayerMovedEvent[]>("OnPlayersMovedBatch",
                        (sender, eventData) => eventsHandler.OnPlayersMovedBatch(eventData));
                    var moveBatchToken2 = kcpMessageChannel.SubscribeToEvent<PlayersBatchMovedEvent>("OnPlayersMovedBatch",
                        (sender, eventData) => eventsHandler.OnPlayersMovedBatch(eventData));

                    // 保存订阅令牌
                    _eventsSubscription = new CompositeSubscriptionToken(new[] {
                        loginJoinedToken, loginLeftToken, moveToken, moveBatchToken, moveBatchToken2
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError("GameClient", $"事件处理器初始化失败: {ex.Message}");
                    UpdateStatus($"事件处理器初始化失败: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("GameClient", $"服务代理或事件处理器初始化失败: {ex.Message}");
                UpdateStatus($"初始化失败: {ex.Message}");
                throw;
            }

            _logger.Log(LogType.Log, "GameClient", "客户端初始化完成");
            UpdateStatus("客户端初始化完成");
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        private async Task ConnectAsync(string host, int tcpPort, int kcpPort)
        {
            _logger.Log(LogType.Log, "GameClient", $"正在连接到服务器 {host}...");

            try
            {
                // 连接TCP通道
                var tcpChannel = _channelManager.GetChannel("TcpChannel") as IHasTransport;
                await tcpChannel.ConnectAsync(host, tcpPort);

                // 连接KCP通道
                var kcpChannel = _channelManager.GetChannel("KcpChannel") as IHasTransport;
                await kcpChannel.ConnectAsync(host, kcpPort);

                _logger.Log(LogType.Log, "GameClient", "已连接到服务器");
                UpdateStatus("已连接到服务器");
            }
            catch (Exception ex)
            {
                _logger.LogError("GameClient", $"连接服务器失败: {ex.Message}");
                UpdateStatus($"连接服务器失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 登录
        /// </summary>
        private async Task LoginAsync(string username, string password)
        {
            _logger.Log(LogType.Log, "GameClient", $"正在登录，用户名: {username}");

            try
            {
                var request = new LoginRequest { Username = username, Password = password };
                var response = await _playerService.LoginAsync(request);

                if (response.Success)
                {
                    _isLoggedIn = true;
                    _playerInfo = response.Player;

                    _logger.Log(LogType.Log, "GameClient", $"登录成功: {_playerInfo.Username} (ID: {_playerInfo.Id})");
                    UpdateStatus($"登录成功: {_playerInfo.Username}");

                    // 更新UI和触发事件
                    if (_sceneController != null)
                    {
                        _sceneController.UpdatePlayerInfo(_playerInfo.Username, _playerInfo.Id);
                    }

                    OnLoginSuccess?.Invoke(_playerInfo);
                }
                else
                {
                    _logger.LogWarning("GameClient", $"登录失败: {response.ErrorMessage}");
                    UpdateStatus($"登录失败: {response.ErrorMessage}");

                    OnLoginFailed?.Invoke(response.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("GameClient", $"登录过程中发生错误: {ex.Message}");
                UpdateStatus($"登录错误: {ex.Message}");

                OnLoginFailed?.Invoke(ex.Message);

                if (ex.InnerException != null)
                {
                    _logger.LogError("GameClient", $"内部错误: {ex.InnerException.Message}");
                }
            }
        }

        /// <summary>
        /// 移动角色
        /// </summary>
        public async Task MoveAsync(float x, float y, float z)
        {
            if (!_isLoggedIn || _playerService == null)
                return;

            try
            {
                // 更新本地位置
                _position = new PulseRPC.Shared.Vector3 { X = x, Y = y, Z = z };

                // 发送移动请求
                await _playerService.MoveAsync(new MoveRequest
                {
                    X = x,
                    Y = y,
                    Z = z
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("GameClient", $"移动请求失败: {ex.Message}");
                UpdateStatus($"移动请求失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 添加新玩家
        /// </summary>
        internal void AddPlayer(Guid playerId, string playerName, PulseRPC.Shared.Vector3 position)
        {
            if (_otherPlayers.ContainsKey(playerId))
                return;

            _otherPlayers[playerId] = new PlayerData
            {
                Id = playerId,
                Name = playerName,
                Position = position
            };

            _logger.Log(LogType.Log, "GameClient", $"玩家 {playerName} (ID: {playerId}) 已加入游戏");
            UpdateStatus($"玩家 {playerName} 已加入游戏");

            // 触发玩家加入事件
            OnPlayerJoined?.Invoke(playerId, playerName, position);

            // 在这里可以实例化玩家角色
            // Instantiate(playerPrefab, new Vector3(position.X, position.Y, position.Z), Quaternion.identity);
        }

        /// <summary>
        /// 移除玩家
        /// </summary>
        internal void RemovePlayer(Guid playerId, string reason)
        {
            if (!_otherPlayers.TryGetValue(playerId, out var player))
                return;

            _logger.Log(LogType.Log, "GameClient", $"玩家 {player.Name} (ID: {playerId}) 已离开游戏，原因: {reason}");
            UpdateStatus($"玩家 {player.Name} 已离开游戏，原因: {reason}");

            // 触发玩家离开事件
            OnPlayerLeft?.Invoke(playerId, reason);

            _otherPlayers.Remove(playerId);

            // 在这里可以销毁玩家角色
            // var playerObject = GetPlayerGameObject(playerId);
            // if (playerObject != null) Destroy(playerObject);
        }

        /// <summary>
        /// 更新玩家位置
        /// </summary>
        internal void UpdatePlayerPosition(Guid playerId, PulseRPC.Shared.Vector3 position)
        {
            // 忽略自己的位置更新
            if (_playerInfo != null && playerId == _playerInfo.Id)
                return;

            if (!_otherPlayers.TryGetValue(playerId, out var player))
                return;

            player.Position = position;

            // 触发玩家移动事件
            OnPlayerMoved?.Invoke(playerId, position);

            // 在这里可以更新玩家角色位置
            // var playerObject = GetPlayerGameObject(playerId);
            // if (playerObject != null)
            //    playerObject.transform.position = new Vector3(position.X, position.Y, position.Z);
        }

        private void OnDestroy()
        {
            ShutdownAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 关闭客户端
        /// </summary>
        private async Task ShutdownAsync()
        {
            _logger.Log(LogType.Log, "GameClient", "正在关闭客户端...");
            UpdateStatus("正在关闭客户端...");

            // 取消所有任务
            _cts?.Cancel();

            // 清理事件订阅
            _eventsSubscription?.Dispose();
            _eventsSubscription = null;

            // 关闭通道
            if (_channelManager != null)
            {
                // 尝试登出
                if (_isLoggedIn && _playerService != null)
                {
                    try
                    {
                        await _playerService.LogoutAsync();
                        _logger.Log(LogType.Log, "GameClient", "已登出");
                        UpdateStatus("已登出");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("GameClient", $"登出失败: {ex.Message}");
                        UpdateStatus($"登出失败: {ex.Message}");
                    }
                }

                // 释放通道资源
                _channelManager.Dispose();
                _channelManager = null;
            }

            _logger.Log(LogType.Log, "GameClient", "客户端已关闭");
            UpdateStatus("客户端已关闭");
        }

        /// <summary>
        /// 更新状态并触发事件
        /// </summary>
        private void UpdateStatus(string status)
        {
            // 触发状态更新事件
            OnStatusUpdate?.Invoke(status);

            // 更新场景控制器
            if (_sceneController != null)
            {
                _sceneController.UpdateStatus(status);
            }
        }

        /// <summary>
        /// 玩家数据
        /// </summary>
        internal class PlayerData
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public PulseRPC.Shared.Vector3 Position { get; set; } = new PulseRPC.Shared.Vector3();
        }

        /// <summary>
        /// 玩家事件处理器
        /// </summary>
        private class PlayerEventsHandler : IPlayerLoginEvents, IPlayerMovementEvents
        {
            private readonly GameClient _client;

            public PlayerEventsHandler(GameClient client)
            {
                _client = client;
            }

            public void OnPlayerJoined(PlayerJoinedEvent eventData)
            {
                _client.AddPlayer(eventData.PlayerId, eventData.PlayerName, eventData.Position);
            }

            public void OnPlayerLeft(PlayerLeftEvent eventData)
            {
                _client.RemovePlayer(eventData.PlayerId, eventData.Reason);
            }

            public void OnPlayerMoved(PlayerMovedEvent eventData)
            {
                _client.UpdatePlayerPosition(eventData.PlayerId,
                    new PulseRPC.Shared.Vector3 { X = eventData.X, Y = eventData.Y, Z = eventData.Z });
            }

            public void OnPlayersMovedBatch(PlayerMovedEvent[] eventData)
            {
                foreach (var evt in eventData)
                {
                    OnPlayerMoved(evt);
                }
            }

            public void OnPlayersMovedBatch(PlayersBatchMovedEvent eventData)
            {
                foreach (var evt in eventData.Events)
                {
                    OnPlayerMoved(evt);
                }
            }
        }

        /// <summary>
        /// 组合订阅令牌
        /// </summary>
        private class CompositeSubscriptionToken : ISubscriptionToken
        {
            private readonly ISubscriptionToken[] _tokens;
            private bool _isDisposed;

            public CompositeSubscriptionToken(ISubscriptionToken[] tokens)
            {
                _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            }

            public Guid Id { get; } = Guid.NewGuid();

            public bool IsActive => !_isDisposed;

            public void Unsubscribe()
            {
                if (_isDisposed)
                    return;

                foreach (var token in _tokens)
                {
                    token.Unsubscribe();
                }

                _isDisposed = true;
            }

            public void Dispose()
            {
                Unsubscribe();
                GC.SuppressFinalize(this);
            }
        }
    }
}
