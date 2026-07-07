# PulseRPC 客户端 API 使用指南

本文按当前 `PulseRPC.Client` 实现说明客户端入口。旧文档中的 `AddPulseRpcTcpClient`、`AddPulseRpcClient`、`PulseRpcClientFactory`、`GetService<T>()`、WebSocket 通道等 API 不在当前源码中。

## 当前入口

核心入口是 `PulseClientBuilder`：

```csharp
using Microsoft.Extensions.Logging;
using PulseRPC.Client;
using PulseRPC.Client.Configuration;
using PulseRPC.Shared;

var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var client = new PulseClientBuilder()
    .WithLogging(loggerFactory)
    .WithLoadBalancing(LoadBalancingStrategy.RoundRobin)
    .Build();

await client.InitializeAsync();

var channel = await client.ConnectToServerAsync(
    host: "127.0.0.1",
    port: 7000,
    serverId: "chat-1",
    transport: TransportType.TCP);

var chatHub = channel.GetHub<IChatRoomHub>();
```

也可以通过 `AddConnection(ConnectionDescriptor)` 预配置连接，再调用 `InitializeAsync()`。

## Source Generator

客户端项目需要引用 `PulseRPC.Client.SourceGenerator`，并用 `[PulseClientGeneration(typeof(...))]` 标记需要生成的 Hub 或客户端接收契约。生成后通过 `IClientChannel.GetHub<T>()` 获取代理。

## 传输边界

当前核心 `TransportType` 仅包含：

- `TransportType.TCP`
- `TransportType.KCP`

当前仓库未实现 WebSocket 客户端通道。

## 示例项目

- `ChatApp.Client.Console`：控制台客户端示例。
- `ChatApp.Unity`：Unity 工程示例。
- `docs/guides/client-server.md`：更完整的当前 API 说明。
