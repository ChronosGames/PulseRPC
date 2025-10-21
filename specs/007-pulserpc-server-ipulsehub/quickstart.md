# 快速开始：服务线程调度与灾难隔离

**功能**: IPulseService 服务实例线程调度
**日期**: 2025-10-21
**面向用户**: 服务开发者和运维工程师

## 概述

本指南帮助您快速上手使用 IPulseService 接口实现服务实例的线程调度控制和灾难隔离。

**核心价值**:
- ✅ 同一服务实例的所有请求在专用线程顺序执行（无需手动加锁）
- ✅ 故障服务实例自动隔离，不影响其他实例
- ✅ 100% 向后兼容现有 IPulseHub 服务
- ✅ 实时监控指标和健康状态查询

---

## 前置条件

- .NET 8.0 SDK 或更高版本
- PulseRPC.Server 0.8.7 或更高版本
- 基本了解 PulseRPC 服务开发

---

## 快速开始：3 步启用线程调度

### 步骤 1: 实现 IPulseService 接口

在现有服务类中添加 `IPulseService` 接口实现：

```csharp
using PulseRPC.Server.Abstractions;

// 原实现（仅 IPulseHub）
public class ChatRoomService : IPulseHub
{
    private readonly string _roomId;

    public ChatRoomService(string roomId)
    {
        _roomId = roomId;
    }

    public async Task<string> SendMessageAsync(string message)
    {
        // 业务逻辑...
        return $"Message sent to room {_roomId}";
    }
}

// 迁移后（IPulseHub + IPulseService）
public class ChatRoomService : IPulseHub, IPulseService
{
    private readonly string _roomId;

    public ChatRoomService(string roomId)
    {
        _roomId = roomId;
    }

    // 新增：IPulseService 接口实现
    public string ServiceName => "ChatRoom";
    public string ServiceId { get; }

    // 在构造函数中生成 ServiceId
    public ChatRoomService(string roomId)
    {
        _roomId = roomId;
        ServiceId = $"ChatRoom:{roomId}"; // 格式: ServiceName:业务ID
    }

    // 业务方法保持不变
    public async Task<string> SendMessageAsync(string message)
    {
        // 业务逻辑无需修改...
        return $"Message sent to room {_roomId}";
    }
}
```

**关键点**:
- `ServiceName`: 服务类型名称（例如 "ChatRoom"）
- `ServiceId`: 唯一实例标识符（例如 "ChatRoom:room-123"）
- ServiceId 必须在构造函数中初始化，之后不可更改

### 步骤 2: 配置服务调度器（可选）

在 `Program.cs` 或 DI 配置中添加调度器选项：

```csharp
using PulseRPC.Server.Configuration;

var builder = WebApplication.CreateBuilder(args);

// 配置服务调度器
builder.Services.Configure<ServiceSchedulingOptions>(options =>
{
    options.WorkerThreadCount = 16;                          // 工作线程数（默认: CPU 核心数）
    options.IdleInstanceTimeout = TimeSpan.FromMinutes(10);  // 空闲超时（默认: 5 分钟）
    options.VirtualNodesPerThread = 150;                     // 一致性哈希虚拟节点（默认: 150）
});

// 配置健康监控
builder.Services.Configure<HealthMonitorOptions>(options =>
{
    options.FailureThreshold = 3;                            // 隔离阈值（默认: 3 次超时）
    options.CoolingPeriod = TimeSpan.FromMinutes(1);         // 冷却期（默认: 1 分钟）
    options.ProbeRequestLimit = 5;                           // 探测请求限制（默认: 5）
    options.ProbeSuccessThreshold = 3;                       // 恢复阈值（默认: 3 次成功）
});

var app = builder.Build();
```

### 步骤 3: 启动服务器并测试

无需修改客户端代码！客户端仍然像往常一样调用服务：

```csharp
// 客户端代码保持不变
var client = new ChatRoomServiceClient(connection);
var response = await client.SendMessageAsync("Hello, world!");
```

**自动行为**:
- 对 ServiceId="ChatRoom:room-123" 的所有请求自动路由到同一线程
- 如果该服务实例出现 3 次连续超时，自动进入隔离状态
- 1 分钟冷却期后自动尝试恢复

---

## ServiceId 生成最佳实践

### 推荐模式

| 模式 | 示例 | 适用场景 |
|------|------|---------|
| **ServiceName:业务ID** | `ChatRoom:room-123` | 大多数场景（推荐） |
| **ServiceName:业务ID:分片键** | `ChatRoom:room-123:server1` | 多服务器分片 |
| **ServiceName:组合键** | `Order:user-456:order-789` | 复杂业务键 |

### 代码示例

```csharp
// 示例 1: 聊天室服务（按房间 ID）
public class ChatRoomService : IPulseHub, IPulseService
{
    public string ServiceName => "ChatRoom";
    public string ServiceId { get; }

    public ChatRoomService(string roomId)
    {
        ServiceId = $"ChatRoom:{roomId}";
    }
}

// 示例 2: 订单处理服务（按用户 ID）
public class OrderProcessorService : IPulseHub, IPulseService
{
    public string ServiceName => "OrderProcessor";
    public string ServiceId { get; }

    public OrderProcessorService(string userId)
    {
        ServiceId = $"OrderProcessor:{userId}";
    }
}

// 示例 3: 游戏房间服务（按游戏 ID + 房间 ID）
public class GameRoomService : IPulseHub, IPulseService
{
    public string ServiceName => "GameRoom";
    public string ServiceId { get; }

    public GameRoomService(string gameId, string roomId)
    {
        ServiceId = $"GameRoom:{gameId}:{roomId}";
    }
}
```

### 验证规则

```csharp
using PulseRPC.Server.Validation;

public ChatRoomService(string roomId)
{
    ServiceId = $"ChatRoom:{roomId}";

    // 自动验证（在生产代码中会自动执行）
    ServiceIdValidator.Validate(ServiceId); // 抛出异常如果无效

    // 手动检查示例
    if (ServiceId.Length > 1000)
        throw new ArgumentException("ServiceId exceeds 1000 characters");
}
```

---

## 监控与诊断

### 查看服务实例指标

通过 HTTP 诊断端点查询实时指标：

```bash
# 查看所有服务实例
curl http://localhost:8080/diagnostics/metrics

# 按服务类型过滤
curl http://localhost:8080/diagnostics/metrics?serviceName=ChatRoom

# 查看特定实例
curl http://localhost:8080/diagnostics/metrics?serviceId=ChatRoom:room-123
```

**响应示例**:
```json
{
  "timestamp": "2025-10-21T12:00:00Z",
  "activeInstances": 125,
  "totalThreads": 16,
  "instances": [
    {
      "serviceName": "ChatRoom",
      "serviceId": "ChatRoom:room-123",
      "healthState": "Healthy",
      "totalRequests": 1000,
      "successRate": 0.998,
      "assignedThreadId": 5,
      "lastActivityUtc": "2025-10-21T11:59:50Z"
    }
  ]
}
```

### 查看健康状态汇总

```bash
curl http://localhost:8080/diagnostics/health
```

**响应示例**:
```json
{
  "status": "Healthy",
  "totalInstances": 125,
  "healthyInstances": 120,
  "isolatedInstances": 3,
  "coolingDownInstances": 2,
  "details": {
    "threadPoolUtilization": 0.78,
    "queueDepth": 15
  }
}
```

### 手动重置隔离的服务实例

```bash
# 重置特定实例的健康状态
curl -X POST http://localhost:8080/diagnostics/instances/ChatRoom:room-123/reset
```

---

## 向后兼容性

### 迁移策略

**无需修改现有服务**:
- 仅实现 IPulseHub 的服务继续使用默认线程池行为
- 无破坏性变更，无需重新编译客户端代码

**渐进式迁移**:
```csharp
// 阶段 1: 保持现有服务不变
public class LegacyService : IPulseHub { ... } // 继续工作

// 阶段 2: 新服务使用 IPulseService
public class NewService : IPulseHub, IPulseService { ... }

// 阶段 3: 逐步迁移旧服务
public class LegacyService : IPulseHub, IPulseService { ... }
```

**混合部署**:
- 同一服务器可以同时运行 IPulseHub-only 和 IPulseService 服务
- 系统自动检测接口类型并选择合适的调度策略

---

## 常见问题

### Q1: 如何确定 ServiceId 是否合适？

**A**: 遵循以下原则：
- ✅ 相同 ServiceId 的请求应该顺序执行（例如同一房间的消息）
- ✅ 不同 ServiceId 可以并发执行（例如不同房间的消息）
- ❌ 避免所有请求使用相同的 ServiceId（会变成单线程瓶颈）

### Q2: ServiceId 可以动态改变吗？

**A**: 不可以。ServiceId 必须在服务实例初始化时确定，之后不可更改。如果需要改变身份，应该创建新的服务实例。

### Q3: 如何处理服务实例被隔离的情况？

**A**: 系统会自动处理：
1. 连续 3 次超时 → 自动进入隔离状态
2. 等待 1 分钟冷却期
3. 允许 5 次探测请求测试恢复
4. 如果 3/5 成功 → 自动恢复为健康状态
5. 如果探测失败 → 重新进入隔离状态

**手动干预**:
```bash
# 立即重置隔离状态（运维操作）
curl -X POST http://localhost:8080/diagnostics/instances/{serviceId}/reset
```

### Q4: 如何避免线程分布不均？

**A**: 系统使用一致性哈希算法自动平衡：
- 默认 150 虚拟节点/线程，分布标准差 ~2.1%
- 负载偏差 <±3%
- 无需手动干预

### Q5: 性能开销有多大？

**A**: 极低开销：
- ServiceId 哈希计算: ~15ns
- 线程映射查找: ~50ns
- 总开销 <0.1% (相比典型 RPC 调用 >1ms)

### Q6: 如何在日志中查看线程分配？

**A**: 启用日志记录：
```csharp
builder.Services.AddLogging(logging =>
{
    logging.AddFilter("PulseRPC.Server.Scheduling", LogLevel.Debug);
});
```

**日志示例**:
```
[2025-10-21 12:00:00] INFO: Service instance ChatRoom:room-123 assigned to thread 5
[2025-10-21 12:05:00] INFO: Service instance ChatRoom:room-123 marked as Isolated
[2025-10-21 12:06:00] INFO: Service instance ChatRoom:room-123 recovered to Healthy
```

---

## 性能调优

### 调整工作线程数

```csharp
// 根据负载调整线程数
builder.Services.Configure<ServiceSchedulingOptions>(options =>
{
    // CPU 密集型负载: 使用 CPU 核心数
    options.WorkerThreadCount = Environment.ProcessorCount;

    // I/O 密集型负载: 使用 2-4 倍 CPU 核心数
    options.WorkerThreadCount = Environment.ProcessorCount * 2;

    // 最大限制: 64 线程
    options.WorkerThreadCount = Math.Min(64, Environment.ProcessorCount * 4);
});
```

### 调整健康检测阈值

```csharp
// 降低灵敏度（减少误报）
builder.Services.Configure<HealthMonitorOptions>(options =>
{
    options.FailureThreshold = 5;                    // 5 次超时才隔离
    options.CoolingPeriod = TimeSpan.FromMinutes(2); // 更长的冷却期
});

// 提高灵敏度（快速隔离故障）
builder.Services.Configure<HealthMonitorOptions>(options =>
{
    options.FailureThreshold = 2;                     // 2 次超时即隔离
    options.CoolingPeriod = TimeSpan.FromSeconds(30); // 更短的冷却期
});
```

---

## 故障排查

### 问题: 服务实例频繁被隔离

**可能原因**:
- 业务逻辑超时阈值设置过低
- 数据库或外部依赖响应慢

**解决方案**:
```csharp
// 方案 1: 增加超时阈值
builder.Services.Configure<ServiceInvokerOptions>(options =>
{
    options.DefaultTimeout = TimeSpan.FromSeconds(60); // 从 30s 增加到 60s
});

// 方案 2: 降低隔离灵敏度
builder.Services.Configure<HealthMonitorOptions>(options =>
{
    options.FailureThreshold = 5; // 从 3 次增加到 5 次
});
```

### 问题: 某些服务实例负载过高

**可能原因**:
- ServiceId 生成规则导致哈希冲突
- 业务数据分布不均（热点实例）

**解决方案**:
```csharp
// 检查 ServiceId 分布
var metrics = await httpClient.GetFromJsonAsync<MetricsResponse>(
    "http://localhost:8080/diagnostics/metrics"
);

// 查看每个线程的负载
foreach (var (threadId, count) in metrics.ThreadWorkloads)
{
    Console.WriteLine($"Thread {threadId}: {count} instances");
}

// 如果分布不均，考虑调整 ServiceId 生成规则或增加虚拟节点
builder.Services.Configure<ServiceSchedulingOptions>(options =>
{
    options.VirtualNodesPerThread = 200; // 从 150 增加到 200
});
```

### 问题: 监控端点无响应

**可能原因**:
- 端点未启用或端口配置错误

**解决方案**:
```csharp
// 确保诊断端点已注册
var app = builder.Build();

app.MapGet("/diagnostics/metrics", async (IServiceMetricsProvider metricsProvider) =>
{
    return await metricsProvider.GetMetricsAsync();
});

app.MapGet("/diagnostics/health", async (IServiceHealthProvider healthProvider) =>
{
    return await healthProvider.GetHealthAsync();
});

app.Run();
```

---

## 下一步

- 📖 阅读 [数据模型设计文档](./data-model.md) 了解内部实现
- 📖 阅读 [技术调研报告](./research.md) 了解设计决策
- 📖 查看 [API 契约定义](./contracts/) 了解接口规范
- 🔧 参与 [GitHub 讨论](https://github.com/ChronosGames/PulseRPC/discussions) 提问或分享经验

---

**版本**: 1.0.0 | **最后更新**: 2025-10-21
