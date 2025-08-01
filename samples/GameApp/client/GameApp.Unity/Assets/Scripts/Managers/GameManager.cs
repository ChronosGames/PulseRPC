using System;
using System.Collections.Generic;
using UnityEngine;
using GameApp.Unity.Network;
using GameApp.Shared.Services;

namespace GameApp.Unity.Managers
{
    /// <summary>
    /// 游戏管理器 - 管理游戏状态和玩家数据
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("游戏状态")]
        [SerializeField] private GameState currentState = GameState.Login;
        [SerializeField] private string currentWorldId = string.Empty;
        [SerializeField] private string currentMapId = string.Empty;

        [Header("玩家信息")]
        [SerializeField] private int currentPlayerId = 0;
        [SerializeField] private string currentCharacterName = string.Empty;
        [SerializeField] private int currentLevel = 1;

        // 单例
        public static GameManager Instance { get; private set; }

        // 游戏状态
        public GameState CurrentState
        {
            get => currentState;
            private set
            {
                var oldState = currentState;
                currentState = value;
                OnGameStateChanged?.Invoke(oldState, value);
            }
        }

        // 当前玩家信息
        public PlayerInfo CurrentPlayer { get; private set; }

        // 其他玩家信息
        private readonly Dictionary<int, PlayerInfo> _otherPlayers = new Dictionary<int, PlayerInfo>();

        // 世界状态
        public WorldState CurrentWorld { get; private set; }

        // 事件
        public event Action<GameState, GameState> OnGameStateChanged;
        public event Action<PlayerInfo> OnPlayerInfoUpdated;
        public event Action<int, PlayerStatus> OnPlayerStatusUpdated;
        public event Action<int, int> OnPlayerLevelChanged;
        public event Action<string, string, string> OnChatMessageReceived;
        public event Action<WorldEvent> OnWorldEventReceived;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                InitializeGameManager();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void InitializeGameManager()
        {
            Debug.Log("GameManager initialized");

            // 监听网络事件
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnGameConnected += OnGameServerConnected;
                NetworkManager.Instance.OnGameDisconnected += OnGameServerDisconnected;
            }
        }

        #region Game State Management

        /// <summary>
        /// 切换游戏状态
        /// </summary>
        public void ChangeState(GameState newState)
        {
            Debug.Log($"Game state changing: {CurrentState} -> {newState}");
            CurrentState = newState;
        }

        /// <summary>
        /// 开始游戏
        /// </summary>
        public void StartGame(PlayerInfo playerInfo)
        {
            CurrentPlayer = playerInfo;
            currentPlayerId = playerInfo.PlayerId;
            currentCharacterName = playerInfo.CharacterName;
            currentLevel = playerInfo.Level;

            Debug.Log($"Game started for player: {CurrentPlayer.CharacterName} (Level {CurrentPlayer.Level})");
            ChangeState(GameState.InGame);

            OnPlayerInfoUpdated?.Invoke(CurrentPlayer);
        }

        /// <summary>
        /// 结束游戏
        /// </summary>
        public void EndGame()
        {
            Debug.Log("Game ended");

            CurrentPlayer = null;
            CurrentWorld = null;
            _otherPlayers.Clear();

            currentPlayerId = 0;
            currentCharacterName = string.Empty;
            currentLevel = 1;
            currentWorldId = string.Empty;
            currentMapId = string.Empty;

            ChangeState(GameState.Login);
        }

        #endregion

        #region Player Management

        /// <summary>
        /// 更新玩家状态
        /// </summary>
        public void UpdatePlayerStatus(int playerId, PlayerStatus status)
        {
            if (CurrentPlayer != null && CurrentPlayer.PlayerId == playerId)
            {
                CurrentPlayer.Status = status;
                OnPlayerInfoUpdated?.Invoke(CurrentPlayer);
            }

            OnPlayerStatusUpdated?.Invoke(playerId, status);
        }

        /// <summary>
        /// 显示升级特效
        /// </summary>
        public void ShowLevelUpEffect(int playerId, int newLevel)
        {
            if (CurrentPlayer != null && CurrentPlayer.PlayerId == playerId)
            {
                var oldLevel = CurrentPlayer.Level;
                CurrentPlayer.Level = newLevel;
                currentLevel = newLevel;

                Debug.Log($"Player leveled up: {oldLevel} -> {newLevel}");
                OnPlayerLevelChanged?.Invoke(oldLevel, newLevel);
                OnPlayerInfoUpdated?.Invoke(CurrentPlayer);

                // 这里可以播放升级特效、音效等
                PlayLevelUpEffect();
            }
        }

        /// <summary>
        /// 更新玩家位置
        /// </summary>
        public void UpdatePlayerPosition(int playerId, PlayerPosition position)
        {
            if (CurrentPlayer != null && CurrentPlayer.PlayerId == playerId)
            {
                CurrentPlayer.Position = position;
            }
            else if (_otherPlayers.ContainsKey(playerId))
            {
                _otherPlayers[playerId].Position = position;
            }

            // 更新Unity中的玩家位置
            UpdatePlayerTransform(playerId, position);
        }

        /// <summary>
        /// 添加其他玩家
        /// </summary>
        public void ShowPlayerJoined(PlayerInfo player)
        {
            if (!_otherPlayers.ContainsKey(player.PlayerId))
            {
                _otherPlayers[player.PlayerId] = player;
                Debug.Log($"Player joined: {player.CharacterName}");

                // 在游戏世界中生成玩家对象
                SpawnPlayerInWorld(player);

                // 显示加入消息
                ShowChatMessage("系统", $"{player.CharacterName} 加入了游戏", "system");
            }
        }

        /// <summary>
        /// 处理玩家离开
        /// </summary>
        public void HandlePlayerLeft(int playerId, string playerName)
        {
            if (_otherPlayers.ContainsKey(playerId))
            {
                _otherPlayers.Remove(playerId);
                Debug.Log($"Player left: {playerName}");

                // 从游戏世界中移除玩家对象
                RemovePlayerFromWorld(playerId);

                // 显示离开消息
                ShowChatMessage("系统", $"{playerName} 离开了游戏", "system");
            }
        }

        #endregion

        #region World Management

        /// <summary>
        /// 处理世界更新
        /// </summary>
        public void HandleWorldUpdate(WorldUpdateEvent updateEvent)
        {
            Debug.Log($"World update received: {updateEvent.UpdateType} in {updateEvent.WorldId}");

            // 根据更新类型处理不同的世界变化
            switch (updateEvent.UpdateType)
            {
                case "weather_change":
                    HandleWeatherChange(updateEvent.Data);
                    break;
                case "event_started":
                    HandleWorldEventStarted(updateEvent.Data);
                    break;
                case "player_count_update":
                    HandlePlayerCountUpdate(updateEvent.Data);
                    break;
                default:
                    Debug.Log($"Unknown world update type: {updateEvent.UpdateType}");
                    break;
            }
        }

        /// <summary>
        /// 处理世界事件
        /// </summary>
        public void HandleWorldEvent(WorldEvent worldEvent)
        {
            Debug.Log($"World event: {worldEvent.Name} ({worldEvent.Type})");
            OnWorldEventReceived?.Invoke(worldEvent);

            // 显示世界事件通知
            ShowWorldEventNotification(worldEvent);
        }

        /// <summary>
        /// 加入世界
        /// </summary>
        public async void JoinWorld(string worldId, Vector3 spawnPosition)
        {
            if (CurrentPlayer == null || NetworkManager.Instance?.GameClient == null)
            {
                Debug.LogError("Cannot join world: Player or GameClient not available");
                return;
            }

            try
            {
                var result = await NetworkManager.Instance.GameClient.JoinWorldAsync(
                    CurrentPlayer.PlayerId, worldId, spawnPosition);

                if (result.Success)
                {
                    currentWorldId = worldId;
                    CurrentWorld = result.WorldState;

                    Debug.Log($"Successfully joined world: {worldId}");
                    ChangeState(GameState.InWorld);

                    // 生成附近的其他玩家
                    foreach (var player in result.NearbyPlayers)
                    {
                        if (player.PlayerId != CurrentPlayer.PlayerId)
                        {
                            ShowPlayerJoined(player);
                        }
                    }
                }
                else
                {
                    Debug.LogError($"Failed to join world: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error joining world: {ex.Message}");
            }
        }

        #endregion

        #region Chat System

        /// <summary>
        /// 显示聊天消息
        /// </summary>
        public void ShowChatMessage(string playerName, string message, string chatType)
        {
            Debug.Log($"[{chatType.ToUpper()}] {playerName}: {message}");
            OnChatMessageReceived?.Invoke(playerName, message, chatType);

            // 这里可以更新UI显示聊天消息
            UIManager.Instance?.ShowChatMessage(playerName, message, chatType);
        }

        /// <summary>
        /// 发送聊天消息
        /// </summary>
        public async void SendChatMessage(string message)
        {
            if (CurrentPlayer == null || NetworkManager.Instance?.GameClient == null)
            {
                Debug.LogError("Cannot send chat: Player or GameClient not available");
                return;
            }

            try
            {
                await NetworkManager.Instance.GameClient.SendWorldChatAsync(CurrentPlayer.PlayerId, message);
                Debug.Log($"Chat message sent: {message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error sending chat message: {ex.Message}");
            }
        }

        #endregion

        #region Network Event Handlers

        private void OnGameServerConnected()
        {
            Debug.Log("Game server connected");
        }

        private void OnGameServerDisconnected(string reason)
        {
            Debug.Log($"Game server disconnected: {reason}");

            // 返回到登录状态
            if (CurrentState != GameState.Login)
            {
                EndGame();
            }
        }

        #endregion

        #region Private Helper Methods

        private void PlayLevelUpEffect()
        {
            // 播放升级特效和音效
            Debug.Log("Playing level up effect");

            // 这里可以实现具体的特效逻辑
            // - 播放升级音效
            // - 显示升级特效
            // - 更新UI显示
        }

        private void UpdatePlayerTransform(int playerId, PlayerPosition position)
        {
            // 更新Unity中玩家GameObject的位置
            var playerObject = FindPlayerObject(playerId);
            if (playerObject != null)
            {
                var unityPosition = new Vector3(position.X, position.Y, position.Z);
                var unityRotation = Quaternion.Euler(0, position.Rotation, 0);

                playerObject.transform.position = unityPosition;
                playerObject.transform.rotation = unityRotation;
            }
        }

        private GameObject FindPlayerObject(int playerId)
        {
            // 查找场景中对应的玩家GameObject
            // 这里需要根据实际的玩家对象管理方式实现
            return GameObject.Find($"Player_{playerId}");
        }

        private void SpawnPlayerInWorld(PlayerInfo player)
        {
            // 在游戏世界中生成玩家对象
            Debug.Log($"Spawning player in world: {player.CharacterName}");

            // 这里需要根据实际的玩家对象管理方式实现
            // - 加载玩家模型
            // - 设置位置和旋转
            // - 添加必要的组件
        }

        private void RemovePlayerFromWorld(int playerId)
        {
            // 从游戏世界中移除玩家对象
            var playerObject = FindPlayerObject(playerId);
            if (playerObject != null)
            {
                Destroy(playerObject);
                Debug.Log($"Player object removed: {playerId}");
            }
        }

        private void HandleWeatherChange(Dictionary<string, object> data)
        {
            Debug.Log("Weather changed");
            // 处理天气变化
        }

        private void HandleWorldEventStarted(Dictionary<string, object> data)
        {
            Debug.Log("World event started");
            // 处理世界事件开始
        }

        private void HandlePlayerCountUpdate(Dictionary<string, object> data)
        {
            Debug.Log("Player count updated");
            // 处理玩家数量更新
        }

        private void ShowWorldEventNotification(WorldEvent worldEvent)
        {
            // 显示世界事件通知
            Debug.Log($"World Event: {worldEvent.Name} - {worldEvent.Type}");

            // 这里可以实现具体的UI通知逻辑
            UIManager.Instance?.ShowWorldEventNotification(worldEvent.Name, worldEvent.Type);
        }

        #endregion

        private void OnDestroy()
        {
            // 清理事件监听
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnGameConnected -= OnGameServerConnected;
                NetworkManager.Instance.OnGameDisconnected -= OnGameServerDisconnected;
            }
        }
    }

    /// <summary>
    /// 游戏状态枚举
    /// </summary>
    public enum GameState
    {
        Login,          // 登录状态
        ZoneSelection,  // 区服选择
        CharacterSelect,// 角色选择
        Loading,        // 加载中
        InGame,         // 游戏中
        InWorld,        // 在世界中
        InBattle,       // 战斗中
        Disconnected    // 断开连接
    }
}
