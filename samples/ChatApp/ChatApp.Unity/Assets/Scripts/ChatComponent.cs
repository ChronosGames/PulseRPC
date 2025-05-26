using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using ChatApp.Shared;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Client.Channels;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Transport;

namespace ChatApp
{
    /// <summary>
    /// 整合的聊天游戏组件 - 包含完整的网络功能和UI交互
    /// </summary>
    [PulseClientGeneration(typeof(IPlayerService))]
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

        // 网络组件
        private IChannelManager _channelManager;
        private TransportFactory _transportFactory;
        private IPlayerService _playerService;
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
        }

        async void Start()
        {
            InitializeUi();
            AppendChatMessage("[系统] 初始化游戏客户端...");

            try
            {
                await InitializeNetworkAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatComponent] 初始化失败: {ex.Message}");
                AppendChatMessage($"[错误] 初始化失败: {ex.Message}");
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
            var serializer = new PulseRPCSerializer();

            // 创建传输工厂
            _transportFactory = new TransportFactory();

            // 创建通道管理器
            _channelManager = new ChannelManager();

            // 创建TCP通道
            var tcpOptions = new TransportOptions { NoDelay = true, KeepAlive = true, AutoReconnect = true };
            var tcpTransport = await _transportFactory.CreateClientTransportAsync(TransportType.Tcp, tcpOptions);
            var tcpChannel = new TransportChannel("TcpChannel", tcpTransport, serializer, null);
            _channelManager.RegisterChannel("TcpChannel", tcpChannel, true);

            // 创建KCP通道
            var kcpOptions = new TransportOptions
            {
                Kcp = new KcpOptions { NoDelay = 1, Interval = 10, Resend = 2, DisableFlowControl = false }
            };
            var kcpTransport = await _transportFactory.CreateClientTransportAsync(TransportType.Kcp, kcpOptions);
            var kcpChannel = new TransportChannel("KcpChannel", kcpTransport, serializer, null);
            _channelManager.RegisterChannel("KcpChannel", kcpChannel);

            // 获取服务代理
            _playerService = _channelManager.GetPlayerService();

            // 设置事件处理器
            SetupEventHandlers();

            AppendChatMessage("[系统] 网络组件初始化完成");
        }

        private void SetupEventHandlers()
        {
            try
            {
                var eventsHandler = new PlayerEventsHandler(this);
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

                _eventsSubscription = new CompositeSubscriptionToken(new[]
                {
                    loginJoinedToken, loginLeftToken, moveToken, moveBatchToken, moveBatchToken2
                });

                AppendChatMessage("[系统] 事件处理器设置完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatComponent] 事件处理器设置失败: {ex.Message}");
                AppendChatMessage($"[错误] 事件处理器设置失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        private async Task ConnectToServerAsync()
        {
            if (_isConnected) return;

            AppendChatMessage($"[系统] 正在连接到服务器 {_host}...");

            try
            {
                // 连接TCP通道
                var tcpChannel = _channelManager.GetChannel("TcpChannel") as IHasTransport;
                await tcpChannel.ConnectAsync(_host, _tcpPort);

                // 连接KCP通道
                var kcpChannel = _channelManager.GetChannel("KcpChannel") as IHasTransport;
                await kcpChannel.ConnectAsync(_host, _kcpPort);

                _isConnected = true;
                AppendChatMessage("[系统] 服务器连接成功");

                // 自动登录
                await LoginAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatComponent] 连接失败: {ex.Message}");
                AppendChatMessage($"[错误] 连接失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 登录游戏
        /// </summary>
        private async Task LoginAsync()
        {
            if (_isLoggedIn || !_isConnected) return;

            AppendChatMessage($"[系统] 正在登录用户 {_username}...");

            try
            {
                var request = new LoginRequest { Username = _username, Password = _password };

                var response = await _playerService.LoginAsync(request);

                if (response.Success)
                {
                    _playerInfo = response.Player;
                    // PlayerInfo 没有 Position 属性，初始化为默认位置
                    _position = new System.Numerics.Vector3(0, 0, 0);
                    _isLoggedIn = true;

                    AppendChatMessage($"[系统] 登录成功: {_playerInfo.Username}");
                    UpdateLoginUI(true);
                    UpdatePlayerInfoDisplay();
                    UpdatePositionDisplay();
                }
                else
                {
                    AppendChatMessage($"[错误] 登录失败: {response.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatComponent] 登录失败: {ex.Message}");
                AppendChatMessage($"[错误] 登录失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 移动到指定位置
        /// </summary>
        public async Task MoveAsync(float x, float y, float z)
        {
            if (!_isLoggedIn) return;

            try
            {
                var request = new MoveRequest { X = x, Y = y, Z = z };

                await _playerService.MoveAsync(request);

                _position = new System.Numerics.Vector3(x, y, z);
                UpdatePositionDisplay();
                AppendChatMessage($"[移动] 移动到: ({x:F1}, {y:F1}, {z:F1})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatComponent] 移动失败: {ex.Message}");
                AppendChatMessage($"[错误] 移动失败: {ex.Message}");
            }
        }

        #endregion

        #region UI交互处理

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
                    AppendChatMessage($"[消息] {inputText}");
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
            try
            {
                AppendChatMessage("[系统] 正在断开连接...");

                _isLoggedIn = false;
                _isConnected = false;

                if (_eventsSubscription != null)
                {
                    _eventsSubscription.Unsubscribe();
                    _eventsSubscription = null;
                }

                if (_channelManager != null)
                {
                    // 断开所有通道
                    var tcpChannel = _channelManager.GetChannel("TcpChannel") as IHasTransport;
                    var kcpChannel = _channelManager.GetChannel("KcpChannel") as IHasTransport;

                    await tcpChannel?.DisconnectAsync();
                    await kcpChannel?.DisconnectAsync();
                }

                UpdateLoginUI(false);
                AppendChatMessage("[系统] 已断开连接");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatComponent] 断开连接失败: {ex.Message}");
                AppendChatMessage($"[错误] 断开连接失败: {ex.Message}");
            }
        }

        #endregion

        #region 移动控制

        public async void MoveForward()
        {
            await MoveAsync(_position.X, _position.Y, _position.Z + _moveDistance);
        }

        public async void MoveBackward()
        {
            await MoveAsync(_position.X, _position.Y, _position.Z - _moveDistance);
        }

        public async void MoveLeft()
        {
            await MoveAsync(_position.X - _moveDistance, _position.Y, _position.Z);
        }

        public async void MoveRight()
        {
            await MoveAsync(_position.X + _moveDistance, _position.Y, _position.Z);
        }

        private void Update()
        {
            if (!_isLoggedIn) return;

            // WASD键盘控制
            bool moved = false;
            float newX = _position.X;
            float newZ = _position.Z;

            if (UnityEngine.Input.GetKeyDown(KeyCode.W))
            {
                newZ += _moveDistance;
                moved = true;
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.S))
            {
                newZ -= _moveDistance;
                moved = true;
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.A))
            {
                newX -= _moveDistance;
                moved = true;
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.D))
            {
                newX += _moveDistance;
                moved = true;
            }

            if (moved)
            {
                // 修复：使用异步方式而不是同步等待，避免主线程卡死
                _ = MoveAsync(newX, _position.Y, newZ);
            }
        }

        #endregion

        #region UI更新方法

        private void AppendChatMessage(string message)
        {
            if (ChatText != null)
            {
                ChatText.text += $"{message}\n";
                // 自动滚动到底部
                if (ChatText.text.Length > 2000)
                {
                    ChatText.text = ChatText.text.Substring(ChatText.text.Length - 1500);
                }
            }
        }

        private void UpdateLoginUI(bool isLoggedIn)
        {
            if (JoinOrLeaveButtonText != null)
            {
                JoinOrLeaveButtonText.text = isLoggedIn ? "已连接" : "连接服务器";
            }

            if (SendMessageButton != null)
            {
                SendMessageButton.interactable = isLoggedIn;
            }

            if (DisconnectButton != null)
            {
                DisconnectButton.interactable = isLoggedIn;
            }

            if (Input != null && Input.placeholder != null)
            {
                Input.placeholder.GetComponent<Text>().text = isLoggedIn ? "输入坐标 (x,z) 或消息..." : "等待连接...";
            }

            SetMoveButtonsEnabled(isLoggedIn);
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
                if (_playerInfo != null)
                {
                    PlayerInfoText.text = $"玩家: {_playerInfo.Username}\nID: {_playerInfo.Id}";
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

        #region 玩家事件处理

        internal void AddPlayer(Guid playerId, string playerName, System.Numerics.Vector3 position)
        {
            _otherPlayers[playerId] = new PlayerData { Id = playerId, Name = playerName, Position = position };

            AppendChatMessage($"[玩家] {playerName} 加入了游戏");
        }

        internal void RemovePlayer(Guid playerId, string reason)
        {
            if (_otherPlayers.TryGetValue(playerId, out var player))
            {
                _otherPlayers.Remove(playerId);
                AppendChatMessage($"[玩家] {player.Name} 离开了游戏: {reason}");
            }
        }

        internal void UpdatePlayerPosition(Guid playerId, System.Numerics.Vector3 position)
        {
            if (_otherPlayers.TryGetValue(playerId, out var player))
            {
                player.Position = position;
                AppendChatMessage($"[移动] {player.Name} 移动到: ({position.X:F1}, {position.Y:F1}, {position.Z:F1})");
            }
        }

        #endregion

        #region 清理资源

        async void OnDestroy()
        {
            await ShutdownAsync();
        }

        private async Task ShutdownAsync()
        {
            try
            {
                _cts?.Cancel();

                if (_eventsSubscription != null)
                {
                    _eventsSubscription.Unsubscribe();
                    _eventsSubscription = null;
                }

                if (_channelManager != null)
                {
                    var tcpChannel = _channelManager.GetChannel("TcpChannel") as IHasTransport;
                    var kcpChannel = _channelManager.GetChannel("KcpChannel") as IHasTransport;

                    await tcpChannel?.DisconnectAsync();
                    await kcpChannel?.DisconnectAsync();
                }

                // TransportFactory 不需要手动释放
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatComponent] 清理资源失败: {ex.Message}");
            }
        }

        #endregion

        #region 测试方法

        [ContextMenu("显示在线玩家")]
        private void DisplayPlayers()
        {
            AppendChatMessage($"[系统] 在线玩家数量: {_otherPlayers.Count}");
            foreach (var player in _otherPlayers.Values)
            {
                AppendChatMessage(
                    $"  - {player.Name}: ({player.Position.X:F1}, {player.Position.Y:F1}, {player.Position.Z:F1})");
            }
        }

        #endregion

        #region 内部类

        internal class PlayerData
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public System.Numerics.Vector3 Position { get; set; } = new System.Numerics.Vector3();
        }

        private class PlayerEventsHandler : IPlayerLoginEvents, IPlayerMovementEvents
        {
            private readonly ChatComponent _component;

            public PlayerEventsHandler(ChatComponent component)
            {
                _component = component;
            }

            public void OnPlayerJoined(PlayerJoinedEvent eventData)
            {
                // 转换 Vector3 类型
                var position = new System.Numerics.Vector3(eventData.X, eventData.Y, eventData.Z);
                _component.AddPlayer(eventData.PlayerId, eventData.PlayerName, position);
            }

            public void OnPlayerLeft(PlayerLeftEvent eventData)
            {
                _component.RemovePlayer(eventData.PlayerId, eventData.Reason);
            }

            public void OnPlayerMoved(PlayerMovedEvent eventData)
            {
                // 转换 Vector3 类型
                var position = new System.Numerics.Vector3(eventData.X, eventData.Y, eventData.Z);
                _component.UpdatePlayerPosition(eventData.PlayerId, position);
            }

            public void OnPlayersMovedBatch(PlayerMovedEvent[] eventData)
            {
                foreach (var moveEvent in eventData)
                {
                    var position = new System.Numerics.Vector3(moveEvent.X, moveEvent.Y, moveEvent.Z);
                    _component.UpdatePlayerPosition(moveEvent.PlayerId, position);
                }
            }

            public void OnPlayersMovedBatch(PlayersBatchMovedEvent eventData)
            {
                foreach (var playerMove in eventData.Updates)
                {
                    var position = new System.Numerics.Vector3(playerMove.X, playerMove.Y, playerMove.Z);
                    _component.UpdatePlayerPosition(playerMove.PlayerId, position);
                }
            }
        }

        private class CompositeSubscriptionToken : ISubscriptionToken
        {
            private readonly ISubscriptionToken[] _tokens;
            private bool _isDisposed;

            public CompositeSubscriptionToken(ISubscriptionToken[] tokens)
            {
                _tokens = tokens;
            }

            public Guid Id { get; } = Guid.NewGuid();
            public bool IsActive => !_isDisposed;

            public void Unsubscribe()
            {
                if (_isDisposed) return;

                foreach (var token in _tokens)
                {
                    try
                    {
                        token?.Unsubscribe();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"取消订阅失败: {ex.Message}");
                    }
                }

                _isDisposed = true;
            }

            public void Dispose()
            {
                Unsubscribe();
            }
        }

        #endregion
    }
}
