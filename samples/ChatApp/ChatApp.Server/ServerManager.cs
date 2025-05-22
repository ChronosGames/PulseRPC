// PulseRPC.Server/ServerManager.cs
using Microsoft.Extensions.Logging;
using PulseRPC.Serialization;
using PulseRPC.Transport;
using PulseRPC.Server.Channels;
using PulseRPC.Server;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Transport;

namespace PulseRPC.Server
{
    /// <summary>
    /// 服务器管理器接口
    /// </summary>
    public interface IServerManager : IDisposable
    {
        /// <summary>
        /// 添加传输
        /// </summary>
        void AddTransport(
            string channelName,
            TransportType transportType,
            int port,
            TransportOptions options = null,
            bool isDefault = false);

        /// <summary>
        /// 启动服务器
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止服务器
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 服务器管理器实现
    /// </summary>
    public class ServerManager : IServerManager
    {
        private readonly ServiceRegistry _serviceRegistry;
        private readonly ISerializer _serializer;
        private readonly ILogger<ServerManager> _logger;
        private readonly Dictionary<string, TransportInfo> _transports = new Dictionary<string, TransportInfo>();
        private readonly ServerChannelManager _channelManager;
        private bool _isRunning;

        public ServerManager(
            ServiceRegistry serviceRegistry,
            ISerializer serializer,
            ILogger<ServerManager> logger)
        {
            _serviceRegistry = serviceRegistry;
            _serializer = serializer;
            _logger = logger;
            _channelManager = new ServerChannelManager();
        }

        /// <summary>
        /// 添加传输
        /// </summary>
        public void AddTransport(
            string channelName,
            TransportType transportType,
            int port,
            TransportOptions options = null,
            bool isDefault = false)
        {
            if (_isRunning)
                throw new InvalidOperationException("服务器运行中，无法添加传输");

            if (_transports.ContainsKey(channelName))
                throw new ArgumentException($"通道已存在: {channelName}");

            // 创建传输信息
            var transportInfo = new TransportInfo
            {
                Name = channelName,
                Type = transportType,
                Port = port,
                Options = options ?? new TransportOptions(),
                IsDefault = isDefault
            };

            _transports.Add(channelName, transportInfo);

            _logger.LogInformation("已添加 {Type} 传输: {Name}, 端口: {Port}",
                transportType, channelName, port);
        }

        /// <summary>
        /// 启动服务器
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isRunning)
                return;

            _logger.LogInformation("正在启动服务器...");

            try
            {
                // 创建传输工厂
                var transportFactory = new TransportFactory(
                    LoggerFactory.Create(builder => builder.AddConsole()));

                // 启动所有传输
                foreach (var transport in _transports.Values)
                {
                    // 创建服务器监听器
                    var listener = await transportFactory.CreateServerListenerAsync(
                        transport.Type,
                        transport.Port,
                        transport.Options);

                    // 创建服务器通道
                    var channel = new ServerTransportChannel(
                        transport.Name,
                        listener,
                        _serializer,
                        _logger as ILogger<ServerTransportChannel>);

                    // 添加消息处理器
                    channel.MessageReceived += OnMessageReceived;

                    // 注册通道
                    _channelManager.RegisterChannel(transport.Name, channel, transport.IsDefault);

                    // 启动监听器
                    await listener.StartAsync(cancellationToken);

                    _logger.LogInformation("已启动 {Type} 传输: {Name}, 端口: {Port}",
                        transport.Type, transport.Name, transport.Port);
                }

                _isRunning = true;
                _logger.LogInformation("服务器已启动");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动服务器失败");

                // 如果启动失败，清理已启动的通道
                await StopAsync(cancellationToken);

                throw;
            }
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!_isRunning)
                return;

            _logger.LogInformation("正在停止服务器...");

            _isRunning = false;

            // 释放通道资源
            _channelManager.Dispose();

            _logger.LogInformation("服务器已停止");

            await Task.CompletedTask;
        }

        /// <summary>
        /// 处理接收到的消息
        /// </summary>
        private async void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                var channel = sender as IServerChannel;
                var message = e.Message;

                // 处理不同类型消息
                switch (message.Header.Type)
                {
                    case MessageType.Request:
                        await HandleRequestAsync(channel, e.ClientId, message);
                        break;

                    case MessageType.Ping:
                        await HandlePingAsync(channel, e.ClientId, message);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理消息异常");
            }
        }

        /// <summary>
        /// 处理请求消息
        /// </summary>
        private async Task HandleRequestAsync(IServerChannel channel, string clientId, NetworkMessage message)
        {
            var header = message.Header;

            try
            {
                // 执行服务方法
                var result = await _serviceRegistry.InvokeMethodAsync(
                    header.ServiceName,
                    header.MethodName,
                    message.Body, CancellationToken.None);

                // 创建响应
                var responseHeader = new MessageHeader
                {
                    Type = MessageType.Response,
                    MessageId = header.MessageId,
                    ServiceName = header.ServiceName,
                    MethodName = header.MethodName,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                // 发送响应
                dynamic response = result;
                await channel.SendMessageAsync(clientId, responseHeader, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行方法异常: {ServiceName}.{MethodName}",
                    header.ServiceName, header.MethodName);

                // 创建错误响应
                var errorHeader = new MessageHeader
                {
                    Type = MessageType.Response,
                    MessageId = header.MessageId,
                    ServiceName = header.ServiceName,
                    MethodName = header.MethodName,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                var errorResponse = new ErrorResponse
                {
                    ErrorCode = "INTERNAL_ERROR",
                    ErrorMessage = ex.Message
                };

                // 发送错误响应
                await channel.SendMessageAsync(clientId, errorHeader, errorResponse);
            }
        }

        /// <summary>
        /// 处理Ping消息
        /// </summary>
        private async Task HandlePingAsync(IServerChannel channel, string clientId, NetworkMessage message)
        {
            // 创建Pong响应
            var responseHeader = new MessageHeader
            {
                Type = MessageType.Pong,
                MessageId = message.Header.MessageId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // 发送Pong响应
            await channel.SendMessageAsync(clientId, responseHeader, null);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 传输信息
        /// </summary>
        private class TransportInfo
        {
            public string Name { get; set; }
            public TransportType Type { get; set; }
            public int Port { get; set; }
            public TransportOptions Options { get; set; }
            public bool IsDefault { get; set; }
        }

        /// <summary>
        /// 错误响应
        /// </summary>
        private class ErrorResponse
        {
            public string ErrorCode { get; set; }
            public string ErrorMessage { get; set; }
        }
    }
}
