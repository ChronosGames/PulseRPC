# PulseRPC.Server

PulseRPC.Server 是 PulseRPC 的服务端运行时。当前实现围绕 `IPulseHub` 契约、TCP/KCP 传输、Source Generator 路由表、服务端消息管线和 `PulseServiceBase` 有状态服务模型展开。

> 首次上手只参考经过 CI 端到端验证的三项目 [HelloRPC](../../samples/HelloRPC/) 黄金路径。

## 当前边界

- 内置传输：`TransportType.TCP`、`TransportType.KCP`。
- 服务端入口：`services.AddPulseServer(...)`。
- 消息执行：固定数量 worker shard；每条连接在其生命周期内绑定一个 shard，每个 shard 使用有界队列。
- Hub 契约：接口继承 `IPulseHub`。
- 服务端路由：依赖 `PulseRPC.Server.SourceGenerator` 生成的 `IServiceRoutingTable`。
- 有状态服务：使用 `PulseServiceBase`、`AddPulseService<TService>()`、`IServiceAccessor<TService>`。
- 客户端推送：使用 `[Channel("CLIENT")]` 的 `IPulseHub` 接收契约与 `IHubContext<TReceiver>`。
- 集群路由：`AddPulseClustering(...)`，动态发现后端在 `PulseRPC.Infrastructure.Consul`、`PulseRPC.Infrastructure.Etcd`、`PulseRPC.Infrastructure.Kubernetes`。

当前源码未内置 WebSocket/QUIC 传输，也没有旧文档中提到的独立 `PulseRPC.ServiceDiscovery`、`PulseRPC.Monitoring`、`PulseRPC.Tracing` 包。

## 快速注册

完整服务端组合位于 [`HelloRPC.Server/Program.cs`](../../samples/HelloRPC/HelloRPC.Server/Program.cs)。它通过 `AddPulseServer` 注册传输、消息引擎和托管服务，并由 Generic Host 统一启动和停止。运行方式见 [快速开始](../../docs/getting-started/quickstart.md)。

## 消息执行与背压

`PulseServerOptions` 是服务端运行时的有效配置入口。消息引擎构造时创建固定数量的 shard，不再为每条连接创建独立 worker、L1/L2/L3 管线或 adaptive scheduler：

```csharp
services.AddPulseServer(options =>
{
    options.AddTcp(7000);
    options.MessageWorkerShardCount = Math.Max(1, Environment.ProcessorCount);
    options.MessageQueueCapacityPerShard = 1024;
    options.MaxConcurrentMessagesPerShard = 8;
    options.MaxConcurrentMessagesPerConnection = 1;
    options.MaxQueuedMessagesPerConnection = 32;
    options.MaxPendingMessageBytesPerConnection = 4 * 1024 * 1024;
    options.MaxPendingMessageBytesPerShard = 64 * 1024 * 1024;
    options.MaxRequestTimeoutMs = 30_000;
});
```

- `MessageWorkerShardCount` 控制固定 shard 数；默认值为逻辑处理器数。
- `MaxConcurrentMessagesPerShard` 控制每个 shard 的在途上限；默认 `1`，保持原串行行为。总在途上限为 shard 数乘以此值。
- `MaxConcurrentMessagesPerConnection` 默认 `1`，保留单连接处理顺序。内部节点连接复用多个 Actor 时可调高，但不得依赖跨请求的网络到达顺序；同一 Actor 仍通过邮箱串行执行，需要业务序号时由契约携带并校验。
- `MessageQueueCapacityPerShard` 控制每个 shard 最多排队的消息数；默认值为 `1024`。
- 连接注册时按轮询分配 shard，并在断开前保持不变。断开会停用该连接的生命周期租约，使旧积压消息不会进入同 ID 的新连接。
- shard 按连接轮询分配可用执行槽。慢请求只占用其在途槽，其他连接可在并发上限内继续执行；处理器实际退出后才释放载荷和执行槽，忽略取消的业务仍会占用容量。
- 队列数量和载荷字节预算同时生效；字节预算包含排队及在途工作，业务实际完成后才释放。新增预算默认 `0`，兼容现有应用；上例给出可调整的起始值，需结合负载验收。
- 入站容量耗尽时 Request 返回 `SERVER_BUSY`；响应队列也满时关闭对应物理连接，使调用方及时失败。OneWay 没有响应，不适合未经业务确认的资产变更。自定义 `IResponseProcessor` 应实现非阻塞的 `TryProcessMessageResult`，默认返回 `false`。
- `MaxRequestTimeoutMs` 从本节点入队开始计时，取服务端上限和客户端期限中的较小值；客户端未传期限时使用服务端上限。过期排队项不进入业务，返回 `TIMEOUT`。执行中超时依赖合作式取消，不能回滚数据库操作，也不会提前释放执行槽。
- 关闭服务器时会停止接收、取消连接工作并等待实际在途工作完成清理。

旧的 `MessageEngineConfiguration`、`TieredEngineManagerOptions`、`TieredMessageProcessorOptions` 和 `ServerPreset` 已仅作二进制兼容保留，不再控制运行时。迁移清单见[迁移指南](../../docs/guides/migration.md)。

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

专属邮箱的可重入读请求由 `ServiceExecutionOptions.MaxConcurrentReentrantRequests` 限制，默认 `64`；写请求仍独占执行。热点 Actor 可使用较小的 `QueueCapacity` 和 `ThrowException` 背压策略，使超量调用收到 `SERVER_BUSY`；`Block` 模式会让调用者等待邮箱容量并占用入站执行槽。

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
- `samples/HelloRPC/HelloRPC.Server`
