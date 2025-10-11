# Tasks: Complete Message Dispatch-Process-Response Pipeline

**输入**: 来自 `/specs/004-pulserpc-server/` 的设计文档
**前置条件**: plan.md (必须), research.md, data-model.md, contracts/, quickstart.md

---

## 执行流程（主流程）

```
1. 从功能目录加载 plan.md
   ✅ 提取: C# 11/.NET 9.0, System.Threading.Channels, MemoryPack, xUnit
2. 加载设计文档:
   ✅ data-model.md: 5 个实体提取完成
   ✅ contracts/: 3 个契约文件 (message-flow.yaml, service-handler.yaml, dispatcher-api.yaml)
   ✅ research.md: 8 项技术决策已记录
   ✅ quickstart.md: 7 个集成场景 + 3 个性能测试
3. 按类别生成任务:
   ✅ Setup: 4 个任务
   ✅ Tests: 18 个任务（契约测试 + 集成测试）
   ✅ Core: 36 个任务（实体 + 管道组件 + 基础设施）
   ✅ Observability: 4 个任务
   ✅ Configuration & DI: 4 个任务
   ✅ Performance: 5 个任务
   ✅ Polish: 9 个任务
4. 应用任务规则:
   ✅ [P] 标记用于并行安全任务（不同文件）
   ✅ TDD 排序强制（测试先于实现）
5. 顺序编号任务: T001-T080
6. 依赖关系已验证
7. 并行执行示例已提供
8. 校验任务完整性:
   ✅ 所有契约都有测试
   ✅ 所有实体都有模型任务
   ✅ 所有集成场景已覆盖
   ✅ 所有 FR 需求已映射
9. Return: SUCCESS (80 个任务准备执行)
```

---

## 格式: `[ID] [P?] 描述`

- **[P]**: 可并行（不同文件、无依赖）
- 描述中包含精确文件路径

---

## 阶段 0: Project Setup

- [X] **T001** 在 `src/PulseRPC.Server/` 中创建项目结构（Core/, Pipeline/, Abstractions/, Models/, Configuration/, Extensions/）
- [X] **T002** 配置项目文件：添加 MemoryPack, System.Threading.Channels, Microsoft.Extensions.DependencyInjection 依赖
- [X] **T003** [P] 在 `tests/PulseRPC.Server.Tests/` 中创建测试项目结构（Unit/, Integration/, Performance/）
- [X] **T004** [P] 配置测试项目：添加 xUnit, FluentAssertions, NSubstitute, BenchmarkDotNet 依赖

---

## Phase 1: Contract Tests (TDD) ⚠️ 必须在 Phase 2 之前完成

**关键**: 这些测试必须编写并且必须失败，然后才能开始任何实现

### Contract Tests - Message Flow
- [ ] **T005** [P] 在 `tests/PulseRPC.Server.Tests/Integration/Contract/MessageFlowContractTests.cs` 中编写消息接收阶段契约测试（验证解析、校验、错误处理）
- [ ] **T006** [P] 在 `tests/PulseRPC.Server.Tests/Integration/Contract/MessageFlowContractTests.cs` 中编写消息分发阶段契约测试（验证服务查找、优先级、队列）
- [ ] **T007** [P] 在 `tests/PulseRPC.Server.Tests/Integration/Contract/MessageFlowContractTests.cs` 中编写服务调用阶段契约测试（验证委托调用、超时、异常隔离）
- [ ] **T008** [P] 在 `tests/PulseRPC.Server.Tests/Integration/Contract/MessageFlowContractTests.cs` 中编写响应生成阶段契约测试（验证序列化、错误响应）
- [ ] **T009** [P] 在 `tests/PulseRPC.Server.Tests/Integration/Contract/MessageFlowContractTests.cs` 中编写响应传输阶段契约测试（验证批处理、重试、清理）

### Contract Tests - Service Handler API
- [ ] **T010** [P] 在 `tests/PulseRPC.Server.Tests/Unit/ServiceHandlerContractTests.cs` 中编写 IServiceHandler.InvokeAsync 契约测试（验证参数反序列化、方法调用、结果序列化）
- [ ] **T011** [P] 在 `tests/PulseRPC.Server.Tests/Unit/ServiceHandlerContractTests.cs` 中编写 IServiceHandler.GetMethodNames 契约测试（验证方法列表返回）
- [ ] **T012** [P] 在 `tests/PulseRPC.Server.Tests/Unit/ServiceHandlerContractTests.cs` 中编写 IServiceHandler 错误处理测试（方法未找到、反序列化失败、取消）

### Contract Tests - Dispatcher API
- [ ] **T013** [P] 在 `tests/PulseRPC.Server.Tests/Unit/DispatcherContractTests.cs` 中编写 IMessageDispatcher.StartAsync/StopAsync 契约测试（验证生命周期管理）
- [ ] **T014** [P] 在 `tests/PulseRPC.Server.Tests/Unit/DispatcherContractTests.cs` 中编写 IMessageDispatcher.DispatchMessageAsync 契约测试（验证路由、FIFO 顺序、背压）
- [ ] **T015** [P] 在 `tests/PulseRPC.Server.Tests/Unit/DispatcherContractTests.cs` 中编写 IMessageDispatcher.RegisterServiceHandler 契约测试（验证唯一性、线程安全）

### Integration Scenario Tests (from quickstart.md)
- [ ] **T016** [P] 在 `tests/PulseRPC.Server.Tests/Integration/Scenarios/NormalFlowTests.cs` 中编写 Scenario 1 测试（正常请求-响应流程，验证 P95 <5ms）
- [ ] **T017** [P] 在 `tests/PulseRPC.Server.Tests/Integration/Scenarios/ConcurrencyTests.cs` 中编写 Scenario 2 测试（5000 客户端并发，验证 50K req/s, P99 <10ms）
- [ ] **T018** [P] 在 `tests/PulseRPC.Server.Tests/Integration/Scenarios/ExceptionHandlingTests.cs` 中编写 Scenario 3 测试（服务方法抛出异常，验证错误响应和服务器继续运行）
- [ ] **T019** [P] 在 `tests/PulseRPC.Server.Tests/Integration/Scenarios/TimeoutTests.cs` 中编写 Scenario 4 测试（慢服务方法，验证超时和并发不阻塞）
- [ ] **T020** [P] 在 `tests/PulseRPC.Server.Tests/Integration/Scenarios/ParsingFailureTests.cs` 中编写 Scenario 5 测试（消息解析失败，验证协议错误响应）
- [ ] **T021** [P] 在 `tests/PulseRPC.Server.Tests/Integration/Scenarios/ConnectionLossTests.cs` 中编写 Scenario 6 测试（处理中连接丢失，验证资源清理）
- [ ] **T022** [P] 在 `tests/PulseRPC.Server.Tests/Integration/Scenarios/BackpressureTests.cs` 中编写 Scenario 7 测试（极端负载下背压，验证多级策略和恢复）

---

## Phase 2: Core Data Models (仅在测试失败后)

- [X] **T023** [P] 在 `src/PulseRPC.Server/Models/RpcMessage.cs` 中实现 RpcMessage 结构（包含 ProtocolVersion, MessageType, RequestId, ServiceName, MethodName, Payload, Metadata, ReceivedAt）
- [X] **T024** [P] 在 `src/PulseRPC.Server/Models/ServerConnection.cs` 中实现 ServerConnection 类（包含连接状态机、统计信息、生命周期管理）
- [X] **T025** [P] 在 `src/PulseRPC.Server/Models/ServiceRegistration.cs` 中实现 ServiceRegistration 类（包含服务元数据、编译方法调用器字典、超时策略）
- [X] **T026** [P] 在 `src/PulseRPC.Server/Models/RequestContext.cs` 中实现 RpcRequestContext 类（包含 RequestId, ClientId, ConnectionId, Metadata, CancellationToken, TraceContext）
- [X] **T027** [P] 在 `src/PulseRPC.Server/Models/ResponseEnvelope.cs` 中实现 ResponseEnvelope 结构（包含 IsSuccess, Payload, ExceptionDetails, DurationMs）
- [X] **T028** [P] 在 `src/PulseRPC.Server/Models/Enums.cs` 中实现所有枚举类型（MessageType, ConnectionState, ServiceState, MessagePriority, TransportType）

---

## Phase 3: Abstractions & Interfaces

- [ ] **T029** [P] 在 `src/PulseRPC.Server/Abstractions/IPulseHub.cs` 中定义用户服务接口（基接口，支持自定义服务实现）
- [ ] **T030** [P] 在 `src/PulseRPC.Server/Abstractions/IServerTransport.cs` 中定义传输层抽象（基于现有 ITransport, 扩展服务器特定功能）
- [ ] **T031** [P] 在 `src/PulseRPC.Server/Abstractions/IAuthenticationHandler.cs` 中定义可选认证钩子接口（支持 FR-051 到 FR-054）
- [ ] **T032** [P] 在 `src/PulseRPC.Server/Abstractions/IRequestContext.cs` 中定义请求上下文接口（暴露 RequestId, Metadata, CancellationToken）
- [ ] **T033** [P] 在 `src/PulseRPC.Server/Abstractions/IServiceHandler.cs` 中定义服务处理器接口（InvokeAsync, GetMethodNames）
- [ ] **T034** [P] 在 `src/PulseRPC.Server/Abstractions/IMessageDispatcher.cs` 中定义消息分发器接口（StartAsync, StopAsync, DispatchMessageAsync, RegisterServiceHandler）

---

## Phase 4: Pipeline Components (按阶段顺序实现)

### Stage 1: Message Reception
- [ ] **T035** 在 `src/PulseRPC.Server/Pipeline/MessageReceiver.cs` 中实现 MessageReceiver 类（从 ITransportChannel 接收网络字节流，处理 FR-001 到 FR-006）
- [ ] **T036** 在 `src/PulseRPC.Server/Pipeline/MessageParser.cs` 中实现 MessageParser 类（解析协议头、验证完整性、MemoryPack 反序列化）
- [ ] **T037** [P] 在 `tests/PulseRPC.Server.Tests/Unit/MessageReceiverTests.cs` 中编写 MessageReceiver 单元测试（缓冲不完整消息、超大消息处理）
- [ ] **T038** [P] 在 `tests/PulseRPC.Server.Tests/Unit/MessageParserTests.cs` 中编写 MessageParser 单元测试（协议版本不匹配、数据损坏处理）

### Stage 2: Message Dispatching
- [ ] **T039** 在 `src/PulseRPC.Server/Pipeline/MessageDispatcher.cs` 中实现 MessageDispatcher 类（实现 IMessageDispatcher, 服务查找、优先级队列、FIFO 顺序保证，处理 FR-007 到 FR-013）
- [ ] **T040** [P] 在 `tests/PulseRPC.Server.Tests/Unit/MessageDispatcherTests.cs` 中编写 MessageDispatcher 单元测试（服务未找到、队列满、优先级调度）

### Stage 3: Service Invocation
- [ ] **T041** 在 `src/PulseRPC.Server/Pipeline/CompiledServiceInvoker.cs` 中实现编译委托生成器（使用 Expression Trees 编译服务方法，一次性启动成本）
- [ ] **T042** 在 `src/PulseRPC.Server/Pipeline/ServiceInvoker.cs` 中实现 ServiceInvoker 类（调用编译委托、超时强制、异常捕获，处理 FR-014 到 FR-020）
- [ ] **T043** [P] 在 `tests/PulseRPC.Server.Tests/Unit/CompiledServiceInvokerTests.cs` 中编写表达式树编译测试（同步/异步方法、泛型参数、性能验证 <10ns）
- [ ] **T044** [P] 在 `tests/PulseRPC.Server.Tests/Unit/ServiceInvokerTests.cs` 中编写 ServiceInvoker 单元测试（超时取消、异常隔离、上下文传递）

### Stage 4: Response Generation
- [ ] **T045** 在 `src/PulseRPC.Server/Pipeline/ResponseBuilder.cs` 中实现 ResponseBuilder 类（序列化成功结果、创建错误响应、处理 FR-021 到 FR-026）
- [ ] **T046** 在 `src/PulseRPC.Server/Pipeline/ErrorResponseFactory.cs` 中实现结构化错误响应工厂（ExceptionData 序列化、堆栈跟踪清理）
- [ ] **T047** [P] 在 `tests/PulseRPC.Server.Tests/Unit/ResponseBuilderTests.cs` 中编写 ResponseBuilder 单元测试（序列化失败处理、RequestId 保留）
- [ ] **T048** [P] 在 `tests/PulseRPC.Server.Tests/Unit/ErrorResponseFactoryTests.cs` 中编写 ErrorResponseFactory 单元测试（异常类型保留、内部异常递归）

### Stage 5: Response Transmission
- [ ] **T049** 在 `src/PulseRPC.Server/Pipeline/ResponseTransmitter.cs` 中实现 ResponseTransmitter 类（Channel-based I/O 线程池、批处理、处理 FR-027 到 FR-031）
- [ ] **T050** 在 `src/PulseRPC.Server/Pipeline/ResponseBatcher.cs` 中实现响应批处理器（组合小响应、减少系统调用）
- [ ] **T051** [P] 在 `tests/PulseRPC.Server.Tests/Unit/ResponseTransmitterTests.cs` 中编写 ResponseTransmitter 单元测试（连接关闭处理、发送重试逻辑）
- [ ] **T052** [P] 在 `tests/PulseRPC.Server.Tests/Unit/ResponseBatcherTests.cs` 中编写 ResponseBatcher 单元测试（批处理延迟 <1ms、吞吐量改善验证）

---

## Phase 5: Core Infrastructure

- [ ] **T053** 在 `src/PulseRPC.Server/Core/ConnectionManager.cs` 中实现 ConnectionManager 类（连接生命周期跟踪、统计收集、清理）
- [ ] **T054** 在 `src/PulseRPC.Server/Core/ServiceRegistry.cs` 中实现 ServiceRegistry 类（线程安全服务注册、CompiledServiceInvoker 集成）
- [ ] **T055** 在 `src/PulseRPC.Server/Core/BackpressurePolicy.cs` 中实现多级背压策略（队列监控 → 节流 → 拒绝，处理 FR-039）
- [ ] **T056** 在 `src/PulseRPC.Server/Core/ServerHost.cs` 中实现 ServerHost 编排器（连接所有管道组件、启动/停止协调、健康检查）
- [ ] **T057** [P] 在 `tests/PulseRPC.Server.Tests/Unit/ConnectionManagerTests.cs` 中编写 ConnectionManager 单元测试（状态转换、资源泄漏检测）
- [ ] **T058** [P] 在 `tests/PulseRPC.Server.Tests/Unit/ServiceRegistryTests.cs` 中编写 ServiceRegistry 单元测试（重复服务名、并发注册）
- [ ] **T059** [P] 在 `tests/PulseRPC.Server.Tests/Unit/BackpressurePolicyTests.cs` 中编写 BackpressurePolicy 单元测试（三级触发、滞后恢复）

---

## Phase 6: Observability & Diagnostics

- [ ] **T060** [P] 在 `src/PulseRPC.Server/Observability/PipelineMetricsCollector.cs` 中实现指标收集器（处理 FR-055 到 FR-062：req/s, 错误率, P50/P75/P95/P99 延迟、活动连接、队列深度）
- [ ] **T061** [P] 在 `src/PulseRPC.Server/Observability/DistributedTracingIntegration.cs` 中实现分布式追踪（System.Diagnostics.Activity, W3C Trace Context 传播）
- [ ] **T062** [P] 在 `src/PulseRPC.Server/Observability/DiagnosticEndpoints.cs` 中实现诊断端点（/diagnostics/health, /diagnostics/metrics, /diagnostics/connections, /diagnostics/queue-stats）
- [ ] **T063** [P] 在 `tests/PulseRPC.Server.Tests/Unit/MetricsCollectorTests.cs` 中编写指标收集器单元测试（百分位数计算、计数器准确性）

---

## Phase 7: Configuration & DI

- [ ] **T064** [P] 在 `src/PulseRPC.Server/Configuration/ServerOptions.cs` 中实现服务器配置模型（处理 FR-063 到 FR-067：线程池大小、队列容量、超时值、传输参数）
- [ ] **T065** [P] 在 `src/PulseRPC.Server/Configuration/ServiceOptions.cs` 中实现服务级配置（每服务/每方法超时、速率限制、优先级）
- [ ] **T066** 在 `src/PulseRPC.Server/Extensions/DependencyInjection/ServerServiceCollectionExtensions.cs` 中实现 DI 扩展方法（AddPulseRpcServer, AddPulseService<T>）
- [ ] **T067** [P] 在 `tests/PulseRPC.Server.Tests/Unit/ServerOptionsTests.cs` 中编写配置验证测试（范围检查、默认值）

---

## Phase 8: Performance Validation

- [ ] **T068** [P] 在 `tests/PulseRPC.Server.Tests/Performance/ThroughputBenchmarks.cs` 中实现吞吐量基准测试（BenchmarkDotNet, 验证 FR-032: ≥100K req/s on 8-core）
- [ ] **T069** [P] 在 `tests/PulseRPC.Server.Tests/Performance/LatencyBenchmarks.cs` 中实现延迟基准测试（验证 FR-033/034: P95 <5ms, P99 <10ms at 50% load）
- [ ] **T070** [P] 在 `tests/PulseRPC.Server.Tests/Performance/ScalabilityBenchmarks.cs` 中实现可扩展性基准测试（验证 FR-035: 10K 并发连接）
- [ ] **T071** [P] 在 `tests/PulseRPC.Server.Tests/Performance/GCPressureBenchmarks.cs` 中实现 GC 压力基准测试（验证 FR-037: P99 GC 暂停 <10ms）
- [ ] **T072** [P] 在 `perf/BenchmarkApp/ServerBenchmarks/StressTest.cs` 中实现 72 小时压力测试（内存泄漏检测、性能退化监控）

---

## Phase 9: Integration & Polish

- [ ] **T073** 在 `tests/PulseRPC.Server.Tests/Integration/EndToEndPipelineTests.cs` 中实现端到端管道测试（使用真实传输层、完整消息流）
- [ ] **T074** 在 `samples/BasicUsage/BasicServerSample.cs` 中创建 quickstart 示例项目（验证所有集成场景）
- [ ] **T075** [P] 在 `tests/PulseRPC.Server.Tests/Integration/EdgeCaseTests.cs` 中编写边缘案例测试（微小消息、大消息、突发流量、内存耗尽、未注册服务）
- [ ] **T076** 运行所有测试并验证覆盖率 >90%（使用 coverlet 或 dotCover）
- [ ] **T077** 运行所有性能基准测试并验证所有目标达成
- [ ] **T078** 执行手动 quickstart 验证（按 `quickstart.md` 场景 1-7 手动测试）
- [ ] **T079** [P] 更新 `docs/PulseRPC-Server-Architecture.md` 文档（架构图、组件说明、配置指南）
- [ ] **T080** [P] 更新 `README.md` 添加服务器使用示例和快速开始指南

---

## 依赖关系

### 关键依赖链

- **Phase 1** (Tests) → 必须在所有实现之前完成（TDD 强制）
- **T023-T028** (Models) → 阻塞所有管道和基础设施任务
- **T029-T034** (Abstractions) → 阻塞实现类
- **T035-T052** (Pipeline) → 按阶段顺序（Reception → Dispatching → Invocation → Response → Transmission）
- **T053-T059** (Infrastructure) → 依赖 Models 和 Pipeline 组件
- **T056** (ServerHost) → 依赖所有 Pipeline 和 Infrastructure 组件
- **T066** (DI Extensions) → 依赖 ServerHost 和 ServiceRegistry
- **T073-T078** (Integration & Polish) → 依赖所有核心实现完成
- **Performance Tests (T068-T072)** → 可在实现后随时运行，但必须在功能完成前全部通过

### 并行执行组

- **Group 1 (Setup)**: T001, T002 并行，T003, T004 并行
- **Group 2 (Contract Tests)**: T005-T022 全部并行（不同测试文件）
- **Group 3 (Models)**: T023-T028 全部并行（不同模型文件）
- **Group 4 (Abstractions)**: T029-T034 全部并行（不同接口文件）
- **Group 5 (Performance Benchmarks)**: T068-T072 全部并行（不同基准文件）
- **Group 6 (Polish)**: T079-T080 并行（不同文档文件）

---

## 并行执行示例

### Example 1: Contract Tests (Phase 1)

```bash
# 同时启动所有契约测试任务（18 个测试类）
Task: "在 tests/PulseRPC.Server.Tests/Integration/Contract/MessageFlowContractTests.cs 中编写消息接收阶段契约测试"
Task: "在 tests/PulseRPC.Server.Tests/Integration/Contract/MessageFlowContractTests.cs 中编写消息分发阶段契约测试"
Task: "在 tests/PulseRPC.Server.Tests/Unit/ServiceHandlerContractTests.cs 中编写 IServiceHandler.InvokeAsync 契约测试"
# ... (all T005-T022 simultaneously)
```

### Example 2: Core Models (Phase 2)

```bash
# 同时启动所有数据模型任务（6 个模型文件）
Task: "在 src/PulseRPC.Server/Models/RpcMessage.cs 中实现 RpcMessage 结构"
Task: "在 src/PulseRPC.Server/Models/ServerConnection.cs 中实现 ServerConnection 类"
Task: "在 src/PulseRPC.Server/Models/ServiceRegistration.cs 中实现 ServiceRegistration 类"
# ... (all T023-T028 simultaneously)
```

### Example 3: Performance Benchmarks (Phase 8)

```bash
# 同时启动所有性能基准测试（5 个基准文件）
Task: "在 tests/PulseRPC.Server.Tests/Performance/ThroughputBenchmarks.cs 中实现吞吐量基准测试"
Task: "在 tests/PulseRPC.Server.Tests/Performance/LatencyBenchmarks.cs 中实现延迟基准测试"
Task: "在 tests/PulseRPC.Server.Tests/Performance/ScalabilityBenchmarks.cs 中实现可扩展性基准测试"
# ... (all T068-T072 simultaneously)
```

---

## 验证清单

### 契约覆盖

- [x] message-flow.yaml 的所有 5 个阶段都有对应测试 (T005-T009)
- [x] service-handler.yaml 的 IServiceHandler 接口都有测试 (T010-T012)
- [x] dispatcher-api.yaml 的 IMessageDispatcher 接口都有测试 (T013-T015)

### 实体覆盖

- [x] data-model.md 的 5 个核心实体都有模型任务 (T023-T027)
- [x] data-model.md 的 5 个枚举类型都有实现 (T028)

### 场景覆盖

- [x] quickstart.md 的 7 个集成场景都有测试 (T016-T022)
- [x] quickstart.md 的 3 个性能验证都有基准测试 (T068-T070)

### TDD 合规

- [x] 所有测试任务 (T005-T022) 在实现任务 (T023+) 之前
- [x] 每个实现组件都有对应单元测试

### 并行独立性

- [x] 标记 [P] 的任务修改不同文件
- [x] 管道阶段任务顺序执行（避免同一文件冲突）

### 功能需求覆盖

- [x] FR-001 到 FR-006 (Message Reception) → T035-T038
- [x] FR-007 到 FR-013 (Message Dispatching) → T039-T040
- [x] FR-014 到 FR-020 (Service Invocation) → T041-T044
- [x] FR-021 到 FR-026 (Response Generation) → T045-T048
- [x] FR-027 到 FR-031 (Message Transmission) → T049-T052
- [x] FR-032 到 FR-040 (Performance) → T068-T072
- [x] FR-041 到 FR-050 (Reliability) → T044, T053, T055, T057, T059
- [x] FR-051 到 FR-054 (Security/Authentication) → T031
- [x] FR-055 到 FR-062 (Observability) → T060-T063
- [x] FR-063 到 FR-067 (Configuration) → T064-T067
- [x] FR-068 到 FR-070 (Deployment) → T074 (sample project)

---

## 任务总数: 80

- **Setup**: 4 tasks (T001-T004)
- **Contract Tests**: 18 tasks (T005-T022)
- **Core Models**: 6 tasks (T023-T028)
- **Abstractions**: 6 tasks (T029-T034)
- **Pipeline Implementation**: 18 tasks (T035-T052)
- **Infrastructure**: 7 tasks (T053-T059)
- **Observability**: 4 tasks (T060-T063)
- **Configuration & DI**: 4 tasks (T064-T067)
- **Performance Validation**: 5 tasks (T068-T072)
- **Integration & Polish**: 8 tasks (T073-T080)

**Parallel Execution Potential**: 42 tasks marked [P] (52.5% parallelizable)

---

## Notes

- **TDD 强制**: Phase 1 的所有测试必须先失败，然后在 Phase 2+ 实现时逐步通过
- **宪法合规**: 所有任务设计符合 5 项宪法原则（性能优先、源生成、企业级可靠性、TDD、现代 .NET 标准）
- **性能目标**: 所有基准测试必须达到目标值（100K req/s, P95 <5ms, P99 <10ms）才能视为完成
- **提交策略**: 每完成一个任务提交一次，保持构建通过和测试绿色
- **避免**: 跳过测试、禁用 hook、批量提交未经验证的代码
