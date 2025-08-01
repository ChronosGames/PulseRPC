using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameApp.Infrastructure.Performance
{
    /// <summary>
    /// 性能优化后台服务 - 定期执行性能优化任务
    /// </summary>
    public class PerformanceOptimizationBackgroundService : BackgroundService
    {
        private readonly ILogger<PerformanceOptimizationBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;

        // 优化任务执行间隔
        private static readonly TimeSpan IndexOptimizationInterval = TimeSpan.FromHours(6);  // 每6小时优化一次索引
        private static readonly TimeSpan DataCleanupInterval = TimeSpan.FromHours(1);        // 每小时清理一次过期数据
        private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(5);      // 每5分钟检查一次健康状态

        private DateTime _lastIndexOptimization = DateTime.MinValue;
        private DateTime _lastDataCleanup = DateTime.MinValue;
        private DateTime _lastHealthCheck = DateTime.MinValue;

        public PerformanceOptimizationBackgroundService(
            ILogger<PerformanceOptimizationBackgroundService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("性能优化后台服务已启动");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;

                    // 创建服务作用域
                    using var scope = _serviceProvider.CreateScope();
                    var databasePerformanceService = scope.ServiceProvider.GetService<IDatabasePerformanceService>();
                    var performanceService = scope.ServiceProvider.GetRequiredService<IPerformanceService>();

                    // 执行健康检查
                    if (now - _lastHealthCheck >= HealthCheckInterval)
                    {
                        await PerformHealthCheckAsync(performanceService);
                        _lastHealthCheck = now;
                    }

                    // 执行数据清理
                    if (now - _lastDataCleanup >= DataCleanupInterval)
                    {
                        if (databasePerformanceService != null)
                        {
                            await PerformDataCleanupAsync(databasePerformanceService);
                        }
                        _lastDataCleanup = now;
                    }

                    // 执行索引优化
                    if (now - _lastIndexOptimization >= IndexOptimizationInterval)
                    {
                        if (databasePerformanceService != null)
                        {
                            await PerformIndexOptimizationAsync(databasePerformanceService);
                        }
                        _lastIndexOptimization = now;
                    }

                    // 等待下一次检查（每分钟检查一次是否需要执行任务）
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，退出循环
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "性能优化后台服务执行出错");

                    // 出错时等待较长时间再重试
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }

            _logger.LogInformation("性能优化后台服务已停止");
        }

        private async Task PerformHealthCheckAsync(IPerformanceService performanceService)
        {
            try
            {
                _logger.LogDebug("执行系统健康检查...");

                var resources = performanceService.GetSystemResourceUsage();
                var stats = await performanceService.GetStatsAsync(TimeSpan.FromMinutes(5));

                // 记录系统资源使用情况
                await performanceService.RecordMetricAsync("system.cpu_usage", resources.CpuUsagePercent);
                await performanceService.RecordMetricAsync("system.memory_usage", resources.MemoryUsagePercent);
                await performanceService.RecordMetricAsync("system.thread_count", resources.ThreadCount);
                await performanceService.RecordMetricAsync("system.gc_collections", resources.GcCollectionCount);

                // 检查警告条件
                await CheckResourceWarningsAsync(resources, performanceService);
                await CheckPerformanceWarningsAsync(stats, performanceService);

                _logger.LogDebug("系统健康检查完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "系统健康检查失败");
            }
        }

        private async Task PerformDataCleanupAsync(IDatabasePerformanceService databasePerformanceService)
        {
            try
            {
                _logger.LogDebug("执行数据清理任务...");

                await databasePerformanceService.CleanupExpiredRedisDataAsync();

                _logger.LogDebug("数据清理任务完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据清理任务失败");
            }
        }

        private async Task PerformIndexOptimizationAsync(IDatabasePerformanceService databasePerformanceService)
        {
            try
            {
                _logger.LogInformation("执行数据库索引优化...");

                await databasePerformanceService.OptimizeMongoIndexesAsync();

                _logger.LogInformation("数据库索引优化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据库索引优化失败");
            }
        }

        private async Task CheckResourceWarningsAsync(SystemResourceUsage resources, IPerformanceService performanceService)
        {
            // CPU使用率警告
            if (resources.CpuUsagePercent > 80)
            {
                _logger.LogWarning("高CPU使用率警告: {CpuUsage}%", resources.CpuUsagePercent);
                await performanceService.RecordMetricAsync("alert.high_cpu", resources.CpuUsagePercent);
            }

            // 内存使用率警告
            if (resources.MemoryUsagePercent > 85)
            {
                _logger.LogWarning("高内存使用率警告: {MemoryUsage}%", resources.MemoryUsagePercent);
                await performanceService.RecordMetricAsync("alert.high_memory", resources.MemoryUsagePercent);

                // 触发垃圾回收
                _logger.LogInformation("触发垃圾回收以释放内存");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            // 线程数警告
            if (resources.ThreadCount > 200)
            {
                _logger.LogWarning("高线程数警告: {ThreadCount}", resources.ThreadCount);
                await performanceService.RecordMetricAsync("alert.high_thread_count", resources.ThreadCount);
            }
        }

        private async Task CheckPerformanceWarningsAsync(PerformanceStats stats, IPerformanceService performanceService)
        {
            // 错误率警告
            if (stats.ErrorRate > 0.05) // 5%错误率
            {
                _logger.LogWarning("高错误率警告: {ErrorRate:P2}", stats.ErrorRate);
                await performanceService.RecordMetricAsync("alert.high_error_rate", stats.ErrorRate * 100);
            }

            // 响应时间警告
            if (stats.AverageResponseTime > 1000) // 1秒
            {
                _logger.LogWarning("高响应时间警告: {ResponseTime}ms", stats.AverageResponseTime);
                await performanceService.RecordMetricAsync("alert.high_response_time", stats.AverageResponseTime);
            }

            // 检查各个操作的性能
            foreach (var operation in stats.Operations.Values)
            {
                // P95响应时间警告
                if (operation.P95ResponseTime > 2000) // 2秒
                {
                    _logger.LogWarning("操作 {Operation} P95响应时间过高: {P95ResponseTime}ms",
                        operation.Name, operation.P95ResponseTime);

                    await performanceService.RecordMetricAsync($"alert.high_p95.{operation.Name}", operation.P95ResponseTime);
                }

                // 操作错误率警告
                if (operation.ErrorRate > 0.1) // 10%错误率
                {
                    _logger.LogWarning("操作 {Operation} 错误率过高: {ErrorRate:P2}",
                        operation.Name, operation.ErrorRate);

                    await performanceService.RecordMetricAsync($"alert.high_error_rate.{operation.Name}", operation.ErrorRate * 100);
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("正在停止性能优化后台服务...");
            await base.StopAsync(cancellationToken);
        }
    }
}
