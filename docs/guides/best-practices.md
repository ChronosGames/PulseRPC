# PulseRPC 最佳实践指南

本文按当前统一 `IPulseHub` 架构整理最佳实践，重点覆盖契约设计、Hub 与 Service 分离、客户端生成代理、服务端推送、统一寻址、动态服务发现集群与 L3 Actor 状态迁移。

当前边界：

- 远程契约统一继承 `IPulseHub`。
- 客户端推送接收契约使用 `[Channel("CLIENT")] : IPulseHub`，不再使用 `IPulseReceiver`。
- 客户端调用通过 Source Generator 生成的 `IClientChannel.GetHub<T>()`。
- 客户端接收推送通过 Source Generator 生成的 `IClientChannel.RegisterReceiver<T>()`。
- 服务端推送当前推荐使用 `IHubContext<TReceiver>`，其泛型约束已统一为 `IPulseHub`。
- 单节点默认使用 `LocalPulseRouter`；集群通过 `AddPulseClustering()` 启用。
- 集群成员可以来自静态 `Members`，也可以由 Consul、etcd 或 Kubernetes 动态发现提供。
- 节点互信默认使用共享密钥，生产可用 `UseCertificateNodeAuthentication()` 切换到证书鉴权。
- L3 Actor 状态迁移是 opt-in：Actor 实现 `IActorStateSnapshot`，并由 `ActorMigrationCoordinator` 主动迁移。

## 目录

- [契约设计](#契约设计)
- [Hub 与 Service 分离](#hub-与-service-分离)
- [客户端实践](#客户端实践)
- [服务端推送](#服务端推送)
- [统一寻址与集群](#统一寻址与集群)
- [认证与安全边界](#认证与安全边界)
- [序列化与协议号](#序列化与协议号)
- [性能实践](#性能实践)
- [错误处理](#错误处理)
- [命名约定](#命名约定)
- [部署与运维](#部署与运维)
- [快速参考](#快速参考)

## 契约设计

### 统一使用 IPulseHub

所有可远程调用的接口都继承 `IPulseHub`。服务端提供的 RPC、服务端内部调用契约、客户端实现的推送接收契约，都使用同一个标记接口。

```csharp
public interface IGameHub : IPulseHub
{
    Task<PlayerInfo> GetPlayerInfoAsync();
    Task MoveAsync(Vector3Dto position);
}

[Channel("CLIENT")]
public interface IGameReceiver : IPulseHub
{
    Task OnMatchFoundAsync(MatchFoundNotification notification);
    Task OnKickedAsync(string reason);
}
```

### 按业务边界拆分 Hub

推荐按领域拆分接口，避免一个宽泛 Hub 承担所有功能。

```csharp
public interface IChatHub : IPulseHub
{
    Task<SendMessageResult> SendMessageAsync(SendMessageRequest request);
    Task<ChatMessage[]> GetRecentMessagesAsync(int count);
    Task<JoinRoomResult> JoinRoomAsync(string roomId);
}

public interface IAccountHub : IPulseHub
{
    Task<SignInResult> SignInAsync(string signInId, string password);
    Task<UserInfo> GetCurrentUserNameAsync();
}
```

避免：

```csharp
public interface IBusinessHub : IPulseHub
{
    Task<object> ProcessAsync(object request);
}
```

### 用请求对象承载复杂参数

参数超过 2 到 3 个，优先定义请求类型，便于版本演进和校验。

```csharp
[MemoryPackable]
public partial class SendMessageRequest
{
    public string RoomId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
```

### 区分外部和内部契约

外部客户端可调用的方法应显式标注 `[ClientFacing]`，内部服务调用使用 `RoleTypes.Internal`。

```csharp
[ClientFacing]
public interface IGameHub : IPulseHub
{
    Task<PlayerInfo> GetPlayerInfoAsync();
}

[Channel("GameServerInternal")]
[Authorize(Role = RoleTypes.Internal)]
public interface IGameServerInternalHub : IPulseHub
{
    Task<bool> OnMatchFoundAsync(string playerId, MatchFoundNotification notification);
}
```

## Hub 与 Service 分离

### 推荐职责边界

| 组件 | 职责 | 生命周期 | 状态 |
|---|---|---|---|
| Hub | 参数验证、身份检查、路由到业务服务 | 通常 Singleton | 无状态 |
| PulseService | 业务状态、业务规则、顺序执行 | 按 key 创建/缓存 | 有状态 |

Hub 中不要保存玩家、房间、战斗等可变共享状态。Hub 实例会被多个请求并发复用。

### Hub 保持无状态

```csharp
public sealed class ChatRoomHub : IChatRoomHub
{
    private readonly IServiceAccessor<ChatRoomService> _rooms;

    public ChatRoomHub(IServiceAccessor<ChatRoomService> rooms)
    {
        _rooms = rooms;
    }

    public async Task<JoinRoomResult> JoinRoomAsync(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            return JoinRoomResult.Failed("RoomId is required");
        }

        var userId = PulseContext.CurrentUserId;
        if (string.IsNullOrEmpty(userId))
        {
            return JoinRoomResult.Failed("Not authenticated");
        }

        var service = await _rooms.GetAsync(roomId);
        if (service.State == ServiceLifecycleState.Created)
        {
            await service.StartAsync();
        }

        return await service.EnqueueAsync(() => service.JoinAsync(userId));
    }
}
```

### Service 管理状态

```csharp
[PulseService(
    Scenario = ServiceScenario.Actor,
    StartupType = ServiceStartupType.OnDemand,
    InstanceScope = ServiceInstanceScope.MultiInstance)]
public sealed class ChatRoomService : PulseServiceBase
{
    private readonly HashSet<string> _members = new();

    public ChatRoomService(string roomId, ILogger<ChatRoomService> logger)
        : base("ChatRoom", roomId, logger)
    {
    }

    public Task<JoinRoomResult> JoinAsync(string userId)
    {
        if (!_members.Add(userId))
        {
            return Task.FromResult(JoinRoomResult.Failed("Already in room"));
        }

        return Task.FromResult(JoinRoomResult.Ok(ServiceId, _members.Count));
    }
}
```

### 注册方式

```csharp
services.AddPulseServer(options =>
{
    options.AddTcp("tcp", 7000, isDefault: true);
});

services.AddPulseService<ChatRoomService>((sp, roomId) =>
{
    var logger = sp.GetRequiredService<ILogger<ChatRoomService>>();
    return new ChatRoomService(roomId, logger);
});

services.AddSingleton<IChatRoomHub, ChatRoomHub>();
```

### 状态存放建议

- 每请求临时信息：使用 `PulseContext.Current`。
- 连接级会话状态：使用当前连接的 `AuthenticationContext.Properties`。
- 业务状态：放在 `PulseService` 或外部存储。
- 跨进程/跨节点状态：不要依赖单机内存，使用数据库、分布式缓存或业务一致性方案。

## 客户端实践

### 使用 Source Generator 标记代理

客户端项目需要引用 `PulseRPC.Client.SourceGenerator`，并放置标记类。

```csharp
[PulseClientGeneration(typeof(IChatHub))]
[PulseClientGeneration(typeof(IGameReceiver))]
public static class HubClientGeneration
{
}
```

### 用 IClientChannel 获取 Hub

```csharp
using Microsoft.Extensions.Logging;
using PulseRPC.Client;
using PulseRPC.Client.Configuration;
using PulseRPC.Shared;

public sealed class ChatClient : IDisposable
{
    private readonly IPulseClient _client;
    private IClientChannel? _channel;
    private IChatHub? _chatHub;

    public ChatClient(ILoggerFactory loggerFactory)
    {
        _client = new PulseClientBuilder()
            .WithLogging(loggerFactory)
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _client.InitializeAsync();
        _channel = await _client.ConnectToServerAsync("127.0.0.1", 7000, transport: TransportType.TCP);
        _chatHub = _channel.GetHub<IChatHub>();
    }

    public async Task<SendMessageResult> SendAsync(SendMessageRequest request)
    {
        if (_chatHub == null)
        {
            throw new InvalidOperationException("Client is not initialized.");
        }

        return await _chatHub.SendMessageAsync(request);
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _client.Dispose();
    }
}
```

不要在新代码中使用旧式 `GetXxxHubAsync()` 或 `RegisterEventListenerAsync()` 示例；当前通用入口是 `channel.GetHub<T>()` 和 `channel.RegisterReceiver<T>()`。

### 管理生命周期

- 长连接客户端复用一个 `IPulseClient`。
- 连接建立后保存 `IClientChannel`，不要每次调用前都重连。
- 退出时先 `DisconnectAsync()`，再 `StopAsync()` / `Dispose()`。
- 对临时调用场景，显式设置超时和重试上限，避免后台任务堆积。

## 服务端推送

### 定义客户端接收契约

```csharp
[Channel("CLIENT")]
public interface IGameReceiver : IPulseHub
{
    Task OnMatchFoundAsync(MatchFoundNotification notification);
    Task OnSystemAnnouncementAsync(string message);
}
```

### 客户端注册接收器

```csharp
public sealed class GameReceiver : IGameReceiver
{
    public Task OnMatchFoundAsync(MatchFoundNotification notification)
    {
        Console.WriteLine($"Match found: {notification.BattleId}");
        return Task.CompletedTask;
    }

    public Task OnSystemAnnouncementAsync(string message)
    {
        Console.WriteLine(message);
        return Task.CompletedTask;
    }
}

var subscription = channel.RegisterReceiver<IGameReceiver>(new GameReceiver());

// 不再接收时取消注册。
subscription.Dispose();
```

### 服务端使用 IHubContext

```csharp
public sealed class GameNotificationService
{
    private readonly IHubContext<IGameReceiver> _receivers;

    public GameNotificationService(IHubContext<IGameReceiver> receivers)
    {
        _receivers = receivers;
    }

    public Task NotifyUserAsync(string userId, MatchFoundNotification notification)
    {
        return _receivers.Clients.User(userId).OnMatchFoundAsync(notification);
    }

    public Task BroadcastAsync(string message)
    {
        return _receivers.Clients.All.OnSystemAnnouncementAsync(message);
    }
}
```

服务端需要注册生成的 HubContext：

```csharp
services.AddAllPulseReceiverContexts();
```

### Fan-out 选择建议

| 目标 | 推荐 API |
|---|---|
| 单个连接 | `Clients.Single(connectionId)` |
| 指定用户 | `Clients.User(userId)` |
| 多个用户 | `Clients.Users(userIds)` |
| 指定组 | `Clients.Group(groupName)` |
| 广播 | `Clients.All` |
| 排除发送者 | `Clients.Except(connectionId)` |

批量推送优先使用 `Users` / `Groups`，不要在业务代码里循环 N 次单用户推送，除非每个用户的 payload 不同。

## 统一寻址与集群

### 单节点默认能力

调用 `AddPulseServer()` 后，框架会注册：

- `IPulseRouter`：默认 `LocalPulseRouter`。
- `IPulseBackplane`：默认 `InProcessBackplane`。
- `IUserConnectionMapping` / `IGroupManager`：支持用户和分组推送。

业务代码通常不直接构造网络消息，优先使用生成代理和 `IHubContext<T>`。只有底层框架扩展或特殊路由场景才直接注入 `IPulseRouter`。

```csharp
public sealed class RawNotificationPublisher
{
    private readonly IPulseRouter _router;

    public RawNotificationPublisher(IPulseRouter router)
    {
        _router = router;
    }

    public ValueTask SendAsync(ushort protocolId, ReadOnlyMemory<byte> body, string userId)
    {
        var address = PulseAddress.User(nameof(IGameReceiver), userId);
        return _router.SendAsync(address, protocolId, body);
    }
}
```

### 静态成员集群

静态成员集群通过 `AddPulseClustering()` 开启。所有节点应使用一致的成员列表。

```csharp
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
        auth.SharedSecret = "replace-with-a-real-secret";
    });
```

### 动态服务发现

动态发现应在 `AddPulseClustering()` 之后接入，用发现驱动的 `DiscoveryClusterMembership` 覆盖静态成员和端点解析。

```csharp
services.AddPulseClustering(
    topology =>
    {
        topology.LocalNodeId = "game-a";
        // 动态发现下 Members 可留空。
    },
    auth => auth.SharedSecret = "replace-with-a-real-secret");

services.AddConsulDiscovery(
    localNodeId: "game-a",
    advertiseHost: "10.0.0.1",
    advertisePort: 7000,
    configure: options =>
    {
        options.Address = "http://consul:8500";
        options.ServiceName = "pulserpc-game";
    });
```

其它后端入口：

```csharp
services.AddEtcdDiscovery("game-a", "10.0.0.1", 7000, options =>
{
    options.ConnectionString = "http://etcd:2379";
    options.KeyPrefix = "/pulserpc/game/nodes/";
});

services.AddKubernetesDiscovery("game-a", "10.0.0.1", 7000, options =>
{
    options.Namespace = "game";
    options.LabelSelector = "app=game-server";
    options.UseInClusterConfig = true;
});
```

集群实践建议：

- 静态部署：`LocalNodeId` 必须与 `Members` 中某个 `NodeId` 一致，所有节点的 `Members` 保持一致。
- 动态部署：`localNodeId` 必须与 `ClusterTopologyOptions.LocalNodeId` 一致，`advertiseHost` / `advertisePort` 必须能被其它节点访问。
- Consul/etcd/Kubernetes 发现后端的 watch 是加速收敛机制，轮询仍作为兜底。
- `(Hub, Key)` 必须在所有节点上使用同样规则生成，避免路由不一致。
- 节点 ID 使用稳定、可观测的命名；Kubernetes 中通常使用 Pod 名。

### L3 Actor 状态迁移

L3 迁移适合需要在节点再均衡、下线或属主迁移时保留内存态的 Actor。默认 L2 语义只保证单一激活，不保证跨激活保留内存状态。

```csharp
public sealed class RoomService : PulseServiceBase, IActorStateSnapshot
{
    private readonly List<string> _members = new();

    public ValueTask<byte[]> CaptureStateAsync(CancellationToken cancellationToken = default)
        => new(MemoryPackSerializer.Serialize(_members));

    public ValueTask RestoreStateAsync(byte[] state, CancellationToken cancellationToken = default)
    {
        var restored = MemoryPackSerializer.Deserialize<List<string>>(state) ?? new List<string>();
        _members.Clear();
        _members.AddRange(restored);
        return default;
    }
}
```

迁移流程由 `ActorMigrationCoordinator` 编排：

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

L3 实践建议：

- 只给确实需要跨节点保留内存态的 Actor 实现 `IActorStateSnapshot`。
- 快照只包含业务状态，不包含连接、logger、DI 服务、线程同步对象等进程内资源。
- `CaptureStateAsync` / `RestoreStateAsync` 保持幂等和版本兼容。
- 迁移前 `MigrateOutAsync` 会先 `StopAsync` 静默排空在途消息；不要在业务外部绕过协调器直接复制状态。
- `IActorStateTransport` 需要由业务或基础设施注册具体实现；不要假设 `AddPulseClustering()` 会自动迁移所有 Actor。

## 认证与安全边界

### 默认拒绝外部暴露

开启 `EnableClientFacingGate` 后，外部客户端只能调用 `[ClientFacing]` 标注的方法。

```csharp
services.AddPulseServer(options =>
{
    options.AddTcp(7000);
    options.EnableClientFacingGate = true;
});
```

```csharp
[ClientFacing]
public interface IAccountHub : IPulseHub
{
    Task<SignInResult> SignInAsync(string signInId, string password);

    [Authorize]
    Task<UserInfo> GetCurrentUserNameAsync();

    [ClientFacing(false)]
    [Authorize(Role = RoleTypes.Internal)]
    Task RebuildIndexAsync();
}
```

### 连接级身份

登录成功后，将身份写入当前连接的 `AuthenticationContext`。后续同一连接请求通过 `PulseContext.CurrentUserId` 和 `PulseContext.Current.User` 读取身份。

不要把用户身份长期保存在 Hub 字段中；Hub 是无状态、可并发复用的。

### 内部调用

内部接口使用 `[Authorize(Role = RoleTypes.Internal)]`。跨节点链路必须启用节点互信，默认是共享密钥；生产可用 `UseCertificateNodeAuthentication()` 切换到证书鉴权。

```csharp
services.UseCertificateNodeAuthentication(options =>
{
    options.LocalCertificate = X509CertificateLoader.LoadPkcs12FromFile(
        "certs/game-a.pfx",
        "pfx-password");
    options.TrustedCertificateAuthorities.Add(
        X509CertificateLoader.LoadCertificateFromFile("certs/cluster-ca.cer"));
});
```

证书鉴权是应用层节点凭据校验，可与真实 TLS 传输配合使用。不要把它误认为自动启用传输层 TLS。

## 序列化与协议号

### MemoryPack 优先

```csharp
[MemoryPackable]
public partial class PlayerInfo
{
    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; }
}
```

建议：

- 客户端和服务端引用同一个 Shared 契约程序集。
- 不要用 `object` / `dynamic` 做 RPC payload。
- 避免在热路径中使用反射序列化。
- 协议号冲突时使用 `[Protocol(0x1234)]` 显式指定。

### 协议号空间

统一 `IPulseHub` 后，协议号按 Hub 接口隔离。同一 Hub 内方法协议号必须唯一，不同 Hub 可以复用同一个 ushort 协议号。

## 性能实践

### 批量接口优先

```csharp
public interface IUserHub : IPulseHub
{
    Task<UserInfo> GetUserAsync(string userId);
    Task<UserInfo[]> GetUsersAsync(string[] userIds);
}
```

需要读取多个对象时，优先暴露批量接口，避免客户端并发发起大量小请求。

### 避免阻塞 Service 队列

```csharp
public async Task<PlayerData> LoadAsync(string playerId)
{
    return await _repository.LoadAsync(playerId);
}
```

不要在 Actor/Service 队列里执行长时间同步 I/O 或 `.Result` / `.Wait()`，否则会阻塞同一实例的后续消息。

### 正确使用只读并发

对明确不修改状态的查询方法，可使用 `[Reentrant]`，让读请求并发执行；不要给会修改状态的方法加该特性。

```csharp
[Reentrant]
public Task<RoomSnapshot> GetSnapshotAsync()
{
    return Task.FromResult(BuildSnapshot());
}
```

### 控制消息语义

默认投递保证是 `DeliveryMode.AtMostOnce`，适合位置同步、心跳、临时提示等实时消息。涉及扣费、发奖、库存等状态变更时，业务侧仍应保证幂等；需要更高投递保证时再使用 `[Delivery]` 并结合去重设计。

```csharp
[Delivery(DeliveryMode.ExactlyOnce)]
Task<RewardResult> ClaimRewardAsync(ClaimRewardRequest request);
```

## 错误处理

### 用业务响应表达可预期错误

```csharp
[MemoryPackable]
public partial class ServiceResult<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
```

参数错误、业务拒绝、权限不足等可预期情况，优先返回明确的业务响应。系统异常、协议错误、网络错误再走异常路径。

### 客户端重试要有边界

```csharp
public static async Task<T> RetryAsync<T>(
    Func<Task<T>> operation,
    int maxAttempts = 3,
    TimeSpan? delay = null)
{
    var wait = delay ?? TimeSpan.FromMilliseconds(200);

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            return await operation();
        }
        catch when (attempt < maxAttempts)
        {
            await Task.Delay(wait);
            wait = TimeSpan.FromMilliseconds(wait.TotalMilliseconds * 2);
        }
    }
}
```

只对幂等或可重复执行的操作做自动重试。创建订单、扣费、发奖等操作必须带业务幂等键。

## 命名约定

| 类型 | 约定 | 示例 |
|---|---|---|
| 服务端 Hub 接口 | `I{Name}Hub : IPulseHub` | `IGameHub` |
| 客户端接收接口 | `[Channel("CLIENT")] I{Name}Receiver : IPulseHub` | `IGameReceiver` |
| Hub 实现 | `{Name}Hub` | `GameHub` |
| 有状态服务 | `{Name}Service : PulseServiceBase` | `PlayerService` |
| 请求类型 | `{Action}Request` | `SendMessageRequest` |
| 结果类型 | `{Action}Result` / `{Action}Response` | `JoinRoomResult` |
| 异步方法 | `Async` 结尾 | `JoinRoomAsync` |
| 推送方法 | `On` 开头 | `OnMatchFoundAsync` |

## 部署与运维

### 运行配置

- 每个服务进程显式配置监听端口和传输协议。
- 集群节点的 `NodeId` 使用稳定、可观测的命名。
- 共享密钥、证书私钥、Consul token 等敏感配置通过环境变量或密钥管理系统注入，不写入仓库。
- 静态成员部署变更 `Members` 时使用滚动发布并观察环重建和失败接管日志。
- 动态发现部署需要监控后端 watch/轮询延迟、注册记录数量和节点端点可达性。
- Kubernetes 部署使用 Pod 名作为节点 ID 时，Actor 属主会随 Pod 生命周期变化；需要保留内存态的 Actor 应接入 L3 迁移或持久化。

### 容器镜像

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 7000

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MyPulseRpcServer.dll"]
```

### 可观测性

日志建议包含：

- `ConnectionId`
- `UserId`
- `ServiceName`
- `ServiceKey`
- `ProtocolId`
- `NodeId`
- 业务相关的 `RoomId` / `PlayerId` / `BattleId`

不要在热路径中输出大 payload；记录关键 ID 和耗时即可。

## 快速参考

### 服务端启动

```csharp
services.AddPulseServer(options =>
{
    options.AddTcp("tcp", 7000, isDefault: true);
});

services.AddSingleton<IGameHub, GameHub>();
```

### 动态发现集群

```csharp
services.AddPulseClustering(
    topology => topology.LocalNodeId = "game-a",
    auth => auth.SharedSecret = "replace-with-a-real-secret");

services.AddConsulDiscovery("game-a", "10.0.0.1", 7000, options =>
{
    options.Address = "http://consul:8500";
    options.ServiceName = "pulserpc-game";
});
```

### L3 状态迁移

```csharp
public sealed class RoomService : PulseServiceBase, IActorStateSnapshot
{
    public ValueTask<byte[]> CaptureStateAsync(CancellationToken ct = default) => new(Array.Empty<byte>());
    public ValueTask RestoreStateAsync(byte[] state, CancellationToken ct = default) => default;
}
```

### 客户端调用

```csharp
var client = new PulseClientBuilder().Build();
await client.InitializeAsync();

var channel = await client.ConnectToServerAsync("127.0.0.1", 7000);
var gameHub = channel.GetHub<IGameHub>();

var player = await gameHub.GetPlayerInfoAsync();
```

### 客户端注册推送接收器

```csharp
var token = channel.RegisterReceiver<IGameReceiver>(new GameReceiver());
```

### 服务端推送

```csharp
await _receiverContext.Clients.User(userId).OnMatchFoundAsync(notification);
await _receiverContext.Clients.Group("room-1").OnSystemAnnouncementAsync(message);
await _receiverContext.Clients.All.OnSystemAnnouncementAsync(message);
```

### 旧 Receiver 迁移

```csharp
// 旧
public interface IGameReceiver : IPulseReceiver
{
}

// 新
[Channel("CLIENT")]
public interface IGameReceiver : IPulseHub
{
}
```

遵循这些实践，可以让代码与当前统一 `IPulseHub` 寻址、生成器和集群实现保持一致，同时避免依赖旧 API 或项目中未显式接入的外部基础设施。
