# PulseRPC Unity客户端

这是PulseRPC的Unity客户端实现，提供了在Unity环境中使用PulseRPC框架的能力，而不依赖于gRPC。

## 特性

- 纯C#实现，无需原生插件
- 完全兼容IL2CPP平台
- 支持iOS、Android、Windows、macOS等所有Unity支持的平台
- 基于TCP协议的直接实现，无需gRPC
- 支持二进制序列化，使用MemoryPack
- 支持请求/响应和实时事件通知

## 基本用法

```csharp
// 创建连接
var connection = new PulseWebSocketConnection("ws://your-server:5000/pulse");
await connection.ConnectAsync();

// 创建服务客户端
var client = PulseClientFactory.Create<IMyService>(connection);

// 调用服务方法
var result = await client.MyMethodAsync(parameter);

// 连接到Hub
var hub = PulseClientFactory.ConnectToHub<IMyHub, IMyReceiver>(connection, myReceiver);

// 调用Hub方法
await hub.SendMessageAsync("Hello from Unity!");

// 断开连接
await connection.DisconnectAsync();
```

## 注意事项

- 对于iOS和WebGL平台，请使用WebSocket连接
- 在Unity中使用异步方法时，请确保正确处理上下文切换
