using System;
using System.Threading.Tasks;
using UnityEngine;
using PulseRPC.Client;
using PulseRPC.Transport;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Client.Channels;

namespace PulseRPC.Examples
{
    /// <summary>
    /// PulseRPC 简单示例
    /// </summary>
    public class SimplePulseRPCExample : MonoBehaviour
    {
        [Header("服务器设置")]
        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 7000;
        [SerializeField] private bool _autoConnect = true;

        // 通道管理器
        private IChannelManager _channelManager;
        // 示例服务接口
        private IExampleService _exampleService;

        private async void Start()
        {
            if (_autoConnect)
            {
                await InitializeAndConnect();
            }
        }

        /// <summary>
        /// 初始化并连接
        /// </summary>
        public async Task InitializeAndConnect()
        {
            try
            {
                // 初始化客户端
                await Initialize();

                // 连接服务器
                await Connect();

                // 调用服务
                await CallService();
            }
            catch (Exception ex)
            {
                Debug.LogError($"发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 初始化客户端
        /// </summary>
        private async Task Initialize()
        {
            Debug.Log("正在初始化客户端...");

            // 创建序列化器
            var serializer = new PulseRPCSerializer();

            // 创建传输工厂
            var transportFactory = new TransportFactory();

            // 创建通道管理器
            _channelManager = new ChannelManager();

            // 创建TCP通道
            var tcpOptions = new TransportOptions { NoDelay = true, KeepAlive = true };
            var tcpTransport = await transportFactory.CreateClientTransportAsync(TransportType.Tcp, tcpOptions);
            var tcpChannel = new TransportChannel("TcpChannel", tcpTransport, serializer, null);

            // 注册通道
            _channelManager.RegisterChannel("TcpChannel", tcpChannel, true);

            // 获取服务代理
            _exampleService = _channelManager.GetService<IExampleService>("ExampleService");

            Debug.Log("客户端初始化完成");
        }

        /// <summary>
        /// 连接服务器
        /// </summary>
        private async Task Connect()
        {
            Debug.Log($"正在连接到服务器 {_host}:{_port}...");

            // 获取默认通道
            var channel = _channelManager.GetDefaultChannel() as IHasTransport;
            if (channel == null)
            {
                throw new InvalidOperationException("通道不支持传输");
            }

            // 连接服务器
            await channel.ConnectAsync(_host, _port);

            Debug.Log("已连接到服务器");
        }

        /// <summary>
        /// 调用服务
        /// </summary>
        private async Task CallService()
        {
            try
            {
                Debug.Log("正在调用服务...");

                // 如果服务代理已成功创建
                if (_exampleService != null)
                {
                    // 调用 Ping 方法
                    var pingResponse = await _exampleService.PingAsync();
                    Debug.Log($"Ping响应: {pingResponse}");

                    // 调用 Echo 方法
                    var echoResponse = await _exampleService.EchoAsync("Hello, PulseRPC!");
                    Debug.Log($"Echo响应: {echoResponse}");

                    // 调用 Add 方法
                    var addResponse = await _exampleService.AddAsync(10, 20);
                    Debug.Log($"Add响应: {addResponse}");

                    // 订阅通知事件
                    await SubscribeToNotifications();
                }
                else
                {
                    Debug.LogWarning("服务代理未初始化");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"调用服务失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 订阅通知事件
        /// </summary>
        private Task SubscribeToNotifications()
        {
            Debug.Log("正在订阅通知事件...");

            // 获取默认通道
            var channel = _channelManager.GetDefaultChannel();

            // 订阅消息通知事件
            var token = channel.SubscribeToEvent<MessageNotification>(
                "OnMessageReceived",
                (sender, notification) =>
                {
                    Debug.Log($"收到消息通知: {notification.Message} (来自: {notification.Sender})");
                });

            // 记录订阅成功
            Debug.Log($"通知事件订阅成功，令牌ID: {token.Id}");

            return Task.CompletedTask;
        }

        private void OnDestroy()
        {
            // 释放资源
            _channelManager?.Dispose();
            Debug.Log("已释放资源");
        }
    }

    /// <summary>
    /// 示例服务接口
    /// </summary>
    public interface IExampleService
    {
        /// <summary>
        /// Ping 服务器
        /// </summary>
        Task<string> PingAsync();

        /// <summary>
        /// 回显消息
        /// </summary>
        Task<string> EchoAsync(string message);

        /// <summary>
        /// 计算两数之和
        /// </summary>
        Task<int> AddAsync(int a, int b);
    }

    /// <summary>
    /// 消息通知
    /// </summary>
    [Serializable]
    public class MessageNotification
    {
        public string Message { get; set; }
        public string Sender { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
