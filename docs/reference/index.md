# 参考手册

本页聚合当前维护者最常查的事实型信息。详细教程请看 `getting-started/` 和 `guides/`。

## 包和项目

| 项目 | 作用 |
| --- | --- |
| `PulseRPC.Abstractions` | 公共契约、基础类型、认证、路由、传输抽象 |
| `PulseRPC.Client` | 客户端运行时、连接管理、负载均衡 |
| `PulseRPC.Server` | 服务端运行时、Hub/Service 调度、集群路由 |
| `PulseRPC.Shared` | TCP/KCP、缓冲池、批处理等共享组件 |
| `PulseRPC.Infrastructure` | 发现和集群成员通用基础设施 |
| `PulseRPC.Infrastructure.*` | Consul、Etcd、Kubernetes 等后端 |
| `PulseRPC.Backplane.Redis` | Redis Backplane 与 CAS + TTL Actor 租约存储 |
| `PulseRPC.Analyzers` | 仓库构建期包边界门禁，不作为运行时包发布 |
| `PulseRPC.*.SourceGenerator` | 客户端/服务端代码生成 |

详细依赖方向、公开类型规则和统一术语见[包边界与核心术语](package-boundaries-and-terminology.md)。

## 常用配置入口

| 入口 | 用途 |
| --- | --- |
| `PulseClientBuilder` | 创建客户端、添加连接、负载均衡、日志 |
| `ClientOptions.LoadBalancing` | 配置动态连接权重来源和一致性哈希虚拟节点 |
| `ServiceProxyOptions.StickyKey` | 为 ConsistentHash 提供稳定的用户/租户/会话 key |
| `ConnectionConfig` / `ConnectionDescriptor` | 客户端连接描述 |
| `AddPulseServer(...)` | 注册默认服务端 |
| `AddNamedPulseServer(...)` | 注册命名服务端 |
| `PulseServerOptions.MessageWorkerShardCount` | 固定服务端消息 worker shard 数；默认等于逻辑处理器数 |
| `PulseServerOptions.MessageQueueCapacityPerShard` | 每个消息 shard 的有界队列容量；默认 `1024` |
| `TransportChannelConfiguration` | 服务端 TCP/KCP 监听配置 |
| `AddPulseClustering(...)` | 注册集群路由和节点配置 |
| `AddRedisActorLeases(...)` | 以 Redis 原子脚本替换默认进程内 Actor 租约 |
| `ProtocolId.Generate(signature)` | 使用与 Source Generator 一致的 FNV-1a 规则计算运行期协议号 |
| `ReceiverDeliveryMode` | Receiver push 的 `BestEffort` / `Strict` 错误策略；取消在两种模式下都传播 |
| `{Hub}RouterProxy` | 对显式 `[PulseHub(Consume = true)]` Hub 生成的强类型 `IPulseRouter` / Actor 出站代理 |
| `UseCertificateNodeAuthentication(...)` | 使用证书签名节点凭据替换共享密钥认证 |
| `AddConsulDiscovery` / `AddEtcdDiscovery` / `AddKubernetesDiscovery` | 覆盖默认成员发现 |

## 协议和传输事实

- TCP/KCP 实现位于 `PulseRPC.Shared`。
- 传输抽象为 `ITransport`、`IClientTransport`、`IServerTransport`、`IServerListener`。
- 当前帧头、握手和协议常量以 `src/PulseRPC.Abstractions/Transport/ProtocolConstants.cs` 以及 `PulseRPC.Shared` 中实现为准。
- `TransportOptions.SmallPacketThreshold` 和 `ChunkSize` 当前保留兼容，已不作为应用层分片主路径依据。
- `TcpNodeTransport` 是默认节点数据面，提供连接复用、实际写完成、执行 ACK、请求关联、超时隔离、断线淘汰和 node wire 能力协商；`SecurityMode` 未显式设置时 fail closed，生产必须声明并实际提供外部 mTLS。
- node wire 当前生产版本为 v2；legacy Actor wire 默认关闭。
- `IHubAddressedClientChannel` 让生成代理显式携带 canonical Hub，服务端按 `(Hub, ProtocolId)` 强校验；生成代理不再回退到无 Hub API。
- `ServiceRoutingTableRegistry` 与 `ResponseSerializerRegistry` 组合所有已加载程序集的生成结果，DI 在解析时取得该组合视图。

## Unity IL2CPP 事实

- Unity 包提供 `link.xml`，保留 PulseRPC 和 MemoryPack 运行时程序集。
- 客户端生成器为每个 `[PulseClientGeneration]` Hub/Receiver 生成 `[Preserve]` AOT 根，并闭合实际 wire payload 的 MemoryPack 泛型调用。
- CI 使用 Unity `2022.3.62f3`、iOS、IL2CPP 和 High managed stripping 构建 smoke 契约，并检查生成 C++ 中的 Hub、Receiver 与 preservation roots。

## 服务端消息执行事实

- `MessageEngine` 在运行时构造固定数量的 worker shard；连接注册时按轮询选择一个 shard，并在本次连接生命周期内保持绑定。
- 每个 shard 只有一个长期 worker 和一个容量固定的 channel。队列满时本次入队立即失败，载荷所有权和请求取消状态在拒绝路径释放。
- 连接断开会停用 generation-scoped lease 并取消在途 handler；旧队列项不会在同 ID 重连后进入新连接。
- 停机会停止新连接和新入队，等待连接停用及所有固定 worker 完成积压终结清理，再停止 dispatcher 和响应处理器。
- 当前消息引擎不使用每连接 L1/L2/L3、adaptive batching 或 `TieredMemoryPool`。对应已发布 API 只作兼容保留，并带有 `[Obsolete]`。
- 队列事实通过 `RuntimeQueueMetrics` 的 `message-engine.shard` 实例报告；不要把旧 tiered monitoring DTO 当作当前指标源。

## 集群租约事实

- `InMemoryActorLeaseStore` 只用于单进程开发；多成员拓扑默认 fail closed。
- `RedisActorLeaseStore` 对每个 `(Hub, Key)` 使用带 TTL 的单个 Redis Hash，并以 Lua 原子执行激活、解析、续租和释放。
- 远程 Actor wire 携带 lease id；执行节点在业务反序列化前核对 owner、lease 与 TTL。
- 租约不替代持久化层事务、幂等和 fencing token 校验。

## Public API

- 公共 API 使用 `PublicAPI.Shipped.txt` 和 `PublicAPI.Unshipped.txt` 管理。
- 新增公共类型或成员后需要更新对应 Unshipped 文件。
- 删除或重命名公共 API 需要明确迁移路径，优先使用 `[Obsolete]` 分阶段替代。

## 术语

以下为快速索引；完整的 Hub/Service/Actor、Channel/Transport/Connection 分层定义见[包边界与核心术语](package-boundaries-and-terminology.md)。

| 术语 | 含义 |
| --- | --- |
| Hub | RPC 契约接口或服务调用面 |
| Actor | 带身份和状态的 Hub/Service 实例 |
| ServiceName | 服务或 Actor 类型名，用于注册和路由 |
| ServiceId | 具体 Actor 实例身份 |
| Receiver | 服务端推送到客户端的回调接口 |
| NodeId | 集群节点身份 |
| Backplane | 节点间广播/路由的共享通道 |
| L2 | 多节点 Actor 属主选择和路由 |
| L3 | 可选 Actor 状态迁移 |
