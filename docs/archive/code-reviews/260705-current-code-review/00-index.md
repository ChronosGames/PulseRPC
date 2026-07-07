# PulseRPC 当前代码全量架构评审索引

日期：2026-07-05  
范围：当前工作区 `PulseRPC.sln`、`src/`、`tests/`、`perf/BenchmarkApp` 与示例项目中的可构建代码。Unity `Library/PackageCache` 等第三方/生成缓存不作为设计问题来源。

## 成功标准

- 对当前实现做跨模块排查，不只评审文档或单个类。
- 每类问题必须给出代码证据、影响、根因和建议。
- 区分 P0/P1/P2/P3，形成可执行优化计划。
- 不在本轮修改产品代码，避免把“评审”混成“局部修复”。

## 文档拆分

1. [01-architecture-boundaries.md](./01-architecture-boundaries.md)  
   项目边界、依赖方向、包职责、全局注册与生成代码边界。

2. [02-api-contracts-and-naming.md](./02-api-contracts-and-naming.md)  
   公共接口契约、未实现/弱实现 API、命名语义与 API 治理。

3. [03-runtime-pipeline-and-transport.md](./03-runtime-pipeline-and-transport.md)  
   客户端运行时、服务端消息管线、传输层、服务发现与观测指标。

4. [04-source-generator-and-protocol.md](./04-source-generator-and-protocol.md)  
   Source Generator、协议号、响应序列化注册、生成类型冲突。

5. [05-roadmap-and-verification.md](./05-roadmap-and-verification.md)  
   分阶段优化计划、验收标准、测试矩阵与风险控制。

## 核心结论

| 优先级 | 结论 | 原因 |
| --- | --- | --- |
| P0 | 公开 API 与实际行为不一致，需要先收敛契约 | `IPulseServer`、`IPulseClientBuilder`、负载均衡、连接池、鉴权、广播过滤、指标等均存在“已公开但未兑现”的行为。 |
| P0 | 服务端连接接入路径存在明显运行时错误 | `PulseServer.ProcessNewConnectionAsync` 把 `IServerTransport` 强转为 `IServerChannel`，但真正的 channel 是 `_channelManager.AddChannel` 创建的返回值。 |
| P0 | 构建器传输配置被丢弃 | `PulseServerBuilder.AddTcpTransport/AddKcpTransport` 收集 `_transports`，`Build()` 调用 `AddPulseServer` 时没有写入 options。 |
| P1 | 包边界和命名空间边界混乱 | `PulseRPC.Abstractions` 包含实现类，且暴露 `PulseRPC.Shared`、`PulseRPC.Client`、`PulseRPC.Channels` 等命名空间；`PulseRPC.Shared` 又暴露 `PulseRPC.Abstractions.Transport.Batching`。 |
| P1 | `PulseRPC.Server` 直接依赖 `PulseRPC.Client` | 服务端包因集群节点链路复用客户端运行时，扩大依赖面并耦合客户端 API/源生成器约束。 |
| P1 | 服务端消息管线有多套抽象但实际路径未闭合 | `MessageDispatcher` 有队列和 worker，但 `MessageEngine` 直接调用 `DispatchAsync`；指标存在常量/占位值。 |
| P1 | Source Generator 输出类型和运行时库类型冲突 | `dotnet build` 出现大量 CS0436，说明生成代码与 `PulseRPC.Server` 中导入类型同名同命名空间。 |
| P2 | 命名语义重复影响维护和用户心智 | `IServiceHandler`、`ValidationResult`、`MessagePriority`、多种 State 类型和 Hub/Service/Actor 术语存在重叠。 |
| P2 | 部分可靠性语义只是本地语义 | 客户端取消不会发送 Cancel 帧，断连不立即完成 pending response，服务发现测试有时序不稳定。 |

## 验证基线

本轮执行过以下基线命令：

```powershell
dotnet build PulseRPC.sln --no-restore --verbosity minimal
dotnet test PulseRPC.sln --no-restore --verbosity quiet
dotnet test tests\PulseRPC.Infrastructure.Tests\PulseRPC.Infrastructure.Tests.csproj --no-restore --verbosity normal
```

结果摘要：

- `dotnet build`：退出码 0，但存在警告。主要为 `CS0436=56`、`CS0414=16`、`NU1902=4`、`NU1903=2`、`CS8002=2`、`CS1574=2`、`CS0649=2`。
- `dotnet test PulseRPC.sln`：退出码 1。解法内测试项目合计约 `233 passed / 1 failed / 21 skipped`。
- 全量测试日志中失败过 `DiscoveryClusterMembershipTests.BackendRemovingNode_TakesPrecedence_ClearsSuspicion`。
- 单独运行 `PulseRPC.Infrastructure.Tests` 时失败变为 `DiscoveryClusterMembershipTests.ProviderChanged_TriggersRefresh_AndRaisesChanged`，断言为 `changedFired >= 1` 但实际为 0，说明服务发现成员刷新/事件通知存在时序不稳定。
- `tests/PulseRPC.Server.Tests/Contracts` 中有 21 个 `Implementation pending` 跳过测试，集中在端到端消息流、`IServiceHandler`、`IMessageDispatcher` 契约。

## 审阅假设

- “全量排查”按当前解决方案和核心源码理解，不包含 Unity 缓存包、NuGet 包源码和历史文档逐行真实性校验。
- 本轮目标是制定计划，不主动重构。若后续进入修复，应按 P0 到 P3 逐步落地，并为每个行为补测试。
- 现有工作区已有大量未跟踪文档，本轮不判断其来源，也不修改它们。

