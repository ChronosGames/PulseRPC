using Xunit;

namespace PulseRPC.Client.Tests;

public class NetworkManagerTests
{
    [Fact]
    public async Task NetworkManager_ShouldManageConnectionsByNodeName()
    {
        // 1. 根据节点名注册连接
        NetworkManager.RegisterNode("MainServer", "localhost", 5000);
        NetworkManager.RegisterNode("NotificationServer", "localhost", 5001);

        // 2. 获取指定节点的连接
        var client1 = NetworkManager.GetOrCreateClient("MainServer");
        var client2 = NetworkManager.GetOrCreateClient("NotificationServer");

        Assert.NotNull(client1);
        Assert.NotNull(client2);
        Assert.NotEqual(client1, client2);

        // 3. 获取相同节点应返回相同实例
        var client1Again = NetworkManager.GetOrCreateClient("MainServer");
        Assert.Equal(client1, client1Again);

        // 4. 测试服务客户端创建
        var service = NetworkManager.CreateServiceClient<IExampleService>("MainServer");
        Assert.NotNull(service);

        // 5. 测试接收器注册
        var receiver = new ExampleReceiver();
        bool registered = NetworkManager.RegisterReceiverHandler("MainServer", receiver);
        Assert.True(registered);
    }

    [Fact]
    public async Task NetworkClient_ShouldReconnectAutomatically()
    {
        // 模拟服务器
        using var server = new MockTcpServer(5002);
        server.Start();

        // 注册节点并创建客户端
        NetworkManager.RegisterNode("TestServer", "localhost", 5002,
            new NodeOptions { AutoReconnect = true, ReconnectInterval = TimeSpan.FromMilliseconds(100) });

        var client = NetworkManager.GetOrCreateClient("TestServer");
        await client.ConnectAsync();

        Assert.True(client.IsConnected);

        // 断开服务器连接
        server.Stop();
        await Task.Delay(50);
        Assert.False(client.IsConnected);

        // 重启服务器
        server.Start();
        await Task.Delay(200); // 等待自动重连

        Assert.True(client.IsConnected);
    }
}
