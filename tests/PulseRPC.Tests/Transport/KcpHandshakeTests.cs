using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Client.Transport;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;
using Xunit;
using Xunit.Abstractions;

namespace PulseRPC.Tests.Transport
{
    /// <summary>
    /// KCP握手协议测试
    /// </summary>
    public class KcpHandshakeTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new();

        public KcpHandshakeTests(ITestOutputHelper output)
        {
            _output = output;
            _logger = new TestLogger(output);
        }

        /// <summary>
        /// 测试正常握手流程
        /// </summary>
        [Fact]
        public async Task KcpHandshake_NormalFlow_ShouldSucceed()
        {
            // Arrange
            var serverPort = GetAvailablePort();
            var serverOptions = new TransportOptions
            {
                Kcp = new KcpOptions { ConversationId = 12345 }
            };

            using var serverListener = new KcpServerListener(serverPort, serverOptions, _logger);
            var connectionReceived = new TaskCompletionSource<bool>();

            serverListener.ConnectionAccepted += (sender, e) =>
            {
                _output.WriteLine($"服务器接受连接: {e.Connection.ConnectionId}");
                connectionReceived.SetResult(true);
            };

            // 启动服务器
            await serverListener.StartAsync(_cts.Token);
            _output.WriteLine($"KCP服务器启动，端口: {serverPort}");

            // Act - 客户端连接
            var clientOptions = new TransportOptions
            {
                ConnectionTimeout = TimeSpan.FromSeconds(5),
                Kcp = new KcpOptions { ConversationId = 12345 }
            };

            using var client = new KcpClientTransport(clientOptions, _logger);

            await client.ConnectAsync("127.0.0.1", serverPort, _cts.Token);

            // Assert
            Assert.True(client.IsConnected, "客户端应该连接成功");

            // 等待服务器接受连接事件
            var connectionAccepted = await connectionReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(connectionAccepted, "服务器应该接受客户端连接");

            await serverListener.StopAsync(_cts.Token);
        }

        /// <summary>
        /// 测试握手超时场景
        /// </summary>
        [Fact]
        public async Task KcpHandshake_ServerNotRunning_ShouldTimeout()
        {
            // Arrange
            var unavailablePort = GetAvailablePort();
            var clientOptions = new TransportOptions
            {
                ConnectionTimeout = TimeSpan.FromSeconds(1), // 短超时时间
                Kcp = new KcpOptions { ConversationId = 12345 }
            };

            using var client = new KcpClientTransport(clientOptions, _logger);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await client.ConnectAsync("127.0.0.1", unavailablePort, _cts.Token);
            });

            Assert.Contains("KCP握手超时", exception.Message);
            Assert.False(client.IsConnected, "客户端不应该连接成功");
        }

        /// <summary>
        /// 测试不同ConversationId的握手
        /// </summary>
        [Fact]
        public async Task KcpHandshake_DifferentConversationId_ShouldCreateSeparateConnections()
        {
            // Arrange
            var serverPort = GetAvailablePort();
            var serverOptions = new TransportOptions();

            using var serverListener = new KcpServerListener(serverPort, serverOptions, _logger);
            var connectionsReceived = 0;

            serverListener.ConnectionAccepted += (sender, e) =>
            {
                Interlocked.Increment(ref connectionsReceived);
                _output.WriteLine($"服务器接受连接: {e.Connection.ConnectionId}");
            };

            await serverListener.StartAsync(_cts.Token);

            // Act - 两个不同ConversationId的客户端
            var client1Options = new TransportOptions
            {
                ConnectionTimeout = TimeSpan.FromSeconds(5),
                Kcp = new KcpOptions { ConversationId = 11111 }
            };

            var client2Options = new TransportOptions
            {
                ConnectionTimeout = TimeSpan.FromSeconds(5),
                Kcp = new KcpOptions { ConversationId = 22222 }
            };

            using var client1 = new KcpClientTransport(client1Options, _logger);
            using var client2 = new KcpClientTransport(client2Options, _logger);

            await client1.ConnectAsync("127.0.0.1", serverPort, _cts.Token);
            await client2.ConnectAsync("127.0.0.1", serverPort, _cts.Token);

            // 等待连接被处理
            await Task.Delay(1000, _cts.Token);

            // Assert
            Assert.True(client1.IsConnected, "客户端1应该连接成功");
            Assert.True(client2.IsConnected, "客户端2应该连接成功");
            Assert.Equal(2, connectionsReceived);

            await serverListener.StopAsync(_cts.Token);
        }

        /// <summary>
        /// 测试重复握手包处理
        /// </summary>
        [Fact]
        public async Task KcpHandshake_DuplicateHandshakePackets_ShouldHandleGracefully()
        {
            // Arrange
            var serverPort = GetAvailablePort();
            var serverOptions = new TransportOptions();
            using var serverListener = new KcpServerListener(serverPort, serverOptions, _logger);

            var connectionsReceived = 0;
            serverListener.ConnectionAccepted += (sender, e) =>
            {
                Interlocked.Increment(ref connectionsReceived);
            };

            await serverListener.StartAsync(_cts.Token);

            // Act - 手动发送多个握手包
            using var udpClient = new UdpClient();
            var serverEndpoint = new IPEndPoint(IPAddress.Loopback, serverPort);

            uint conversationId = 12345;
            byte[] handshakeData = BitConverter.GetBytes(conversationId);

            // 发送多个握手包
            for (int i = 0; i < 3; i++)
            {
                await udpClient.SendAsync(handshakeData, handshakeData.Length, serverEndpoint);
                await Task.Delay(100, _cts.Token);
            }

            // 等待处理
            await Task.Delay(1000, _cts.Token);

            // Assert - 应该只创建一个连接
            Assert.Equal(1, connectionsReceived);

            await serverListener.StopAsync(_cts.Token);
        }

        /// <summary>
        /// 测试错误格式的握手包
        /// </summary>
        [Fact]
        public async Task KcpHandshake_InvalidPacketSize_ShouldBeIgnored()
        {
            // Arrange
            var serverPort = GetAvailablePort();
            using var serverListener = new KcpServerListener(serverPort, new TransportOptions(), _logger);

            var connectionsReceived = 0;
            serverListener.ConnectionAccepted += (sender, e) =>
            {
                Interlocked.Increment(ref connectionsReceived);
            };

            await serverListener.StartAsync(_cts.Token);

            // Act - 发送错误大小的数据包
            using var udpClient = new UdpClient();
            var serverEndpoint = new IPEndPoint(IPAddress.Loopback, serverPort);

            // 发送不同大小的错误包
            await udpClient.SendAsync(new byte[2], 2, serverEndpoint); // 太小
            await udpClient.SendAsync(new byte[8], 8, serverEndpoint); // 太大
            await udpClient.SendAsync(new byte[0], 0, serverEndpoint); // 空包

            await Task.Delay(1000, _cts.Token);

            // Assert - 不应该创建任何连接
            Assert.Equal(0, connectionsReceived);

            await serverListener.StopAsync(_cts.Token);
        }

        /// <summary>
        /// 测试并发握手
        /// </summary>
        [Fact]
        public async Task KcpHandshake_ConcurrentConnections_ShouldSucceed()
        {
            // Arrange
            var serverPort = GetAvailablePort();
            using var serverListener = new KcpServerListener(serverPort, new TransportOptions(), _logger);

            var connectionsReceived = 0;
            serverListener.ConnectionAccepted += (sender, e) =>
            {
                Interlocked.Increment(ref connectionsReceived);
            };

            await serverListener.StartAsync(_cts.Token);

            // Act - 并发连接
            const int concurrentConnections = 5;
            var tasks = new Task[concurrentConnections];
            var clients = new KcpClientTransport[concurrentConnections];

            for (int i = 0; i < concurrentConnections; i++)
            {
                var clientOptions = new TransportOptions
                {
                    ConnectionTimeout = TimeSpan.FromSeconds(5),
                    Kcp = new KcpOptions { ConversationId = (uint)(10000 + i) }
                };

                clients[i] = new KcpClientTransport(clientOptions, _logger);

                tasks[i] = clients[i].ConnectAsync("127.0.0.1", serverPort, _cts.Token);
            }

            await Task.WhenAll(tasks);

            // 等待所有连接被处理
            await Task.Delay(2000, _cts.Token);

            // Assert
            for (int i = 0; i < concurrentConnections; i++)
            {
                Assert.True(clients[i].IsConnected, $"客户端{i}应该连接成功");
                clients[i].Dispose();
            }

            Assert.Equal(concurrentConnections, connectionsReceived);

            await serverListener.StopAsync(_cts.Token);
        }

        /// <summary>
        /// 获取可用端口
        /// </summary>
        private static int GetAvailablePort()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
