// See https://aka.ms/new-console-template for more information

using System;
using System.Threading.Tasks;
using ChatApp.Shared.Hubs;
using ChatApp.Shared.Models;
using ChatApp.Shared.Services;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Network;

namespace ChatApp.Console
{
    /// <summary>
    /// 聊天客户端程序
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            // 创建TCP连接
            var connection = new TcpConnection("localhost", 7000);
            await connection.ConnectAsync();

            System.Console.WriteLine("已连接到服务器");

            // 创建序列化器
            var serializer = new JsonSerializer();

            try
            {
                // 创建聊天服务客户端
                var chatService = PulseRPCFactory.CreateServiceClient<IChatService>(connection, serializer);

                // 创建聊天Hub客户端
                var chatHub = PulseRPCFactory.CreateServiceClient<IChatHub>(connection, serializer);

                // 创建接收器实现
                var chatHubReceiver = new ChatHubReceiver();

                // 注册接收器处理器
                PulseRPCFactory.RegisterReceiverHandler<IChatHubReceiver>(connection, serializer, chatHubReceiver);

                // 输入用户名和房间名
                System.Console.Write("请输入您的用户名: ");
                var userName = System.Console.ReadLine() ?? "Guest";

                System.Console.Write("请输入房间名: ");
                var roomName = System.Console.ReadLine() ?? "General";

                // 加入聊天室
                var joinRequest = new JoinRequest
                {
                    UserName = userName,
                    RoomName = roomName
                };

                var joinResult = await chatHub.JoinAsync(joinRequest);
                if (joinResult)
                {
                    System.Console.WriteLine($"已加入房间: {roomName}");
                }
                else
                {
                    System.Console.WriteLine("加入房间失败");
                    return;
                }

                // 发送测试报告
                await chatService.SendReportAsync($"{userName} 已连接");

                // 消息发送循环
                while (true)
                {
                    var message = System.Console.ReadLine();
                    if (string.IsNullOrEmpty(message))
                    {
                        continue;
                    }

                    if (message == "/exit")
                    {
                        break;
                    }

                    if (message == "/exception")
                    {
                        try
                        {
                            await chatHub.GenerateException("测试异常");
                        }
                        catch (Exception ex)
                        {
                            System.Console.WriteLine($"[异常测试] {ex.Message}");
                        }
                        continue;
                    }

                    // 发送消息
                    await chatHub.SendMessageAsync(message);
                }

                // 离开聊天室
                await chatHub.LeaveAsync();

                System.Console.WriteLine("已离开聊天室");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"发生错误: {ex.Message}");
            }
            finally
            {
                // 关闭连接
                await connection.DisconnectAsync();
            }
        }
    }

    /// <summary>
    /// 聊天Hub接收器实现
    /// </summary>
    class ChatHubReceiver : IChatHubReceiver
    {
        /// <summary>
        /// 处理用户加入事件
        /// </summary>
        public void OnJoin(string name)
        {
            System.Console.WriteLine($"[系统] {name} 加入了聊天室");
        }

        /// <summary>
        /// 处理用户离开事件
        /// </summary>
        public void OnLeave(string name)
        {
            System.Console.WriteLine($"[系统] {name} 离开了聊天室");
        }

        /// <summary>
        /// 处理新消息事件
        /// </summary>
        public void OnSendMessage(MessageResponse message)
        {
            System.Console.WriteLine($"[{message.UserName}] {message.Message}");
        }

        /// <summary>
        /// 处理Hello请求
        /// </summary>
        public Task<string> HelloAsync(string name, int age)
        {
            var response = $"你好 {name}，你今年 {age} 岁了";
            System.Console.WriteLine($"[系统] {response}");
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// 简单的TCP连接实现
    /// </summary>
    class TcpConnection : IPulseClientConnection
    {
        private readonly string _host;
        private readonly int _port;
        private System.Net.Sockets.TcpClient _client;
        private System.Net.Sockets.NetworkStream _stream;

        public TcpConnection(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public async Task ConnectAsync()
        {
            _client = new System.Net.Sockets.TcpClient();
            await _client.ConnectAsync(_host, _port);
            _stream = _client.GetStream();
        }

        public Task DisconnectAsync()
        {
            _client?.Close();
            return Task.CompletedTask;
        }

        public Task<TResponse> SendRequestAsync<TResponse>(IPacket request, System.Threading.CancellationToken cancellationToken = default)
            where TResponse : IPacket
        {
            // 简化的实现，实际应该处理序列化、消息ID等
            throw new NotImplementedException("实际实现应处理消息发送和接收");
        }

        public Task SendPacketAsync(IPacket packet, System.Threading.CancellationToken cancellationToken = default)
        {
            // 简化的实现，实际应该处理序列化、消息ID等
            throw new NotImplementedException("实际实现应处理消息发送");
        }

        public void RegisterHandler(string packetType, object handler)
        {
            // 简化的实现，实际应该保存处理器并在收到消息时调用
            System.Console.WriteLine($"注册处理器: {packetType}");
        }
    }

    /// <summary>
    /// 简单的JSON序列化器实现
    /// </summary>
    class JsonSerializer : IPulseRPCSerializer
    {
        public byte[] Serialize(object obj)
        {
            return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(obj);
        }

        public object? Deserialize(byte[] data, Type type)
        {
            return System.Text.Json.JsonSerializer.Deserialize(data, type);
        }

        public void RegisterPacket(ushort id, Type type)
        {
            // 简化的实现，实际应该保存类型映射
            System.Console.WriteLine($"注册消息类型: {id} -> {type.Name}");
        }
    }
}
