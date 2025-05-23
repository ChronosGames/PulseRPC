using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChatApp.Shared;
using PulseRPC;
using PulseRPC.Transport;
using PulseRPC.Client.Channels;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Client;
using UnityEngine;

namespace ChatApp
{
    public class NetworkManager : MonoBehaviour
    {
        [Header("服务器配置")] [SerializeField] private string _host = "game-server.example.com";
        [SerializeField] private int _tcpPort = 7000;
        [SerializeField] private int _kcpPort = 7001;

        // 通道管理器
        private IChannelManager _channelManager;
        private TransportFactory _transportFactory;

        // 游戏服务
        private IPlayerService _playerService;

        // 事件订阅
        private List<ISubscriptionToken> _subscriptions = new List<ISubscriptionToken>();

        private void Awake()
        {
            // 创建通道工厂和管理器
            _transportFactory = new TransportFactory();
            _channelManager = new ChannelManager();

            // 初始化网络系统
            DontDestroyOnLoad(gameObject);
        }

        private async void Start()
        {
            // 初始化通道
            await InitializeChannelsAsync();

            // 获取服务代理
            _playerService = _channelManager.GetPlayerService<IPlayerService>();

            // 连接到服务器
            await ConnectAsync();
        }

        private async Task InitializeChannelsAsync()
        {
            try
            {
                // 创建序列化器
                var serializer = new PulseRPCSerializer();

                // 创建TCP通道
                var tcpOptions = new TransportOptions
                {
                    ReadBufferSize = 8192,
                    WriteBufferSize = 8192,
                    ConnectionTimeout = 5000,
                    UseCompression = true
                };

                var tcpTransport = await _transportFactory.CreateClientTransportAsync(TransportType.Tcp, tcpOptions);
                var tcpChannel = new TransportChannel("TcpChannel", tcpTransport, serializer, null);
                _channelManager.RegisterChannel("TcpChannel", tcpChannel, true);

                // 创建KCP通道
                var kcpOptions = new TransportOptions
                {
                    ReadBufferSize = 8192,
                    WriteBufferSize = 8192,
                    ConnectionTimeout = 5000,
                    UseCompression = false,
                    Kcp = new KcpOptions { NoDelay = 1 }
                };

                var kcpTransport = await _transportFactory.CreateClientTransportAsync(TransportType.Kcp, kcpOptions);
                var kcpChannel = new TransportChannel("KcpChannel", kcpTransport, serializer, null);
                _channelManager.RegisterChannel("KcpChannel", kcpChannel);

                Debug.Log("通道初始化完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"通道初始化失败: {ex.Message}");
            }
        }

        private async Task ConnectAsync()
        {
            try
            {
                // 连接TCP通道
                var tcpChannel = _channelManager.GetChannel("TcpChannel") as IHasTransport;
                await tcpChannel.ConnectAsync(_host, _tcpPort, CancellationToken.None);

                // 连接KCP通道
                var kcpChannel = _channelManager.GetChannel("KcpChannel") as IHasTransport;
                await kcpChannel.ConnectAsync(_host, _kcpPort, CancellationToken.None);

                Debug.Log("已连接到服务器");

                // 登录
                await LoginAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"连接失败: {ex.Message}");
            }
        }

        private async Task LoginAsync()
        {
            try
            {
                var response = await _playerService.LoginAsync(new LoginRequest
                {
                    Username = "Player" + UnityEngine.Random.Range(1000, 9999), Password = "password"
                });

                if (response.Success)
                {
                    Debug.Log($"登录成功: {response.Player.Username}");

                    // 订阅事件
                    SubscribeToEvents();
                }
                else
                {
                    Debug.LogError($"登录失败: {response.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"登录失败: {ex.Message}");
            }
        }

        private void SubscribeToEvents()
        {
            // 订阅登录事件 (TCP)
            var tcpChannel = _channelManager.GetChannel("TcpChannel");
            var loginJoinedToken = tcpChannel.SubscribeToEvent<PlayerJoinedEvent>("OnPlayerJoined", OnPlayerJoined);
            var loginLeftToken = tcpChannel.SubscribeToEvent<PlayerLeftEvent>("OnPlayerLeft", OnPlayerLeft);
            _subscriptions.Add(loginJoinedToken);
            _subscriptions.Add(loginLeftToken);

            // 订阅移动事件 (KCP)
            var kcpChannel = _channelManager.GetChannel("KcpChannel");
            var moveToken = kcpChannel.SubscribeToEvent<PlayerMovedEvent>("OnPlayerMoved", OnPlayerMoved);
            var moveBatchToken = kcpChannel.SubscribeToEvent<PlayerMovedEvent[]>("OnPlayersMovedBatch", OnPlayersMovedBatch);
            _subscriptions.Add(moveToken);
            _subscriptions.Add(moveBatchToken);

            Debug.Log("已订阅游戏事件");
        }

        private void OnPlayerJoined(object sender, PlayerJoinedEvent eventData)
        {
            Debug.Log($"玩家加入: {eventData.PlayerName} (ID: {eventData.PlayerId})");
            // 处理玩家加入...
        }

        private void OnPlayerLeft(object sender, PlayerLeftEvent eventData)
        {
            Debug.Log($"玩家离开: {eventData.PlayerId}, 原因: {eventData.Reason}");
            // 处理玩家离开...
        }

        private void OnPlayerMoved(object sender, PlayerMovedEvent eventData)
        {
            // 更新玩家位置
            var playerObject = GetPlayerObject(eventData.PlayerId);
            if (playerObject != null)
            {
                var position = new Vector3(eventData.X, eventData.Y, eventData.Z);
                playerObject.transform.position = position;
                playerObject.transform.rotation = Quaternion.Euler(0, eventData.RotationY, 0);
            }
        }

        private void OnPlayersMovedBatch(object sender, PlayerMovedEvent[] eventData)
        {
            // 批量更新玩家位置
            foreach (var moveEvent in eventData)
            {
                OnPlayerMoved(sender, moveEvent);
            }
        }

        private GameObject GetPlayerObject(Guid playerId)
        {
            // 查找玩家对象的实现
            // 这里应该连接到 PlayerManager 或其他玩家管理系统
            return null;
        }

        private void OnDestroy()
        {
            // 清理订阅
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            // 断开连接
            _channelManager?.Dispose();
        }
    }
}
