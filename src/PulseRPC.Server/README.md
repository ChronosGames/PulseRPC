# PulseRPC.Server

PulseRPC.Server 是 PulseRPC 的服务端运行时。当前实现围绕 `IPulseHub` 契约、TCP/KCP 传输、Source Generator 路由表、服务端消息管线和 `PulseServiceBase` 有状态服务模型展开。

## 当前边界

- 内置传输：`TransportType.TCP`、`TransportType.KCP`。
- 服务端入口：`services.AddPulseServer(...)`。
- Hub 契约：接口继承 `IPulseHub`。
- 服务端路由：依赖 `PulseRPC.Server.SourceGenerator` 生成的 `IServiceRoutingTable`。
- 有状态服务：使用 `PulseServiceBase`、`AddPulseService<TService>()`、`IServiceAccessor<TService>`。
- 客户端推送：使用 `[Channel("CLIENT")]` 的 `IPulseHub` 接收契约与 `IHubContext<TReceiver>`。
- 集群路由：`AddPulseClustering(...)`，动态发现后端在 `PulseRPC.Infrastructure.Consul`、`PulseRPC.Infrastructure.Etcd`、`PulseRPC.Infrastructure.Kubernetes`。

当前源码未内置 WebSocket/QUIC 传输，也没有旧文档中提到的独立 `PulseRPC.ServiceDiscovery`、`PulseRPC.Monitoring`、`PulseRPC.Tracing` 包。

## 快速注册

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PulseRPC.Server.Extensions;

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

`AddPulseServer` 会注册传输提供程序、消息分发器、响应处理器、通道管理器、单节点路由和托管服务。使用 Generic Host 时，服务器随 `host.RunAsync()` 自动启动和停止。

## 有状态服务

有状态业务对象建议继承 `PulseServiceBase`，并通过 `AddPulseService<TService>()` 注册工厂：

```csharp
services.AddPulseService<ChatRoomService>((sp, roomId) =>
{
    var logger = sp.GetRequiredService<ILogger<ChatRoomService>>();
    return new ChatRoomService(roomId, logger);
});

services.AddSingleton<IChatRoomHub, ChatRoomHub>();
```

Hub 中通过 `IServiceAccessor<TService>` 获取实例，并使用 `EnqueueAsync` 进入实例队列，保证同一实例内顺序执行。

## 生成器要求

服务端项目需要引用 `PulseRPC.Server.SourceGenerator`。生成器负责协议号映射、服务端路由表、响应序列化器和客户端推送代理相关代码。

常见约定：

- 请求/响应模型优先标注 `[MemoryPackable]`。
- Hub 接口继承 `IPulseHub`。
- 客户端接收契约使用 `[Channel("CLIENT")]` 并继承 `IPulseHub`。
- 协议号冲突时使用 `[Protocol(0x1234)]` 手动指定。

## 相关文档

- `docs/guides/client-server.md`
- `docs/getting-started/quickstart.md`
- `docs/concepts/architecture.md`
- `samples/ChatApp/ChatApp.Server`
