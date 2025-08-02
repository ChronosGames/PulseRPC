using System;
using System.Threading.Tasks;
using UnityEngine;
using PulseRPC.Client;
using GameApp.Shared.Services;
using GameApp.Unity.Managers;
using GameApp.Unity.Utils;
using PulseRPC;

namespace GameApp.Unity.Network
{
    /// <summary>
    /// 游戏客户端 - 负责与 GameServer 的 PulseRPC 通信
    /// </summary>
    public class GameClient
    {
        private IPulseClient _pulseClient;
        private IPlayerService _playerService;
        private IWorldService _worldService;

        // 事件监听器
        private PlayerEventsImpl _playerEvents;
        private WorldEventsImpl _worldEvents;

        public bool IsConnected => _pulseClient?.IsConnected ?? false;

        // 事件
        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<string> OnConnectionError;

        /// <summary>
        /// 初始化客户端
        /// </summary>
        public async Task<bool> InitializeAsync(string serverAddress, int tcpPort, int kcpPort)
        {
            try
            {
                Debug.Log($"Initializing GameClient: {serverAddress}:{tcpPort}/{kcpPort}");

                // 创建 PulseRPC 客户端
                _pulseClient = PulseRpcClientFactory.CreateClient(builder =>
                {
                    // TCP通道用于可靠消息传输
                    builder.AddTcp("TcpChannel", serverAddress, tcpPort);

                    // KCP通道用于低延迟游戏数据传输
                    builder.AddKcp("KcpChannel", serverAddress, kcpPort);
                });

                // 连接到服务器
                await _pulseClient.ConnectAsync();

                // 获取服务代理
                _playerService = await _pulseClient.GetServiceAsync<IPlayerService>();
                _worldService = await _pulseClient.GetServiceAsync<IWorldService>();

                // 注册事件监听器
                _playerEvents = new PlayerEventsImpl();
                _worldEvents = new WorldEventsImpl();

                await _pulseClient.RegisterEventListenerAsync<IPlayerEvents>(_playerEvents);
                await _pulseClient.RegisterEventListenerAsync<IWorldEvents>(_worldEvents);

                Debug.Log("GameClient initialized successfully");
                OnConnected?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"GameClient initialization failed: {ex.Message}");
                OnConnectionError?.Invoke(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 玩家登录游戏服务器
        /// </summary>
        public async Task<GameApp.Shared.Services.LoginResponse> LoginAsync(string gameTicket)
        {
            try
            {
                if (_playerService == null)
                {
                    throw new InvalidOperationException("GameClient not initialized");
                }

                var request = new GameApp.Shared.Services.LoginRequest
                {
                    GameTicket = gameTicket,
                    DeviceId = SystemInfo.deviceUniqueIdentifier,
                    ClientInfo = new ClientInfo
                    {
                        Version = Application.version,
                        Platform = Application.platform.ToString(),
                        UnityVersion = Application.unityVersion
                    }
                };

                var response = await _playerService.LoginAsync(request);

                if (response.Success)
                {
                    Debug.Log($"Player login successful: {response.PlayerInfo?.CharacterName}");
                }
                else
                {
                    Debug.LogWarning($"Player login failed: {response.Message}");
                }

                return response;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Player login error: {ex.Message}");
                return new GameApp.Shared.Services.LoginResponse
                {
                    Success = false,
                    Message = $"登录失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 获取玩家信息
        /// </summary>
        public async Task<PlayerInfo> GetPlayerInfoAsync(int playerId)
        {
            try
            {
                if (_playerService == null)
                {
                    throw new InvalidOperationException("GameClient not initialized");
                }

                var request = new GetPlayerInfoRequest { PlayerId = playerId };
                var playerInfo = await _playerService.GetPlayerInfoAsync(request);

                Debug.Log($"Retrieved player info: {playerInfo.CharacterName} (Level {playerInfo.Level})");
                return playerInfo;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Get player info error: {ex.Message}");
                return new PlayerInfo();
            }
        }

        /// <summary>
        /// 更新玩家位置
        /// </summary>
        public async Task UpdatePositionAsync(int playerId, Vector3 position, float rotation)
        {
            try
            {
                if (_playerService == null)
                {
                    throw new InvalidOperationException("GameClient not initialized");
                }

                var request = new UpdatePositionRequest
                {
                    PlayerId = playerId,
                    Position = new PlayerPosition
                    {
                        WorldId = "current_world", // 从当前状态获取
                        MapId = "current_map",     // 从当前状态获取
                        X = position.x,
                        Y = position.y,
                        Z = position.z,
                        Rotation = rotation,
                        LastUpdate = DateTime.UtcNow
                    }
                };

                await _playerService.UpdatePositionAsync(request);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Update position error: {ex.Message}");
            }
        }

        /// <summary>
        /// 加入世界
        /// </summary>
        public async Task<JoinWorldResponse> JoinWorldAsync(int playerId, string worldId, Vector3 spawnPosition)
        {
            try
            {
                if (_worldService == null)
                {
                    throw new InvalidOperationException("GameClient not initialized");
                }

                var request = new JoinWorldRequest
                {
                    PlayerId = playerId,
                    WorldId = worldId,
                    SpawnPosition = new PlayerPosition
                    {
                        WorldId = worldId,
                        X = spawnPosition.x,
                        Y = spawnPosition.y,
                        Z = spawnPosition.z,
                        LastUpdate = DateTime.UtcNow
                    }
                };

                var response = await _worldService.JoinWorldAsync(request);

                if (response.Success)
                {
                    Debug.Log($"Joined world successfully: {worldId}");
                }
                else
                {
                    Debug.LogWarning($"Join world failed: {response.Message}");
                }

                return response;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Join world error: {ex.Message}");
                return new JoinWorldResponse
                {
                    Success = false,
                    Message = $"加入世界失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 发送世界聊天
        /// </summary>
        public async Task SendWorldChatAsync(int playerId, string message)
        {
            try
            {
                if (_worldService == null)
                {
                    throw new InvalidOperationException("GameClient not initialized");
                }

                var request = new WorldChatRequest
                {
                    PlayerId = playerId,
                    Message = message,
                    ChatType = "world"
                };

                await _worldService.SendWorldChatAsync(request);
                Debug.Log($"World chat sent: {message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Send world chat error: {ex.Message}");
            }
        }

        /// <summary>
        /// 玩家登出
        /// </summary>
        public async Task LogoutAsync(int playerId)
        {
            try
            {
                if (_playerService == null)
                {
                    return;
                }

                var request = new LogoutRequest
                {
                    PlayerId = playerId,
                    Reason = "user_logout"
                };

                await _playerService.LogoutAsync(request);
                Debug.Log("Player logout successful");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Player logout error: {ex.Message}");
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                if (_pulseClient != null)
                {
                    await _pulseClient.DisconnectAsync();
                    _pulseClient = null;

                    Debug.Log("GameClient disconnected");
                    OnDisconnected?.Invoke("User disconnected");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Disconnect error: {ex.Message}");
            }
        }

        #region Event Handlers

        /// <summary>
        /// 玩家事件处理器实现
        /// </summary>
        private class PlayerEventsImpl : IPulseEventHandler, IPlayerEvents
        {
            public void OnPlayerStatusUpdate(PlayerStatusUpdateEvent eventData)
            {
                Debug.Log($"Player status update: Player {eventData.PlayerId}, Health: {eventData.Status.Health}/{eventData.Status.MaxHealth}");

                // 在主线程中处理UI更新
                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    // 更新玩家状态UI
                    GameManager.Instance?.UpdatePlayerStatus(eventData.PlayerId, eventData.Status);
                });
            }

            public void OnPlayerLevelUp(PlayerLevelUpEvent eventData)
            {
                Debug.Log($"Player level up: Player {eventData.PlayerId}, Level: {eventData.OldLevel} -> {eventData.NewLevel}");

                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    // 显示升级特效和UI
                    GameManager.Instance?.ShowLevelUpEffect(eventData.PlayerId, eventData.NewLevel);
                });
            }

            public void OnPlayerMoved(PlayerMovedEvent eventData)
            {
                // 位置更新频率较高，只在Debug模式下输出
                #if UNITY_EDITOR
                Debug.Log($"Player moved: Player {eventData.PlayerId}, Position: ({eventData.Position.X}, {eventData.Position.Y}, {eventData.Position.Z})");
                #endif

                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    // 更新其他玩家位置
                    GameManager.Instance?.UpdatePlayerPosition(eventData.PlayerId, eventData.Position);
                });
            }
        }

        /// <summary>
        /// 世界事件处理器实现
        /// </summary>
        private class WorldEventsImpl : IPulseEventHandler, IWorldEvents
        {
            public void OnWorldUpdate(WorldUpdateEvent eventData)
            {
                Debug.Log($"World update: {eventData.WorldId}, Type: {eventData.UpdateType}");

                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    // 处理世界更新
                    GameManager.Instance?.HandleWorldUpdate(eventData);
                });
            }

            public void OnPlayerJoined(PlayerJoinedEvent eventData)
            {
                Debug.Log($"Player joined world: {eventData.Player.CharacterName} joined {eventData.WorldId}");

                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    // 显示玩家加入消息
                    GameManager.Instance?.ShowPlayerJoined(eventData.Player);
                });
            }

            public void OnPlayerLeft(PlayerLeftEvent eventData)
            {
                Debug.Log($"Player left world: {eventData.PlayerName} left {eventData.WorldId}");

                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    // 移除玩家并显示离开消息
                    GameManager.Instance?.HandlePlayerLeft(eventData.PlayerId, eventData.PlayerName);
                });
            }

            public void OnWorldChatMessage(WorldChatMessageEvent eventData)
            {
                Debug.Log($"World chat: [{eventData.PlayerName}] {eventData.Message}");

                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    // 显示聊天消息
                    GameManager.Instance?.ShowChatMessage(eventData.PlayerName, eventData.Message, eventData.ChatType);
                });
            }

            public void OnWorldEventNotification(WorldEventNotificationEvent eventData)
            {
                Debug.Log($"World event: {eventData.Event.Name} in {eventData.WorldId}");

                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    // 处理世界事件通知
                    GameManager.Instance?.HandleWorldEvent(eventData.Event);
                });
            }
        }

        #endregion
    }
}
