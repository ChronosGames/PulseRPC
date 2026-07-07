# 01 架构边界评审

## 当前模块关系

当前解决方案中的核心项目可以粗略分为：

- `PulseRPC.Abstractions`：公共契约、属性、消息头、集群接口、调度接口。
- `PulseRPC.Shared`：TCP/KCP、缓冲池、批处理、共享运行时。
- `PulseRPC.Client`：客户端连接、通道、负载均衡、重试、代理生成器打包。
- `PulseRPC.Server`：服务端生命周期、通道管理、消息引擎、路由、集群、服务管理。
- `PulseRPC.Infrastructure*`：服务发现后端和集群成员视图。
- `PulseRPC.*.SourceGenerator`：客户端/服务端生成器和分析器。

期望的依赖方向应当是：

```text
Abstractions <- Shared <- Client
Abstractions <- Shared <- Server
Abstractions <- Infrastructure
Client.SourceGenerator -> user code only as analyzer
Server.SourceGenerator -> user code only as analyzer
```

实际代码中存在反向或交叉依赖：

- `PulseRPC.Server` 引用 `PulseRPC.Client`：`src/PulseRPC.Server/PulseRPC.Server.csproj:23`。
- `PulseRPC.Abstractions` 中包含实现型代码和运行时依赖：`MemoryPack`、`System.Threading.Channels`、`DiagnosticSource`、`System.CodeDom`。
- `PulseRPC.Shared` 中存在 `namespace PulseRPC.Abstractions.Transport.Batching`：`src/PulseRPC.Shared/Batching/BatchedTransport.cs:6`。
- `PulseRPC.Abstractions` 文件暴露多个非 Abstractions 命名空间：`PulseRPC.Shared`、`PulseRPC.Client`、`PulseRPC.Channels`、`PulseRPC.Messaging`。

## 问题 A1：Abstractions 不再是纯抽象包

证据：

- `src/PulseRPC.Abstractions/Transport/ITransport.cs:7` 使用 `namespace PulseRPC.Shared`。
- `src/PulseRPC.Abstractions/Channels/IClientChannel.cs:5` 使用 `namespace PulseRPC.Client`。
- `src/PulseRPC.Abstractions/Channels/ITransportChannel.cs:3` 使用 `namespace PulseRPC.Channels`，并定义 `IServiceHandler`、`IRequestContext` 等运行时接口。
- `src/PulseRPC.Abstractions/Messaging/MessagePacket.cs:27` 定义 `MessagePacketHolder`，`MessagePacket.cs:92` 定义 `ref struct MessagePacket`，包含序列化和内存池语义。
- `src/PulseRPC.Abstractions/Memory/ZeroCopyCircularBuffer.cs`、`Protocol/PendingRequestManager.cs`、`Transport/Batching/*` 属于实现或运行时机制。

影响：

- 用户安装 `PulseRPC.Abstractions` 时会获得实现细节和更多传递依赖，不利于 Unity/裁剪/多目标框架。
- 包名、命名空间、职责不一致，导致 API 稳定性和文档表达困难。
- 后续做语义版本治理时，很难判断哪些类型是稳定契约，哪些只是内部实现。

根因：

- 为快速复用公共类型，把“消息结构、传输事件、缓冲实现、调度实现”都放进了最底层包。
- 缺少明确的包分层标准：什么可以公开、什么只能 internal、什么属于 Shared。

建议：

1. 定义包职责：
   - `PulseRPC.Abstractions`：只保留跨包公共契约、属性、轻量 value object、错误码和最小 DTO。
   - `PulseRPC.Shared`：保留 TCP/KCP 基类、缓冲、消息编解码、批处理等共享实现。
   - `PulseRPC.Protocol` 或 `PulseRPC.Core`：如果消息协议类型需要独立复用，可单独建立更准确的包。
2. 对已公开类型不要直接移动破坏兼容；先新增正确命名空间类型，再以 `[Obsolete]` 或 type-forwarding 方式过渡。
3. 评估 `MessagePacketHolder`、`ZeroCopyCircularBuffer`、`PendingRequestManager` 是否确实需要 public；不需要则 internal 化。

## 问题 A2：命名空间不反映程序集边界

证据：

- Abstractions 项目中定义 `PulseRPC.Shared.ITransport`。
- Abstractions 项目中定义 `PulseRPC.Client.IClientChannel`。
- Shared 项目中定义 `PulseRPC.Abstractions.Transport.Batching.BatchedTransport`。
- Server 项目中 `Channels/ServerChannelManager.cs` 使用 `namespace PulseRPC.Server.Processing`，而相邻接口 `IServerChannelManager.cs` 使用 `PulseRPC.Server.Transport`。

影响：

- 用户通过命名空间无法判断类型来自哪个包。
- IDE 自动 using 会引入非预期包，公共 API 文档也难以按模块组织。
- 后续拆包或 NuGet 发布时容易出现类型迁移和二义性。

根因：

- 项目文件夹、程序集名、命名空间演进不同步。
- “Shared”同时被用作程序集名、命名空间名和抽象契约命名空间，语义过载。

建议：

- 新代码统一按程序集职责命名：`PulseRPC.Abstractions.*`、`PulseRPC.Shared.*`、`PulseRPC.Client.*`、`PulseRPC.Server.*`。
- 旧命名空间保留兼容包装，不再新增类型。
- 在 CI 增加命名空间规则检查，至少禁止 `PulseRPC.Abstractions` 项目新增 `PulseRPC.Client` / `PulseRPC.Shared` 命名空间类型。

## 问题 A3：Server 直接依赖 Client

证据：

- `src/PulseRPC.Server/PulseRPC.Server.csproj:23` 引用 `..\PulseRPC.Client\PulseRPC.Client.csproj`。
- `src/PulseRPC.Server/Clustering/PulseNodeLink.cs` 通过 `new PulseClientBuilder()` 复用客户端出站连接。
- csproj 注释说明这是为节点间通信复用客户端全双工能力。

影响：

- 服务端包被迫携带客户端运行时、客户端 Source Generator 包装策略和客户端配置语义。
- 服务端内部集群链路与公开客户端 API 绑定，后续调整客户端生命周期会影响服务端。
- 依赖图从“Server 与 Client 平级依赖 Shared”变成“Server 上层依赖 Client 上层”，架构上更难解释。

根因：

- 节点间链路和用户客户端都需要“主动拨出 + InvokeRaw”能力，当前直接复用了最高层客户端。
- 缺少一个更小的出站连接抽象，例如 `INodeLinkTransport`、`IRpcPeerConnection` 或 `PulseRPC.ClientCore`。

建议：

1. 抽出最小出站 RPC 运行时到 `PulseRPC.Shared` 或新包 `PulseRPC.Transport.ClientRuntime`。
2. `PulseRPC.Client` 只保留面向应用开发者的 builder、负载均衡、连接池、服务代理。
3. `PulseRPC.Server` 的集群链路依赖最小抽象，不依赖 `PulseClientBuilder`。
4. 迁移前新增测试覆盖节点间 `Ask/Send`，确保拆分不改变协议。

## 问题 A4：全局静态注册隐藏运行时依赖

证据：

- `src/PulseRPC.Server/Processing/Pipeline/ResponseProcessor.cs:17` 定义 `ResponseSerializerRegistry` 静态类和 `Instance`。
- `src/PulseRPC.Server/Services/ServiceRoutingTableRegistry.cs` 定义全局 `ServiceRoutingTableRegistry.Instance`。
- `src/PulseRPC.Server/Extensions/PulseServerServiceCollectionExtensions.cs:183`、`:190` 在 DI 注册时读取这些全局实例。
- `MessageDispatcher` 构造时依赖 `ServiceRoutingTableRegistry.Instance`，为空则抛出。

影响：

- 生成器、模块初始化、DI 注册三者顺序被隐藏，故障通常表现为运行时缺失而非编译期错误。
- 多 AppDomain/多测试项目/多服务实例场景中，全局实例会污染测试隔离。
- 用户看 `AddPulseServer` API 很难知道必须依赖源生成器注册副作用。

根因：

- 生成器生成运行时表时采用 ModuleInitializer + 静态单例，降低了用户配置成本，但牺牲了显式性。

建议：

- 保留自动注册体验，但把生成表也注册成显式服务：`services.AddGeneratedPulseRpcRouting()`。
- 在 `AddPulseServer` 中检测缺失时给出明确诊断：缺少哪个生成表、可能原因、如何启用 generator。
- 单元测试中禁用全局静态或允许隔离重置，避免跨测试污染。

## 问题 A5：服务端存在多套“消息处理架构”但主路径不清晰

证据：

- `MessageEngine`、`MessageDispatcher`、`TieredMessageProcessor`、`ServiceThreadScheduler`、`ThreadAffinityScheduler`、`ResponseProcessor` 同时存在。
- `MessageDispatcher.StartAsync` 会启动 worker，但 `MessageEngine.StartAsync` 只启动 `_responseProcessor`，没有启动 `_messageDispatcher`。
- `MessageEngine.ProcessSingleMessage` 直接调用 `_messageDispatcher.DispatchAsync`，绕过 `MessageDispatcher` 内部优先级队列。

影响：

- 文档中“三层/多级缓冲/优先级调度”的架构与实际请求路径不一致。
- 性能问题无法通过指标定位，因为队列、worker、优先级等对象可能并未处于主路径。
- 后续优化容易在非主路径上投入，实际无收益。

根因：

- 多轮性能设计迭代后保留了多个实现，没有做“唯一主路径”裁剪。

建议：

- 先画出当前真实路径：transport -> channel manager -> message engine -> dispatcher direct -> routing table -> response processor -> channel。
- 对不在主路径的队列和调度器标记为实验/废弃/internal，或接入主路径并补契约测试。
- 指标命名必须只描述真实路径，不再用临时常量填充。

## 目标边界建议

建议目标结构：

```text
PulseRPC.Abstractions
  - attributes, marker interfaces, protocol IDs, transport contracts, clustering contracts

PulseRPC.Protocol
  - MessageHeader, MessageFlags, framing helpers, serialization-neutral protocol primitives

PulseRPC.Shared
  - TCP/KCP base, buffer pools, codecs, batching, common runtime utilities

PulseRPC.Client
  - user-facing client, connection manager, load balancing, generated stubs integration

PulseRPC.Server
  - server lifecycle, channel manager, routing, service lifecycle, clustering orchestration

PulseRPC.Infrastructure.*
  - discovery/backplane providers only
```

短期不建议一次性大搬迁。应先冻结新增混乱边界，再按 P0 API 修复、P1 依赖解耦、P2 命名兼容迁移推进。

