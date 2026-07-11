# 集群与路由

PulseRPC 集群能力围绕 `IPulseRouter`、`IClusterMembership`、`INodeEndpointResolver`、`INodeLink` 和 `IPulseBackplane` 组织。目标是让 Hub/Actor 调用可以在单节点、本地多实例和多节点之间保持统一寻址模型。

## 层级模型

- L0：本进程服务实例，直接调度。
- L1：本节点多个实例或多连接之间的路由。
- L2：多节点 Actor 属主选择、租约目录和节点间调用。严格的跨进程单一激活依赖共享租约存储；默认进程内存储不提供该保证。
- L3：可选状态迁移，通过 `IActorStateSnapshot` 和 `IActorStateTransport` 保留内存态。

## 成员发现

核心抽象在 `PulseRPC.Infrastructure`，后端实现包括：

- 静态成员配置
- Consul
- Etcd
- Kubernetes

启用动态发现时，后端扩展通常应在 `AddPulseClustering(...)` 之后调用，以覆盖默认静态注册。

成员发现只回答“有哪些节点”，不等于 Actor 租约协调。生产多节点还需要共享的 `IActorLeaseStore`，并提供原子 compare-and-set、TTL、owner 与 lease-id 校验。

## Gateway 路由

客户端可以通过生成的 `gatewayChannel.GetGatewayActor<IHub>(key)` 复用普通 Hub Stub。调用先进入 Gateway Front，再由 `IPulseRouter` 定位 Actor owner；远程 owner 经 `TransportBackedNodeLink`、内置 `TcpNodeTransport` 和 `IClusterInternalHub` 接收请求。生成的 keyed 路由在 Actor 继承 `PulseServiceBase` 时把调用提交到 mailbox。

后端若要向原客户端推送或发起反向 Ask，会使用 `GatewayVirtualChannel` 表示远程连接，再经 `IGatewayRelayHub` 回到持有真实连接的 Gateway。完整调用链和代码见[经 Gateway 调用 Actor](../guides/gateway-actors.md)。

## 认证边界

节点间连接使用独立的节点认证机制，例如共享密钥或证书认证。它不同于业务客户端认证，不应混用业务用户 token。

内置节点链路先认证物理连接，再协商 node wire 版本和能力。当前 wire v2 会传播外部调用者的 `ClaimsPrincipal`（包括多个 identity、重复 claim、issuer、value type 和 claim properties）、权限、角色及过期时间；业务 token 不会跨节点转发。接收端把调用者上下文恢复为 `ExternalUser`，并在反序列化业务参数前执行授权。节点若未协商 `ClaimsPrincipal` 能力，外部用户调用会 fail closed，不会静默降级为匿名 legacy 调用。

claims 快照建立在“已认证节点可代表其入口认证结果”的集群信任模型上。节点端口必须位于可信网络边界；`TcpNodeTransport` 本身不提供线路加密，生产部署必须使用私网加 mTLS service mesh/TLS 终止层，并配置证书节点认证或足够强的轮换密钥。Actor key 与 payload 中的 user id 都不是身份凭据。

## Node wire 与传输

`TcpNodeTransport` 为每个目标节点复用已认证连接，提供有界并发写与请求关联、连接/请求超时、断线清理和受冷却约束的重连。连接建立顺序固定为底层握手、调用方节点认证、wire/能力协商及响应节点凭据校验；业务帧只在双向身份检查成功后发送。生产传输要求 wire v2 的版本化信封、claims、lease fencing、Hub 协议归属、双向节点认证能力，缺少能力时拒绝建会话。V2 Send 使用执行完成 ACK，而不是把“写入 socket”误当成远端确认。

`TcpNodeTransportOptions.SecurityMode` 必须显式设置。生产只允许声明 `ExternalMutualTls`，表示节点端口已由外部 mTLS 层保护；`InsecureDevelopment` 只接受 loopback 端点。应用层节点凭据仍有有限有效窗口，不能替代 mTLS 的机密性、完整性与防中间人保护。

历史 `AskActor` / `SendActor` wire 默认关闭。滚动升级期间可暂时设置 `ClusterNodeWireOptions.AllowLegacyActorProtocol`，但 legacy 不携带 claims 或 lease id，不应作为长期生产配置。

## 可靠性边界

集群路由解决“消息发往哪里”，不自动提供业务强一致。需要 exactly-once 效果的业务仍需业务幂等、持久化、去重窗口和补偿策略。

默认 `IActorLeaseStore` 仍是供单进程开发使用的 `InMemoryActorLeaseStore`，但多成员拓扑会在解析 `IActorDirectory` 时 fail closed，除非测试显式设置 `AllowInMemoryStoreForMultiNode=true`。生产多节点应调用 `AddRedisActorLeases(...)`；`RedisActorLeaseStore` 以单键 Lua 脚本原子完成 owner/lease compare-and-set、TTL、续租和 compare-and-delete。也可以提供等价的 Etcd/数据库实现。

所有本地和远程候选都必须先取得共享租约；wire v2 携带 lease id，执行节点在反序列化和激活前核对当前 owner、lease id、TTL 与本节点身份，并在接纳远程调用后启动 owner 心跳。网络失败不会主动释放远端租约，接管方必须等待旧租约失效，避免把网络分区误当成节点退出。

租约 fencing 是“执行准入”保护，不会自动把业务状态写入变成 exactly-once。长时间处理、外部副作用和跨租约状态写入仍应使用业务幂等键、事务/outbox，必要时让持久化层同时校验 fencing token。框架也没有通用 durable Actor state store；业务应在 `OnStartingAsync`/`OnStoppingAsync` 与写路径中接入自己的仓储和崩溃恢复策略。

生成路由表把 canonical Hub 与协议号作为原子路由键；客户端、Gateway、节点入口及本地 keyed 路由均在 placement、反序列化和服务激活前校验 `(Hub, ProtocolId)`。协议号冲突和 canonical Hub 重名是构建错误。

同一入口顺序执行 `ClientFacingGate` 与完整授权描述：`[Authorize]`、`[AllowAnonymous]`、角色、权限、scope、自定义 policy、`[Internal]` 和 `[ExternalOnly]`。声明 policy 但未注册 `IPulseAuthorizationPolicyEvaluator` 时 fail closed。资源归属等依赖业务数据的授权仍应由业务代码执行。

仓库包含共享拓扑 `ThreeNodeTcpTopology`：外部用户先进入 A 的真实 `GatewayFrontHub`，再经真实 loopback TCP 到 B、C；C 会校验跨两条节点链路恢复的 claims、角色、权限与 token 剥离。故障测试覆盖 C 停机快速失败、无后台重复、成员隔离以及 TTL 后恢复；`cluster-three-hop` 基准复用完全相同的拓扑并报告吞吐量和 p50/p95/p99。它是回归与容量测试起点，不替代目标网络、Redis、TLS 和真实负载下的上线压测。

## 相关文档

- [架构总览](architecture.md)
- [Actor 模型](actor-model.md)
- [经 Gateway 调用 Actor](../guides/gateway-actors.md)
- [部署指南](../guides/deployment.md)
- [参考手册](../reference/index.md)
