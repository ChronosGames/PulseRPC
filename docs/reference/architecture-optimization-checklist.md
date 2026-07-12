# 架构优化清单

本清单记录当前源码已验证的优化状态。历史评审背景见 `docs/archive/code-reviews/260705-current-code-review/`；本页只描述当前事实。

## 已完成的高优先级收敛

### 六项 P0 验收

| P0 | 当前结果 | 回归证据 |
| --- | --- | --- |
| 连接接入 | 新连接事件发布真实 `IServerChannel`，处理器失败会移除 channel 并关闭 transport | `PulseServerLifecycleTests` |
| Builder 配置 | TCP/KCP 配置和 default 标记写入运行时 options | `PulseServerLifecycleTests.PulseServerBuilder_Build_MustApplyConfiguredTransports` |
| 公开 API 契约 | Channel、服务清单、指标、过滤广播返回真实数据；未支持配置与策略 fail-fast | `PulseServerLifecycleTests`、`PulseClientBuilderContractTests` |
| 客户端状态 | 状态事件在赋值前捕获 previous state | `PulseClientLifecycleTests` |
| Discovery 稳定性 | watch/refresh/changed 竞态收敛，Infrastructure 全套连续 20 轮通过 | `DiscoveryClusterMembershipTests` + CI stress gate |
| 跨端协议 | transport wire v3、MessageHeader wire v2、协议号/重载、恶意 legacy chunk 与版本不匹配均明确验证或拒绝 | `MessageHeaderWireVersionTests`、`WireContractConsistencyTests`、`TcpLegacyChunkRejectionTests`、`TransportWireCodecTests` |

- [x] 跨端 wire 契约：`MessageHeader` 使用显式 wire v2，旧帧按破坏性升级策略拒绝；客户端/服务端协议号、重载和序列化契约有一致性测试。
- [x] 连接接入契约：服务端连接事件使用真实 `IServerChannel`，停机会等待创建失败的连接回滚和已接受的 Pong 发送；builder 传输选项与客户端 previous/current 状态事件均有行为测试。
- [x] 未实现公开能力不再静默退化：鉴权/连接池/重试配置会明确失败；wire 压缩/加密按 v3 能力协商严格启用；WeightedRoundRobin/ConsistentHash 已具备强类型输入契约，非法动态权重或缺失 sticky key 时同样 fail-fast；管理面返回真实数据或明确 unavailable。
- [x] Actor 生命周期：同一地址的创建、发布、移除与迁移串行化；启动失败不会发布半初始化实例；lease 心跳、迁移清理和回收均有并发回归测试。
- [x] Service/Hub 工厂生命周期：公开 `AddPulseHubFactory` 路径同样按 key 串行，统一调用 `StartAsync` / `StopAsync` / `DisposeAsync`，创建中实例不可见且失败实例必释放。
- [x] 传输生命周期：TCP 发送等待底层写完成，后台 handler、重连、延迟关闭和节点接收观察任务可追踪；TCP/KCP 自动重连、KCP 延迟握手和来源端点校验有回归测试。
- [x] 背压与批处理：已接受发送都有确定 completion；释放会排空队列，`DropNewest` 确定返回 false，`Reject` 明确抛错，无法可靠通知旧请求的 `DropOldest` fail-fast。
- [x] 端到端取消：客户端取消等待时发送 `Cancel` frame，服务端按 `(ConnectionId, MessageId)` 取消在途 dispatch token；取消帧发送任务纳入 Channel 释放等待。
- [x] Receiver 重载：完整 wire 签名分配独立协议号；指定连接注册会安装真实 Dispatcher，不再返回 no-op token。
- [x] Source Generator：客户端和服务端使用增量生成，未变化的 Compilation 二次运行必须复用输出；生成代码继续接受 C# 9 语法门禁。
- [x] API/ABI 与发布治理：九个运行时包均启用 PublicApiAnalyzer；三个多目标包分别冻结 `netstandard2.1` / `net10.0` ABI，十二组 Shipped 基线共记录 7,781 个签名；XML 与 NuGet README 警告作为错误；CI 拒绝空 Shipped 基线并验证 Debug/Release、测试、样例和包内容。
- [x] Discovery 稳定性：Infrastructure 测试连续 20 轮通过，作为 CI 固定门禁。

## 后续优化队列

### P1：下一阶段应做

- [x] 完成真正的 abortive disconnect；`graceful=false` 会取消重连/后台任务并立即关闭 TCP/KCP socket，不排空待发队列。
- [x] KCP 故障注入覆盖丢包、乱序和 30 秒模拟断网；无认证 token 的 NAT rebinding 会 fail-closed，防止 conversation ID 被新端点劫持。
- [x] CI `run-tests` 使用带健康检查的真实 Redis 容器，Actor lease 集成测试在 CI 中不跳过。
- [x] 传输与 Actor 高并发基准覆盖批处理背压、热实例查找、mailbox 背压和并发创建/移除；`architecture-baseline` 导出带环境/workload 元数据的 JSON，支持同机延迟、分配、吞吐和背压等待回归比较，CI 固定运行 smoke。
- [x] `ServiceFactoryExample` 已重写为当前 `AddPulseService` / `IServiceAccessor` API；`JsonTranscoding` 已改为可构建的显式 ASP.NET JSON 网关样例。

### P2：协议/API 演进

- [x] transport wire v3 为 TCP/KCP 定义必需能力协商、显式帧标志、Brotli 阈值、AES-256-GCM envelope、方向密钥/序号、会话固定 key id 与旧 key 轮换窗口；能力不一致、缺失加密标志、篡改、重放和未知 key 均 fail-closed，并有真实 TCP/KCP 双向回归测试。
- [x] WeightedRoundRobin 通过每次选择调用的 `IConnectionWeightProvider` 获取动态正权重并执行平滑加权轮询；ConsistentHash 通过 `ServiceProxyOptions.StickyKey` / `LoadBalancingContext` 使用确定性虚拟节点环，缺 key、非法权重、重复连接 ID 或上下文丢失均 fail-fast。
- [x] `PulseRPC.Analyzers` 以 `PRPC2001`–`PRPC2003` 禁止 Abstractions 反向依赖、历史错位命名空间继续扩张及新增公开实现型类型；既有 Shipped API 仅作兼容豁免，并以正式术语文档统一 Hub/Service/Actor 与 Channel/Transport/Connection。
- [x] 延迟统计使用固定内存、并发累计且可合并的 histogram，不再只保留最近 100–1024 个样本；Engine、分层处理器、自适应调度和 Pipeline 共享相同分桶语义，Prometheus 导出标准 bucket/sum/count。所有实际有界运行时队列注册真实 capacity/depth，并提供 saturation、高水位、饱和事件、入队等待和拒绝计数；诊断端点不再返回伪造的 L1/L2/L3 `null` 容量。

## 固定验证门禁

```bash
dotnet build PulseRPC.sln -c Release --warnaserror
dotnet test PulseRPC.sln -c Release --no-build --no-restore
dotnet pack PulseRPC.Packaging.slnf -c Release --warnaserror
```

此外，维护中样例必须独立 `dotnet build -c Release --warnaserror`，Infrastructure.Tests 必须连续运行 20 次。
