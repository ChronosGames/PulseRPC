# 实施计划：服务线程调度与灾难隔离

**分支**: `007-pulserpc-server-ipulsehub` | **日期**: 2025-10-21 | **规格**: [spec.md](./spec.md)
**输入**: 功能规格来自 `/specs/007-pulserpc-server-ipulsehub/spec.md`

## 概述

本功能引入 **IPulseService** 接口，为 PulseRPC.Server 提供基于服务实例的线程调度控制能力。通过将特定 ServiceId 的所有请求路由到专用线程，实现服务实例级别的灾难隔离，防止单个故障服务实例影响整个服务器。同时保持与现有 IPulseHub 接口的完全向后兼容，支持渐进式迁移。

**核心技术方案**：
- IPulseService 接口定义不可变的 ServiceName 和 ServiceId 属性
- 使用一致性哈希算法将服务实例映射到工作线程
- 基于连续超时的健康检测和熔断器模式实现自动隔离与恢复
- HTTP 诊断端点暴露实时监控指标（JSON 格式）
- 扩展现有 ServiceThreadScheduler 和 MessageDispatcher 基础设施

## 技术上下文

**语言/版本**: C# / .NET 9.0 (SDK 9.0.313)
**主要依赖**:
- System.Threading.Channels 9.0.0（无锁消息队列）
- Microsoft.Extensions.Logging 9.0.0（日志记录）
- Microsoft.Extensions.DependencyInjection 9.0.0（依赖注入）
- MemoryPack 1.21.4（序列化）

**测试框架**:
- xUnit 2.6.1（单元测试）
- FluentAssertions 8.3.0（断言库）
- NSubstitute 5.0.0（模拟框架）

**目标平台**: .NET 9.0+, 支持 Linux/Windows/macOS
**项目类型**: 类库 + 服务端框架（单一代码库）

**性能目标**:
- 10,000 个并发服务实例分布在 16 个工作线程，无性能下降
- 服务实例故障时其他实例延迟影响 <1%
- 线程分配稳定性 99.99%（相同 ServiceId 保持在同一线程）
- 监控指标延迟 <5 秒

**约束**:
- 必须保持 IPulseHub 100% 向后兼容
- ServiceId 长度限制 1000 字符（防止哈希碰撞）
- 冷却期默认 1 分钟（可配置）
- 空闲超时默认 5 分钟（可配置）
- 健康检测阈值默认 3 次连续超时（可配置）

**规模/范围**:
- 支持最多 50,000 个活跃服务实例
- 工作线程数量：Environment.ProcessorCount 至 64（可配置）
- 单服务器部署（不涉及分布式调度）

## Constitution 检查

*门控：必须在 Phase 0 研究前通过。Phase 1 设计后重新检查。*

**注意**: 当前 constitution 文件为模板状态，无具体项目原则。以下检查基于 PulseRPC 项目的 CLAUDE.md 中记录的约定。

### 检查项

| 检查项 | 状态 | 说明 |
|--------|------|------|
| **序列化约定** | ✅ 通过 | ServiceInstanceHealth 等新实体将使用 MemoryPack 序列化（如需持久化） |
| **传输层解耦** | ✅ 通过 | IPulseService 为抽象接口，不依赖 TCP/KCP 传输细节 |
| **测试约定** | ✅ 通过 | 计划包含单元测试、集成测试和契约测试 |
| **代码约定** | ✅ 通过 | 启用 nullable reference types, 生成 XML 文档 |
| **异步模式** | ✅ 通过 | 所有公共 API 使用 async/await 模式，支持 CancellationToken |
| **向后兼容** | ✅ 通过 | IPulseHub 接口保持不变，PublicAPI.Unshipped.txt 管理新 API |

### 设计原则验证

- **最小化复杂性**: 扩展现有调度器而非重写，复用 ServiceThreadScheduler 基础设施
- **渐进式增强**: IPulseService 为可选接口，不强制迁移现有服务
- **明确职责**: ServiceId 生成由服务实现负责，避免中间件耦合
- **可观测性优先**: HTTP 诊断端点符合云原生标准（Prometheus/OpenTelemetry 兼容）

## 项目结构

### 文档（本功能）

```
specs/007-pulserpc-server-ipulsehub/
├── plan.md              # 本文件 (/speckit.plan 命令输出)
├── research.md          # Phase 0 输出（技术调研）
├── data-model.md        # Phase 1 输出（数据模型设计）
├── quickstart.md        # Phase 1 输出（快速开始指南）
├── contracts/           # Phase 1 输出（接口契约定义）
│   ├── IPulseService.cs       # 服务实例接口契约
│   ├── IServiceScheduler.cs   # 调度器接口契约
│   └── diagnostics-api.yaml   # HTTP 诊断端点 OpenAPI 规范
└── tasks.md             # Phase 2 输出 (/speckit.tasks 命令 - 本命令不创建)
```

### 源代码（仓库根目录）

**结构决策**: PulseRPC 采用单一代码库结构，按层次组织（Server/Client/Shared/Tests）。本功能主要扩展 `src/PulseRPC.Server/` 项目。

```
src/PulseRPC.Server/
├── Abstractions/
│   ├── IPulseHub.cs                    # [已存在] 保持不变
│   ├── IPulseService.cs                # [新增] 服务实例接口
│   ├── IServiceScheduler.cs            # [新增] 调度器抽象
│   └── IServiceInstanceHealthMonitor.cs # [新增] 健康监控接口
│
├── Scheduling/
│   ├── ServiceThreadScheduler.cs       # [已存在] 扩展以支持 IPulseService
│   ├── ServiceThreadPool.cs            # [已存在] 工作线程池管理
│   ├── ServiceSchedulingKey.cs         # [新增] 复合调度键（ServiceName + ServiceId）
│   ├── ServiceInstanceHealthMonitor.cs # [新增] 健康状态监控器
│   ├── CircuitBreakerPolicy.cs         # [新增] 熔断器策略
│   └── ConsistentHashRing.cs           # [新增] 一致性哈希环
│
├── Pipeline/
│   ├── MessageDispatcher.cs            # [已存在] 扩展以检测 IPulseService
│   ├── ServiceInvoker.cs               # [已存在] 集成健康监控
│   └── SchedulingMiddleware.cs         # [新增] 调度中间件
│
├── Observability/
│   ├── DiagnosticEndpoints.cs          # [已存在] 扩展添加 /metrics 和 /health
│   ├── ServiceInstanceMetrics.cs       # [新增] 服务实例指标收集器
│   └── HealthCheckEndpoint.cs          # [新增] 健康检查端点实现
│
├── Configuration/
│   ├── ServiceSchedulingOptions.cs     # [新增] 调度器配置选项
│   └── HealthMonitorOptions.cs         # [新增] 健康监控配置选项
│
└── Models/
    ├── ServiceInstanceHealth.cs        # [新增] 健康状态记录
    ├── ThreadAffinity.cs               # [新增] 线程亲和性映射
    └── HealthState.cs                  # [新增] 健康状态枚举

tests/PulseRPC.Server.Tests/
├── Unit/
│   ├── ServiceSchedulingKeyTests.cs
│   ├── ConsistentHashRingTests.cs
│   ├── CircuitBreakerPolicyTests.cs
│   └── ServiceInstanceHealthMonitorTests.cs
│
├── Integration/
│   ├── IPulseServiceSchedulingTests.cs
│   ├── BackwardCompatibilityTests.cs
│   ├── DisasterIsolationTests.cs
│   └── DiagnosticEndpointTests.cs
│
└── Contract/
    ├── IPulseServiceContractTests.cs
    └── DiagnosticsApiContractTests.cs

perf/BenchmarkApp/
└── ServiceSchedulingBenchmarks/
    ├── ThreadAffinityBenchmark.cs      # 线程亲和性性能测试
    ├── HashDistributionBenchmark.cs    # 哈希分布质量测试
    └── IsolationOverheadBenchmark.cs   # 隔离机制开销测试
```

## 复杂度追踪

*仅在 Constitution Check 有违规需要证明时填写*

| 违规项 | 为何需要 | 被拒绝的简单替代方案及原因 |
|--------|----------|---------------------------|
| 无违规 | N/A | N/A |

本设计遵循最小化复杂度原则：
- 复用现有 ServiceThreadScheduler 基础设施
- 使用标准一致性哈希算法（非自定义调度策略）
- 熔断器模式为成熟的故障隔离模式
- HTTP 诊断端点遵循 RESTful 约定

---

## Phase 0: 概要与研究

### 研究目标

1. **一致性哈希算法选型**
   - 评估虚拟节点数量对分布均匀性的影响
   - 性能对比：FNV-1a vs MurmurHash3 vs xxHash
   - 验证哈希碰撞概率（1000-50000 ServiceId 场景）

2. **熔断器状态机最佳实践**
   - 研究 Polly、Hystrix 等成熟熔断器实现
   - 确定冷却期和探测请求阈值的推荐值
   - 状态转换：Healthy → Isolated → CoolingDown → ProbeAllowed → Healthy

3. **诊断端点设计模式**
   - 对比 ASP.NET Core HealthChecks vs 自定义端点
   - 确定指标格式：Prometheus 文本格式 vs JSON
   - 认证与授权策略（诊断端点的安全性）

4. **线程亲和性数据结构**
   - ConcurrentDictionary vs 自定义无锁哈希表
   - 空闲实例清理策略：定时扫描 vs TTL 过期队列
   - 内存开销评估（50000 活跃实例场景）

5. **ServiceId 生成指南**
   - 推荐模式：GUID vs 业务 ID vs 组合键
   - 防止碰撞的验证规则
   - 长度限制与性能权衡

### 输出

研究结果将记录在 `research.md`，包含：
- 每个决策点的选择
- 选择理由（性能、简洁性、可维护性）
- 被拒绝的替代方案及原因
- 参考实现或基准测试数据

---

## Phase 1: 设计与契约

### 数据模型设计

输出文件：`data-model.md`

#### 核心实体

**IPulseService**（接口）
```csharp
public interface IPulseService
{
    /// <summary>
    /// 服务类型名称（不可变）。例如："ChatRoom", "OrderProcessor"
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// 服务实例唯一标识符（不可变）。例如："room-123", "order-456"
    /// 由服务实现类在初始化时生成，之后不可更改。
    /// </summary>
    string ServiceId { get; }
}
```

**ServiceSchedulingKey**（值对象）
```csharp
public readonly struct ServiceSchedulingKey : IEquatable<ServiceSchedulingKey>
{
    public string ServiceName { get; }
    public string ServiceId { get; }
    public int HashCode { get; } // 预计算的哈希值

    // GetHashCode, Equals, == 运算符重载
}
```

**ServiceInstanceHealth**（状态记录）
```csharp
public sealed class ServiceInstanceHealth
{
    public ServiceSchedulingKey Key { get; init; }
    public HealthState State { get; set; }
    public long TotalRequests { get; set; }
    public long SuccessfulRequests { get; set; }
    public int ConsecutiveTimeouts { get; set; }
    public DateTime LastActivityUtc { get; set; }
    public DateTime? CoolingPeriodExpiresUtc { get; set; }
    public int ProbeRequestsAllowed { get; set; }

    public double SuccessRate => TotalRequests > 0
        ? (double)SuccessfulRequests / TotalRequests
        : 0.0;
}
```

**HealthState**（枚举）
```csharp
public enum HealthState
{
    Healthy = 0,        // 正常运行
    Isolated = 1,       // 已隔离，拒绝所有请求
    CoolingDown = 2,    // 冷却期中，等待探测窗口
    ProbeAllowed = 3    // 允许探测请求，测试恢复
}
```

**ThreadAffinity**（映射记录）
```csharp
public sealed class ThreadAffinity
{
    public ServiceSchedulingKey Key { get; init; }
    public int AssignedThreadId { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime LastAccessUtc { get; set; }

    public TimeSpan IdleDuration(DateTime now)
        => now - LastAccessUtc;
}
```

#### 状态转换图

```
[Healthy] ──(3次连续超时)──> [Isolated]
    ↑                            |
    |                    (冷却期结束)
    |                            ↓
    └────(探测成功)──── [ProbeAllowed]
                                 |
                         (探测失败)
                                 ↓
                            [Isolated]
```

### API 契约设计

输出目录：`contracts/`

#### 1. IPulseService 接口契约

文件：`contracts/IPulseService.cs`

已在数据模型部分定义，作为公共 API 契约。

#### 2. IServiceScheduler 接口契约

文件：`contracts/IServiceScheduler.cs`

```csharp
/// <summary>
/// 服务实例调度器接口
/// </summary>
public interface IServiceScheduler
{
    /// <summary>
    /// 启动调度器
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止调度器
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 调度服务实例的工作项到专用线程
    /// </summary>
    /// <param name="key">服务调度键</param>
    /// <param name="work">要执行的工作</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ScheduleAsync(
        ServiceSchedulingKey key,
        Func<Task> work,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取调度器指标
    /// </summary>
    SchedulerMetrics GetMetrics();
}
```

#### 3. 诊断 HTTP 端点契约

文件：`contracts/diagnostics-api.yaml`

```yaml
openapi: 3.0.3
info:
  title: PulseRPC 服务实例诊断 API
  version: 1.0.0
  description: 服务实例健康监控和指标暴露端点

paths:
  /diagnostics/metrics:
    get:
      summary: 获取服务实例指标
      parameters:
        - name: serviceName
          in: query
          schema:
            type: string
          description: 可选的服务名称过滤器
        - name: serviceId
          in: query
          schema:
            type: string
          description: 可选的服务实例 ID 过滤器
      responses:
        '200':
          description: 指标数据（JSON 格式）
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/MetricsResponse'

  /diagnostics/health:
    get:
      summary: 获取服务实例健康状态
      responses:
        '200':
          description: 健康状态（JSON 格式）
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HealthResponse'

  /diagnostics/instances/{serviceId}/reset:
    post:
      summary: 手动重置服务实例健康状态
      parameters:
        - name: serviceId
          in: path
          required: true
          schema:
            type: string
      responses:
        '200':
          description: 重置成功
        '404':
          description: 服务实例不存在

components:
  schemas:
    MetricsResponse:
      type: object
      properties:
        timestamp:
          type: string
          format: date-time
        activeInstances:
          type: integer
        totalThreads:
          type: integer
        instances:
          type: array
          items:
            $ref: '#/components/schemas/InstanceMetrics'

    InstanceMetrics:
      type: object
      properties:
        serviceName:
          type: string
        serviceId:
          type: string
        healthState:
          type: string
          enum: [Healthy, Isolated, CoolingDown, ProbeAllowed]
        totalRequests:
          type: integer
        successRate:
          type: number
          format: double
        assignedThreadId:
          type: integer
        lastActivityUtc:
          type: string
          format: date-time

    HealthResponse:
      type: object
      properties:
        status:
          type: string
          enum: [Healthy, Degraded, Unhealthy]
        totalInstances:
          type: integer
        healthyInstances:
          type: integer
        isolatedInstances:
          type: integer
```

### 快速开始指南

输出文件：`quickstart.md`

将包含：
1. 如何定义 IPulseService 服务（代码示例）
2. 如何配置调度器选项
3. 如何从 IPulseHub 迁移到 IPulseHub + IPulseService
4. 如何访问诊断端点查看指标
5. 故障排查常见问题

---

## Phase 2: 任务分解

**注意**: 任务分解由 `/speckit.tasks` 命令执行，不在本命令范围内。

预期任务将包括：
- Phase 2.1: 核心接口与数据模型实现
- Phase 2.2: 调度器与线程亲和性逻辑
- Phase 2.3: 健康监控与熔断器
- Phase 2.4: 诊断端点实现
- Phase 2.5: 集成测试与性能验证
- Phase 2.6: 文档与迁移指南

---

## 关键设计决策

### 1. ServiceId 生成职责

**决策**: 由服务实现类自行生成
**理由**:
- 服务实例最了解自身业务上下文（如房间 ID、用户会话 ID）
- 避免引入额外的中间件依赖
- 支持灵活的 ID 生成策略（GUID、业务键、组合键）

**权衡**: 服务开发者需要理解 ServiceId 的唯一性要求

### 2. 健康检测机制

**决策**: 基于连续超时计数
**理由**:
- 简单可靠，无需额外的线程堆栈采样
- 复用现有的超时管理基础设施（ServiceInvoker）
- 低开销，适合高并发场景

**权衡**: 无法检测不产生超时的慢操作（如 CPU 密集型循环）

### 3. 自动恢复策略

**决策**: 熔断器模式（冷却期 + 探测请求）
**理由**:
- 成熟的故障恢复模式（Polly、Hystrix 等库验证）
- 平衡自动恢复和稳定性（避免频繁故障）
- 可配置的冷却期和探测阈值

**权衡**: 增加状态机复杂度（4 个状态）

### 4. 监控指标暴露方式

**决策**: HTTP 诊断端点（JSON 格式）
**理由**:
- 符合云原生标准（Kubernetes health checks, Prometheus scraping）
- 易于集成到现有监控基础设施
- 支持人工检查和自动化采集

**权衡**: 需要额外的 HTTP 端点实现

### 5. 一致性哈希实现

**决策**: 使用虚拟节点的一致性哈希环
**理由**:
- 标准算法，成熟的负载均衡方案
- 虚拟节点提高分布均匀性
- 支持动态线程数调整（未来扩展）

**权衡**: 内存开销（虚拟节点映射表）

---

## 风险与缓解

| 风险 | 影响 | 概率 | 缓解措施 |
|------|------|------|----------|
| 一致性哈希分布不均 | 性能 | 中 | Phase 0 研究验证分布质量；性能测试覆盖 |
| ServiceId 碰撞 | 功能 | 低 | 长度验证（1000字符限制）；哈希函数选择 |
| 熔断器误触发 | 可用性 | 中 | 可配置阈值；手动重置端点；详细日志 |
| 内存泄漏（大量实例） | 稳定性 | 中 | 空闲实例清理；内存压力测试 |
| 向后兼容性破坏 | 集成 | 低 | 契约测试；渐进式部署；文档说明 |

---

## 交付成果清单

### Phase 0 输出
- [ ] `research.md` - 技术调研报告

### Phase 1 输出
- [ ] `data-model.md` - 数据模型设计文档
- [ ] `contracts/IPulseService.cs` - 服务实例接口契约
- [ ] `contracts/IServiceScheduler.cs` - 调度器接口契约
- [ ] `contracts/diagnostics-api.yaml` - 诊断 API OpenAPI 规范
- [ ] `quickstart.md` - 快速开始指南
- [ ] 更新 `.claude/agent-context.json`（新增技术栈）

### Phase 2 输出（/speckit.tasks）
- [ ] `tasks.md` - 任务分解与依赖关系

---

## 下一步

执行以下命令以继续：

```bash
# 当前命令已完成 Phase 0 和 Phase 1，输出：
# - specs/007-pulserpc-server-ipulsehub/plan.md
# - specs/007-pulserpc-server-ipulsehub/research.md
# - specs/007-pulserpc-server-ipulsehub/data-model.md
# - specs/007-pulserpc-server-ipulsehub/quickstart.md
# - specs/007-pulserpc-server-ipulsehub/contracts/

# 下一步：生成任务分解
/speckit.tasks
```
