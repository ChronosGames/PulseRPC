# PulseRPC 优雅关闭（Graceful Shutdown）

## 概述

优雅关闭功能确保服务器节点在停止时能够妥善处理所有进行中的请求，保存状态，并通知客户端和其他节点，避免服务中断。

## 核心特性

✅ **分阶段关闭流程** - 5个明确定义的关闭阶段
✅ **客户端通知** - 提前通知客户端迁移到其他节点
✅ **请求排空** - 等待现有请求完成
✅ **状态保存** - 自动保存Service状态
✅ **健康检查集成** - 与负载均衡器无缝集成
✅ **超时控制** - 防止关闭流程无限等待
✅ **进度监控** - 实时查看关闭进度

## 关闭流程

### 5个阶段

```
1. PreparingShutdown     → 通知客户端和其他节点
2. RejectingNewConnections → 健康检查返回不健康状态
3. DrainingRequests      → 等待现有请求完成
4. SavingState           → 保存Service状态到数据库
5. CleaningUp            → 清理资源、注销节点
```

### 时间线示例（默认配置）

```
T+0s   : 接收到关闭信号
T+0s   : 阶段1 - 发送迁移通知给客户端
T+5s   : 阶段2 - 停止接受新连接（健康检查返回不健康）
T+7s   : 阶段3 - 等待请求完成（最多10秒）
T+17s  : 阶段4 - 保存Service状态（最多15秒）
T+32s  : 阶段5 - 清理资源
T+33s  : 关闭完成（总超时30秒）
```

## 快速开始

### 1. 添加服务

```csharp
// Startup.cs 或 Program.cs
var builder = WebApplication.CreateBuilder(args);

// 添加集群路由
builder.Services.AddClusterRouting(options =>
{
    options.NodeId = 1;
    options.EtcdEndpoints = new[] { "http://localhost:2379" };
});

// 添加优雅关闭支持
builder.Services.AddGracefulShutdown(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
    options.DrainTimeout = TimeSpan.FromSeconds(10);
    options.SaveStateTimeout = TimeSpan.FromSeconds(15);
    options.NotifyClientsBeforeShutdown = true;
    options.ClientNotificationLeadTime = TimeSpan.FromSeconds(5);
});

var app = builder.Build();
app.Run();
```

### 2. 使用默认配置

```csharp
// 使用默认配置（推荐）
builder.Services.AddGracefulShutdown();
```

### 3. 配置健康检查端点

```csharp
// 配置健康检查端点（供负载均衡器使用）
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
```

## 使用示例

### 示例1：手动触发优雅关闭

```csharp
public class MaintenanceController : ControllerBase
{
    private readonly IGracefulShutdownCoordinator _coordinator;

    public MaintenanceController(IGracefulShutdownCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    /// <summary>
    /// 触发优雅关闭（用于计划内维护）
    /// </summary>
    [HttpPost("shutdown")]
    public async Task<IActionResult> InitiateShutdown(
        [FromBody] ShutdownRequest request)
    {
        await _coordinator.InitiateShutdownAsync(
            reason: request.Reason,
            cancellationToken: HttpContext.RequestAborted);

        return Ok(new { message = "优雅关闭已启动" });
    }

    /// <summary>
    /// 查询关闭进度
    /// </summary>
    [HttpGet("shutdown/progress")]
    public IActionResult GetShutdownProgress()
    {
        if (!_coordinator.IsShuttingDown)
        {
            return Ok(new { isShuttingDown = false });
        }

        var progress = _coordinator.GetProgress();
        return Ok(progress);
    }
}

public class ShutdownRequest
{
    public string Reason { get; set; } = "Manual shutdown";
}
```

### 示例2：监听请求生命周期

```csharp
public class RequestTrackingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IGracefulShutdownCoordinator _coordinator;

    public RequestTrackingMiddleware(
        RequestDelegate next,
        IGracefulShutdownCoordinator coordinator)
    {
        _next = next;
        _coordinator = coordinator;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 检查是否可以接受新请求
        if (!_coordinator.CanAcceptNewConnections())
        {
            context.Response.StatusCode = 503; // Service Unavailable
            await context.Response.WriteAsync("Server is shutting down");
            return;
        }

        // 注册请求
        _coordinator.RegisterPendingRequest();
        try
        {
            await _next(context);
        }
        finally
        {
            // 标记请求完成
            _coordinator.MarkRequestCompleted();
        }
    }
}

// 注册中间件
app.UseMiddleware<RequestTrackingMiddleware>();
```

### 示例3：Service状态保存

```csharp
public class PlayerService : BaseService
{
    private readonly ILogger<PlayerService> _logger;
    private readonly IMongoDatabase _database;

    public async Task SaveStateAsync()
    {
        _logger.LogInformation("保存玩家状态...");

        var playerData = new PlayerData
        {
            PlayerId = this.PlayerId,
            Position = this.Position,
            Inventory = this.Inventory,
            // ... 其他状态
            LastSavedAt = DateTime.UtcNow
        };

        await _database
            .GetCollection<PlayerData>("players")
            .ReplaceOneAsync(
                p => p.PlayerId == this.PlayerId,
                playerData,
                new ReplaceOptions { IsUpsert = true });

        _logger.LogInformation("玩家状态已保存");
    }

    // 在优雅关闭时自动调用
    public override async ValueTask DisposeAsync()
    {
        await SaveStateAsync();
        await base.DisposeAsync();
    }
}
```

## 配置参考

### GracefulShutdownOptions

```csharp
public class GracefulShutdownOptions
{
    /// <summary>
    /// 优雅关闭超时时间（默认30秒）
    /// 如果超过此时间，将执行强制关闭
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 排空请求的最大等待时间（默认10秒）
    /// </summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 保存Service状态的超时时间（默认15秒）
    /// </summary>
    public TimeSpan SaveStateTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 是否在关闭前通知客户端迁移（默认true）
    /// </summary>
    public bool NotifyClientsBeforeShutdown { get; set; } = true;

    /// <summary>
    /// 客户端迁移通知的提前时间（默认5秒）
    /// </summary>
    public TimeSpan ClientNotificationLeadTime { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 是否自动保存Service状态（默认true）
    /// </summary>
    public bool AutoSaveServiceState { get; set; } = true;

    /// <summary>
    /// 是否清理固定映射（默认true）
    /// </summary>
    public bool CleanupFixedMappings { get; set; } = true;

    /// <summary>
    /// 健康检查不健康状态延迟时间（默认2秒）
    /// </summary>
    public TimeSpan HealthCheckUnhealthyDelay { get; set; } = TimeSpan.FromSeconds(2);
}
```

## 与Kubernetes集成

### 1. 配置探针

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: game-server
spec:
  containers:
  - name: server
    image: game-server:latest
    ports:
    - containerPort: 8080

    # 存活探针（检测是否需要重启）
    livenessProbe:
      httpGet:
        path: /health/live
        port: 8080
      initialDelaySeconds: 30
      periodSeconds: 10
      timeoutSeconds: 5
      failureThreshold: 3

    # 就绪探针（检测是否可以接收流量）
    readinessProbe:
      httpGet:
        path: /health/ready
        port: 8080
      initialDelaySeconds: 5
      periodSeconds: 5
      timeoutSeconds: 3
      failureThreshold: 2

    # 优雅关闭配置
    lifecycle:
      preStop:
        exec:
          # 给予30秒时间完成优雅关闭
          command: ["/bin/sh", "-c", "sleep 30"]

    # 确保有足够时间完成关闭
    terminationGracePeriodSeconds: 60
```

### 2. 滚动更新策略

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: game-server
spec:
  replicas: 3
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxSurge: 1        # 最多比期望多1个Pod
      maxUnavailable: 0  # 保证始终有3个Pod可用
  template:
    # ... pod配置
```

## 与负载均衡器集成

### Nginx配置示例

```nginx
upstream game_servers {
    # 定期健康检查
    server server1:8080 max_fails=2 fail_timeout=10s;
    server server2:8080 max_fails=2 fail_timeout=10s;
    server server3:8080 max_fails=2 fail_timeout=10s;

    # 使用IP哈希保持会话亲和性
    ip_hash;
}

server {
    listen 80;

    location / {
        proxy_pass http://game_servers;

        # 健康检查
        health_check uri=/health/ready interval=5s fails=2 passes=2;

        # 连接超时配置
        proxy_connect_timeout 5s;
        proxy_read_timeout 60s;
        proxy_send_timeout 60s;
    }
}
```

## 监控和日志

### 日志示例

```
[WARN] 开始优雅关闭流程, 原因: Planned maintenance
[INFO] 阶段1: 准备关闭 - 通知客户端和其他节点
[INFO] 通知客户端迁移到节点: 2,3,4
[INFO] 通知集群节点 1 即将下线
[INFO] 关闭状态变更: RejectingNewConnections
[INFO] 阶段2: 拒绝新连接 - 健康检查将返回不健康状态
[INFO] 关闭状态变更: DrainingRequests
[INFO] 阶段3: 排空现有请求
[DEBUG] 等待请求完成... 剩余: 5, 已用时: 250ms
[DEBUG] 等待请求完成... 剩余: 2, 已用时: 1500ms
[INFO] 所有请求已完成
[INFO] 关闭状态变更: SavingState
[INFO] 阶段4: 保存Service状态
[INFO] Service状态保存完成
[INFO] 关闭状态变更: CleaningUp
[INFO] 阶段5: 清理资源
[INFO] 清理了 15 个固定映射
[INFO] 节点已从集群注销
[INFO] 资源清理完成
[INFO] 关闭状态变更: Shutdown
[INFO] 优雅关闭流程完成，总耗时: 12500ms
```

### Prometheus指标（建议实现）

```
# 关闭状态（0=Running, 1=Shutting Down）
pulserpc_shutdown_state{node="1"} 0

# 待完成请求数
pulserpc_pending_requests{node="1"} 5

# 关闭进度百分比
pulserpc_shutdown_progress{node="1"} 45

# 关闭总次数
pulserpc_shutdown_total{node="1",reason="planned"} 3

# 强制关闭次数（超时）
pulserpc_forced_shutdown_total{node="1"} 0
```

## 故障场景处理

### 场景1：关闭超时

```csharp
// 配置较短的超时时间
builder.Services.AddGracefulShutdown(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(15);  // 15秒后强制关闭
    options.DrainTimeout = TimeSpan.FromSeconds(5);      // 5秒后停止等待请求
});
```

**日志输出**：
```
[ERROR] 优雅关闭超时 (15秒)，执行强制关闭
[WARN] 排空请求超时，仍有 3 个待完成请求
[WARN] 执行强制关闭
```

### 场景2：紧急关闭

```csharp
// 直接调用强制关闭
await coordinator.ForceShutdownAsync();
```

### 场景3：节点故障

- **优雅关闭**：节点主动触发，有序清理
- **故障关闭**：节点直接宕机，依赖健康检查和其他节点接管

## 最佳实践

### 1. 超时配置

```csharp
// 根据实际业务调整超时时间
options.ShutdownTimeout = TimeSpan.FromMinutes(2);  // 复杂游戏可能需要更长时间
options.DrainTimeout = TimeSpan.FromSeconds(30);    // 根据平均请求时长设置
options.SaveStateTimeout = TimeSpan.FromSeconds(60); // 根据Service数量和状态大小设置
```

### 2. 状态保存

```csharp
// 定期保存状态，减少关闭时的保存时间
public class PlayerService
{
    private readonly Timer _autoSaveTimer;

    public PlayerService()
    {
        // 每5分钟自动保存一次
        _autoSaveTimer = new Timer(
            async _ => await SaveStateAsync(),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
    }
}
```

### 3. 客户端重连

```csharp
// 客户端实现自动重连逻辑
public class GameClient
{
    public async Task OnServerMigrationNotification(ClientMigrationInfo info)
    {
        _logger.LogInformation(
            "服务器即将关闭，推荐迁移到节点: {Nodes}, 剩余时间: {Seconds}秒",
            string.Join(",", info.RecommendedNodes),
            info.RemainingSeconds);

        // 选择新节点
        var targetNode = SelectBestNode(info.RecommendedNodes);

        // 保存当前状态
        await SaveClientStateAsync();

        // 断开当前连接
        await DisconnectAsync();

        // 重连到新节点
        await ConnectAsync(targetNode);

        // 恢复状态
        await RestoreClientStateAsync();
    }
}
```

### 4. 监控和告警

```csharp
// 记录关闭指标
public class ShutdownMetricsCollector
{
    public void RecordShutdown(ShutdownProgress progress)
    {
        // 记录关闭时长
        _metrics.RecordShutdownDuration(
            progress.State,
            (DateTime.UtcNow - progress.StartedAt).TotalSeconds);

        // 如果有错误，触发告警
        if (progress.Errors.Any())
        {
            _alerting.SendAlert(
                "Graceful shutdown had errors",
                string.Join(", ", progress.Errors));
        }
    }
}
```

## 故障排查

### Q: 健康检查仍然返回健康状态？

**A**: 检查探针配置和健康检查标签：
```csharp
// 确保健康检查注册了正确的标签
services.AddHealthChecks()
    .AddCheck<GracefulShutdownHealthCheck>(
        "graceful-shutdown",
        tags: new[] { "ready", "live" });  // ✅ 正确

// 确保探针检查正确的端点
readinessProbe:
  httpGet:
    path: /health/ready  # ✅ 正确，会检查graceful-shutdown
```

### Q: 请求一直无法排空？

**A**: 检查是否正确注册和完成请求：
```csharp
// 确保每个请求都有配对的注册和完成
_coordinator.RegisterPendingRequest();
try
{
    await ProcessRequestAsync();
}
finally
{
    _coordinator.MarkRequestCompleted();  // ✅ 必须在finally中
}
```

### Q: Service状态未保存？

**A**: 实现IAsyncDisposable并在Dispose中保存：
```csharp
public class MyService : BaseService, IAsyncDisposable
{
    public override async ValueTask DisposeAsync()
    {
        // ✅ 保存状态
        await SaveStateAsync();
        await base.DisposeAsync();
    }
}
```

## 总结

优雅关闭机制确保：

1. ✅ **零停机时间** - 通过负载均衡器平滑切换
2. ✅ **无请求丢失** - 等待所有请求完成
3. ✅ **状态一致性** - 自动保存Service状态
4. ✅ **客户端体验** - 提前通知，自动迁移
5. ✅ **可观测性** - 完整的日志和进度监控
6. ✅ **容错性** - 超时保护，强制关闭兜底

建议在生产环境中**必须启用**优雅关闭功能。
