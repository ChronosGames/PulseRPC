# PulseRPC.Server 架构设计深度分析

> 文档状态：历史架构分析。当前源码已使用 `PulseServiceBase` 与 `ServiceExecutionOptions`，本文中的 `BaseService`/`ConcurrentServiceBase` 分类是早期命名。

## 执行摘要

本报告深入分析 PulseRPC.Server 项目的核心架构设计，重点关注 **IPulseService** 和 **IPulseHub** 两个关键接口。

### 核心发现
- **IPulseHub** 是标记接口，定义远程可调用性
- **IPulseService** 启用线程亲和性和灾难隔离
- 两接口可独立或联合使用
- 架构采用分层设计，分离关注点明确

---

## 一、核心接口定义

### IPulseHub（标记接口）
**位置**：`src/PulseRPC.Abstractions/IPulseHub.cs`

```csharp
public interface IPulseHub
{
    // 所有远程服务都应继承此接口
}
```

**职责**：
- 仅用于标记，无方法定义
- 使源代码生成器识别服务
- 支持自动代码生成

### IPulseService（调度接口）
**位置**：`src/PulseRPC.Server/Abstractions/IPulseService.cs`

```csharp
public interface IPulseService
{
    // 服务类型名称（不可变）
    string ServiceName { get; }
    
    // 服务实例唯一标识（不可变）
    string ServiceId { get; }
}
```

**职责**：
- 定义服务实例身份
- 启用线程亲和性调度
- 启用灾难隔离和健康监控

---

## 二、IPulseService 和 IPulseHub 的关系

### 四种实现模式

1. **仅 IPulseHub**（无状态）
   - 使用默认线程池调度
   - 支持并发执行
   - 适合数据库查询、无状态操作

2. **IPulseHub + IPulseService**（有状态）
   - 基于 ServiceId 线程调度
   - 相同 ServiceId 串行执行
   - 适合聊天室、游戏房间

3. **BaseService**（Actor 模型）
   - 强制单线程语义
   - 支持定时器、系统消息
   - 完整生命周期管理

4. **ConcurrentServiceBase**（并发）
   - 支持多线程处理
   - 可配置并发度
   - 适合 I/O 密集型

---

## 三、生命周期管理

### ThreadAffinityManager
- 维护 ServiceId → Thread 映射
- 定时清理空闲实例（默认 1 分钟）
- 使用一致性哈希分配线程

### ServiceInstanceHealthMonitor
- 记录请求结果
- 触发熔断器状态转换
- 健康状态：Healthy → Isolated → CoolingDown → ProbeAllowed

### ServiceSchedulingOptions
```csharp
public sealed class ServiceSchedulingOptions
{
    public int WorkerThreadCount { get; set; } = Environment.ProcessorCount;
    public TimeSpan IdleInstanceTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public int VirtualNodesPerThread { get; set; } = 150;
}
```

---

## 四、DI 容器注册

### 注册基础设施
```csharp
services.AddIPulseServiceScheduling(
    configureScheduling: options =>
    {
        options.WorkerThreadCount = 16;
        options.IdleInstanceTimeout = TimeSpan.FromMinutes(10);
    },
    configureHealthMonitor: options =>
    {
        options.FailureThreshold = 3;
        options.CoolingPeriod = TimeSpan.FromMinutes(1);
    });
```

### 注册服务
```csharp
// 简单注册
builder.Services.AddSingleton<IGameHub, GameHub>();

// 工厂注册（多实例）
builder.Services.AddSingleton<Func<string, ChatRoomService>>(sp =>
    roomId => new ChatRoomService(
        sp.GetRequiredService<ILogger<ChatRoomService>>(),
        roomId));
```

---

## 五、服务隔离实现

### 隔离层次
1. **线程隔离**：不同 ServiceId → 不同线程
2. **健康监控**：故障实例自动隔离
3. **请求路由**：服务注册表管理

### 故障隔离流程
```
Healthy → (3次失败) → Isolated → (冷却期) → CoolingDown
                                      ↓
                              (探测成功≥阈值)
                                    ↓
                            ← Healthy ←
```

### 存在的问题
1. **隔离不完整**：仅隔离线程，不隔离内存和 CPU
2. **生命周期不清**：IPulseService 实例创建销毁逻辑不明确
3. **文档不足**：隔离能力和边界未清楚说明

---

## 六、示例项目

### ChatApp
- PlayerHub 仅实现 IPulseHub
- 无状态或简单有状态
- 依赖 RequestContext 获取上下文

### DistributedGameApp
- 多个 Hub 服务（GameHub、BattleHub）
- Consul 集成服务注册
- 完整的集群架构

---

## 七、最佳实践

### 选择实现模式
- 无状态 → 仅 IPulseHub
- 有状态，需要串行 → IPulseHub + IPulseService
- 复杂有状态 → BaseService
- 并发密集 → ConcurrentServiceBase

### 配置建议
- CPU 密集：WorkerThreadCount = CPU 核数
- I/O 密集：WorkerThreadCount = CPU 核数 * 2-4
- 调优虚拟节点以获得均匀分布

### 监控诊断
- 使用健康监控 API
- 暴露诊断端点
- 支持手动恢复隔离实例

---

## 八、架构优缺点

### 优点
✅ 清晰的责任分离
✅ 灵活的使用模式
✅ 自动线程管理
✅ 自动故障隔离
✅ 生成器自动化（无反射）
✅ 完全向后兼容
✅ 集群友好

### 缺点
❌ 文档不足
❌ 隔离不完整（仅线程）
❌ 生命周期管理不明确
❌ 健康监控粒度不够
❌ 配置参数众多（调优困难）
❌ 跨服务影响不清晰

---

## 九、改进方向

1. **明确生命周期**：定义 IServiceLifecycle，区分创建、初始化、销毁
2. **增强隔离**：细化故障类型分类，支持更细粒度的隔离
3. **自动化工厂**：PulseServiceFactory 模式自动管理实例生命周期
4. **完善文档**：明确说明隔离范围和未隔离资源
5. **诊断工具**：提供更好的服务实例观测能力

---

**报告日期**：2025-11-10
**分析深度**：Very Thorough（非常深入）
