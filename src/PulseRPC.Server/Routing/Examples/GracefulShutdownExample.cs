/*
 * 优雅关闭使用示例
 *
 * 注意：这些示例需要在ASP.NET Core Web应用中使用
 * 需要添加以下NuGet包：
 * - Microsoft.AspNetCore.App (Framework Reference)
 * - Microsoft.Extensions.Diagnostics.HealthChecks
 *
 * 使用方法：
 * 1. 创建一个ASP.NET Core Web应用
 * 2. 复制需要的示例代码到项目中
 * 3. 按照示例配置服务和中间件
 */

namespace PulseRPC.Server.Routing.Examples;

// ============================================
// 以下代码仅作为参考示例，不会被编译
// 请在ASP.NET Core应用中使用
// ============================================

#if FALSE  // 禁用编译

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// 优雅关闭使用示例
/// </summary>
public class GracefulShutdownExample
{
    /// <summary>
    /// 示例1：基本配置
    /// </summary>
    public static void Example1_BasicSetup()
    {
        var builder = WebApplication.CreateBuilder();

        // 添加集群路由
        builder.Services.AddClusterRouting(options =>
        {
            options.NodeId = 1;
            options.NodeName = "GameServer-01";
            options.EtcdEndpoints = new[] { "http://localhost:2379" };
        });

        // 添加优雅关闭（使用默认配置）
        builder.Services.AddGracefulShutdown();

        var app = builder.Build();

        // 配置健康检查端点
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live")
        });

        app.Run();
    }

    /// <summary>
    /// 示例2：自定义配置
    /// </summary>
    public static void Example2_CustomConfiguration()
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddClusterRouting(options =>
        {
            options.NodeId = 1;
            options.EtcdEndpoints = new[] { "http://localhost:2379" };
        });

        // 自定义优雅关闭配置
        builder.Services.AddGracefulShutdown(options =>
        {
            // 总超时时间（超过此时间将强制关闭）
            options.ShutdownTimeout = TimeSpan.FromMinutes(2);

            // 排空请求的最大等待时间
            options.DrainTimeout = TimeSpan.FromSeconds(30);

            // 保存Service状态的超时时间
            options.SaveStateTimeout = TimeSpan.FromMinutes(1);

            // 启用客户端迁移通知
            options.NotifyClientsBeforeShutdown = true;

            // 提前5秒通知客户端
            options.ClientNotificationLeadTime = TimeSpan.FromSeconds(5);

            // 自动保存Service状态
            options.AutoSaveServiceState = true;

            // 清理固定映射
            options.CleanupFixedMappings = true;

            // 健康检查延迟
            options.HealthCheckUnhealthyDelay = TimeSpan.FromSeconds(2);
        });

        var app = builder.Build();
        app.Run();
    }

    /// <summary>
    /// 示例3：手动触发关闭
    /// </summary>
    public class MaintenanceController : ControllerBase
    {
        private readonly IGracefulShutdownCoordinator _coordinator;
        private readonly ILogger<MaintenanceController> _logger;

        public MaintenanceController(
            IGracefulShutdownCoordinator coordinator,
            ILogger<MaintenanceController> logger)
        {
            _coordinator = coordinator;
            _logger = logger;
        }

        /// <summary>
        /// POST /api/maintenance/shutdown
        /// 触发优雅关闭（用于计划内维护）
        /// </summary>
        [HttpPost("shutdown")]
        public async Task<IActionResult> InitiateShutdown([FromBody] ShutdownRequest request)
        {
            _logger.LogWarning("收到手动关闭请求: {Reason}", request.Reason);

            // 异步触发关闭流程
            _ = Task.Run(async () =>
            {
                await _coordinator.InitiateShutdownAsync(
                    reason: request.Reason ?? "Manual maintenance shutdown",
                    cancellationToken: CancellationToken.None);
            });

            return Accepted(new
            {
                message = "优雅关闭已启动",
                reason = request.Reason
            });
        }

        /// <summary>
        /// GET /api/maintenance/shutdown/progress
        /// 查询关闭进度
        /// </summary>
        [HttpGet("shutdown/progress")]
        public IActionResult GetShutdownProgress()
        {
            if (!_coordinator.IsShuttingDown)
            {
                return Ok(new
                {
                    isShuttingDown = false,
                    state = "Running"
                });
            }

            var progress = _coordinator.GetProgress();
            return Ok(new
            {
                isShuttingDown = true,
                state = progress.State.ToString(),
                completionPercentage = progress.CompletionPercentage,
                currentStep = progress.CurrentStep,
                pendingRequests = progress.PendingRequests,
                startedAt = progress.StartedAt,
                estimatedCompletionTime = progress.EstimatedCompletionTime,
                errors = progress.Errors
            });
        }

        /// <summary>
        /// POST /api/maintenance/shutdown/force
        /// 强制关闭（紧急情况）
        /// </summary>
        [HttpPost("shutdown/force")]
        public async Task<IActionResult> ForceShutdown()
        {
            _logger.LogError("收到强制关闭请求");

            await _coordinator.ForceShutdownAsync();

            return Ok(new { message = "强制关闭已执行" });
        }
    }

    public class ShutdownRequest
    {
        public string? Reason { get; set; }
    }

    /// <summary>
    /// 示例4：请求跟踪中间件
    /// </summary>
    public class RequestTrackingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IGracefulShutdownCoordinator _coordinator;
        private readonly ILogger<RequestTrackingMiddleware> _logger;

        public RequestTrackingMiddleware(
            RequestDelegate next,
            IGracefulShutdownCoordinator coordinator,
            ILogger<RequestTrackingMiddleware> logger)
        {
            _next = next;
            _coordinator = coordinator;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 检查是否可以接受新请求
            if (!_coordinator.CanAcceptNewConnections())
            {
                _logger.LogWarning("拒绝新请求，服务器正在关闭");

                context.Response.StatusCode = 503; // Service Unavailable
                context.Response.Headers["Retry-After"] = "60"; // 建议60秒后重试

                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Server is shutting down",
                    message = "Please retry with another server",
                    retryAfterSeconds = 60
                });
                return;
            }

            // 注册请求
            _coordinator.RegisterPendingRequest();
            try
            {
                // 处理请求
                await _next(context);
            }
            finally
            {
                // 标记请求完成
                _coordinator.MarkRequestCompleted();
            }
        }
    }

    /// <summary>
    /// 示例5：完整的应用程序配置
    /// </summary>
    public static void Example5_CompleteSetup()
    {
        var builder = WebApplication.CreateBuilder();

        // 配置日志
        builder.Logging.AddConsole();

        // 添加集群路由
        builder.Services.AddClusterRouting(options =>
        {
            options.NodeId = 1;
            options.NodeName = Environment.MachineName;
            options.EtcdEndpoints = new[] { "http://etcd-server:2379" };
            options.FixedMappingTTL = TimeSpan.FromHours(24);
        });

        // 添加优雅关闭
        builder.Services.AddGracefulShutdown(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
            options.DrainTimeout = TimeSpan.FromSeconds(10);
        });

        // 添加控制器
        builder.Services.AddControllers();

        var app = builder.Build();

        // 使用请求跟踪中间件
        app.UseMiddleware<RequestTrackingMiddleware>();

        // 配置健康检查端点
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var result = System.Text.Json.JsonSerializer.Serialize(new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.Select(e => new
                    {
                        name = e.Key,
                        status = e.Value.Status.ToString(),
                        description = e.Value.Description,
                        data = e.Value.Data
                    })
                });
                await context.Response.WriteAsync(result);
            }
        });

        app.MapHealthChecks("/health/live");

        // 映射控制器
        app.MapControllers();

        // 监听关闭事件
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Register(() =>
        {
            Console.WriteLine("应用正在停止...");
        });

        lifetime.ApplicationStopped.Register(() =>
        {
            Console.WriteLine("应用已停止");
        });

        app.Run();
    }

    /// <summary>
    /// 示例6：监控关闭进度
    /// </summary>
    public class ShutdownMonitor : IHostedService
    {
        private readonly IGracefulShutdownCoordinator _coordinator;
        private readonly ILogger<ShutdownMonitor> _logger;
        private Timer? _monitorTimer;

        public ShutdownMonitor(
            IGracefulShutdownCoordinator coordinator,
            ILogger<ShutdownMonitor> logger)
        {
            _coordinator = coordinator;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // 每秒检查一次关闭状态
            _monitorTimer = new Timer(
                MonitorShutdownProgress,
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1));

            return Task.CompletedTask;
        }

        private void MonitorShutdownProgress(object? state)
        {
            if (!_coordinator.IsShuttingDown)
            {
                return;
            }

            var progress = _coordinator.GetProgress();

            _logger.LogInformation(
                "关闭进度: {State} - {Percentage}% - {Step} - 待处理请求: {Requests}",
                progress.State,
                progress.CompletionPercentage,
                progress.CurrentStep,
                progress.PendingRequests);

            // 如果有错误，记录日志
            if (progress.Errors.Any())
            {
                foreach (var error in progress.Errors)
                {
                    _logger.LogError("关闭错误: {Error}", error);
                }
            }

            // 关闭完成后停止监控
            if (progress.State == ShutdownState.Shutdown)
            {
                _monitorTimer?.Dispose();
                _logger.LogInformation("关闭监控已停止");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _monitorTimer?.Dispose();
            return Task.CompletedTask;
        }
    }
}

#endif  // FALSE

