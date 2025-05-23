# PulseRPC.Client.Unity

PulseRPC Unity 客户端是一个适用于 Unity 环境的 RPC（远程过程调用）框架，提供了高效、可靠的网络通信功能。

## 项目结构

重构后的项目结构如下：

```
PulseRPC.Client.Unity/
├── Channels/                 - 通道相关实现
│   ├── ChannelManager.cs     - 通道管理器
│   ├── IMessageChannel.cs    - 消息通道接口
│   └── TransportChannel.cs   - 传输通道实现
├── Transport/                - 传输层实现
│   └── TransportFactory.cs   - 传输工厂和各种传输实现
├── Serialization/            - 序列化相关实现
│   ├── ISerializer.cs        - 序列化器接口
│   └── PulseRPCSerializer.cs - 序列化器实现
├── Messaging/                - 消息传递相关实现
│   └── IEventHandler.cs      - 事件处理器接口
├── Generated/                - 生成的代理代码
├── Examples/                 - 示例代码
└── UnityClient.cs            - Unity客户端主类
```

## 使用方法

### 1. 初始化客户端

```csharp
// 创建序列化器
var serializer = new PulseRPCSerializer();

// 创建传输工厂
var transportFactory = new TransportFactory();

// 创建通道管理器
var channelManager = new ChannelManager();

// 创建TCP通道
var tcpOptions = new TransportOptions { NoDelay = true, KeepAlive = true };
var tcpTransport = await transportFactory.CreateClientTransportAsync(TransportType.Tcp, tcpOptions);
var tcpChannel = new TransportChannel("TcpChannel", tcpTransport, serializer);
channelManager.RegisterChannel("TcpChannel", tcpChannel, true);

// 连接到服务器
await tcpChannel.ConnectAsync("localhost", 7000);
```

### 2. 调用远程服务

```csharp
// 获取服务代理
var playerService = channelManager.GetService<IPlayerService>();

// 调用远程方法
var response = await playerService.LoginAsync(new LoginRequest
{
    Username = "Player123",
    Password = "password"
});

if (response.Success)
{
    Debug.Log($"登录成功: {response.Player.Username}");
}
else
{
    Debug.LogError($"登录失败: {response.ErrorMessage}");
}
```

### 3. 订阅事件

```csharp
// 订阅事件
var loginEventsHandler = new PlayerLoginEventsImpl();
var tcpChannel = channelManager.GetChannel("TcpChannel");
var token = tcpChannel.SubscribeToEvent<PlayerJoinedEvent>("OnPlayerJoined",
    (sender, eventData) => loginEventsHandler.OnPlayerJoined(eventData));

// 处理事件的实现
private class PlayerLoginEventsImpl : IPlayerLoginEvents
{
    public void OnPlayerJoined(PlayerJoinedEvent eventData)
    {
        Debug.Log($"玩家加入: {eventData.PlayerName} (ID: {eventData.PlayerId})");
    }

    public void OnPlayerLeft(PlayerLeftEvent eventData)
    {
        Debug.Log($"玩家离开: {eventData.PlayerId}, 原因: {eventData.Reason}");
    }
}
```

### 4. 清理资源

```csharp
// 取消订阅
token.Dispose();

// 断开连接并释放资源
channelManager.Dispose();
```

## 注意事项

1. 在实际项目中，您需要为特定的服务和事件生成代理代码。可以使用 PulseRPC.Client.SourceGenerator 来自动生成。

2. 本框架提供了多种传输实现，包括 TCP、KCP 和 WebSocket。您可以根据需求选择合适的传输方式。

3. 默认序列化器使用 BinaryFormatter，但建议在生产环境中使用更高效、安全的序列化方案，如 MemoryPack 或 MessagePack。

4. 异步操作需要在 Unity 中正确处理，建议使用协程或专门的异步库（如 UniTask）来避免线程问题。
