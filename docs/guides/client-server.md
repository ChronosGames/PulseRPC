# PulseRPC 客户端和服务端使用指南

本文面向当前实现，说明如何按统一 `IPulseHub` 架构编写服务端、客户端、客户端推送接收器，以及如何启用单节点路由、动态服务发现集群与 L3 Actor 状态迁移。

当前事实边界：

- 远程可调用契约统一继承 `IPulseHub`。
- 客户端调用服务端 Hub：通过 `PulseClientBuilder` 创建 `IPulseClient`，连接后使用 Source Generator 生成的 `IClientChannel.GetHub<T>()`。
- 服务端推送客户端：客户端接收契约写成 `[Channel("CLIENT")] : IPulseHub`，客户端用 `RegisterReceiver<T>()` 注册实现，服务端用 `IHubContext<TReceiver>` 推送。
- 服务端入站路由依赖 `PulseRPC.Server.SourceGenerator` 生成的 `IServiceRoutingTable`，业务 Hub 实现按普通 DI 注册。
- 单节点默认注册 `IPulseRouter` + `InProcessBackplane`；调用 `AddPulseClustering()` 后升级为集群路由。
- 集群成员可以使用静态 `Members`，也可以在 `AddPulseClustering()` 后接入 Consul、etcd 或 Kubernetes 动态发现。
- 节点互信默认使用共享密钥，也可以用 `UseCertificateNodeAuthentication()` 切换为证书鉴权。
- L3 Actor 状态迁移已提供 `IActorStateSnapshot` 与 `ActorMigrationCoordinator`；业务需要实现快照并注册 `IActorStateTransport` 后主动触发迁移。

## 目录

1. [核心模型](#核心模型)
2. [契约定义](#契约定义)
3. [服务端开发](#服务端开发)
4. [客户端开发](#客户端开发)
5. [服务端推送客户端](#服务端推送客户端)
6. [统一寻址与集群](#统一寻址与集群)
7. [认证与客户端可见性](#认证与客户端可见性)
8. [序列化与协议号](#序列化与协议号)
9. [最佳实践](#最佳实践)
10. [故障排查](#故障排查)

## 核心模型

### 一切契约都是 IPulseHub

`IPulseHub` 是唯一的远程契约标记接口。服务端提供的 RPC、服务端之间的内部调用契约、客户端实现的推送接收契约，都使用它。

```csharp
public interface IChatRoomHub : IPulseHub
{
    Task<ChatLoginResult> LoginAsync(string token);
    Task<JoinRoomResult> JoinRoomAsync(string roomId);
    Task<SendMessageResult> SendMessageAsync(string message);
}
```

客户端接收服务端推送时，不再使用 `IPulseReceiver`。接收契约标注 `[Channel("CLIENT")]`：

```csharp
[Channel("CLIENT")]
public interface IChatReceiver : IPulseHub
{
    Task OnMessageAsync(ChatMessage message);
}
```

### 线上地址三元组

一次调用由三部分定位：

| 字段 | 含义 |
|---|---|
| `ServiceName` | Hub 名称，通常对应接口名 |
| `ServiceKey` | Actor/Service 实例键、组名、用户 ID 或连接 ID |
| `ProtocolId` | 方法协议号，由 Source Generator 按方法签名生成 |

运行时统一用 `PulseAddress` 描述投递目标，再由 `IPulseRouter` 解析为本地连接、本地 Actor、Fan-out 或远程节点。

## 契约定义

### 请求和响应模型

消息类型优先使用 MemoryPack：

```csharp
using MemoryPack;

[MemoryPackable]
public partial class ChatMessage
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

[MemoryPackable]
public partial class SendMessageResult
{
    public bool Success { get; set; }
    public long MessageId { get; set; }
    public string? ErrorMessage { get; set; }
}
```

### 方向和可见性标注

常用标注：

| 标注 | 用途 |
|---|---|
| `[Channel("CLIENT")]` | 表示该 Hub 由客户端实现，服务端可向它推送 |
| `[Channel("GameServer")]` | 表示该 Hub 由指定服务端角色提供，可用于跨服契约消歧 |
| `[PulseHub(Provide = true, Consume = true)]` | 少数歧义场景下覆盖生成器默认推断 |
| `[Authorize]` / `[Authorize(Role = RoleTypes.Internal)]` | 方法或接口鉴权 |
| `[ClientFacing]` | 开启 `EnableClientFacingGate` 后，白名单式允许外部客户端调用 |
| `[Delivery(DeliveryMode.AtMostOnce)]` | 声明投递保证；默认是至多一次 |

## 服务端开发

### 1. 注册并启动 PulseServer

`AddPulseServer` 会注册服务端运行时、传输监听、消息处理管线、单节点 `IPulseRouter` 和托管服务。使用 Host 时，推荐让 `IHostedService` 随 `host.RunAsync()` 自动启动。

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PulseRPC.Server.Extensions;
using PulseRPC.Shared;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddPulseServer(options =>
        {
            options
                .AddTcp("tcp", 7000, isDefault: true, tcp =>
                {
                    tcp.NoDelay = true;
                    tcp.KeepAlive = true;
                })
                .AddKcp("kcp", 7001, isDefault: false, kcp =>
                {
                    kcp.NoDelay = true;
                    kcp.Interval = 10;
                });

            // 开启后，只有 [ClientFacing] 标注的方法允许外部客户端调用。
            options.EnableClientFacingGate = true;
        });

        services.AddSingleton<IChatRoomHub, ChatRoomHub>();
    })
    .Build();

await host.RunAsync();
```

如果不启动 Host，只想手动控制 `IPulseServer` 生命周期，可以像示例项目一样从 DI 取出 `IPulseServer` 后调用 `StartAsync()` / `StopAsync()`。

### 2. 实现无状态 Hub

当前推荐把 Hub 作为无状态入口，注册为 Singleton；真正有状态的房间、玩家、战斗等对象放到 `PulseService` 或业务服务里。

```csharp
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Services;

public sealed class ChatRoomHub : IChatRoomHub
{
    private readonly IServiceAccessor<ChatRoomService> _rooms;

    public ChatRoomHub(IServiceAccessor<ChatRoomService> rooms)
    {
        _rooms = rooms;
    }

    public Task<ChatLoginResult> LoginAsync(string token)
    {
        // 示例项目中把登录后的身份写入当前连接的 AuthenticationContext。
        // 实际项目可接入 JWT 或自定义认证服务。
        return Task.FromResult(ChatLoginResult.Ok("user-1", "Alice"));
    }

    public async Task<JoinRoomResult> JoinRoomAsync(string roomId)
    {
        var service = await _rooms.GetAsync(roomId);
        if (service.State == ServiceLifecycleState.Created)
        {
            await service.StartAsync();
        }

        var userId = PulseContext.CurrentUserId ?? "anonymous";
        return await service.EnqueueAsync(() => service.JoinAsync(userId));
    }

    public async Task<SendMessageResult> SendMessageAsync(string message)
    {
        var roomId = PulseContext.Current?.AuthenticationContext?.Properties.TryGetValue("RoomId", out var value) == true
            ? value as string
            : null;

        if (string.IsNullOrEmpty(roomId))
        {
            return new SendMessageResult { Success = false, ErrorMessage = "Not in room" };
        }

        var service = await _rooms.GetAsync(roomId);
        return await service.EnqueueAsync(() => service.SendMessageAsync(message));
    }
}
```

### 3. 注册有状态 PulseService

`AddPulseService<TService>()` 用于注册按 key 创建和缓存的服务实例。Hub 通过 `IServiceAccessor<TService>` 获取对应实例，并用 `EnqueueAsync` 进入实例队列，保证同一 key 内顺序执行。

```csharp
services.AddPulseService<ChatRoomService>((sp, roomId) =>
{
    var logger = sp.GetRequiredService<ILogger<ChatRoomService>>();
    return new ChatRoomService(roomId, logger);
});

services.AddSingleton<IChatRoomHub, ChatRoomHub>();
```

### 4. Source Generator 要求

服务端项目需要引用 `PulseRPC.Server.SourceGenerator`。生成器会生成协议号路由、响应序列化器和推送代理相关代码。业务代码只需要：

- 契约接口继承 `IPulseHub`。
- Hub 实现注册进 DI，如 `services.AddSingleton<IChatRoomHub, ChatRoomHub>()`。
- 对客户端推送契约，调用生成的 `services.AddAllPulseReceiverContexts()` 或对应的单个注册扩展。

## 客户端开发

### 1. 标记需要生成的代理

客户端项目需要引用 `PulseRPC.Client.SourceGenerator`，并放置一个标记类：

```csharp
using PulseRPC;

[PulseClientGeneration(typeof(IChatRoomHub))]
[PulseClientGeneration(typeof(IChatReceiver))]
public static class HubClientGeneration
{
}
```

生成器会为普通 Hub 生成 `IClientChannel.GetHub<T>()` 支持，为 `[Channel("CLIENT")]` 接收契约生成 `RegisterReceiver<T>()` 支持。

### 2. 创建客户端并连接

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

var login = await chatHub.LoginAsync("token");
var joined = await chatHub.JoinRoomAsync("lobby");
var sent = await chatHub.SendMessageAsync("hello");

await channel.DisconnectAsync();
await client.StopAsync();
client.Dispose();
```

也可以在构建器里预先添加连接：

```csharp
var client = new PulseClientBuilder()
    .AddTcpConnection("chat-1", "ChatServer", "127.0.0.1", 7000)
    .Build();

await client.InitializeAsync();
```

### 3. 运行时连接管理

`IPulseClient.Connections` 提供连接查询、路由和生命周期管理：

```csharp
var all = client.Connections.GetAllConnections();
var connection = client.Connections.GetConnection("chat-1");
var routed = await client.Connections.RouteAsync(nameof(IChatRoomHub));
var health = await client.CheckHealthAsync();
```

### 4. 加权与粘性路由

`WeightedRoundRobin` 使用平滑加权轮询。默认权重源会在每次选择时读取 `ConnectionDescriptor.Weight`；需要根据实时容量、限流或健康快照动态调整时，实现线程安全的 `IConnectionWeightProvider`：

```csharp
using System.Collections.Concurrent;

public sealed class CapacityWeightProvider : IConnectionWeightProvider
{
    private readonly ConcurrentDictionary<string, int> _weights = new();

    public int GetWeight(IClientChannel connection)
        => _weights.TryGetValue(connection.Id, out var weight) ? weight : 1;

    public void Update(string connectionId, int weight)
        => _weights[connectionId] = weight;
}

var weights = new CapacityWeightProvider();
var client = new PulseClientBuilder()
    .Configure(options => options.LoadBalancing.WeightProvider = weights)
    .WithLoadBalancing(LoadBalancingStrategy.WeightedRoundRobin)
    .Build();
```

权重必须大于零；提供者返回零或负数时选择会立即失败。连接成员或权重快照变化后，轮询状态会重新建立，不会沿用旧比例。

`ConsistentHash` 的唯一稳定输入是调用级 `StickyKey`。生成的服务工厂会把 `ServiceProxyOptions` 传入上下文路由：

```csharp
var client = new PulseClientBuilder()
    .WithLoadBalancing(LoadBalancingStrategy.ConsistentHash)
    .Build();

var chat = await client.GetServiceAsync<IChatRoomHub>(
    new ServiceProxyOptions { StickyKey = $"tenant:{tenantId}" });
```

同一逻辑用户、租户或会话必须始终提供完全相同、非空且不含临时时间戳的 key。哈希使用连接 `Id` 和确定性虚拟节点；候选列表顺序不影响结果，新增连接只重映射落到新连接的区间。没有 `StickyKey`、连接 ID 重复，或自定义连接管理器无法保留上下文时会 fail-fast。`WithLoadBalancing(..., IReadOnlyDictionary<string, object>)` 仍不接受未定义的松散参数。

## 服务端推送客户端

### 1. 定义客户端接收契约

```csharp
[Channel("CLIENT")]
public interface ITimerReceiver : IPulseHub
{
    Task OnTickAsync(string message);
}
```

### 2. 客户端实现并注册

```csharp
public sealed class TimerReceiver : ITimerReceiver
{
    public Task OnTickAsync(string message)
    {
        Console.WriteLine(message);
        return Task.CompletedTask;
    }
}

var receiver = new TimerReceiver();
var subscription = channel.RegisterReceiver<ITimerReceiver>(receiver);

// 不再接收时取消注册。
subscription.Dispose();
```

### 3. 服务端注入 IHubContext

`IHubContext<TReceiver>` 是当前服务端推送 API，泛型约束已经统一为 `where TReceiver : class, IPulseHub`。底层由生成的 Fan-out 代理和 `IPulseRouter` 完成投递。

```csharp
public sealed class TimerHub : ITimerHub
{
    private readonly IHubContext<ITimerReceiver> _timerReceiver;

    public TimerHub(IHubContext<ITimerReceiver> timerReceiver)
    {
        _timerReceiver = timerReceiver;
    }

    public async Task StartAsync(TimeSpan interval)
    {
        var connectionId = PulseContext.CurrentConnectionId
            ?? throw new InvalidOperationException("No connection");

        await _timerReceiver.Clients.Single(connectionId)
            .OnTickAsync($"tick: {DateTimeOffset.UtcNow:O}");
    }
}
```

可用目标选择器：

| API | 目标 |
|---|---|
| `Clients.All` | 所有已认证客户端 |
| `Clients.Single(connectionId)` | 单个连接 |
| `Clients.Only(connectionIds)` | 指定连接集合 |
| `Clients.Except(connectionId)` | 除某连接外的全部连接 |
| `Clients.User(userId)` / `Clients.Users(userIds)` | 指定用户 |
| `Clients.Group(groupName)` / `Clients.Groups(groupNames)` | 指定组 |
| `Clients.GroupExcept(groupName, connectionId)` | 组内排除某连接 |

服务端需要注册生成的 HubContext：

```csharp
services.AddAllPulseReceiverContexts();
```

## 统一寻址与集群

### 单节点默认路由

`AddPulseServer()` 内部会注册：

- `IPulseBackplane`：默认 `InProcessBackplane`。
- `IPulseRouter`：默认 `LocalPulseRouter`。
- `IUserConnectionMapping` / `IGroupManager`：用于 `User` / `Group` Fan-out。

业务通常不需要直接调用 `IPulseRouter`，生成代理会构造 `PulseAddress` 并转交路由器。需要自定义投递时可以直接注入：

```csharp
public sealed class NotifyService
{
    private readonly IPulseRouter _router;

    public NotifyService(IPulseRouter router)
    {
        _router = router;
    }

    public ValueTask SendRawAsync(ReadOnlyMemory<byte> body, ushort protocolId)
    {
        var address = PulseAddress.User(nameof(IChatReceiver), "user-1");
        return _router.SendAsync(address, protocolId, body);
    }
}
```

### 集群基础与静态成员

集群基础入口是 `AddPulseClustering()`。它注册集群拓扑、一致性哈希、租约 Actor 目录、共享密钥节点鉴权、节点间链路，并用 `ClusterPulseRouter` 覆盖默认 `IPulseRouter`。不接入发现后端时，节点列表来自静态 `Members`。

```csharp
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Extensions;

services.AddPulseServer(options =>
{
    options.AddTcp("tcp", 7000, isDefault: true);
});

services.AddPulseClustering(
    topology =>
    {
        topology.LocalNodeId = "game-a";
        topology.Members.Add(new ClusterNodeEndpoint
        {
            NodeId = "game-a",
            Host = "10.0.0.1",
            Port = 7000,
        });
        topology.Members.Add(new ClusterNodeEndpoint
        {
            NodeId = "game-b",
            Host = "10.0.0.2",
            Port = 7000,
        });
    },
    auth =>
    {
        auth.SharedSecret = "replace-with-a-real-shared-secret";
    },
    lease =>
    {
        lease.LeaseDuration = TimeSpan.FromSeconds(30);
    });
```

### 动态服务发现

动态发现后端应在 `AddPulseClustering()` 之后调用。发现层会用 `DiscoveryClusterMembership` 覆盖静态 `IClusterMembership` 与 `INodeEndpointResolver`，并作为 `IHostedService` 完成本节点注册、后台刷新、watch 触发和优雅下线。

#### Consul

```csharp
using PulseRPC.Infrastructure.Consul;

services.AddPulseClustering(
    topology =>
    {
        topology.LocalNodeId = "game-a";
        // 动态发现下 Members 可留空，端点由 Consul 提供。
    },
    auth => auth.SharedSecret = "replace-with-a-real-shared-secret");

services.AddConsulDiscovery(
    localNodeId: "game-a",
    advertiseHost: "10.0.0.1",
    advertisePort: 7000,
    configure: options =>
    {
        options.Address = "http://consul:8500";
        options.ServiceName = "pulserpc-game";
        options.EnableWatch = true;
    });
```

#### etcd

```csharp
using PulseRPC.Infrastructure.Etcd;

services.AddEtcdDiscovery(
    localNodeId: "game-a",
    advertiseHost: "10.0.0.1",
    advertisePort: 7000,
    configure: options =>
    {
        options.ConnectionString = "http://etcd:2379";
        options.KeyPrefix = "/pulserpc/game/nodes/";
        options.LeaseTtlSeconds = 15;
        options.EnableWatch = true;
    });
```

#### Kubernetes

```csharp
using PulseRPC.Infrastructure.Kubernetes;

services.AddKubernetesDiscovery(
    localNodeId: Environment.GetEnvironmentVariable("HOSTNAME")!,
    advertiseHost: Environment.GetEnvironmentVariable("POD_IP")!,
    advertisePort: 7000,
    configure: options =>
    {
        options.Namespace = "game";
        options.LabelSelector = "app=game-server";
        options.NodePort = 7000;
        options.UseInClusterConfig = true;
        options.EnableWatch = true;
    });
```

动态发现使用要点：

- `localNodeId` 必须与 `ClusterTopologyOptions.LocalNodeId` 一致。
- Consul/etcd 由后端注册记录和 watch/轮询驱动成员变化。
- Kubernetes 通过 Pod 列表和 Pod watch 发现成员，通常用 Pod 名作为 `NodeId`，Pod IP 作为端点。
- `DiscoveryOptions.PollInterval` 是 watch 的兜底轮询周期；后端 watch 失效时仍会按周期刷新。

### 证书节点鉴权

共享密钥是默认实现。生产环境可以在 `AddPulseClustering()` 之后用证书鉴权覆盖默认 `INodeAuthenticator`：

```csharp
using System.Security.Cryptography.X509Certificates;

services.UseCertificateNodeAuthentication(options =>
{
    options.LocalCertificate = X509CertificateLoader.LoadPkcs12FromFile(
        "certs/game-a.pfx",
        "pfx-password");

    options.TrustedCertificateAuthorities.Add(
        X509CertificateLoader.LoadCertificateFromFile("certs/cluster-ca.cer"));

    options.RequireNodeIdMatchesCertificate = true;
});
```

`CertificateNodeAuthenticator` 是应用层节点凭据校验：凭据包含本节点证书、时间戳和签名；对端校验证书信任、签名、时效和可选的 `nodeId`/证书主体匹配。它可与真实 TLS 传输一起使用，但本身不是传输层 TLS 配置。

### L3 Actor 状态迁移

L2 租约保证同一 `(Hub, Key)` 在同一时刻只有一个属主，但属主变化时未实现快照的 Actor 会按空状态重新激活。需要跨节点保留状态时，让 Actor 实现 `IActorStateSnapshot`，并用 `ActorMigrationCoordinator` 编排迁出/迁入。

```csharp
using MemoryPack;
using PulseRPC.Clustering;

public sealed class RoomService : PulseServiceBase, IActorStateSnapshot
{
    private readonly List<string> _members = new();

    public RoomService(string roomId, ILogger<RoomService> logger)
        : base("Room", roomId, logger)
    {
    }

    public ValueTask<byte[]> CaptureStateAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask<byte[]>(MemoryPackSerializer.Serialize(_members));
    }

    public ValueTask RestoreStateAsync(byte[] state, CancellationToken cancellationToken = default)
    {
        var restored = MemoryPackSerializer.Deserialize<List<string>>(state) ?? new List<string>();
        _members.Clear();
        _members.AddRange(restored);
        return default;
    }
}
```

迁移协调器使用方式：

```csharp
services.AddSingleton<ActorMigrationCoordinator>();
services.AddSingleton<IActorStateTransport, MyActorStateTransport>();

var actor = await roomAccessor.GetAsync("room-42");
await migrationCoordinator.MigrateOutAsync(
    hub: nameof(IRoomHub),
    key: "room-42",
    targetNodeId: "game-b",
    localActor: actor,
    cancellationToken);
```

边界说明：

- `MigrateOutAsync` 会先 `StopAsync` 静默 Actor，等待队列在途消息排空，再捕获快照。
- 只有实现 `IActorStateSnapshot` 的 Actor 会迁移状态；未实现时迁入后等价于 L2 空状态重新激活。
- `IActorStateTransport` 是快照搬运抽象，当前需要业务或基础设施层注册具体实现。
- 迁移应由运维、再均衡或业务管理流程主动触发，不是 `AddPulseClustering()` 后自动对所有 Actor 迁移。
- Gateway/虚拟连接相关类型已存在，使用时应结合当前样例和集成测试验证实际链路。

## 认证与客户端可见性

### 方法鉴权

```csharp
public interface IAccountHub : IPulseHub
{
    Task<SignInResult> SignInAsync(string signInId, string password);

    [Authorize]
    Task<UserInfo> GetCurrentUserNameAsync();

    [Authorize(Role = "Administrators")]
    Task<string> DangerousOperationAsync();
}
```

服务端可以在登录方法中把身份写入当前连接的 `AuthenticationContext`，后续同一连接请求会通过 `PulseContext.CurrentUserId` / `PulseContext.Current.User` 读取身份。

### ClientFacing 门闸

`ClientFacingAttribute` 是协议层白名单。默认 `EnableClientFacingGate = false`，升级旧项目时行为不变；开启后，外部客户端只能调用 `[ClientFacing]` 标注的方法。

```csharp
[ClientFacing]
public interface IGameHub : IPulseHub
{
    Task<PlayerInfo> GetPlayerAsync(string playerId);

    [ClientFacing(false)]
    [Authorize(Role = RoleTypes.Internal)]
    Task RebuildIndexAsync();
}
```

```csharp
services.AddPulseServer(options =>
{
    options.AddTcp(7000);
    options.EnableClientFacingGate = true;
});
```

## 序列化与协议号

- 默认序列化提供程序是 `PulseRPCSerializerProvider.Instance`。
- 请求/响应类型建议标注 `[MemoryPackable]`。
- 协议号按 Hub 接口隔离，同一 Hub 内方法协议号必须唯一。
- 如生成器报告协议号冲突，使用 `[Protocol(0x1234)]` 手动指定。
- 客户端和服务端必须引用同一份契约程序集，避免签名不一致导致协议号不同。

## 最佳实践

### Hub 保持无状态

Hub 作为协议入口，只做参数验证、身份检查、路由和调用业务服务。连接状态放在 `AuthenticationContext.Properties` 或专门的连接映射中；业务状态放在 `PulseService` 或外部存储中。

### Actor/Service 通过 key 隔离

房间、玩家、战斗等天然有实例键的对象，使用 `AddPulseService<TService>()` 和 `IServiceAccessor<TService>.GetAsync(key)`。同一个 key 内用队列串行处理，不同 key 可以并发。

### 可迁移 Actor 显式实现快照

需要动态迁移并保留内存状态的 Actor 实现 `IActorStateSnapshot`。快照内容应只包含业务状态，不包含连接对象、logger、DI 服务等进程内资源；恢复后这些运行时依赖由新节点 DI 重新提供。

### 明确外部和内部契约

外部客户端能调用的方法尽量用 `[ClientFacing]` 白名单；跨服务器内部方法使用 `[Authorize(Role = RoleTypes.Internal)]`，并避免在客户端项目生成或暴露不必要的 Stub。

### 推送契约统一写成 CLIENT Hub

旧写法：

```csharp
public interface IGameReceiver : IPulseReceiver
{
}
```

新写法：

```csharp
[Channel("CLIENT")]
public interface IGameReceiver : IPulseHub
{
}
```

仓库中已包含迁移分析器和 CodeFix，用于提示 `IPulseReceiver` 到统一 `IPulseHub` 的迁移。

## 故障排查

### `GetHub<T>()` 抛出“不支持的 Hub 类型”

检查客户端项目是否添加了：

```csharp
[PulseClientGeneration(typeof(IYourHub))]
public static class HubClientGeneration
{
}
```

并确认项目引用了 `PulseRPC.Client.SourceGenerator`。

### 服务端收不到请求

检查：

- 服务端项目是否引用 `PulseRPC.Server.SourceGenerator`。
- Hub 实现是否注册到 DI，例如 `services.AddSingleton<IChatRoomHub, ChatRoomHub>()`。
- 客户端和服务端是否引用同一份契约程序集。
- 服务端传输是否启动，端口和协议是否一致。

### 客户端收不到推送

检查：

- 接收接口是否标注 `[Channel("CLIENT")]` 并继承 `IPulseHub`。
- 客户端是否对该接口添加 `[PulseClientGeneration(typeof(IReceiver))]`。
- 客户端是否调用 `channel.RegisterReceiver<IReceiver>(receiver)`。
- 服务端是否注册生成的 `services.AddAllPulseReceiverContexts()`。
- 目标连接是否已认证或已加入对应用户/组映射。

### 集群路由不符合预期

检查：

- 所有节点 `Members` 是否完全一致。
- `LocalNodeId` 是否与 `Members` 中的节点 ID 匹配。
- 所有节点的共享密钥是否一致。
- 目标 `(Hub, Key)` 是否在各节点上一致计算。

动态发现部署还应检查：

- 是否在 `AddPulseClustering()` 之后调用 `AddConsulDiscovery()` / `AddEtcdDiscovery()` / `AddKubernetesDiscovery()`。
- `localNodeId` 是否与 `ClusterTopologyOptions.LocalNodeId` 一致。
- Consul/etcd/Kubernetes 后端是否能看到本节点注册记录。
- `advertiseHost` / `advertisePort` 是否能被其它节点访问。

### Actor 迁移后状态丢失

检查：

- Actor 是否实现 `IActorStateSnapshot`。
- 是否注册了 `IActorStateTransport` 和 `ActorMigrationCoordinator`。
- `MigrateOutAsync` 是否实际执行成功并把快照发送到目标节点。
- `MigrateInAsync` 是否在目标节点激活前调用了 `RestoreStateAsync`。

---

更多可运行代码可参考：

- `samples/ChatApp`
- `samples/JwtAuthentication`
- `samples/DistributedGameApp`
- `docs/concepts/architecture.md`
