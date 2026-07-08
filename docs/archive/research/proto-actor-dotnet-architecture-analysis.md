# Proto.Actor .NET 架构对比与 PulseRPC 可采纳设计

> 文档状态：研究归档，2026-07-08。本文基于 Proto.Actor .NET 官方文档、GitHub 仓库公开说明，以及 PulseRPC 当前设计文档和源码事实整理；用于后续架构演进参考，不声明 PulseRPC 已实现本文所有建议。
>
> 主要外部资料：
> - Proto.Actor .NET 仓库：[asynkron/protoactor-dotnet](https://github.com/asynkron/protoactor-dotnet)
> - Proto.Actor 集群文档：[Working with a cluster (.NET)](https://proto.actor/docs/protoactor/cluster/using-cluster-net/)
> - Proto.Actor 集群总览：[Proto.Cluster](https://asynkron.se/docs/protoactor/cluster/)

## 1. 摘要

Proto.Actor .NET 是一个通用 Actor / Remote / Cluster 框架。它的核心取向是：API 保持小而明确，网络、集群、序列化优先复用已有生态能力，分布式层将“进程内 Actor 引用”扩展为“可远程寻址的 PID / grain identity”。PulseRPC 当前更偏 RPC 与游戏后端：契约、协议号、源生成器、客户端推送、服务端 Actor 生命周期已经具备，但跨节点 Actor placement、连接/分组 backplane、多跳回执与网关桥接仍是主要复杂点。

可直接采纳的方向不是照搬 Proto.Actor 的完整 ActorSystem，而是抽取以下架构设计：

1. **把本地 Actor、远程节点、客户端连接统一纳入显式地址模型**，让路由层而不是业务 Hub 决定投递路径。
2. **把集群拆成 Remote、Membership、Identity Lookup、Placement、CodeGen 五个独立子系统**，避免一次性引入“大而全”的分布式运行时。
3. **以 request-response 激活 Actor / grain**，将“定位 + 激活 + 首次投递”合并为可重试流程。
4. **引入可插拔 identity lookup / directory**，首版可用内存或静态策略，生产再接数据库、Redis、Consul、Kubernetes 等后端。
5. **让源生成器承担分布式调用的类型安全外壳**，运行时只处理 address、protocol id、payload 与 correlation。

## 2. Proto.Actor .NET 架构拆解

### 2.1 ActorSystem、PID 与 Props

Proto.Actor 将 ActorSystem 作为运行时根对象；Actor 通过 `Props` 描述创建方式、middleware 与 mailbox 等配置；spawn 后返回 PID，PID 是后续发送消息的地址引用。仓库 README 的 Hello World 展示了 `ActorSystem`、`Props.FromProducer`、`Spawn`、`Send` 这一最小闭环。

**可借鉴点：** PulseRPC 当前有 `PulseServiceManager` 管理 keyed service 实例，但调用侧常常仍以 Hub / Service 类型为中心。可以进一步把“服务实例地址”提升为一等概念：`PulseAddress.Actor(hub, key)` 与 `PulseAddress.Connection(...)` 一样可被路由器消费，业务层不感知本地或远程。

### 2.2 Remote 是 Cluster 的基础层

Proto.Actor 文档明确要求 Cluster 使用时必须配置 Remote。Remote 负责跨进程通信，Cluster 在其上叠加成员、身份、激活和 grain API。

**可借鉴点：** PulseRPC 不应让 cluster placement 直接依赖 TCP/KCP 细节。建议建立清晰的 `INodeTransport` / `INodeConnection` 边界：

```text
PulseAddress + MessageHeader
        ↓
IPulseRouter
        ↓
本地 mailbox / 本地 client channel / node-to-node transport / backplane
```

这样可以先用现有 TCP/KCP 或静态节点连接完成 POC，后续再替换为 gRPC、QUIC、WebSocket、Redis Stream、NATS 等。

### 2.3 ClusterConfig：成员发现与身份查找分离

Proto.Actor 的 Cluster 配置拆成 `clusterName`、`clusterProvider` 与 `identityLookup`。其中 cluster provider 负责知道“当前有哪些成员”，identity lookup 负责知道“某个 grain identity 当前在哪里”。这两个职责分离非常关键：成员列表变动不等同于 Actor 所有权迁移，Actor 所有权也不应由传输层临时猜测。

**可借鉴点：** PulseRPC 已经有服务发现与集群路由设计，但应避免把这些职责揉进 `ServerChannelManager` 或 Hub 调度器。建议拆分为：

| Proto.Actor 概念 | PulseRPC 对应建议 | 职责 |
| --- | --- | --- |
| `clusterProvider` | `IClusterMembershipProvider` | 节点加入、离开、健康状态、能力标签 |
| `identityLookup` | `IActorDirectory` / `IPlacementDirectory` | `(hub, key)` 到 owner node / activation id 的映射 |
| Remote | `INodeTransport` | 节点间 request/response、one-way、stream |
| Cluster kind | `PulseActorKind` / Hub name | 某类 Actor 的创建与路由元数据 |
| CodeGen grain client | 源生成的 Hub/Actor proxy | 类型安全调用外壳 |

### 2.4 Grain / Virtual Actor：逻辑身份优先于物理位置

Proto.Actor 文档说明 grain 不需要显式 spawn；首次向某个 kind + identity 发送消息时，集群在某个成员上激活它。发送 grain 消息推荐 request-response，因为框架需要确认消息已经送达或 grain 已完成激活，并可在获取 PID 前重试。

**可借鉴点：** PulseRPC 的 Actor 服务天然适合“玩家、房间、公会、战斗实例”等 keyed identity。跨节点演进时，不应要求业务代码先获取“某节点上的实例”，而应始终以 `(Hub, ServiceKey)` 调用。运行时负责：

1. 查 directory；
2. 未命中则按 placement strategy 选择 owner；
3. owner 侧激活本地实例；
4. 将请求投递到 mailbox；
5. response 沿 correlation / reply-to 返回。

### 2.5 CodeGen：隐藏协议样板，但不隐藏分布式语义

Proto.Actor 官方建议用 `Proto.Cluster.CodeGen` 生成 grain 扩展方法，以避免手写 grain 协议中容易出错的响应类型和特殊消息处理。PulseRPC 已经更依赖源生成器：客户端代理、服务端 skeleton、协议号、序列化都由生成器参与。

**可借鉴点：** 继续强化生成器，但生成器输出应保持薄：

- 生成强类型 proxy / skeleton；
- 固化 Hub 内 protocol id；
- 生成地址工厂与方法元数据；
- 不把 placement、重试、租约、backplane 策略写死在生成代码中。

### 2.6 设计原则：显式序列化与性能边界

Proto.Actor README 强调“小 API”“复用已有技术”“传数据而不是对象”“不要为了魔法 API 牺牲性能”。这与 PulseRPC 当前 MemoryPack、协议号、EnvelopeRelay 的方向一致。

**可借鉴点：** PulseRPC 的跨节点转发应坚持“头部可读、body 不反序列化”的 relay 模型。只有 owner 节点上的目标 skeleton 才反序列化业务 payload；中间网关和路由节点只读 `ServiceName`、`ServiceKey`、`ProtocolId`、`MessageId`、`ReplyTo` 等头部字段。

## 3. 与 PulseRPC 当前设计的关键差异

| 维度 | Proto.Actor .NET | PulseRPC 当前/设计态 | 差异与风险 |
| --- | --- | --- | --- |
| 基础抽象 | ActorSystem + PID + message | IPulseHub + MessageHeader + source-generated RPC | PulseRPC 类型安全更强，但分布式地址抽象需要补齐 |
| 本地并发 | mailbox 串行处理 Actor 消息 | PulseService mailbox / keyed service 管理 | 方向相近，可复用现有串行一致性 |
| 远程通信 | Remote 先行，Cluster 架在 Remote 上 | 传输层已有 TCP/KCP，node-to-node 与 Gateway 仍需产品化 | 需要明确节点间传输边界 |
| 成员发现 | cluster provider 可插拔 | 文档中已有发现概念，落地程度需分阶段 | 不要让发现直接承担 placement |
| Actor 定位 | identity lookup 可插拔 | 需要 `IActorDirectory` / lease / activation record | 这是跨节点 Actor 的核心缺口 |
| 激活模型 | 首次请求触发 grain activation | 当前服务实例偏本地 manager 获取/创建 | 可改造为 owner 节点激活 |
| API 表达 | message / generated grain client | Hub 接口 + source generator | PulseRPC 保持现有 RPC 体验即可 |
| 客户端连接 | 通常经业务 API 或桥接层接入 | PulseRPC 原生关注客户端推送、分组、用户连接 | PulseRPC 需要额外设计 connection directory/backplane |
| 投递语义 | grain request-response 便于确认激活 | 当前 RPC request-response + 推送 one-way | 跨节点 one-way 仍需失败、重试、幂等边界 |

## 4. 推荐采纳的架构设计

### 4.1 引入四层路由平面

建议把 PulseRPC 路由拆成四层，每层都可以单独测试：

```text
L0 生成器层：IXxxHub 方法 → protocol id + serializer + typed proxy
L1 地址层：PulseAddress(Connection/User/Group/Actor/Node)
L2 决策层：IPulseRouter + IActorDirectory + IConnectionDirectory
L3 投递层：local mailbox / local channel / node transport / backplane
```

好处：

- 源生成器不需要知道集群策略；
- backplane 不需要知道 Hub 方法签名；
- Actor directory 不需要知道客户端分组；
- node transport 只负责可靠传帧，不负责业务路由。

### 4.2 将 Actor Directory 设计成“identity lookup + lease”

借鉴 Proto.Actor identity lookup，PulseRPC 可以定义：

```csharp
public interface IActorDirectory
{
    ValueTask<ActorPlacement?> TryGetOwnerAsync(string hub, string key, CancellationToken ct);
    ValueTask<ActorPlacement> GetOrActivateAsync(string hub, string key, ActorActivationRequest request, CancellationToken ct);
    ValueTask RenewLeaseAsync(ActorPlacement placement, CancellationToken ct);
    ValueTask ReleaseAsync(ActorPlacement placement, CancellationToken ct);
}
```

首版不必做强一致全球数据库，可分三级：

1. **P1 InMemoryDirectory**：单进程和测试使用，验证 API；
2. **P2 StaticPartitionDirectory**：按 hash ring / rendezvous hashing 将 `(hub,key)` 固定到节点；
3. **P3 LeasedDirectory**：Redis/Etcd/数据库 CAS + TTL lease，支持节点故障后重激活。

### 4.3 Placement Strategy 独立于 Directory 后端

Proto.Actor 文档和议题中多次出现不同 placement / affinity 策略。PulseRPC 可定义策略接口：

```csharp
public interface IActorPlacementStrategy
{
    ValueTask<NodeId> SelectOwnerAsync(ActorIdentity identity, IReadOnlyList<ClusterMember> candidates, CancellationToken ct);
}
```

建议内置策略：

- `HashPlacementStrategy`：稳定、易测试，适合玩家 ID、房间 ID；
- `LocalAffinityPlacementStrategy`：优先本节点，适合从分区日志或本地连接入口激活的 Actor；
- `LeastLoadedPlacementStrategy`：依赖指标，后置；
- `PinnedPlacementStrategy`：运维或测试显式指定。

### 4.4 request-response 激活，不把 one-way 用作首次激活协议

Proto.Actor 文档指出 grain 需要 request-response 来确认投递和激活。PulseRPC 可以保留业务 one-way 推送，但 **Actor 首次激活、跨节点定位、Gateway 多跳** 不建议只用 fire-and-forget。建议：

- 对 Actor Ask：天然 request-response；
- 对 Actor Tell：运行时内部先执行 `GetOrActivateAsync`，激活成功后再投递；必要时返回仅运行时可见的 ack；
- 对 fan-out 推送：默认 best-effort，但 backplane 可暴露统计 ack，不承诺每个客户端都成功。

### 4.5 统一 MessageHeader 的多跳字段

当前设计文档已经提出 `SourceNodeId`、`ReplyTo`、`HopLimit`。与 Proto.Actor 的远程 PID / cluster lookup 思路对齐，建议将这些字段视为跨节点能力的最小协议地基：

- `SourceNodeId`：发起节点或 Gateway；
- `ReplyTo`：响应路由地址；
- `HopLimit`：避免 Gateway / backend / backplane 环路；
- `CorrelationId`：继续复用 MessageId；
- `TraceId` / `SpanId`：可后续添加，先不要阻塞 POC。

### 4.6 Node-to-node transport 不绑定业务序列化

建议节点间帧只要求能携带：

```text
FrameType + MessageHeader + ReadOnlyMemory<byte> Body
```

中间节点可做 EnvelopeRelay；目标 owner 节点才反序列化。这样既保留 MemoryPack 业务 payload，也允许未来将 node transport 切到 gRPC stream、QUIC 或消息队列。

### 4.7 源生成器生成“分布式透明但语义显式”的代理

推荐生成三类 proxy：

| Proxy | 输入地址 | 调用语义 |
| --- | --- | --- |
| `IUserHubProxy.ForActor(key)` | `PulseAddress.Actor("UserHub", key)` | Ask/Tell 到 Actor owner |
| `IUserReceiverProxy.ForConnection(id)` | `PulseAddress.Connection(id)` | 单连接推送或 reverse ask |
| `IUserReceiverProxy.ForGroup(group)` | `PulseAddress.Group(group)` | fan-out 推送 |

用户仍写强类型 Hub 方法，但代码中能看出“这是 Actor 调用 / 连接调用 / 分组广播”，避免过度透明导致运维不可解释。

### 4.8 Connection Directory 与 Actor Directory 分离

Proto.Actor 的 cluster 更关注 Actor identity；PulseRPC 还必须管理客户端连接、用户、分组、Gateway 虚拟连接。因此建议额外定义：

```csharp
public interface IConnectionDirectory
{
    ValueTask<ConnectionPlacement?> FindConnectionAsync(string connectionId, CancellationToken ct);
    ValueTask<IReadOnlyList<ConnectionPlacement>> FindUserAsync(string userId, CancellationToken ct);
    ValueTask<IReadOnlyList<NodeId>> FindGroupOwnersAsync(string groupName, CancellationToken ct);
}
```

不要用 Actor directory 存用户连接；两者生命周期、基数、变更频率、故障语义完全不同。

## 5. 不建议直接照搬的部分

1. **不要把 PulseRPC 改造成裸 message actor 框架。** PulseRPC 的核心价值是 RPC 契约、source generator、客户端推送与 Unity 友好体验。
2. **不要把 Protobuf 作为唯一序列化方向。** Proto.Actor 的“Protobuf all the way”符合其跨语言定位；PulseRPC 现有 MemoryPack 和协议号设计可继续保留，只需让跨节点 envelope 独立于 payload。
3. **不要默认暴露 PID 风格 API 给业务。** PulseRPC 用户更适合看到 Hub interface、Actor key、User/Group/Connection，而不是通用 PID。
4. **不要在第一阶段做完整 gossip、分区迁移和复杂 placement。** 先把接口、静态成员、hash owner、EnvelopeRelay、多跳 response 打通。
5. **不要让生成器承载动态集群策略。** 策略应在运行时 DI 中替换，生成器只负责协议与类型安全。

## 6. 分阶段落地路线

### Phase A：架构边界与协议地基

> 落地备注（2026-07-08）：仓库已补齐 Phase A 的最小公共边界：`PulseAddress`、`IPulseRouter`、`IActorDirectory`、`INodeTransport`、`IConnectionDirectory`，并保留单进程 / in-memory 目录语义作为默认验证路径。

- 新增 `PulseAddress`、`IPulseRouter`、`INodeTransport`、`IActorDirectory`、`IConnectionDirectory` 抽象。
- `MessageHeader` 增加 `SourceNodeId`、`ReplyTo`、`HopLimit`。
- 为 EnvelopeRelay 增加跨节点转发单元测试。
- 只支持单进程 / in-memory directory，确保现有单节点行为不变。

### Phase B：静态集群 + hash placement

> 落地备注（2026-07-08）：仓库已补齐 Phase B 的最小静态集群边界：静态成员由 `StaticClusterMembership` / `ClusterTopologyOptions` 提供，`HashPlacementStrategy` 以 `(hub,key)` 选择 owner，`TransportBackedNodeLink` 可通过 `INodeTransport` 发送最小原始帧，`ClusterPulseRouter` 的 Actor Ask 已经走跨节点 request-response。

- `StaticClusterMembership` 从 `ClusterTopologyOptions.Members` 读取静态节点（承担 StaticClusterMembershipProvider 角色）。
- `HashPlacementStrategy` 将 `(hub,key)` 映射到 owner node。
- `INodeTransport` 先复用现有长连接能力或实现最小 TCP node channel。
- Actor Ask 跨节点 request-response 打通。

### Phase C：Gateway 与连接 backplane

> 落地备注（2026-07-08）：仓库已补齐 Phase C 的最小连接目录桥接：Gateway 前置 Hub 会把真实客户端注册为虚拟连接，`BackplaneConnectionDirectory` 通过 `IPulseBackplane` 模型 Y 解析 connection/user/group 成员，Fan-out 继续保持“本地投递 + 远端节点扩散”。

- Gateway 注册虚拟连接到 `IConnectionDirectory`。
- User/Group fan-out 先做“本地投递 + 远端节点扩散”。
- `HopLimit`、`ReplyTo`、`MessageId` 端到端测试覆盖 client → gateway → backend actor → gateway → client。

### Phase D：Lease directory 与故障恢复

> 落地备注（2026-07-08）：仓库已补齐 Phase D 的最小租约恢复边界：`IActorLeaseStore` 明确 CAS + TTL 后端契约，默认 `InMemoryActorLeaseStore` 保持单进程验证路径，`ActorLeaseHeartbeat` 对本节点 owner 做周期续租，`ClusterPulseRouter` 在本地激活后登记心跳；重复激活、重试和幂等 token 语义由租约 + `MessageId` 去重共同约束。

- Redis/Etcd/数据库 CAS + TTL lease。
- owner 心跳续租，节点失效后重激活。
- 明确重复激活、消息重试、幂等 token 的语义。

### Phase E：高级能力

- placement 策略插件化：local affinity、least loaded、pinned。
- gossip membership 或 Kubernetes/Consul provider。
- 分布式 tracing、指标、dead letter、slow mailbox 诊断。
- 可选 exactly-once / outbox / inbox 语义，默认不承诺。

## 7. 需要验证的关键问题

| 问题 | 验证方式 | 通过标准 |
| --- | --- | --- |
| Hub 内 protocol id + address 是否足够唯一定位目标方法 | 生成器 golden test + 跨节点调度测试 | 不同 Hub 可复用 id，同 Hub 内冲突被诊断 |
| EnvelopeRelay 是否能不反序列化 body 完成转发 | 单元测试构造 header/body，检查 body byte-for-byte 保持 | 中间节点不依赖业务 DTO 程序集 |
| Actor 首次跨节点 Ask 是否可重试 | owner 未激活、owner 节点重启、directory miss 场景 | 调用方得到成功响应或明确超时/失败 |
| Gateway reverse ask 是否能回到原客户端 | 多跳 integration test | response 按 MessageId/ReplyTo 返回正确 connection |
| 分组 fan-out 是否跨节点完整 | 多节点假实现测试 | 所有节点本地 group 成员都收到一次 |
| lease 失效是否避免双活 | 故障注入测试 | 同一 `(hub,key)` 不出现长期双 owner；短暂双活有 fencing token |

## 8. 结论

Proto.Actor .NET 最值得 PulseRPC 借鉴的是“分布式 Actor 运行时的职责切分”，而不是某个具体 API 名称。PulseRPC 应保持现有强类型 RPC 与客户端推送优势，在其下方补齐地址、目录、placement、节点传输和 Gateway 回执。推荐的最小演进路径是：

```text
显式 PulseAddress
  → 可插拔 ActorDirectory / ConnectionDirectory
  → Remote/NodeTransport 与 Router 解耦
  → request-response 激活 Actor
  → Gateway + backplane
  → lease directory 与故障恢复
```

这样既能吸收 Proto.Actor 的成熟分布式 Actor 经验，又不会牺牲 PulseRPC 面向游戏后端、Unity 客户端和源生成 RPC 的产品定位。
