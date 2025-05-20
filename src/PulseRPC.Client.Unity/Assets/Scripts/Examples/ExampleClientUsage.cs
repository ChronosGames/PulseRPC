using System;
using UnityEngine;
using UnityEngine.UI;

namespace PulseRPC.Examples
{
    public class GameClientBehaviour : MonoBehaviour
    {
        [SerializeField] private UnityNetworkClient _networkClient;

        private IPlayerService _playerService;
        private List<ISubscriptionToken> _subscriptions = new List<ISubscriptionToken>();

        private async void Start()
        {
            // 获取服务 - 将使用生成的PlayerServiceProxy
            _playerService = _networkClient.GetService<IPlayerService>();

            // 连接服务器
            await _networkClient.ConnectAsync();

            // 订阅事件
            SubscribeToEvents();
        }

        private void SubscribeToEvents()
        {
            // 订阅事件 - 无需反射
            _subscriptions.Add(_playerService.SubscribeToPlayerJoined(OnPlayerJoined));
            _subscriptions.Add(_playerService.SubscribeToPlayerLeft(OnPlayerLeft));
            _subscriptions.Add(_playerService.SubscribeToPlayerMoved(OnPlayerMoved));
        }

        // 事件处理方法
        private void OnPlayerJoined(PlayerJoinedEvent evt)
        {
            // 已在主线程中，可以安全更新UI
            Debug.Log($"玩家加入: {evt.PlayerName}");
        }

        // 其他事件处理方法...

        // 调用服务方法 - 无需反射
        public async void LoginUser(string username, string password)
        {
            try
            {
                var response = await _playerService.LoginAsync(new LoginRequest
                {
                    Username = username,
                    Password = password
                });

                if (response.Success)
                {
                    Debug.Log($"登录成功: {response.Player.Username}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"登录失败: {ex.Message}");
            }
        }
    }
}
