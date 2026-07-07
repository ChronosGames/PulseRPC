# 05 优化计划与验证矩阵

## 先后顺序

当前不建议先做大规模重构。更稳的顺序是：

1. 修复公开 API 明显违约和接入路径运行时错误。
2. 补齐测试，让当前真实行为可验证。
3. 收敛架构边界和命名空间。
4. 最后再做传输性能、队列调度和包拆分。

## P0：契约违约与运行时错误

### P0-1 修复服务端新连接事件对象

范围：

- `src/PulseRPC.Server/PulseServer.cs`

动作：

- 使用 `_channelManager.AddChannel(e.Transport)` 的返回值触发 `ClientConnected`。
- 失败时确保已注册 channel 被移除，避免半注册。

验收：

- 新增测试：模拟 `ServerConnectionEventArgs`，验证 `ClientConnectedEventArgs.Channel` 是 `IServerChannel`。
- 新增测试：连接处理失败时 transport 被关闭且 channel manager 不残留。

原因：

- 当前实现把 `IServerTransport` 强转 `IServerChannel`，核心连接路径会异常。

### P0-2 修复 `PulseServerBuilder` 丢弃传输配置

范围：

- `src/PulseRPC.Server/Extensions/PulseServerServiceCollectionExtensions.cs`

动作：

- `Build()` 把 `_transports` 写入 `PulseServerOptions.Transports`。
- 删除或整理旧注释实现。

验收：

- 新增测试：builder 添加 TCP/KCP 后，DI 中的 options 包含对应配置。
- 新增测试：默认 transport 标记保留。

原因：

- 用户显式调用 `AddTcpTransport` 但运行时无效，是高优先级配置违约。

### P0-3 对未实现公开 API 做显式处理

范围：

- `IPulseServer.GetChannel/GetAllChannels/GetRegisteredServices/GetPerformanceMetrics/BroadcastAsync(filter)`
- `IPulseClientBuilder` 鉴权、连接池、重试、负载均衡 options
- 客户端 `graceful` 参数

动作：

- 能快速实现的补实现。
- 不能实现的改为 `[Obsolete]`、抛 `NotSupportedException` 或从接口拆出。
- 文档明确当前状态，不静默返回 null/空/0。

验收：

- 所有公开 API 至少有一个行为测试。
- 不允许新增“返回空但 TODO”的 public API。

原因：

- 框架类库的核心价值是契约可信。空实现比没有 API 更危险。

### P0-4 修复客户端状态事件

范围：

- `src/PulseRPC.Client/PulseClient.cs`

动作：

- 在修改 `_state` 之前捕获 previous state。

验收：

- 新增测试：`InitializeAsync`、`StopAsync` 事件序列符合预期。

原因：

- 当前 `StopAsync` 事件 previous/current 都可能表达为 Stopping，破坏生命周期语义。

### P0-5 处理 DiscoveryClusterMembership 测试不稳定

范围：

- `src/PulseRPC.Infrastructure/Discovery/DiscoveryClusterMembership.cs`
- `tests/PulseRPC.Infrastructure.Tests/DiscoveryClusterMembershipTests.cs`

动作：

- 先增强测试诊断：refresh 次数、事件次数、snapshot 变化日志。
- 明确 `Changed` 事件语义。
- 修复 watch signal 到 refresh/raise changed 的竞态。

验收：

- `dotnet test tests\PulseRPC.Infrastructure.Tests\PulseRPC.Infrastructure.Tests.csproj --no-restore` 连续 20 次通过。

原因：

- 服务发现是集群路由基础；成员视图事件不稳定会导致一致性哈希环和路由缓存不稳定。

## P1：主路径闭合与架构边界

### P1-1 明确并闭合服务端真实消息路径

动作：

- 决定 `MessageDispatcher` 是直接 router 还是队列 dispatcher。
- 若保留队列，`MessageEngine` 必须启动 dispatcher 并进入队列。
- 若不保留队列，删除或 internal 化未使用 worker/channel。

验收：

- 启用 `tests/PulseRPC.Server.Tests/Contracts/IMessageDispatcherContractTests.cs` 中的跳过测试，并全部通过。
- 端到端请求路径测试覆盖 Request/Response、OneWay、Error、Cancel。

原因：

- 当前“高性能管线”概念多，但主路径不清晰，优化无从验证。

### P1-2 统一响应序列化失败语义

动作：

- 启动时验证 routing table 与 response serializer registry 完整匹配。
- 序列化失败时发送 Error frame，不让客户端等超时。

验收：

- 测试：缺少 serializer 时服务端启动失败或请求立即收到错误。
- 测试：serializer 抛异常时客户端收到明确错误码。

原因：

- 业务执行完成但响应丢失，是 RPC 框架最难排查的故障之一。

### P1-3 拆分 Server -> Client 依赖

动作：

- 抽出节点间出站链路最小抽象和实现。
- `PulseRPC.Server` 不再引用 `PulseRPC.Client`。

验收：

- `dotnet list src\PulseRPC.Server\PulseRPC.Server.csproj reference` 不包含 `PulseRPC.Client`。
- 集群 `Ask/Send` 测试通过。

原因：

- Server 和 Client 应是同层产品包，不应互相依赖。

### P1-4 修复 Source Generator 类型冲突

动作：

- 调整生成类型命名空间或移除运行时库内同名生成类型。
- 服务端生成器改为 `netstandard2.0`。

验收：

- `dotnet build PulseRPC.sln --no-restore --verbosity minimal` 不再出现 `CS0436`。
- 生成器加载测试通过。

原因：

- CS0436 说明用户项目编译期已有类型冲突，即使当前可通过，也会污染使用体验。

### P1-5 让管理面 API 返回真实数据

动作：

- `GetRegisteredServices` 基于生成 manifest 或 service manager metadata。
- `GetPerformanceMetrics` 只返回真实计数/延迟/吞吐。
- `BroadcastAsync(filter)` 实现 filter。

验收：

- 管理 API 单元测试和一个集成测试覆盖。

原因：

- 管理 API 是生产诊断入口，不能返回占位数据。

## P2：命名、API 治理和协议一致性

### P2-1 命名去重与术语表

动作：

- 制定术语表：Hub、Service、Actor、Channel、Transport、Connection。
- 重命名或废弃重复类型：`IServiceHandler`、`ValidationResult`、`MessagePriority`。

验收：

- 新增正式术语文档到现有文档目录。
- 新 API 不再引入重复短名。

原因：

- 命名是接口设计的一部分。重复名称会把复杂度转嫁给用户。

### P2-2 包边界治理

动作：

- 禁止 Abstractions 新增实现型类型。
- Shared 中不再新增 `PulseRPC.Abstractions.*` 命名空间。
- 梳理 public/internal surface。

验收：

- 增加边界检查脚本或 analyzer。
- 所有发布包都有 `PublicAPI.*.txt` 或等价 API baseline。

原因：

- 包边界不清会放大所有后续重构成本。

### P2-3 协议注释和 legacy path 清理

动作：

- 删除“ProtocolId=0 表示方法名路径”的过期注释，或真正实现 legacy path。
- 统一 Error code、Cancel、Deadline 语义。

验收：

- 协议文档与代码注释一致。
- 协议兼容测试覆盖旧/新路径。

原因：

- 协议层注释不准确会导致生成器和运行时修复方向错误。

## P3：性能与长期演进

### P3-1 传输层内存与生命周期优化

动作：

- TCP 接收 memory lifetime 文档化或改为 owned buffer。
- KCP 接收使用池化。
- `TcpTransport/KcpTransport.Id` 改为 abstract 或构造注入。

验收：

- microbenchmark 显示分配下降。
- 压测无内存生命周期错误。

原因：

- 高性能 RPC 框架必须先保证 buffer ownership 清晰，再谈零拷贝。

### P3-2 端到端取消与 graceful close

动作：

- 实现 Cancel frame。
- disconnect 时 fail pending responses。
- graceful close 区分 drain/abort。

验收：

- 长耗时请求取消测试。
- 断线 fail-fast 测试。

原因：

- 可靠性语义不能只在本地 Task 层成立。

### P3-3 依赖与安全警告治理

动作：

- 处理 `NU1902/NU1903`：`KubernetesClient`、`SharpCompress`、`Snappier`。
- 确认 `CS8002` KubernetesClient 无强名称是否可接受。

验收：

- `dotnet build` 不出现安全漏洞 warning，或有明确抑制理由。

原因：

- 企业级框架需要可解释的依赖风险。

## 验证矩阵

| 领域 | 命令/测试 | 通过标准 |
| --- | --- | --- |
| 构建 | `dotnet build PulseRPC.sln --no-restore --verbosity minimal` | 退出码 0；P1 后无 CS0436；安全 warning 有处理结论 |
| 解法测试 | `dotnet test PulseRPC.sln --no-restore` | 退出码 0；无非预期跳过 |
| 基础设施 | `dotnet test tests\PulseRPC.Infrastructure.Tests\PulseRPC.Infrastructure.Tests.csproj --no-restore` | 连续 20 次通过 |
| 服务端契约 | 启用 `tests/PulseRPC.Server.Tests/Contracts/*` | Request/Response、OneWay、Error、Cancel、Dispatcher、Handler 契约通过 |
| 客户端配置 | 新增 Builder 行为测试 | 鉴权、重试、连接池、负载均衡 options 要么生效，要么明确不支持 |
| 源生成器 | SourceGenerator tests + sample build | 无生成类型冲突；协议号冲突诊断稳定 |
| 包边界 | 边界检查脚本/analyzer | Abstractions 不新增实现型 public 类型；Server 不依赖 Client |

## 不建议的做法

- 不建议先做大包拆分。当前 P0 行为问题未修复，拆包会放大验证成本。
- 不建议继续新增配置项。已有配置项未完全生效，应先收敛。
- 不建议用更多抽象覆盖未实现行为。先让 public API 真实，再抽象复用。
- 不建议优化不在主路径上的队列或调度器。先确认真实请求路径。
