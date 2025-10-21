# 数据模型设计：服务线程调度与灾难隔离

**功能**: 服务线程调度与灾难隔离
**日期**: 2025-10-21
**状态**: 完成

## 概述

本文档定义了实现 IPulseService 服务实例线程调度功能所需的核心数据模型。模型遵循以下原则：
- **不可变性**: 关键标识符（ServiceName, ServiceId）初始化后不可更改
- **值对象**: ServiceSchedulingKey 为值类型，支持高效哈希和比较
- **状态机**: HealthState 定义明确的状态转换规则
- **可观测性**: 所有实体包含时间戳和指标字段

---

## 核心实体

### 1. IPulseService（接口）

**目的**: 定义服务实例的调度标识

**命名空间**: `PulseRPC.Server.Abstractions`

**定义**:
```csharp
namespace PulseRPC.Server.Abstractions;

/// <summary>
/// 服务实例接口，提供线程调度所需的标识信息
/// </summary>
/// <remarks>
/// 服务实现类可以同时实现 IPulseHub 和 IPulseService 以启用线程调度控制。
/// ServiceName 和 ServiceId 必须在服务实例初始化时确定，之后不可更改。
/// </remarks>
public interface IPulseService
{
    /// <summary>
    /// 服务类型名称（不可变）
    /// </summary>
    /// <example>ChatRoom, OrderProcessor, GameRoom</example>
    string ServiceName { get; }

    /// <summary>
    /// 服务实例唯一标识符（不可变）
    /// </summary>
    /// <remarks>
    /// 推荐格式: "{ServiceName}:{业务ID}"，例如 "ChatRoom:room-123"
    /// 最大长度: 1000 字符
    /// 允许字符: 字母、数字、连字符、下划线、冒号
    /// </remarks>
    /// <example>ChatRoom:room-123, OrderProcessor:order-456</example>
    string ServiceId { get; }
}
```

**属性约束**:

| 属性 | 类型 | 可空 | 最大长度 | 验证规则 |
|------|------|------|---------|---------|
| ServiceName | string | 否 | 200 | `^[a-zA-Z][a-zA-Z0-9]*$` |
| ServiceId | string | 否 | 1000 | `^[a-zA-Z0-9\-:_]+$` |

**生命周期**: 服务实例初始化时确定，直至实例销毁保持不变

**示例实现**:
```csharp
public class ChatRoomService : IPulseHub, IPulseService
{
    public string ServiceName => "ChatRoom";
    public string ServiceId { get; }

    public ChatRoomService(string roomId)
    {
        ServiceId = $"ChatRoom:{roomId}";
        ServiceIdValidator.Validate(ServiceId); // 验证长度和字符
    }

    // IPulseHub 方法实现...
}
```

---

### 2. ServiceSchedulingKey（值对象）

**目的**: 服务实例的复合调度键，用于哈希和线程分配

**命名空间**: `PulseRPC.Server.Scheduling`

**定义**:
```csharp
namespace PulseRPC.Server.Scheduling;

/// <summary>
/// 服务调度键，结合 ServiceName 和 ServiceId 的复合键
/// </summary>
[MemoryPackable]
public readonly partial struct ServiceSchedulingKey : IEquatable<ServiceSchedulingKey>
{
    /// <summary>
    /// 服务类型名称
    /// </summary>
    [MemoryPackOrder(0)]
    public string ServiceName { get; init; }

    /// <summary>
    /// 服务实例标识符
    /// </summary>
    [MemoryPackOrder(1)]
    public string ServiceId { get; init; }

    /// <summary>
    /// 预计算的哈希值（使用 xxHash64）
    /// </summary>
    [MemoryPackIgnore]
    public ulong HashCode { get; init; }

    public ServiceSchedulingKey(string serviceName, string serviceId)
    {
        ServiceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        ServiceId = serviceId ?? throw new ArgumentNullException(nameof(serviceId));

        // 预计算哈希值
        var combined = $"{serviceName}:{serviceId}";
        HashCode = XxHash64.Hash(Encoding.UTF8.GetBytes(combined));
    }

    public bool Equals(ServiceSchedulingKey other)
        => ServiceName == other.ServiceName && ServiceId == other.ServiceId;

    public override bool Equals(object? obj)
        => obj is ServiceSchedulingKey other && Equals(other);

    public override int GetHashCode()
        => (int)(HashCode ^ (HashCode >> 32)); // 将 ulong 折叠为 int

    public static bool operator ==(ServiceSchedulingKey left, ServiceSchedulingKey right)
        => left.Equals(right);

    public static bool operator !=(ServiceSchedulingKey left, ServiceSchedulingKey right)
        => !left.Equals(right);

    public override string ToString()
        => $"{ServiceName}:{ServiceId}";
}
```

**特性**:
- **不可变**: 值类型，初始化后不可更改
- **高效哈希**: 预计算 xxHash64，避免重复计算
- **可序列化**: MemoryPack 支持（用于未来的分布式场景）

---

### 3. ServiceInstanceHealth（状态记录）

**目的**: 跟踪服务实例的健康状态和请求指标

**命名空间**: `PulseRPC.Server.Models`

**定义**:
```csharp
namespace PulseRPC.Server.Models;

/// <summary>
/// 服务实例健康状态记录
/// </summary>
public sealed class ServiceInstanceHealth
{
    /// <summary>
    /// 服务调度键
    /// </summary>
    public required ServiceSchedulingKey Key { get; init; }

    /// <summary>
    /// 当前健康状态
    /// </summary>
    public HealthState State { get; set; }

    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// 成功请求数
    /// </summary>
    public long SuccessfulRequests { get; set; }

    /// <summary>
    /// 连续超时次数（重置于成功请求）
    /// </summary>
    public int ConsecutiveTimeouts { get; set; }

    /// <summary>
    /// 最后活动时间（UTC）
    /// </summary>
    public DateTime LastActivityUtc { get; set; }

    /// <summary>
    /// 冷却期过期时间（UTC），仅在 Isolated 状态有效
    /// </summary>
    public DateTime? CoolingPeriodExpiresUtc { get; set; }

    /// <summary>
    /// 探测窗口剩余允许的探测请求数
    /// </summary>
    public int ProbeRequestsAllowed { get; set; }

    /// <summary>
    /// 实例创建时间（UTC）
    /// </summary>
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>
    /// 计算成功率（0.0 到 1.0）
    /// </summary>
    [MemoryPackIgnore]
    public double SuccessRate => TotalRequests > 0
        ? (double)SuccessfulRequests / TotalRequests
        : 0.0;

    /// <summary>
    /// 是否允许处理新请求
    /// </summary>
    [MemoryPackIgnore]
    public bool CanAcceptRequests => State == HealthState.Healthy || State == HealthState.ProbeAllowed;
}
```

**状态转换触发器**:

| 当前状态 | 触发条件 | 新状态 | 副作用 |
|---------|---------|--------|--------|
| Healthy | ConsecutiveTimeouts >= 3 | Isolated | 设置 CoolingPeriodExpiresUtc |
| Isolated | now >= CoolingPeriodExpiresUtc | CoolingDown | 无 |
| CoolingDown | 自动 | ProbeAllowed | ProbeRequestsAllowed = 5 |
| ProbeAllowed | SuccessfulRequests >= 3 (探测期) | Healthy | 重置 ConsecutiveTimeouts |
| ProbeAllowed | 任意失败 | Isolated | 重新设置 CoolingPeriodExpiresUtc |

---

### 4. HealthState（枚举）

**目的**: 定义服务实例健康状态的枚举值

**命名空间**: `PulseRPC.Server.Models`

**定义**:
```csharp
namespace PulseRPC.Server.Models;

/// <summary>
/// 服务实例健康状态枚举
/// </summary>
public enum HealthState
{
    /// <summary>
    /// 健康状态：实例正常运行，接受所有请求
    /// </summary>
    Healthy = 0,

    /// <summary>
    /// 已隔离状态：实例因连续故障被隔离，拒绝所有请求
    /// </summary>
    Isolated = 1,

    /// <summary>
    /// 冷却中状态：冷却期已过，准备进入探测窗口
    /// </summary>
    CoolingDown = 2,

    /// <summary>
    /// 探测允许状态：允许有限数量的探测请求，测试恢复能力
    /// </summary>
    ProbeAllowed = 3
}
```

**状态转换图**:
```
     ┌───────────┐
     │  Healthy  │
     └─────┬─────┘
           │ (3次连续超时)
           ↓
     ┌───────────┐
     │ Isolated  │
     └─────┬─────┘
           │ (冷却期结束)
           ↓
   ┌──────────────┐
   │ CoolingDown  │
   └───────┬───────┘
           │ (自动转换)
           ↓
  ┌──────────────────┐
  │  ProbeAllowed    │────(探测失败)───> Isolated
  └────────┬─────────┘
           │ (3/5 成功)
           ↓
     ┌───────────┐
     │  Healthy  │
     └───────────┘
```

---

### 5. ThreadAffinity（映射记录）

**目的**: 记录服务实例到工作线程的亲和性映射

**命名空间**: `PulseRPC.Server.Scheduling`

**定义**:
```csharp
namespace PulseRPC.Server.Scheduling;

/// <summary>
/// 线程亲和性映射记录
/// </summary>
public sealed class ThreadAffinity
{
    /// <summary>
    /// 服务调度键
    /// </summary>
    public required ServiceSchedulingKey Key { get; init; }

    /// <summary>
    /// 分配的工作线程 ID
    /// </summary>
    public int AssignedThreadId { get; set; }

    /// <summary>
    /// 创建时间（UTC）
    /// </summary>
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>
    /// 最后访问时间（UTC）
    /// </summary>
    public DateTime LastAccessUtc { get; set; }

    /// <summary>
    /// 计算空闲时长
    /// </summary>
    /// <param name="now">当前时间</param>
    /// <returns>空闲时长</returns>
    public TimeSpan IdleDuration(DateTime now)
        => now - LastAccessUtc;

    /// <summary>
    /// 判断是否超过空闲阈值
    /// </summary>
    /// <param name="idleThreshold">空闲阈值</param>
    /// <param name="now">当前时间</param>
    /// <returns>是否超过阈值</returns>
    public bool IsIdle(TimeSpan idleThreshold, DateTime now)
        => IdleDuration(now) > idleThreshold;
}
```

**空闲清理策略**:
- **触发频率**: 每 1 分钟扫描一次
- **默认阈值**: 5 分钟空闲（可配置）
- **清理操作**: 从 ConcurrentDictionary 中移除 ThreadAffinity 记录

---

## 配置对象

### 6. ServiceSchedulingOptions

**目的**: 调度器配置选项

**命名空间**: `PulseRPC.Server.Configuration`

**定义**:
```csharp
namespace PulseRPC.Server.Configuration;

/// <summary>
/// 服务调度器配置选项
/// </summary>
public sealed class ServiceSchedulingOptions
{
    /// <summary>
    /// 工作线程数量（默认: Environment.ProcessorCount）
    /// </summary>
    public int WorkerThreadCount { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// 服务实例空闲超时阈值（默认: 5 分钟）
    /// </summary>
    public TimeSpan IdleInstanceTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 一致性哈希虚拟节点数量（每个线程）
    /// </summary>
    public int VirtualNodesPerThread { get; set; } = 150;

    /// <summary>
    /// 验证配置有效性
    /// </summary>
    public void Validate()
    {
        if (WorkerThreadCount < 1 || WorkerThreadCount > 64)
            throw new ArgumentOutOfRangeException(nameof(WorkerThreadCount), "Must be between 1 and 64");

        if (IdleInstanceTimeout < TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(IdleInstanceTimeout), "Must be at least 10 seconds");

        if (VirtualNodesPerThread < 10 || VirtualNodesPerThread > 500)
            throw new ArgumentOutOfRangeException(nameof(VirtualNodesPerThread), "Must be between 10 and 500");
    }
}
```

### 7. HealthMonitorOptions

**目的**: 健康监控配置选项

**命名空间**: `PulseRPC.Server.Configuration`

**定义**:
```csharp
namespace PulseRPC.Server.Configuration;

/// <summary>
/// 健康监控配置选项
/// </summary>
public sealed class HealthMonitorOptions
{
    /// <summary>
    /// 触发隔离的连续超时次数阈值（默认: 3）
    /// </summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>
    /// 冷却期时长（默认: 1 分钟）
    /// </summary>
    public TimeSpan CoolingPeriod { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 探测窗口允许的最大请求数（默认: 5）
    /// </summary>
    public int ProbeRequestLimit { get; set; } = 5;

    /// <summary>
    /// 探测窗口成功请求阈值以恢复健康（默认: 3）
    /// </summary>
    public int ProbeSuccessThreshold { get; set; } = 3;

    /// <summary>
    /// 验证配置有效性
    /// </summary>
    public void Validate()
    {
        if (FailureThreshold < 1 || FailureThreshold > 10)
            throw new ArgumentOutOfRangeException(nameof(FailureThreshold), "Must be between 1 and 10");

        if (CoolingPeriod < TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(CoolingPeriod), "Must be at least 10 seconds");

        if (ProbeRequestLimit < 1 || ProbeRequestLimit > 100)
            throw new ArgumentOutOfRangeException(nameof(ProbeRequestLimit), "Must be between 1 and 100");

        if (ProbeSuccessThreshold < 1 || ProbeSuccessThreshold > ProbeRequestLimit)
            throw new ArgumentOutOfRangeException(nameof(ProbeSuccessThreshold), "Must be between 1 and ProbeRequestLimit");
    }
}
```

---

## 数据关系图

```
┌─────────────────┐
│  IPulseService  │ (接口)
│  - ServiceName  │
│  - ServiceId    │
└────────┬────────┘
         │ 生成
         ↓
┌──────────────────────┐
│ ServiceSchedulingKey │ (值对象)
│  - ServiceName       │
│  - ServiceId         │
│  - HashCode (xxHash) │
└──────────┬───────────┘
           │
           ├──────────> 映射到 ThreadAffinity (1:1)
           │              - AssignedThreadId
           │              - CreatedAtUtc
           │              - LastAccessUtc
           │
           └──────────> 映射到 ServiceInstanceHealth (1:1)
                          - State (HealthState)
                          - TotalRequests
                          - ConsecutiveTimeouts
                          - CoolingPeriodExpiresUtc
```

---

## 存储与索引

### 主存储

所有数据保存在内存中，无持久化（重启后丢失）：

| 数据结构 | 键 | 值 | 估算内存 (50K 实例) |
|---------|-----|-----|-------------------|
| ConcurrentDictionary | ServiceSchedulingKey | ThreadAffinity | ~8MB |
| ConcurrentDictionary | ServiceSchedulingKey | ServiceInstanceHealth | ~12MB |
| SortedDictionary (一致性哈希环) | ulong (hash) | int (threadId) | ~0.2MB (2400 虚拟节点) |

**总计**: ~20MB (50,000 个服务实例)

### 索引设计

无额外索引。查询通过以下方式：
- **按 ServiceId 查询**: O(1) 通过 ConcurrentDictionary
- **按 ServiceName 过滤**: O(n) 扫描（诊断端点查询，非热路径）
- **按 HealthState 过滤**: O(n) 扫描（诊断端点查询，非热路径）

---

## 验证规则总结

| 实体/属性 | 验证规则 | 错误消息 |
|----------|---------|---------|
| ServiceName | 非空，最大 200 字符，正则 `^[a-zA-Z][a-zA-Z0-9]*$` | "Invalid ServiceName format" |
| ServiceId | 非空，最大 1000 字符，正则 `^[a-zA-Z0-9\-:_]+$` | "Invalid ServiceId format" |
| WorkerThreadCount | 1-64 | "WorkerThreadCount must be between 1 and 64" |
| FailureThreshold | 1-10 | "FailureThreshold must be between 1 and 10" |
| CoolingPeriod | >= 10 秒 | "CoolingPeriod must be at least 10 seconds" |

---

## 迁移策略

### 从 IPulseHub 到 IPulseHub + IPulseService

**步骤 1**: 添加 IPulseService 接口实现

```csharp
// 原实现
public class MyService : IPulseHub { ... }

// 迁移后
public class MyService : IPulseHub, IPulseService
{
    public string ServiceName => "MyService";
    public string ServiceId { get; }

    public MyService(string instanceId)
    {
        ServiceId = $"MyService:{instanceId}";
    }
    // 其他方法保持不变
}
```

**步骤 2**: 无需更改服务方法签名或业务逻辑

**步骤 3**: 配置调度器选项（可选）

```csharp
services.Configure<ServiceSchedulingOptions>(options =>
{
    options.WorkerThreadCount = 16;
    options.IdleInstanceTimeout = TimeSpan.FromMinutes(10);
});
```

**兼容性保证**: IPulseHub-only 服务继续使用默认线程池行为，无破坏性变更。
