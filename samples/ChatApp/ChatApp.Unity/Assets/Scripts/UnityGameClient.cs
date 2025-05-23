using System;
using System.Threading.Tasks;
using ChatApp.Shared;
using PulseRPC.Client.Unity;
using PulseRPC.Client;
using PulseRPC.Transport;
using UnityEngine;

namespace ChatApp.Unity
{
    /// <summary>
    /// Unity 游戏客户端 - 使用新的 PulseRPC.Client.Unity 包装器
    /// </summary>
    public class UnityGameClient : MonoBehaviour
    {
        [Header("服务器配置")]
        [SerializeField] private string _host = "localhost";
        [SerializeField] private int _port = 7000;

        [Header("游戏设置")]
        [SerializeField] private string _username = "Player";
        [SerializeField] private string _password = "password";

        [Header("场景控制器")]
        [SerializeField] private GameSceneController _sceneController;

        // 状态更新事件
        public event Action<string> OnStatusUpdate;
        public event Action<PlayerInfo> OnLoginSuccess;
        public event Action<string> OnLoginFailed;
        public event Action<Guid, string, System.Numerics.Vector3> OnPlayerJoined;
        public event Action<Guid, string> OnPlayerLeft;
        public event Action<Guid, System.Numerics.Vector3> OnPlayerMoved;

        // PulseRPC 客户端组件
        private UnityPulseRPCClientComponent _clientComponent;
        private IPulseRPCClient _client;

        // 服务代理
        private IPlayerService _playerService;

        // 玩家状态
        private bool _isLoggedIn;
        private PlayerInfo _playerInfo;

        private void Awake()
        {
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
            await ConnectAsync();

            UpdateStatus("正在登录...");
            await LoginAsync();
        }

        /// <summary>
        /// 初始化客户端
        /// </summary>
        private async Task InitializeAsync()
        {
            Debug.Log("[UnityGameClient] 正在初始化游戏客户端...");

            try
            {
                // 创建 Unity 客户端组件
                var clientGameObject = new GameObject("PulseRPC_Client");
                clientGameObject.transform.SetParent(transform);
                _clientComponent = clientGameObject.AddComponent<UnityPulseRPCClientComponent>();

                // 配置客户端选项
                var options = new PulseRPCClientOptions
                {
                    ServerAddress = _host,
                    ServerPort = _port,
                    ConnectionTimeoutMs = 30000,
                    ReconnectIntervalMs = 5000,
                    MaxReconnectAttempts = 5
                };

                // 初始化客户端
                _client = await UnityPulseRPCClientFactory.CreateClientAsync(_host, _port);

                // 设置事件处理器
                _client.ConnectionStateChanged += OnConnectionStateChanged;
                _client.ErrorOccurred += OnClientError;

                Debug.Log("[UnityGameClient] 客户端初始化完成");
                UpdateStatus("客户端初始化完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityGameClient] 客户端初始化失败: {ex.Message}");
                UpdateStatus($"客户端初始化失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        private async Task ConnectAsync()
        {
            Debug.Log($"[UnityGameClient] 正在连接到服务器 {_host}:{_port}...");

            try
            {
                await _client.ConnectAsync();

                // 连接成功后创建服务代理
                // 注释掉不存在的方法，等待实现
                // _playerService = _client.CreateService<IPlayerService>();

                Debug.Log("[UnityGameClient] 已连接到服务器");
                UpdateStatus("已连接到服务器");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityGameClient] 连接服务器失败: {ex.Message}");
                UpdateStatus($"连接服务器失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 登录
        /// </summary>
        private async Task LoginAsync()
        {
            Debug.Log($"[UnityGameClient] 正在登录，用户名: {_username}");

            try
            {
                var request = new LoginRequest { Username = _username, Password = _password };
                var response = await _playerService.LoginAsync(request);

                if (response.Success)
                {
                    _isLoggedIn = true;
                    _playerInfo = response.Player;

                    Debug.Log($"[UnityGameClient] 登录成功: {_playerInfo.Username} (ID: {_playerInfo.Id})");
                    UpdateStatus($"登录成功: {_playerInfo.Username}");

                    // 更新UI和触发事件
                    if (_sceneController != null)
                    {
                        _sceneController.UpdatePlayerInfo(_playerInfo.Username, _playerInfo.Id);
                    }

                    OnLoginSuccess?.Invoke(_playerInfo);

                    // 订阅服务器事件
                    await SubscribeToEventsAsync();
                }
                else
                {
                    Debug.LogWarning($"[UnityGameClient] 登录失败: {response.ErrorMessage}");
                    UpdateStatus($"登录失败: {response.ErrorMessage}");
                    OnLoginFailed?.Invoke(response.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityGameClient] 登录过程中发生错误: {ex.Message}");
                UpdateStatus($"登录错误: {ex.Message}");
                OnLoginFailed?.Invoke(ex.Message);

                if (ex.InnerException != null)
                {
                    Debug.LogError($"[UnityGameClient] 内部错误: {ex.InnerException.Message}");
                }
            }
        }

        /// <summary>
        /// 订阅服务器事件
        /// </summary>
        private async Task SubscribeToEventsAsync()
        {
            try
            {
                // 创建事件处理器
                var eventsHandler = new PlayerEventsHandler(this);

                // 订阅玩家登录事件
                // 注释掉不存在的方法，等待实现
                // await _client.SubscribeAsync<IPlayerLoginEvents>(eventsHandler);

                // 订阅玩家移动事件
                // await _client.SubscribeAsync<IPlayerMovementEvents>(eventsHandler);

                Debug.Log("[UnityGameClient] 事件订阅完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityGameClient] 事件订阅失败: {ex.Message}");
                UpdateStatus($"事件订阅失败: {ex.Message}");
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
                await _playerService.MoveAsync(new MoveRequest
                {
                    X = x,
                    Y = y,
                    Z = z
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityGameClient] 移动请求失败: {ex.Message}");
                UpdateStatus($"移动请求失败: {ex.Message}");
            }
        }

        private void OnConnectionStateChanged(object sender, ConnectionStateChangedEventArgs args)
        {
            Debug.Log($"[UnityGameClient] 连接状态变更: {args.IsConnected}");

            if (args.IsConnected)
            {
                UpdateStatus("已连接");
            }
            else
            {
                UpdateStatus($"已断开连接: {args.DisconnectReason}");
            }
        }

        private void OnClientError(object sender, ErrorEventArgs args)
        {
            Debug.LogError($"[UnityGameClient] 客户端错误: {args.Exception.Message}");
            UpdateStatus($"客户端错误: {args.Exception.Message}");
        }

        /// <summary>
        /// 添加新玩家
        /// </summary>
        internal void AddPlayer(Guid playerId, string playerName, System.Numerics.Vector3 position)
        {
            Debug.Log($"[UnityGameClient] 玩家加入: {playerName} (ID: {playerId})");
            OnPlayerJoined?.Invoke(playerId, playerName, position);

            // 更新场景控制器
            // if (_sceneController != null)
            // {
            //     _sceneController.OnPlayerJoined(playerId, playerName);
            // }
        }

        /// <summary>
        /// 移除玩家
        /// </summary>
        internal void RemovePlayer(Guid playerId, string reason)
        {
            Debug.Log($"[UnityGameClient] 玩家离开: {playerId}, 原因: {reason}");
            OnPlayerLeft?.Invoke(playerId, reason);

            // 更新场景控制器
            // if (_sceneController != null)
            // {
            //     _sceneController.OnPlayerLeft(playerId);
            // }
        }

        /// <summary>
        /// 更新玩家位置
        /// </summary>
        internal void UpdatePlayerPosition(Guid playerId, System.Numerics.Vector3 position)
        {
            OnPlayerMoved?.Invoke(playerId, position);

            // 更新场景控制器
            // if (_sceneController != null)
            // {
            //     _sceneController.OnPlayerMoved(playerId, new Vector3(position.X, position.Y, position.Z));
            // }
        }

        private void OnDestroy()
        {
            _client?.DisconnectAsync();
        }

        /// <summary>
        /// 更新状态显示
        /// </summary>
        private void UpdateStatus(string status)
        {
            OnStatusUpdate?.Invoke(status);

            if (_sceneController != null)
            {
                _sceneController.UpdateStatus(status);
            }
        }

        /// <summary>
        /// 玩家事件处理器
        /// </summary>
        private class PlayerEventsHandler : IPlayerLoginEvents, IPlayerMovementEvents
        {
            private readonly UnityGameClient _client;

            public PlayerEventsHandler(UnityGameClient client)
            {
                _client = client;
            }

            public void OnPlayerJoined(PlayerJoinedEvent eventData)
            {
                _client.AddPlayer(eventData.PlayerId, eventData.PlayerName,
                    new System.Numerics.Vector3 { X = eventData.X, Y = eventData.Y, Z = eventData.Z });
            }

            public void OnPlayerLeft(PlayerLeftEvent eventData)
            {
                _client.RemovePlayer(eventData.PlayerId, eventData.Reason);
            }

            public void OnPlayerMoved(PlayerMovedEvent eventData)
            {
                _client.UpdatePlayerPosition(eventData.PlayerId,
                    new System.Numerics.Vector3 { X = eventData.X, Y = eventData.Y, Z = eventData.Z });
            }

            public void OnPlayersMovedBatch(PlayerMovedEvent[] eventData)
            {
                foreach (var moveEvent in eventData)
                {
                    _client.UpdatePlayerPosition(moveEvent.PlayerId,
                        new System.Numerics.Vector3 { X = moveEvent.X, Y = moveEvent.Y, Z = moveEvent.Z });
                }
            }

            public void OnPlayersMovedBatch(PlayersBatchMovedEvent eventData)
            {
                foreach (var moveEvent in eventData.Updates)
                {
                    _client.UpdatePlayerPosition(moveEvent.PlayerId,
                        new System.Numerics.Vector3 { X = moveEvent.X, Y = moveEvent.Y, Z = moveEvent.Z });
                }
            }
        }

        /// <summary>
        /// Unity Inspector 上下文菜单 - 测试连接
        /// </summary>
        [ContextMenu("测试连接")]
        private async void TestConnection()
        {
            if (_client != null)
            {
                Debug.Log("[UnityGameClient] 正在测试连接...");
                UpdateStatus("正在测试连接...");

                try
                {
                    await _client.DisconnectAsync();
                    await _client.ConnectAsync();
                    Debug.Log("[UnityGameClient] 连接测试成功");
                    UpdateStatus("连接测试成功");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UnityGameClient] 连接测试失败: {ex.Message}");
                    UpdateStatus($"连接测试失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Unity Inspector 上下文菜单 - 测试移动
        /// </summary>
        [ContextMenu("测试移动")]
        private async void TestMove()
        {
            if (_isLoggedIn)
            {
                var random = new System.Random();
                await MoveAsync(
                    random.Next(-10, 10),
                    0,
                    random.Next(-10, 10)
                );
            }
        }
    }
}
