# PulseRPC 快速开始指南

本文按当前实现说明最短上手路径。旧版文档中出现的 `PulseRPC.ServiceDiscovery`、`PulseRPC.Monitoring`、`PulseRPC.Tracing`、`AddPulseRpcServer`、`AddPulseRpcClient` 等包名或扩展方法不在当前仓库实现中，请不要继续使用。

## 前置条件

- .NET SDK：以仓库根目录 `global.json` 为准，当前锁定 `10.0.100`，允许 roll-forward。
- 运行时项目：优先参考 `samples/ChatApp`、`samples/JwtAuthentication`、`samples/HubFactoryExample`。
- 消息模型：优先使用 MemoryPack，并为请求/响应类型标注 `[MemoryPackable]`。

## 安装包

按角色引用当前实际存在的包：

```bash
# 服务端
dotnet add package PulseRPC.Server
dotnet add package PulseRPC.Server.SourceGenerator

# 多节点 Actor 租约（Redis）
dotnet add package PulseRPC.Backplane.Redis

# 客户端
dotnet add package PulseRPC.Client
dotnet add package PulseRPC.Client.SourceGenerator

# 契约共享项目
dotnet add package PulseRPC.Abstractions
dotnet add package MemoryPack
```

如果使用动态集群发现，再按后端选择：

```bash
dotnet add package PulseRPC.Infrastructure.Consul
dotnet add package PulseRPC.Infrastructure.Etcd
dotnet add package PulseRPC.Infrastructure.Kubernetes
```

生产多节点 Actor 必须将 `PulseRPC.Backplane.Redis` 与服务端包保持相同版本，注册共享的 `IConnectionMultiplexer` 后调用 `AddRedisActorLeases(...)`。不要以进程内 `InMemoryActorLeaseStore` 替代共享租约后端；详见[部署指南](../guides/deployment.md)。

## 定义契约

远程调用契约继承 `IPulseHub`：

```csharp
using MemoryPack;
using PulseRPC;

public interface IChatRoomHub : IPulseHub
{
    Task<JoinRoomResult> JoinRoomAsync(string roomId);
    Task<SendMessageResult> SendMessageAsync(string message);
}

[MemoryPackable]
public partial class JoinRoomResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

[MemoryPackable]
public partial class SendMessageResult
{
    public bool Success { get; set; }
    public long MessageId { get; set; }
    public string? ErrorMessage { get; set; }
}
```

## 配置服务端

当前服务端入口是 `AddPulseServer`。它支持 TCP/KCP 传输，并注册服务端消息处理、路由和托管服务。

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PulseRPC.Server;
using PulseRPC.Server.Extensions;
using PulseRPC.Shared;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddPulseServer(options =>
        {
            options.AddTcp("tcp", 7000, isDefault: true);
            options.AddKcp("kcp", 7001, isDefault: false);
        });

        services.AddSingleton<IChatRoomHub, ChatRoomHub>();
    })
    .Build();

await host.RunAsync();
```

有状态对象建议使用 `PulseServiceBase` 与 `AddPulseService<TService>()`，无状态 Hub 只负责参数校验、认证和路由。完整写法见 [客户端和服务端使用指南](../guides/client-server.md)。

## 配置客户端

当前客户端入口是 `PulseClientBuilder`。客户端通过 `ConnectionDescriptor` 或便捷方法建立连接，再使用 Source Generator 生成的扩展方法获取 Hub 代理。

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
await chatHub.JoinRoomAsync("lobby");
await chatHub.SendMessageAsync("hello");

await channel.DisconnectAsync();
await client.StopAsync();
client.Dispose();
```

客户端项目需要用 `[PulseClientGeneration(typeof(IChatRoomHub))]` 标记要生成代理的契约，并引用 `PulseRPC.Client.SourceGenerator` 作为 Analyzer。

## 动态发现与集群

动态发现不是旧的 `PulseRPC.ServiceDiscovery` 包。当前实现位于 `PulseRPC.Infrastructure.*`：

- `PulseRPC.Infrastructure.Consul`：`AddConsulDiscovery(...)`
- `PulseRPC.Infrastructure.Etcd`：`AddEtcdDiscovery(...)`
- `PulseRPC.Infrastructure.Kubernetes`：`AddKubernetesDiscovery(...)`

这些扩展应在 `AddPulseClustering(...)` 之后调用，用于覆盖静态成员列表。详见 [客户端和服务端使用指南](../guides/client-server.md) 的“统一寻址与集群”章节。

## 可运行示例

```bash
# 服务端
cd samples/ChatApp/ChatApp.Server
dotnet run

# 控制台客户端
cd samples/ChatApp/ChatApp.Client.Console
dotnet run
```

如果需要认证示例，请查看 `samples/JwtAuthentication`。需要向 HTTP/JSON 调用方提供显式网关时，参考 `samples/JsonTranscoding`；该示例是应用层映射，不是自动的 PulseRPC wire 转码。

## 下一步

- [客户端和服务端使用指南](../guides/client-server.md)
- [最佳实践](../guides/best-practices.md)
- [架构总览](../concepts/architecture.md)
