using System;
using System.Collections.Generic;
using ChatApp.Unity;
using ChatApp.Shared;
using UnityEngine;

namespace ChatApp
{
    /// <summary>
    /// 玩家管理器，负责管理玩家角色的实例
    /// </summary>
    public class PlayerManager : MonoBehaviour
    {
        [Header("Player Prefab")]
        public GameObject playerPrefab;

        [Header("Game Client")]
        public ChatComponent gameClient;

        [Header("角色颜色")]
        [SerializeField] private Color _localPlayerColor = Color.green;
        [SerializeField] private Color _remotePlayerColor = Color.blue;

        // 本地玩家引用
        private GameObject _localPlayerObject;

        // 玩家对象缓存
        private readonly Dictionary<Guid, GameObject> playerObjects = new Dictionary<Guid, GameObject>();

        private void Awake()
        {
            // 确保UnityGameClient已初始化
            if (gameClient == null)
            {
                gameClient = FindObjectOfType<ChatComponent>();
                if (gameClient == null)
                {
                    Debug.LogError("找不到UnityGameClient组件，请确保场景中有UnityGameClient组件");
                    return;
                }
            }
        }

        private void Start()
        {
            if (gameClient != null)
            {
                // 订阅事件
                gameClient.OnLoginSuccess += OnPlayerLoggedIn;
                gameClient.OnPlayerJoined += OnRemotePlayerJoined;
                gameClient.OnPlayerLeft += OnRemotePlayerLeft;
                gameClient.OnPlayerMoved += OnRemotePlayerMoved;
            }
        }

        private void OnDestroy()
        {
            if (gameClient != null)
            {
                // 取消订阅事件
                gameClient.OnLoginSuccess -= OnPlayerLoggedIn;
                gameClient.OnPlayerJoined -= OnRemotePlayerJoined;
                gameClient.OnPlayerLeft -= OnRemotePlayerLeft;
                gameClient.OnPlayerMoved -= OnRemotePlayerMoved;
            }
        }

        /// <summary>
        /// 当本地玩家登录成功时
        /// </summary>
        private void OnPlayerLoggedIn(PlayerInfo playerInfo)
        {
            // 创建本地玩家
            if (playerPrefab != null && _localPlayerObject == null)
            {
                _localPlayerObject = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
                _localPlayerObject.name = $"Player_{playerInfo.Username}_{playerInfo.Id}";

                // 设置本地玩家颜色
                var renderer = _localPlayerObject.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = _localPlayerColor;
                }

                // 添加到玩家对象缓存
                playerObjects[playerInfo.Id] = _localPlayerObject;

                Debug.Log($"本地玩家角色已创建: {playerInfo.Username}");
            }
        }

        /// <summary>
        /// 当远程玩家加入时
        /// </summary>
        private void OnRemotePlayerJoined(Guid playerId, string playerName, System.Numerics.Vector3 position)
        {
            if (playerPrefab != null && !playerObjects.ContainsKey(playerId))
            {
                var playerPosition = new Vector3(position.X, position.Y, position.Z);
                var playerObject = Instantiate(playerPrefab, playerPosition, Quaternion.identity);
                playerObject.name = $"Player_{playerName}_{playerId}";

                // 设置远程玩家颜色
                var renderer = playerObject.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = _remotePlayerColor;
                }

                // 添加到玩家对象缓存
                playerObjects[playerId] = playerObject;

                Debug.Log($"远程玩家角色已创建: {playerName}");
            }
        }

        /// <summary>
        /// 当远程玩家离开时
        /// </summary>
        private void OnRemotePlayerLeft(Guid playerId, string reason)
        {
            if (playerObjects.TryGetValue(playerId, out var playerObject))
            {
                Destroy(playerObject);
                playerObjects.Remove(playerId);

                Debug.Log($"玩家角色已移除: {playerId}, 原因: {reason}");
            }
        }

        /// <summary>
        /// 当远程玩家移动时
        /// </summary>
        private void OnRemotePlayerMoved(Guid playerId, System.Numerics.Vector3 position)
        {
            if (playerObjects.TryGetValue(playerId, out var playerObject))
            {
                var targetPosition = new Vector3(position.X, position.Y, position.Z);
                playerObject.transform.position = targetPosition;
            }
        }

        /// <summary>
        /// 更新本地玩家位置
        /// </summary>
        public void UpdateLocalPlayerPosition(Vector3 position)
        {
            if (_localPlayerObject != null)
            {
                _localPlayerObject.transform.position = position;
            }

            // 同时发送移动请求到服务器
            if (gameClient != null)
            {
                _ = gameClient.MoveAsync(position.x, position.y, position.z);
            }
        }

        /// <summary>
        /// 获取本地玩家对象
        /// </summary>
        public GameObject GetLocalPlayerObject()
        {
            return _localPlayerObject;
        }

        /// <summary>
        /// 获取指定玩家对象
        /// </summary>
        public GameObject GetPlayerObject(Guid playerId)
        {
            playerObjects.TryGetValue(playerId, out var playerObject);
            return playerObject;
        }

        /// <summary>
        /// 获取所有玩家对象
        /// </summary>
        public Dictionary<Guid, GameObject> GetAllPlayerObjects()
        {
            return new Dictionary<Guid, GameObject>(playerObjects);
        }
    }
}
