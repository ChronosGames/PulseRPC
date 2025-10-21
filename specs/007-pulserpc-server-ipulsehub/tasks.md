# 任务列表：服务线程调度与灾难隔离

**输入**: 来自 `/specs/007-pulserpc-server-ipulsehub/` 的设计文档
**前置条件**: plan.md (必需), spec.md (必需), research.md, data-model.md, contracts/

**组织方式**: 任务按用户故事分组，以实现每个故事的独立实施和测试

## 格式说明：`[ID] [P?] [Story] 描述`
- **[P]**: 可并行执行（不同文件，无依赖关系）
- **[Story]**: 任务所属的用户故事（例如 US1, US2, US3）
- 描述中包含具体文件路径

## 路径约定
- PulseRPC 项目采用单一代码库结构
- 主要路径：`src/PulseRPC.Server/`, `tests/PulseRPC.Server.Tests/`
- 根据 plan.md 中的项目结构调整路径

---

## Phase 1: 项目设置（共享基础设施）

**目的**: 项目初始化和基础结构准备

- [ ] **T001** 更新 PublicAPI.Unshipped.txt 添加新的公共接口（IPulseService, IServiceScheduler, HealthState 等）
- [ ] **T002** [P] 配置 MemoryPack 源生成器以支持新的可序列化类型
- [ ] **T003** [P] 更新项目文档（CLAUDE.md）记录新的调度机制

**检查点**: 项目环境配置完成

---

## Phase 2: 基础设施（阻塞性前置条件）

**目的**: 核心基础设施，所有用户故事实施前必须完成

**⚠️ 关键**: 在此阶段完成前，不能开始任何用户故事的工作

- [X] **T004** [P] 实现 ServiceSchedulingKey 值对象（`src/PulseRPC.Server/Scheduling/ServiceSchedulingKey.cs`）
  - 包含 ServiceName, ServiceId 属性
  - 实现 IEquatable<ServiceSchedulingKey>
  - 预计算 xxHash64 哈希值
  - 提供 == 和 != 运算符重载

- [X] **T005** [P] 实现 HealthState 枚举（`src/PulseRPC.Server/Models/HealthState.cs`）
  - 定义 Healthy, Isolated, CoolingDown, ProbeAllowed 四种状态
  - 添加 XML 文档注释

- [X] **T006** [P] 实现 ServiceInstanceHealth 类（`src/PulseRPC.Server/Models/ServiceInstanceHealth.cs`）
  - 包含 Key, State, TotalRequests, SuccessfulRequests, ConsecutiveTimeouts 等属性
  - 实现 SuccessRate 计算属性
  - 实现 CanAcceptRequests 属性

- [X] **T007** [P] 实现 ThreadAffinity 类（`src/PulseRPC.Server/Models/ThreadAffinity.cs`）
  - 包含 Key, AssignedThreadId, CreatedAtUtc, LastAccessUtc 属性
  - 实现 IdleDuration 和 IsIdle 方法

- [X] **T008** [P] 创建配置类 ServiceSchedulingOptions（`src/PulseRPC.Server/Configuration/ServiceSchedulingOptions.cs`）
  - WorkerThreadCount, IdleInstanceTimeout, VirtualNodesPerThread 属性
  - Validate() 方法验证配置有效性

- [X] **T009** [P] 创建配置类 HealthMonitorOptions（`src/PulseRPC.Server/Configuration/HealthMonitorOptions.cs`）
  - FailureThreshold, CoolingPeriod, ProbeRequestLimit, ProbeSuccessThreshold 属性
  - Validate() 方法验证配置有效性

- [X] **T010** [P] 创建 ServiceIdValidator 验证类（`src/PulseRPC.Server/Validation/ServiceIdValidator.cs`）
  - Validate(string serviceId) 静态方法
  - 检查长度限制（1000 字符）
  - 正则验证（仅允许字母、数字、连字符、下划线、冒号）

**检查点**: 基础设施就绪 - 用户故事实施现在可以并行开始

---

## Phase 3: 用户故事 1 - 服务实例线程亲和性 (优先级: P1) 🎯 MVP

**目标**: 实现基于 ServiceId 的线程亲和性，确保相同服务实例的所有请求在专用线程上顺序执行

**独立测试**: 部署多个服务实例，发送带有不同 ServiceId 的并发请求，验证相同 ServiceId 的请求在同一线程处理，不同 ServiceId 在不同线程处理

### US1 测试任务

- [ ] **T011** [P] [US1] 单元测试 - ServiceSchedulingKey 哈希一致性（`tests/PulseRPC.Server.Tests/Unit/ServiceSchedulingKeyTests.cs`）
  - 验证相同 ServiceName+ServiceId 生成相同哈希
  - 验证不同组合生成不同哈希
  - 验证 Equals 和 GetHashCode 正确性

- [ ] **T012** [P] [US1] 单元测试 - ConsistentHashRing 分布均匀性（`tests/PulseRPC.Server.Tests/Unit/ConsistentHashRingTests.cs`）
  - 验证 1000-10000 个 ServiceId 的分布标准差 <5%
  - 验证虚拟节点数量影响
  - 验证线程映射稳定性

### US1 实施任务

- [X] **T013** [US1] 定义 IPulseService 公共接口（`src/PulseRPC.Server/Abstractions/IPulseService.cs`）
  - 添加 ServiceName 属性（只读 string）
  - 添加 ServiceId 属性（只读 string）
  - 添加详细 XML 文档注释和示例

- [X] **T014** [US1] 实现 ConsistentHashRing 一致性哈希环（`src/PulseRPC.Server/Scheduling/ConsistentHashRing.cs`）
  - 构造函数初始化虚拟节点（默认 150/线程）
  - 使用 System.IO.Hashing.XxHash64 计算哈希
  - GetThread(string serviceId) 方法查找目标线程
  - 使用 SortedDictionary<ulong, int> 存储环

- [X] **T015** [US1] 实现 ThreadAffinityManager 线程亲和性管理器（`src/PulseRPC.Server/Scheduling/ThreadAffinityManager.cs`）
  - 使用 ConcurrentDictionary<ServiceSchedulingKey, ThreadAffinity> 存储映射
  - GetOrCreateAffinity(ServiceSchedulingKey) 方法
  - 定时清理空闲实例（Timer，1 分钟间隔）
  - UpdateLastAccess(ServiceSchedulingKey) 方法

- [ ] **T016** [US1] 扩展 ServiceThreadScheduler 支持 IPulseService（`src/PulseRPC.Server/Scheduling/ServiceThreadScheduler.cs`）
  - 集成 ConsistentHashRing
  - 集成 ThreadAffinityManager
  - ScheduleAsync 方法根据 ServiceSchedulingKey 路由到专用线程
  - 向后兼容非 IPulseService 的服务（使用默认调度）

- [ ] **T017** [US1] 扩展 MessageDispatcher 检测 IPulseService（`src/PulseRPC.Server/Pipeline/MessageDispatcher.cs`）
  - DispatchMessageAsync 检查服务是否实现 IPulseService
  - 如果实现，提取 ServiceName 和 ServiceId 构建 ServiceSchedulingKey
  - 调用 ServiceThreadScheduler.ScheduleAsync 进行调度

- [ ] **T018** [P] [US1] 集成测试 - 单线程亲和性（`tests/PulseRPC.Server.Tests/Integration/IPulseServiceSchedulingTests.cs`）
  - 创建测试服务实现 IPulseHub + IPulseService
  - 发送 100 个并发请求到同一 ServiceId
  - 验证所有请求在同一线程 ID 执行（通过 Thread.CurrentThread.ManagedThreadId）

- [ ] **T019** [P] [US1] 集成测试 - 多实例并发（`tests/PulseRPC.Server.Tests/Integration/IPulseServiceSchedulingTests.cs`）
  - 创建 1000 个不同 ServiceId 的并发请求
  - 验证请求分布到 16 个工作线程
  - 验证负载均衡（每个线程处理约 62.5 个实例，偏差 <10%）

- [ ] **T020** [P] [US1] 契约测试 - IPulseService 接口契约（`tests/PulseRPC.Server.Tests/Contract/IPulseServiceContractTests.cs`）
  - 验证 ServiceName 和 ServiceId 属性可访问
  - 验证属性不可变（只读）

**检查点**: 此时用户故事 1 应完全功能且可独立测试

---

## Phase 4: 用户故事 2 - 服务实例灾难隔离 (优先级: P1)

**目标**: 实现故障服务实例的自动隔离与恢复，防止单个实例故障影响其他实例

**独立测试**: 向一个服务实例注入故障（无限循环），验证：(1) 该实例停止响应，(2) 其他实例正常处理请求，(3) 系统检测并隔离故障实例

### US2 测试任务

- [ ] **T021** [P] [US2] 单元测试 - CircuitBreakerPolicy 状态转换（`tests/PulseRPC.Server.Tests/Unit/CircuitBreakerPolicyTests.cs`）
  - 验证 Healthy → Isolated 转换（3 次超时）
  - 验证 Isolated → CoolingDown → ProbeAllowed 转换（冷却期结束）
  - 验证 ProbeAllowed → Healthy 转换（3/5 成功）
  - 验证 ProbeAllowed → Isolated 转换（探测失败）

- [ ] **T022** [P] [US2] 单元测试 - ServiceInstanceHealthMonitor 健康跟踪（`tests/PulseRPC.Server.Tests/Unit/ServiceInstanceHealthMonitorTests.cs`）
  - 验证请求成功时更新 SuccessfulRequests
  - 验证请求失败时更新 ConsecutiveTimeouts
  - 验证冷却期过期时间计算

### US2 实施任务

- [X] **T023** [US2] 实现 CircuitBreakerPolicy 熔断器策略（`src/PulseRPC.Server/Scheduling/CircuitBreakerPolicy.cs`）
  - EvaluateTransition(ServiceInstanceHealth, bool requestSucceeded, DateTime now) 方法
  - 实现完整的 4 状态转换逻辑（参考 research.md）
  - 集成 HealthMonitorOptions 配置

- [X] **T024** [US2] 实现 ServiceInstanceHealthMonitor 健康监控器（`src/PulseRPC.Server/Scheduling/ServiceInstanceHealthMonitor.cs`）
  - 使用 ConcurrentDictionary<ServiceSchedulingKey, ServiceInstanceHealth> 存储健康状态
  - RecordRequestResult(ServiceSchedulingKey, bool success, DateTime timestamp) 方法
  - GetHealth(ServiceSchedulingKey) 方法
  - CanAcceptRequest(ServiceSchedulingKey) 方法（检查 HealthState）
  - 集成 CircuitBreakerPolicy 进行状态转换

- [ ] **T025** [US2] 扩展 ServiceInvoker 集成健康监控（`src/PulseRPC.Server/Pipeline/ServiceInvoker.cs`）
  - InvokeAsync 方法调用前检查 ServiceInstanceHealthMonitor.CanAcceptRequest
  - 如果 HealthState 为 Isolated，返回 "Service Unavailable" 错误
  - 调用后记录结果到 ServiceInstanceHealthMonitor.RecordRequestResult
  - 传播 ServiceSchedulingKey 上下文

- [ ] **T026** [US2] 实现定时冷却期检查器（`src/PulseRPC.Server/Scheduling/CoolingPeriodChecker.cs`）
  - 使用 Timer 每 10 秒扫描一次 ServiceInstanceHealth
  - 检测冷却期过期的实例，触发状态转换到 CoolingDown
  - 集成到 ServiceThreadScheduler 生命周期

- [ ] **T027** [P] [US2] 集成测试 - 故障隔离（`tests/PulseRPC.Server.Tests/Integration/DisasterIsolationTests.cs`）
  - 创建测试服务，模拟超时故障（Thread.Sleep 超过超时阈值）
  - 发送 3 个请求触发隔离
  - 验证第 4 个请求被拒绝（"Service Unavailable"）
  - 验证其他 ServiceId 的请求正常处理（延迟 <5ms）

- [ ] **T028** [P] [US2] 集成测试 - 自动恢复（`tests/PulseRPC.Server.Tests/Integration/DisasterIsolationTests.cs`）
  - 触发服务实例隔离
  - 等待冷却期（1 分钟 + 缓冲时间）
  - 发送探测请求（5 次，其中 3 次成功）
  - 验证实例恢复到 Healthy 状态
  - 验证后续请求正常处理

**检查点**: 此时用户故事 1 和 2 应都能独立工作

---

## Phase 5: 用户故事 3 - IPulseHub 向后兼容 (优先级: P2)

**目标**: 确保现有 IPulseHub-only 服务无需修改即可继续工作，支持渐进式迁移

**独立测试**: 部署遗留 IPulseHub 服务与新 IPulseService 服务并存，验证两者都能正确工作

### US3 测试任务

- [ ] **T029** [P] [US3] 集成测试 - IPulseHub-only 服务兼容性（`tests/PulseRPC.Server.Tests/Integration/BackwardCompatibilityTests.cs`）
  - 创建仅实现 IPulseHub 的测试服务
  - 验证服务正常注册和调用
  - 验证使用默认线程池行为（非专用线程）

- [ ] **T030** [P] [US3] 集成测试 - 混合模式（`tests/PulseRPC.Server.Tests/Integration/BackwardCompatibilityTests.cs`）
  - 同时部署 50 个 IPulseHub-only 服务和 10 个 IPulseService 服务
  - 验证所有服务成功注册
  - 并发调用两种服务，验证无错误
  - 验证 IPulseService 服务使用专用线程，IPulseHub-only 服务使用线程池

- [ ] **T031** [P] [US3] 契约测试 - 接口兼容性（`tests/PulseRPC.Server.Tests/Contract/IPulseServiceContractTests.cs`）
  - 验证 IPulseService 可与 IPulseHub 共同实现
  - 验证服务方法签名不受影响

### US3 实施任务

- [ ] **T032** [US3] 在 MessageDispatcher 实现服务类型检测逻辑（`src/PulseRPC.Server/Pipeline/MessageDispatcher.cs`）
  - 使用反射或类型检查判断服务是否实现 IPulseService
  - 如果实现，提取 ServiceSchedulingKey 并调度到专用线程
  - 如果未实现，保持原有行为（使用 Task.Run 或线程池）

- [ ] **T033** [US3] 添加迁移指南文档（`docs/IPulseService-Migration-Guide.md`）
  - 说明如何从 IPulseHub 迁移到 IPulseHub + IPulseService
  - 提供代码示例（before/after）
  - 说明向后兼容保证

- [ ] **T034** [P] [US3] 更新快速开始指南（`specs/007-pulserpc-server-ipulsehub/quickstart.md`）
  - 补充向后兼容性说明
  - 添加混合部署示例

**检查点**: 所有用户故事 1, 2, 3 应独立功能

---

## Phase 6: 用户故事 4 - 动态线程池管理 (优先级: P2)

**目标**: 基于服务实例活跃度自动管理线程分配和回收

**独立测试**: 监控线程分配指标，变化服务实例数量从 10 到 10,000，验证高效资源利用

### US4 测试任务

- [ ] **T035** [P] [US4] 单元测试 - 空闲实例清理（`tests/PulseRPC.Server.Tests/Unit/ThreadAffinityManagerTests.cs`）
  - 创建 100 个线程亲和性记录
  - 模拟时间推进（超过空闲阈值）
  - 验证空闲实例被清理
  - 验证活跃实例保留

- [ ] **T036** [P] [US4] 性能测试 - 大规模实例管理（`perf/BenchmarkApp/ServiceSchedulingBenchmarks/ThreadAffinityBenchmark.cs`）
  - 使用 BenchmarkDotNet 测试 10,000-50,000 个实例的内存开销
  - 测试查找性能（GetThread 方法）
  - 测试清理性能（CleanupIdleInstances 方法）

### US4 实施任务

- [ ] **T037** [US4] 在 ThreadAffinityManager 实现空闲清理逻辑（已在 T015 部分实现，此任务验证完整性）
  - CleanupIdleInstances(object? state) 私有方法
  - 遍历 ConcurrentDictionary，检查 IdleDuration
  - 移除超过 IdleInstanceTimeout 的记录
  - 记录清理日志（ILogger.LogInformation）

- [ ] **T038** [US4] 实现背压（Backpressure）机制（`src/PulseRPC.Server/Scheduling/BackpressureHandler.cs`）
  - 监控队列深度（QueueDepth 属性）
  - 当队列深度超过阈值（如 80% 容量），返回 Backpressure 信号
  - 集成到 MessageDispatcher.DispatchMessageAsync

- [ ] **T039** [US4] 添加线程池容量监控（`src/PulseRPC.Server/Scheduling/ServiceThreadScheduler.cs`）
  - GetMetrics() 方法返回 SchedulerMetrics
  - 包含 ActiveInstances, TotalThreads, ThreadWorkloads, TotalQueueDepth 等指标

- [ ] **T040** [P] [US4] 集成测试 - 空闲实例回收（`tests/PulseRPC.Server.Tests/Integration/IPulseServiceSchedulingTests.cs`）
  - 创建 1000 个服务实例
  - 模拟空闲（5 分钟 + 缓冲时间，使用 FakeTimeProvider）
  - 验证线程亲和性记录被清理
  - 向已清理实例发送新请求，验证重新建立亲和性

**检查点**: 用户故事 1-4 应独立功能

---

## Phase 7: 用户故事 5 - 服务实例监控与可观测性 (优先级: P3)

**目标**: 提供实时监控指标和健康状态查询，支持诊断和故障排查

**独立测试**: 部署instrumented services，验证指标收集、查询和告警功能

### US5 测试任务

- [ ] **T041** [P] [US5] 契约测试 - 诊断 API 端点（`tests/PulseRPC.Server.Tests/Contract/DiagnosticsApiContractTests.cs`）
  - 验证 GET /diagnostics/metrics 返回 JSON 符合 schema
  - 验证 GET /diagnostics/health 返回 JSON 符合 schema
  - 验证 POST /diagnostics/instances/{serviceId}/reset 成功响应

- [ ] **T042** [P] [US5] 集成测试 - 指标端点查询（`tests/PulseRPC.Server.Tests/Integration/DiagnosticEndpointTests.cs`）
  - 创建 10 个服务实例，发送请求
  - 查询 /diagnostics/metrics
  - 验证返回的实例数量、成功率、线程分配正确

- [ ] **T043** [P] [US5] 集成测试 - 健康状态端点（`tests/PulseRPC.Server.Tests/Integration/DiagnosticEndpointTests.cs`）
  - 创建服务实例，触发隔离
  - 查询 /diagnostics/health
  - 验证 healthyInstances 和 isolatedInstances 计数正确

### US5 实施任务

- [ ] **T044** [P] [US5] 实现 ServiceInstanceMetrics 指标收集器（`src/PulseRPC.Server/Observability/ServiceInstanceMetrics.cs`）
  - GetAllMetrics() 方法返回所有实例指标
  - GetMetricsByServiceName(string serviceName) 方法过滤查询
  - GetMetricsByServiceId(string serviceId) 方法单实例查询
  - 集成 ServiceInstanceHealthMonitor 和 ThreadAffinityManager 数据

- [ ] **T045** [US5] 实现 /diagnostics/metrics 端点（`src/PulseRPC.Server/Observability/DiagnosticEndpoints.cs`）
  - ASP.NET Core MapGet 注册路由
  - 支持 ?serviceName 和 ?serviceId 查询参数
  - 返回 MetricsResponse JSON（符合 OpenAPI schema）
  - 添加速率限制（100 req/min）

- [ ] **T046** [US5] 实现 /diagnostics/health 端点（`src/PulseRPC.Server/Observability/HealthCheckEndpoint.cs`）
  - 聚合所有实例的健康状态
  - 计算 healthyInstances, isolatedInstances, coolingDownInstances 等
  - 返回 HealthResponse JSON
  - 集成 ASP.NET Core HealthChecks 框架

- [ ] **T047** [US5] 实现 /diagnostics/instances/{serviceId}/reset 端点（`src/PulseRPC.Server/Observability/DiagnosticEndpoints.cs`）
  - ASP.NET Core MapPost 注册路由
  - 调用 ServiceInstanceHealthMonitor.ResetHealth(ServiceSchedulingKey)
  - 记录审计日志（谁在何时重置了哪个实例）
  - 返回重置前的健康状态

- [ ] **T048** [P] [US5] 集成测试 - 手动重置健康状态（`tests/PulseRPC.Server.Tests/Integration/DiagnosticEndpointTests.cs`）
  - 创建服务实例并触发隔离
  - 调用 POST /diagnostics/instances/{serviceId}/reset
  - 验证实例恢复到 Healthy 状态
  - 验证后续请求正常处理

**检查点**: 所有用户故事应独立功能

---

## Phase 8: 集成与优化

**目的**: 跨用户故事的改进和最终集成

- [ ] **T049** [P] 添加性能基准测试（`perf/BenchmarkApp/ServiceSchedulingBenchmarks/HashDistributionBenchmark.cs`）
  - 测试一致性哈希分布质量（标准差、最大偏差）
  - 测试不同虚拟节点数量的影响（50, 100, 150, 200）

- [ ] **T050** [P] 添加性能基准测试（`perf/BenchmarkApp/ServiceSchedulingBenchmarks/IsolationOverheadBenchmark.cs`）
  - 测试健康监控开销（每次调用增加的延迟）
  - 对比启用/禁用隔离机制的性能差异

- [ ] **T051** [P] 添加日志记录（所有相关类）
  - ServiceThreadScheduler: 记录实例分配到线程的日志
  - CircuitBreakerPolicy: 记录状态转换日志
  - ThreadAffinityManager: 记录空闲清理日志
  - 使用结构化日志（ILogger with scopes）

- [ ] **T052** [P] 更新 PublicAPI.Shipped.txt（如需发布）
  - 将 PublicAPI.Unshipped.txt 内容移至 Shipped.txt
  - 标记版本号

- [ ] **T053** 验证 quickstart.md 中的所有示例代码可运行
  - 创建示例项目测试所有代码片段
  - 验证配置选项示例正确

- [ ] **T054** [P] 代码审查和重构
  - 检查所有公共 API 的 XML 文档注释完整性
  - 验证 nullable reference types 正确使用
  - 移除未使用的代码

**检查点**: 功能完整，可发布

---

## 依赖关系与执行顺序

### 阶段依赖

- **项目设置 (Phase 1)**: 无依赖 - 可立即开始
- **基础设施 (Phase 2)**: 依赖 Phase 1 完成 - 阻塞所有用户故事
- **用户故事 (Phase 3-7)**: 都依赖 Phase 2 完成
  - 用户故事之间大部分独立（可并行，如果有足够人力）
  - US3（向后兼容）可能需要 US1 完成后验证
  - 建议按优先级顺序实施（P1 → P2 → P3）
- **集成与优化 (Phase 8)**: 依赖所需的用户故事完成

### 用户故事依赖

- **用户故事 1 (P1)**: Phase 2 完成后可开始 - 无其他故事依赖
- **用户故事 2 (P1)**: Phase 2 完成后可开始 - 依赖 US1 的调度基础设施
- **用户故事 3 (P2)**: Phase 2 完成后可开始 - 建议在 US1 完成后验证兼容性
- **用户故事 4 (P2)**: Phase 2 完成后可开始 - 依赖 US1 的 ThreadAffinityManager
- **用户故事 5 (P3)**: Phase 2 完成后可开始 - 依赖 US2 的 ServiceInstanceHealthMonitor

### 故事内任务顺序

- 测试任务应在实施前完成（TDD 方法）
- 模型/实体优先于服务
- 服务优先于端点
- 核心实现优先于集成
- 故事完成后再进入下一个优先级

### 并行执行机会

- Phase 1 中所有标记 [P] 的任务可并行
- Phase 2 中所有标记 [P] 的任务可并行（T004-T010）
- Phase 2 完成后，所有用户故事可并行开始（如果有多个开发者）
- 每个用户故事内标记 [P] 的任务可并行（如 T011 和 T012）
- 不同用户故事可由不同团队成员并行工作

---

## 并行示例：用户故事 1

```bash
# 同时启动 US1 的所有测试任务:
T011: "单元测试 - ServiceSchedulingKey 哈希一致性"
T012: "单元测试 - ConsistentHashRing 分布均匀性"
T018: "集成测试 - 单线程亲和性"
T019: "集成测试 - 多实例并发"
T020: "契约测试 - IPulseService 接口契约"

# 等待测试全部失败（TDD red phase）后，启动基础模型任务:
T013: "定义 IPulseService 公共接口"

# 然后并行启动核心实现:
T014: "实现 ConsistentHashRing 一致性哈希环"
T015: "实现 ThreadAffinityManager 线程亲和性管理器"

# 最后集成到现有基础设施:
T016: "扩展 ServiceThreadScheduler 支持 IPulseService"
T017: "扩展 MessageDispatcher 检测 IPulseService"
```

---

## 实施策略

### MVP 优先（仅用户故事 1）

1. 完成 Phase 1: 项目设置
2. 完成 Phase 2: 基础设施（关键 - 阻塞所有故事）
3. 完成 Phase 3: 用户故事 1（线程亲和性）
4. **停止并验证**: 独立测试用户故事 1
5. 部署/演示（如果准备就绪）

### 渐进式交付

1. 完成 Setup + 基础设施 → 基础就绪
2. 添加用户故事 1 → 独立测试 → 部署/演示（MVP！）
3. 添加用户故事 2 → 独立测试 → 部署/演示（灾难隔离）
4. 添加用户故事 3 → 独立测试 → 部署/演示（向后兼容）
5. 添加用户故事 4 → 独立测试 → 部署/演示（动态管理）
6. 添加用户故事 5 → 独立测试 → 部署/演示（可观测性）
7. 每个故事增加价值而不破坏先前故事

### 并行团队策略

如果有多个开发者：

1. 团队共同完成 Setup + 基础设施
2. 基础设施完成后分工：
   - 开发者 A: 用户故事 1 + 2（核心调度和隔离，优先级 P1）
   - 开发者 B: 用户故事 3 + 4（兼容性和动态管理，优先级 P2）
   - 开发者 C: 用户故事 5（可观测性，优先级 P3）
3. 故事完成并独立集成

---

## 任务统计

- **总任务数**: 54
- **Phase 1 (设置)**: 3 任务
- **Phase 2 (基础设施)**: 7 任务
- **Phase 3 (US1 - 线程亲和性)**: 10 任务（3 测试 + 7 实施）
- **Phase 4 (US2 - 灾难隔离)**: 8 任务（2 测试 + 6 实施）
- **Phase 5 (US3 - 向后兼容)**: 6 任务（3 测试 + 3 实施）
- **Phase 6 (US4 - 动态管理)**: 6 任务（2 测试 + 4 实施）
- **Phase 7 (US5 - 可观测性)**: 8 任务（3 测试 + 5 实施）
- **Phase 8 (集成优化)**: 6 任务

**并行机会**: 约 30 个任务标记为 [P]，理论上可显著加速执行

**独立测试点**: 每个用户故事（US1-US5）都有明确的独立测试标准

**建议 MVP 范围**: Phase 1 + Phase 2 + Phase 3 (用户故事 1)，共 20 个任务

---

## 注意事项

- **[P] 任务** = 不同文件，无依赖关系
- **[Story] 标签** 映射任务到特定用户故事，便于追溯
- 每个用户故事应能独立完成和测试
- 验证测试在实施前失败（TDD red-green-refactor）
- 在每个任务或逻辑组后提交
- 在任何检查点停止以独立验证故事
- 避免：模糊任务、相同文件冲突、破坏独立性的跨故事依赖

---

**任务文档版本**: 1.0.0 | **生成日期**: 2025-10-21
