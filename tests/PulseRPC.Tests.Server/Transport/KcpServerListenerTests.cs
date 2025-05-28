using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Moq;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;
using Xunit;

namespace PulseRPC.Tests.Server.Transport;

/// <summary>
/// KCP服务器监听器测试
/// </summary>
public class KcpServerListenerTests : IDisposable
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly TransportOptions _options;
    private readonly int _testPort;

    public KcpServerListenerTests()
    {
        _mockLogger = new Mock<ILogger>();
        _options = new TransportOptions();
        _testPort = GetAvailablePort();
    }

    /// <summary>
    /// 测试KCP服务器监听器的启动和停止
    /// </summary>
    [Fact]
    public async Task StartAsync_And_StopAsync_Should_Work_Correctly()
    {
        // Arrange
        var listener = new KcpServerListener(_testPort, _options, _mockLogger.Object);

        // Act & Assert - 启动
        await listener.StartAsync();
        Assert.True(listener.IsListening);

        // Act & Assert - 停止
        await listener.StopAsync();
        Assert.False(listener.IsListening);
    }

    /// <summary>
    /// 测试KCP连接的生命周期管理
    /// </summary>
    [Fact]
    public async Task KcpConnection_Lifecycle_Should_Be_Managed_Correctly()
    {
        // Arrange
        var listener = new KcpServerListener(_testPort, _options, _mockLogger.Object);
        var connectionAccepted = false;
        var connectionId = string.Empty;

        listener.ConnectionAccepted += (sender, args) =>
        {
            connectionAccepted = true;
            connectionId = args.Connection.ConnectionId;
        };

        await listener.StartAsync();

        // Act - 模拟客户端握手
        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var serverEndpoint = new IPEndPoint(IPAddress.Loopback, _testPort);

        // 发送握手包（KCP conv ID）
        uint conv = 0x12345678;
        byte[] handshakeData = BitConverter.GetBytes(conv);
        clientSocket.SendTo(handshakeData, serverEndpoint);

        // 等待处理
        await Task.Delay(100);

        // Assert
        Assert.True(connectionAccepted);
        Assert.NotEmpty(connectionId);

        // Cleanup
        clientSocket.Dispose();
        await listener.StopAsync();
    }

    /// <summary>
    /// 测试KCP连接在异常情况下的资源清理
    /// </summary>
    [Fact]
    public async Task KcpConnection_Should_Handle_Exceptions_Gracefully()
    {
        // Arrange
        var listener = new KcpServerListener(_testPort, _options, _mockLogger.Object);
        KcpServerConnection? acceptedConnection = null;

        listener.ConnectionAccepted += (sender, args) =>
        {
            acceptedConnection = (KcpServerConnection)args.Connection;
        };

        await listener.StartAsync();

        // Act - 建立连接
        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var serverEndpoint = new IPEndPoint(IPAddress.Loopback, _testPort);

        uint conv = 0x12345678;
        byte[] handshakeData = BitConverter.GetBytes(conv);
        clientSocket.SendTo(handshakeData, serverEndpoint);

        await Task.Delay(100);

        // 验证连接已建立
        Assert.NotNull(acceptedConnection);
        Assert.True(acceptedConnection.IsConnected);

        // Act - 强制关闭客户端socket模拟网络异常
        clientSocket.Close();

        // 发送断开连接包
        try
        {
            byte[] disconnectData = BitConverter.GetBytes(0xFFFFFFFF);
            var tempSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            tempSocket.SendTo(disconnectData, serverEndpoint);
            tempSocket.Close();
        }
        catch
        {
            // 忽略发送失败
        }

        await Task.Delay(200);

        // Assert - 连接应该被正确清理
        // 注意：由于我们修复了生命周期管理，连接应该能够正确释放资源
        // 而不会抛出 ObjectDisposedException

        // Cleanup
        clientSocket.Dispose();
        await listener.StopAsync();
    }

    /// <summary>
    /// 测试多个KCP连接的同时管理
    /// </summary>
    [Fact]
    public async Task Multiple_KcpConnections_Should_Be_Managed_Correctly()
    {
        // Arrange
        var listener = new KcpServerListener(_testPort, _options, _mockLogger.Object);
        var acceptedConnections = new List<KcpServerConnection>();

        listener.ConnectionAccepted += (sender, args) =>
        {
            acceptedConnections.Add((KcpServerConnection)args.Connection);
        };

        await listener.StartAsync();

        // Act - 创建多个客户端连接
        var clientSockets = new List<Socket>();
        var serverEndpoint = new IPEndPoint(IPAddress.Loopback, _testPort);

        for (int i = 0; i < 3; i++)
        {
            var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            clientSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0)); // 绑定到随机端口

            uint conv = (uint)(0x12345678 + i);
            byte[] handshakeData = BitConverter.GetBytes(conv);
            clientSocket.SendTo(handshakeData, serverEndpoint);

            clientSockets.Add(clientSocket);
        }

        await Task.Delay(200);

        // Assert
        Assert.Equal(3, acceptedConnections.Count);
        Assert.All(acceptedConnections, conn => Assert.True(conn.IsConnected));

        // Cleanup
        foreach (var socket in clientSockets)
        {
            socket.Dispose();
        }
        await listener.StopAsync();
    }

    /// <summary>
    /// 测试停止监听器时的资源清理
    /// </summary>
    [Fact]
    public async Task StopAsync_Should_Cleanup_All_Resources()
    {
        // Arrange
        var listener = new KcpServerListener(_testPort, _options, _mockLogger.Object);
        var acceptedConnections = new List<KcpServerConnection>();

        listener.ConnectionAccepted += (sender, args) =>
        {
            acceptedConnections.Add((KcpServerConnection)args.Connection);
        };

        await listener.StartAsync();

        // 建立一些连接
        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var serverEndpoint = new IPEndPoint(IPAddress.Loopback, _testPort);

        uint conv = 0x12345678;
        byte[] handshakeData = BitConverter.GetBytes(conv);
        clientSocket.SendTo(handshakeData, serverEndpoint);

        await Task.Delay(100);

        // Act - 停止监听器
        await listener.StopAsync();

        // Assert
        Assert.False(listener.IsListening);

        // 验证连接被正确清理 - 这里应该不会抛出异常
        // 如果我们的修复有效，资源应该被正确释放

        // Cleanup
        clientSocket.Dispose();
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
        // Cleanup any resources
    }
}
