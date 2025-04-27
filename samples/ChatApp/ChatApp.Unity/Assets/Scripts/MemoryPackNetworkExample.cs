using System;
using System.Collections.Generic;
using UnityEngine;
using UnityTCP.Enhanced;
using UnityTCP.MemoryPackModels;

namespace UnityTCP.Examples
{
    /// <summary>
    /// 演示如何使用MemoryPack网络管理器的示例
    /// </summary>
    public class MemoryPackNetworkExample : MonoBehaviour
    {
        [SerializeField] private bool autoConnect = true;
        [SerializeField] private string serverIp = "127.0.0.1";
        [SerializeField] private int serverPort = 7777;
        [SerializeField] private bool isServer = false;

        // MemoryPack网络管理器
        private MemoryPackNetworkManager _networkManager;

        // 本地玩家ID
        private uint _localPlayerId;

        // 玩家列表
        private Dictionary<uint, GameObject> _players = new Dictionary<uint, GameObject>();

        // 玩家预制体
        [SerializeField] private GameObject playerPrefab;

        // 自动发送速率
        [SerializeField] private float sendRate = 10f;
        private float _nextSendTime;

        private void Awake()
        {
            // 添加网络管理器组件
            _networkManager = gameObject.AddComponent<MemoryPackNetworkManager>();

            // 生成本地玩家ID
            _localPlayerId = (uint)UnityEngine.Random.Range(1, 10000);
        }

        private void Start()
        {
            // 注册消息处理器
            _networkManager.RegisterHandler<PlayerState>(OnPlayerStateReceived);
            _networkManager.RegisterHandler<WorldState>(OnWorldStateReceived);
            _networkManager.RegisterHandler<NetworkCommand>(OnNetworkCommandReceived);
            _networkManager.RegisterHandler<ChatMessage>(OnChatMessageReceived);

            // 绑定事件
            _networkManager.ClientConnected += OnClientConnected;
            _networkManager.ClientDisconnected += OnClientDisconnected;
            _networkManager.ServerStarted += OnServerStarted;
            _networkManager.ServerStopped += OnServerStopped;
            _networkManager.ErrorOccurred += OnErrorOccurred;

            if (isServer)
            {
                // 启动服务器
                StartServer();
            }
            else if (autoConnect)
            {
                // 连接到服务器
                ConnectToServer();
            }
        }

        private void Update()
        {
            // 定时发送状态更新
            if (Time.time >= _nextSendTime && (_networkManager != null))
            {
                _nextSendTime = Time.time + (1f / sendRate);
                SendPlayerState();
            }
        }

        private void OnDestroy()
        {
            // 清理
            if (_networkManager != null)
            {
                _networkManager.ClientConnected -= OnClientConnected;
                _networkManager.ClientDisconnected -= OnClientDisconnected;
                _networkManager.ServerStarted -= OnServerStarted;
                _networkManager.ServerStopped -= OnServerStopped;
                _networkManager.ErrorOccurred -= OnErrorOccurred;
            }
        }

        #region 网络操作

        /// <summary>
        /// 启动服务器
        /// </summary>
        public async void StartServer()
        {
            try
            {
                await _networkManager.StartServerAsync(serverPort);
                Debug.Log($"服务器已启动，端口: {serverPort}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"启动服务器失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public void StopServer()
        {
            _networkManager.StopServer();
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async void ConnectToServer()
        {
            try
            {
                await _networkManager.ConnectToServerAsync(serverIp, serverPort);
                Debug.Log($"已连接到服务器: {serverIp}:{serverPort}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"连接服务器失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 断开与服务器的连接
        /// </summary>
        public void DisconnectFromServer()
        {
            _networkManager.DisconnectClient();
        }

        #endregion

        #region 消息发送

        /// <summary>
        /// 发送玩家状态
        /// </summary>
        private async void SendPlayerState()
        {
            if (!_networkManager) return;

            try
            {
                // 创建玩家状态消息
                var playerState = PlayerState.FromTransform(transform, _localPlayerId);

                if (isServer)
                {
                    // 服务器广播给所有客户端
                    await _networkManager.BroadcastAsync(playerState);
                }
                else
                {
                    // 客户端发送到服务器
                    await _networkManager.SendToServerAsync(playerState);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"发送玩家状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送移动命令
        /// </summary>
        public async void SendMoveCommand(Vector3 direction, float speed)
        {
            if (!_networkManager) return;

            try
            {
                // 创建移动命令
                var command = NetworkCommand.CreateMoveCommand(_localPlayerId, direction, speed);

                // 发送到服务器
                await _networkManager.SendToServerAsync(command);
            }
            catch (Exception ex)
            {
                Debug.LogError($"发送移动命令失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送聊天消息
        /// </summary>
        public async void SendChatMessage(string message, uint receiverId = 0)
        {
            if (!_networkManager || string.IsNullOrEmpty(message)) return;

            try
            {
                // 创建聊天消息
                var chatMessage = new ChatMessage
                {
                    SenderId = _localPlayerId,
                    SenderName = $"Player{_localPlayerId}",
                    ReceiverId = receiverId, // 0表示广播给所有人
                    Content = message,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
                    Channel = 0 // 默认频道
                };

                // 发送到服务器
                await _networkManager.SendToServerAsync(chatMessage);
            }
            catch (Exception ex)
            {
                Debug.LogError($"发送聊天消息失败: {ex.Message}");
            }
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理玩家状态消息
        /// </summary>
        private void OnPlayerStateReceived(PlayerState playerState)
        {
            // 忽略自己的状态更新
            if (playerState.PlayerId == _localPlayerId)
                return;

            // 查找或创建玩家对象
            if (!_players.TryGetValue(playerState.PlayerId, out var playerObject))
            {
                // 创建新玩家
                playerObject = Instantiate(playerPrefab);
                playerObject.name = $"Player{playerState.PlayerId}";
                _players[playerState.PlayerId] = playerObject;
            }

            // 更新玩家变换
            playerState.ApplyToTransform(playerObject.transform);
        }

        /// <summary>
        /// 处理世界状态消息
        /// </summary>
        private void OnWorldStateReceived(WorldState worldState)
        {
            Debug.Log($"收到世界状态 ID: {worldState.StateId}, 服务器时间: {worldState.ServerTime}, 玩家数量: {worldState.Players.Length}");

            // 更新所有玩家状态
            foreach (var playerState in worldState.Players)
            {
                // 忽略自己的状态更新
                if (playerState.PlayerId == _localPlayerId)
                    continue;

                // 查找或创建玩家对象
                if (!_players.TryGetValue(playerState.PlayerId, out var playerObject))
                {
                    // 创建新玩家
                    playerObject = Instantiate(playerPrefab);
                    playerObject.name = $"Player{playerState.PlayerId}";
                    _players[playerState.PlayerId] = playerObject;
                }

                // 更新玩家变换
                playerState.ApplyToTransform(playerObject.transform);
            }
        }

        /// <summary>
        /// 处理网络命令
        /// </summary>
        private void OnNetworkCommandReceived(NetworkCommand command)
        {
            // 处理不同类型的命令
            switch (command.CommandType)
            {
                case 1: // 移动命令
                    HandleMoveCommand(command);
                    break;

                case 2: // 动作命令
                    HandleActionCommand(command);
                    break;

                default:
                    Debug.Log($"收到未知命令类型: {command.CommandType}, 目标: {command.TargetId}");
                    break;
            }
        }

        /// <summary>
        /// 处理移动命令
        /// </summary>
        private void HandleMoveCommand(NetworkCommand command)
        {
            // 找到目标玩家
            if (_players.TryGetValue(command.TargetId, out var playerObject))
            {
                // 提取移动方向和速度
                Vector3 direction = new Vector3(command.Param1, command.Param2, command.Param3);
                float speed = command.Param4;

                // 这里可以实现平滑移动逻辑
                Debug.Log($"玩家 {command.TargetId} 移动: 方向={direction}, 速度={speed}");
            }
        }

        /// <summary>
        /// 处理动作命令
        /// </summary>
        private void HandleActionCommand(NetworkCommand command)
        {
            // 找到目标玩家
            if (_players.TryGetValue(command.TargetId, out var playerObject))
            {
                // 提取动作ID
                byte actionId = (byte)command.Param1;

                // 这里可以触发相应的动画或行为
                Debug.Log($"玩家 {command.TargetId} 执行动作: {actionId}");
            }
        }

        /// <summary>
        /// 处理聊天消息
        /// </summary>
        private void OnChatMessageReceived(ChatMessage chatMessage)
        {
            // 判断消息是否发给自己
            bool isForMe = chatMessage.ReceiverId == 0 || chatMessage.ReceiverId == _localPlayerId;

            if (isForMe)
            {
                Debug.Log($"[聊天] {chatMessage.SenderName}: {chatMessage.Content}");

                // 这里可以更新UI显示聊天消息
            }
        }

        #endregion

        #region 事件处理

        private void OnClientConnected(string clientId)
        {
            Debug.Log($"客户端已连接: {clientId}");
        }

        private void OnClientDisconnected(string clientId)
        {
            Debug.Log($"客户端已断开: {clientId}");
        }

        private void OnServerStarted()
        {
            Debug.Log("服务器已启动");
        }

        private void OnServerStopped()
        {
            Debug.Log("服务器已停止");
        }

        private void OnErrorOccurred(Exception exception)
        {
            Debug.LogError($"网络错误: {exception.Message}");
        }

        #endregion
    }
}
