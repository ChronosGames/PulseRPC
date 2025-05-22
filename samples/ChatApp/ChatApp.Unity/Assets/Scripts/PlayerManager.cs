using System;
using System.Collections.Generic;
using ChatApp.Unity;
using UnityEngine;

namespace ChatApp
{
    /// <summary>
    /// 玩家管理器，负责管理玩家角色的实例
    /// </summary>
    public class PlayerManager : MonoBehaviour
    {
        [Header("游戏客户端")]
        [SerializeField] private GameClient _gameClient;

        [Header("玩家预制体")]
        [SerializeField] private GameObject _playerPrefab;

        [Header("角色颜色")]
        [SerializeField] private Color _localPlayerColor = Color.green;
        [SerializeField] private Color _remotePlayerColor = Color.blue;

        // 本地玩家引用
        private GameObject _localPlayerObject;

        // 玩家对象缓存
        private readonly Dictionary<Guid, GameObject> _playerObjects = new Dictionary<Guid, GameObject>();

        private void Awake()
        {
            // 确保GameClient已初始化
            if (_gameClient == null)
            {
                _gameClient = FindObjectOfType<GameClient>();
                if (_gameClient == null)
                {
                    Debug.LogError("找不到GameClient组件，请确保场景中有GameClient组件");
                    return;
                }
            }
        }

        private void Start()
        {
            if (_gameClient != null)
            {
                // 订阅事件
                _gameClient.OnLoginSuccess += OnPlayerLoggedIn;
                _gameClient.OnPlayerJoined += OnRemotePlayerJoined;
                _gameClient.OnPlayerLeft += OnRemotePlayerLeft;
                _gameClient.OnPlayerMoved += OnRemotePlayerMoved;
            }
        }

        private void OnDestroy()
        {
            if (_gameClient != null)
            {
                // 取消订阅事件
                _gameClient.OnLoginSuccess -= OnPlayerLoggedIn;
                _gameClient.OnPlayerJoined -= OnRemotePlayerJoined;
                _gameClient.OnPlayerLeft -= OnRemotePlayerLeft;
                _gameClient.OnPlayerMoved -= OnRemotePlayerMoved;
            }
        }

        /// <summary>
        /// 当本地玩家登录成功时
        /// </summary>
        private void OnPlayerLoggedIn(PlayerInfo playerInfo)
        {
            // 创建本地玩家
            if (_playerPrefab != null && _localPlayerObject == null)
            {
                _localPlayerObject = Instantiate(_playerPrefab, Vector3.zero, Quaternion.identity);
                _localPlayerObject.name = $"Player_{playerInfo.Username}_{playerInfo.Id}";

                // 设置本地玩家颜色
                var renderer = _localPlayerObject.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = _localPlayerColor;
                }

                // 添加到玩家对象缓存
                _playerObjects[playerInfo.Id] = _localPlayerObject;

                Debug.Log($"本地玩家角色已创建: {playerInfo.Username}");
            }
        }

        /// <summary>
        /// 当远程玩家加入时
        /// </summary>
        private void OnRemotePlayerJoined(Guid playerId, string playerName, PulseRPC.Shared.Vector3 position)
        {
            if (_playerPrefab != null && !_playerObjects.ContainsKey(playerId))
            {
                var playerPosition = new Vector3(position.X, position.Y, position.Z);
                var playerObject = Instantiate(_playerPrefab, playerPosition, Quaternion.identity);
                playerObject.name = $"Player_{playerName}_{playerId}";

                // 设置远程玩家颜色
                var renderer = playerObject.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = _remotePlayerColor;
                }

                // 添加到玩家对象缓存
                _playerObjects[playerId] = playerObject;

                Debug.Log($"远程玩家角色已创建: {playerName}");
            }
        }

        /// <summary>
        /// 当远程玩家离开时
        /// </summary>
        private void OnRemotePlayerLeft(Guid playerId, string reason)
        {
            if (_playerObjects.TryGetValue(playerId, out var playerObject))
            {
                Destroy(playerObject);
                _playerObjects.Remove(playerId);

                Debug.Log($"玩家角色已移除: {playerId}, 原因: {reason}");
            }
        }

        /// <summary>
        /// 当远程玩家移动时
        /// </summary>
        private void OnRemotePlayerMoved(Guid playerId, PulseRPC.Shared.Vector3 position)
        {
            if (_playerObjects.TryGetValue(playerId, out var playerObject))
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
        }
    }
}
