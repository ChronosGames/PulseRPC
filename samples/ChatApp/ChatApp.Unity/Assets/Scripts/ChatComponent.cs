using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Client.Channels;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Transport;

namespace ChatApp.Unity
{
    /// <summary>
    /// 整合的聊天游戏组件 - 包含完整的网络功能和UI交互
    /// </summary>
    [PulseClientGeneration(typeof(IPlayerHub))]
    [PulseClientGeneration(typeof(IPlayerLoginEvents))]
    [PulseClientGeneration(typeof(IPlayerMovementEvents))]
    public class ChatComponent : MonoBehaviour
    {
        [Header("UI组件")] public Text ChatText;
        public Button JoinOrLeaveButton;
        public Text JoinOrLeaveButtonText;
        public Button SendMessageButton;
        public InputField Input;
        public InputField ReportInput;
        public Button SendReportButton;
        public Button DisconnectButton;
        public Button ExceptionButton;
        public Button UnaryExceptionButton;
        public Text LabelRtt;

        [Header("移动控制UI")] public Button MoveForwardButton;
        public Button MoveBackwardButton;
        public Button MoveLeftButton;
        public Button MoveRightButton;
        public Text PlayerInfoText;
        public Text PositionText;

        [Header("服务器配置")] [SerializeField] private string _host = "localhost";
        [SerializeField] private int _tcpPort = 7000;
        [SerializeField] private int _kcpPort = 7001;

        [Header("游戏设置")] [SerializeField] private string _username = "Player";
        [SerializeField] private string _password = "password";
        [SerializeField] private float _moveDistance = 1.0f;

        [Header("场景控制器")]
        [SerializeField] private GameSceneController _sceneController;

        // 状态更新事件 - 从UnityGameClient合并的事件系统
        public event Action<string> OnStatusUpdate;
        public event Action<PlayerInfo> OnLoginSuccess;
        public event Action<string> OnLoginFailed;
        public event Action<Guid, string, System.Numerics.Vector3> OnPlayerJoined;
        public event Action<Guid, string> OnPlayerLeft;
        public event Action<Guid, System.Numerics.Vector3> OnPlayerMoved;

        // 网络组件
        private IChannelManager _channelManager;
        private IPlayerHub _playerService;
        private ISubscriptionToken _eventsSubscription;
        private CancellationTokenSource _cts;

        // 玩家状态
        private bool _isLoggedIn;
        private bool _isConnected;
        private PlayerInfo _playerInfo;
        private System.Numerics.Vector3 _position = new System.Numerics.Vector3();
        private readonly Dictionary<Guid, PlayerData> _otherPlayers = new Dictionary<Guid, PlayerData>();

        // UI状态
        private bool isJoin;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);

            // 查找场景控制器（如果未指定）
            if (_sceneController == null)
                _sceneController = FindObjectOfType<GameSceneController>();
        }

        async void Start()
        {
            InitializeUi();
            UpdateStatus("正在初始化游戏客户端...");

            try
            {
                await InitializeNetworkAsync();
                UpdateStatus("网络组件初始化完成，等待用户连接...");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatComponent] 初始化失败: {ex.Message}");
                UpdateStatus($"初始化失败: {ex.Message}");
            }
        }

        private void InitializeUi()
        {
            this.isJoin = false;
            this._isConnected = false;
            this._isLoggedIn = false;

            // 初始化聊天UI
            if (SendMessageButton != null)
            {
                this.SendMessageButton.interactable = false;
                this.SendMessageButton.onClick.AddListener(SendMessage);
            }

            if (ChatText != null)
                this.ChatText.text = string.Empty;

            if (Input != null)
            {
                this.Input.text = string.Empty;
                if (Input.placeholder != null)
                    this.Input.placeholder.GetComponent<Text>().text = "等待连接...";
            }

            if (JoinOrLeaveButton != null)
            {
                this.JoinOrLeaveButton.onClick.AddListener(JoinOrLeave);
            }

            if (JoinOrLeaveButtonText)
                this.JoinOrLeaveButtonText.text = "连接服务器";

            if (DisconnectButton)
            {
                this.DisconnectButton.onClick.AddListener(DisconnectServer);
                this.DisconnectButton.interactable = false;
            }

            // 初始化移动控制UI
            if (MoveForwardButton != null)
                MoveForwardButton.onClick.AddListener(MoveForward);

            if (MoveBackwardButton != null)
                MoveBackwardButton.onClick.AddListener(MoveBackward);

            if (MoveLeftButton != null)
                MoveLeftButton.onClick.AddListener(MoveLeft);

            if (MoveRightButton != null)
                MoveRightButton.onClick.AddListener(MoveRight);

            // 禁用移动按钮直到连接成功
            SetMoveButtonsEnabled(false);

            if (ExceptionButton != null)
                this.ExceptionButton.interactable = false;

            UpdatePlayerInfoDisplay();
            UpdatePositionDisplay();
        }

        #region 网络功能

        /// <summary>
        /// 初始化网络连接
        /// </summary>
        private async Task InitializeNetworkAsync()
        {
            Debug.Log("[ChatComponent] 正在初始化网络组件...");

            _cts = new CancellationTokenSource();

            // 创建序列化器
            var serializer = PulseRPCSerializerProvider.Instance;

            // 创建传输工厂
            _transportFactory = new TransportFactory();

            // 创建通道管理器
            _channelManager = new ChannelManager();

            // 创建TCP通道 - 用于可靠消息传输
            var tcpOptions = new TransportOptions
            {
                NoDelay = true,
                KeepAlive = true,
                AutoReconnect = true
            };
            var tcpTransport = await _transportFactory.CreateTransportAsync(TransportType.Tcp, tcpOptions);
            var tcpChannel = new TransportChannel("TcpChannel", tcpTransport, serializer, null);
            _channelManager.RegisterChannel("TcpChannel", tcpChannel, true);

            // 创建KCP通道 - 用于低延迟游戏数据传输
            var kcpOptions = new TransportOptions
            {
                Kcp = new KcpOptions
                {
                    NoDelay = 1,               // 无延迟模式
                    Interval = 10,             // 10ms更新间隔
                    Resend = 2,                // 快重传
                    DisableFlowControl = true  // 关闭拥塞控制
                }
            };
            var kcpTransport = await _transportFactory.CreateTransportAsync(TransportType.Kcp, kcpOptions);
            var kcpChannel = new TransportChannel("KcpChannel", kcpTransport, serializer, null);
            _channelManager.RegisterChannel("KcpChannel", kcpChannel);

            // 获取服务代理
            _playerService = _channelManager.GetPlayerHub();

            // 设置事件处理器
            SetupEventHandlers();

            UpdateStatus("网络组件初始化完成");
        }

        private void SetupEventHandlers()
        {
            try
            {
                var eventsHandler = new PlayerEventsHandler(this);
                var tcpMessageChannel = _channelManager.GetChannel("TcpChannel");
                var kcpMessageChannel = _channelManager.GetChannel("KcpChannel");

                // 登录事件 (TCP通道)
                var loginJoinedToken = tcpMessageChannel.SubscribeToEvent<PlayerJoinedEvent>("OnPlayerJoined", (sender, eventData) => eventsHandler.OnPlayerJoined(eventData));
                var loginLeftToken = tcpMessageChannel.SubscribeToEvent<PlayerLeftEvent>("OnPlayerLeft", (sender, eventData) => eventsHandler.OnPlayerLeft(eventData));

                // 移动事件 (KCP通道)
                var moveToken = kcpMessageChannel.SubscribeToEvent<PlayerMovedEvent>("OnPlayerMoved",
                    (sender, eventData) => eventsHandler.OnPlayerMoved(eventData));
                var moveBatchToken = kcpMessageChannel.SubscribeToEvent<PlayerMovedEvent[]>("OnPlayersMovedBatch",
                    (sender, eventData) => eventsHandler.OnPlayersMovedBatch(eventData));
                var moveBatchToken2 = kcpMessageChannel.SubscribeToEvent<PlayersBatchMovedEvent>("OnPlayersMovedBatch",
                    (sender, eventData) => eventsHandler.OnPlayersMovedBatch(eventData));

                _eventsSubscription = new CompositeSubscriptionToken(new[]
                {
                    loginJoinedToken, loginLeftToken, moveToken, moveBatchToken, moveBatchToken2
                });

                UpdateStatus("事件处理器设置完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatComponent] 事件处理器设置失败: {ex.Message}");
                UpdateStatus($"事件处理器设置失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        private async Task ConnectToServerAsync()
        {
            Debug.Log($"[ChatComponent] 正在连接到服务器 {_host}...");
            UpdateStatus($"正在连接到服务器 {_host}...");

            try
            {
                // 连接TCP通道
                var tcpChannel = _channelManager.GetChannel("TcpChannel");
                await tcpChannel.ConnectAsync(_host, _tcpPort);

                // 连接KCP通道
                var kcpChannel = _channelManager.GetChannel("KcpChannel");
                await kcpChannel.ConnectAsync(_host, _kcpPort);

                _isConnected = true;
                Debug.Log("[ChatComponent] 已连接到服务器");
                UpdateStatus("已连接到服务器");

                // 自动登录
                await LoginAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatComponent] 连接服务器失败: {ex.Message}");
                UpdateStatus($"连接服务器失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 登录
        /// </summary>
        private async Task LoginAsync()
        {
            Debug.Log($"[ChatComponent] 正在登录，用户名: {_username}");
            UpdateStatus($"正在登录用户: {_username}...");

            try
            {
                var request = new LoginRequest { Username = _username, Password = _password };
                var response = await _playerService.LoginAsync(request);

                if (response.Success)
                {
                    _isLoggedIn = true;
                    _playerInfo = response.Player;

                    Debug.Log($"[ChatComponent] 登录成功: {_playerInfo.Username} (ID: {_playerInfo.Id})");
                    UpdateStatus($"登录成功: {_playerInfo.Username}");
                    AppendChatMessage($"[系统] 欢迎, {_playerInfo.Username}!");

                    // 更新UI
                    UpdateLoginUI(true);

                    // 更新场景控制器
                    if (_sceneController != null)
                    {
                        _sceneController.UpdatePlayerInfo(_playerInfo.Username, _playerInfo.Id);
                    }

                    // 触发登录成功事件
                    OnLoginSuccess?.Invoke(_playerInfo);
                }
                else
                {
                    Debug.LogWarning($"[ChatComponent] 登录失败: {response.ErrorMessage}");
                    UpdateStatus($"登录失败: {response.ErrorMessage}");
                    AppendChatMessage($"[错误] 登录失败: {response.ErrorMessage}");
                    OnLoginFailed?.Invoke(response.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatComponent] 登录过程中发生错误: {ex.Message}");
                UpdateStatus($"登录错误: {ex.Message}");
                AppendChatMessage($"[错误] 登录失败: {ex.Message}");
                OnLoginFailed?.Invoke(ex.Message);
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
                _position = new System.Numerics.Vector3(
                    _position.X + x,
                    _position.Y + y,
                    _position.Z + z);

                // 发送移动请求
                await _playerService.MoveAsync(new MoveRequest
                {
                    X = _position.X,
                    Y = _position.Y,
                    Z = _position.Z
                });

                Debug.Log($"[ChatComponent] 已移动到 ({_position.X}, {_position.Y}, {_position.Z})");
                UpdatePositionDisplay();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatComponent] 移动请求失败: {ex.Message}");
                UpdateStatus($"移动请求失败: {ex.Message}");
                AppendChatMessage($"[错误] 移动失败: {ex.Message}");
            }
        }

        #endregion

        #region UI事件处理

        public async void JoinOrLeave()
        {
            if (!_isConnected)
            {
                try
                {
                    if (JoinOrLeaveButtonText != null)
                        JoinOrLeaveButtonText.text = "连接中...";

                    await ConnectToServerAsync();
                }
                catch (Exception ex)
                {
                    if (JoinOrLeaveButtonText != null)
                        JoinOrLeaveButtonText.text = "连接服务器";

                    Debug.LogError($"连接失败: {ex.Message}");
                }
            }
            else
            {
                // 断开连接逻辑
                await DisconnectFromServerAsync();
            }
        }

        public async void SendMessage()
        {
            if (!string.IsNullOrEmpty(Input?.text))
            {
                var inputText = Input.text.Trim();

                // 尝试解析输入为移动坐标
                if (inputText.Contains(","))
                {
                    var parts = inputText.Split(',');
                    if (parts.Length >= 2 &&
                        float.TryParse(parts[0].Trim(), out float x) &&
                        float.TryParse(parts[1].Trim(), out float z))
                    {
                        float y = parts.Length >= 3 && float.TryParse(parts[2].Trim(), out float yValue) ? yValue : 0;
                        await MoveAsync(x, y, z);
                    }
                    else
                    {
                        AppendChatMessage($"[错误] 无效的坐标格式，请使用 x,z 或 x,y,z");
                    }
                }
                else
                {
                    // 作为普通消息处理
                    AppendChatMessage($"[{_username}] {inputText}");
                }

                Input.text = string.Empty;
            }
        }

        public async void DisconnectServer()
        {
            await DisconnectFromServerAsync();
        }

        private async Task DisconnectFromServerAsync()
        {
            Debug.Log("[ChatComponent] 正在断开连接...");
            UpdateStatus("正在断开连接...");

            try
            {
                // 清理事件订阅
                _eventsSubscription?.Dispose();
                _eventsSubscription = null;

                // 关闭通道
                if (_channelManager != null)
                {
                    _channelManager.Dispose();
                    _channelManager = null;
                }

                _isConnected = false;
                _isLoggedIn = false;
                _playerInfo = null;
                _otherPlayers.Clear();

                // 更新UI
                UpdateLoginUI(false);
                AppendChatMessage("[系统] 已断开连接");
                UpdateStatus("已断开连接");

                // 重新初始化网络组件
                await InitializeNetworkAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatComponent] 断开连接时发生错误: {ex.Message}");
                UpdateStatus($"断开连接错误: {ex.Message}");
            }
        }

        #endregion

        #region 移动控制

        public async void MoveForward()
        {
            await MoveAsync(0, 0, _moveDistance);
        }

        public async void MoveBackward()
        {
            await MoveAsync(0, 0, -_moveDistance);
        }

        public async void MoveLeft()
        {
            await MoveAsync(-_moveDistance, 0, 0);
        }

        public async void MoveRight()
        {
            await MoveAsync(_moveDistance, 0, 0);
        }

        #endregion

        #region 状态更新和显示

        /// <summary>
        /// 更新状态并触发事件
        /// </summary>
        private void UpdateStatus(string status)
        {
            Debug.Log($"[ChatComponent] 状态: {status}");

            // 触发状态更新事件
            OnStatusUpdate?.Invoke(status);

            // 更新场景控制器
            if (_sceneController != null)
            {
                _sceneController.UpdateStatus(status);
            }
        }

        private void Update()
        {
            // RTT更新
            if (LabelRtt != null && _channelManager != null)
            {
                try
                {
                    var channel = _channelManager.GetChannel("TcpChannel");
                    // TODO: 实现RTT显示
                    LabelRtt.text = "RTT: --ms";
                }
                catch
                {
                    LabelRtt.text = "RTT: --ms";
                }
            }
        }

        private void AppendChatMessage(string message)
        {
            if (ChatText != null)
            {
                ChatText.text += $"\n{DateTime.Now:HH:mm:ss} {message}";

                // 保持聊天记录在合理长度
                var lines = ChatText.text.Split('\n');
                if (lines.Length > 50)
                {
                    ChatText.text = string.Join("\n", lines, lines.Length - 50, 50);
                }
            }
        }

        private void UpdateLoginUI(bool isLoggedIn)
        {
            if (JoinOrLeaveButtonText != null)
            {
                JoinOrLeaveButtonText.text = isLoggedIn ? "断开连接" : "连接服务器";
            }

            if (SendMessageButton != null)
            {
                SendMessageButton.interactable = isLoggedIn;
            }

            if (Input != null && Input.placeholder != null)
            {
                var placeholderText = Input.placeholder.GetComponent<Text>();
                placeholderText.text = isLoggedIn ? "输入消息..." : "等待连接...";
            }

            if (DisconnectButton != null)
            {
                DisconnectButton.interactable = isLoggedIn;
            }

            if (ExceptionButton != null)
            {
                ExceptionButton.interactable = isLoggedIn;
            }

            // 更新移动按钮状态
            SetMoveButtonsEnabled(isLoggedIn);

            UpdatePlayerInfoDisplay();
        }

        private void SetMoveButtonsEnabled(bool enabled)
        {
            if (MoveForwardButton != null) MoveForwardButton.interactable = enabled;
            if (MoveBackwardButton != null) MoveBackwardButton.interactable = enabled;
            if (MoveLeftButton != null) MoveLeftButton.interactable = enabled;
            if (MoveRightButton != null) MoveRightButton.interactable = enabled;
        }

        private void UpdatePlayerInfoDisplay()
        {
            if (PlayerInfoText != null)
            {
                if (_isLoggedIn && _playerInfo != null)
                {
                    PlayerInfoText.text = $"玩家: {_playerInfo.Username}\nID: {_playerInfo.Id}\n在线玩家: {_otherPlayers.Count + 1}";
                }
                else
                {
                    PlayerInfoText.text = "未登录";
                }
            }
        }

        private void UpdatePositionDisplay()
        {
            if (PositionText != null)
            {
                PositionText.text = $"位置: ({_position.X:F1}, {_position.Y:F1}, {_position.Z:F1})";
            }
        }

        #endregion

        #region 玩家管理

        /// <summary>
        /// 添加新玩家
        /// </summary>
        internal void AddPlayer(Guid playerId, string playerName, System.Numerics.Vector3 position)
        {
            if (_playerInfo != null && playerId == _playerInfo.Id)
                return; // 忽略自己

            if (_otherPlayers.ContainsKey(playerId))
                return;

            var playerData = new PlayerData
            {
                Id = playerId,
                Name = playerName,
                Position = position
            };

            _otherPlayers[playerId] = playerData;

            // 触发事件
            OnPlayerJoined?.Invoke(playerId, playerName, position);

            AppendChatMessage($"[系统] 玩家 {playerName} 加入了游戏");
            UpdatePlayerInfoDisplay();

            Debug.Log($"[ChatComponent] 玩家加入: {playerName} ({playerId})");
        }

        /// <summary>
        /// 移除玩家
        /// </summary>
        internal void RemovePlayer(Guid playerId, string reason)
        {
            if (!_otherPlayers.TryGetValue(playerId, out var player))
                return;

            Debug.Log($"[ChatComponent] 玩家 {player.Name} (ID: {playerId}) 已离开游戏，原因: {reason}");
            AppendChatMessage($"[系统] 玩家 {player.Name} 离开了游戏: {reason}");

            // 触发玩家离开事件
            OnPlayerLeft?.Invoke(playerId, reason);

            _otherPlayers.Remove(playerId);
            UpdatePlayerInfoDisplay();
        }

        /// <summary>
        /// 更新玩家位置
        /// </summary>
        internal void UpdatePlayerPosition(Guid playerId, System.Numerics.Vector3 position)
        {
            if (_playerInfo != null && playerId == _playerInfo.Id)
                return; // 忽略自己

            if (_otherPlayers.TryGetValue(playerId, out var playerData))
            {
                playerData.Position = position;

                // 触发事件
                OnPlayerMoved?.Invoke(playerId, position);
            }
        }

        #endregion

        #region 生命周期管理

        async void OnDestroy()
        {
            await ShutdownAsync();
        }

        private async Task ShutdownAsync()
        {
            Debug.Log("[ChatComponent] 正在关闭组件...");
            UpdateStatus("正在关闭...");

            // 取消所有任务
            _cts?.Cancel();

            // 清理事件订阅
            _eventsSubscription?.Dispose();
            _eventsSubscription = null;

            // 关闭通道
            if (_channelManager != null)
            {
                _channelManager.Dispose();
                _channelManager = null;
            }

            Debug.Log("[ChatComponent] 组件已关闭");
            UpdateStatus("已关闭");
        }

        #endregion

        #region 调试功能

        [ContextMenu("显示在线玩家")]
        private void DisplayPlayers()
        {
            if (!_isLoggedIn)
            {
                Debug.Log("[ChatComponent] 请先登录");
                return;
            }

            Debug.Log($"[ChatComponent] 在线玩家列表:");
            Debug.Log($"* {_playerInfo.Username} (你) - 位置: ({_position.X}, {_position.Y}, {_position.Z})");

            foreach (var player in _otherPlayers.Values)
            {
                Debug.Log($"* {player.Name} - 位置: ({player.Position.X}, {player.Position.Y}, {player.Position.Z})");
            }
        }

        [ContextMenu("测试连接")]
        private async void TestConnection()
        {
            if (_channelManager != null && !_isConnected)
            {
                Debug.Log("[ChatComponent] 正在测试连接...");
                UpdateStatus("正在测试连接...");

                try
                {
                    await ConnectToServerAsync();
                    Debug.Log("[ChatComponent] 连接测试成功");
                    UpdateStatus("连接测试成功");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ChatComponent] 连接测试失败: {ex.Message}");
                    UpdateStatus($"连接测试失败: {ex.Message}");
                }
            }
        }

        [ContextMenu("测试移动")]
        private async void TestMove()
        {
            if (_isLoggedIn)
            {
                var random = new System.Random();
                await MoveAsync(
                    random.Next(-2, 3) * _moveDistance,
                    0,
                    random.Next(-2, 3) * _moveDistance
                );
            }
        }

        #endregion

        #region 内部类

        /// <summary>
        /// 玩家数据
        /// </summary>
        internal class PlayerData
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public System.Numerics.Vector3 Position { get; set; } = new System.Numerics.Vector3();
        }

        /// <summary>
        /// 玩家事件处理器
        /// </summary>
        private class PlayerEventsHandler : IPlayerLoginEvents, IPlayerMovementEvents
        {
            private readonly ChatComponent _component;

            public PlayerEventsHandler(ChatComponent component)
            {
                _component = component;
            }

            public void OnPlayerJoined(PlayerJoinedEvent eventData)
            {
                var position = eventData.Position != System.Numerics.Vector3.Zero
                    ? eventData.Position
                    : new System.Numerics.Vector3(eventData.X, eventData.Y, eventData.Z);
                _component.AddPlayer(eventData.PlayerId, eventData.PlayerName, position);
            }

            public void OnPlayerLeft(PlayerLeftEvent eventData)
            {
                _component.RemovePlayer(eventData.PlayerId, eventData.Reason);
            }

            public void OnPlayerMoved(PlayerMovedEvent eventData)
            {
                _component.UpdatePlayerPosition(eventData.PlayerId,
                    new System.Numerics.Vector3(eventData.X, eventData.Y, eventData.Z));
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
                foreach (var moveEvent in eventData.Updates)
                {
                    OnPlayerMoved(moveEvent);
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

        #endregion
    }
}
