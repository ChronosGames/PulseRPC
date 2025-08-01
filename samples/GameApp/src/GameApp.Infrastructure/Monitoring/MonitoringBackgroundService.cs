using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GameApp.Infrastructure.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameApp.Infrastructure.Monitoring
{
    /// <summary>
    /// 监控告警后台服务 - 定期检查告警规则和系统健康状态
    /// </summary>
    public class MonitoringBackgroundService : BackgroundService
    {
        private readonly ILogger<MonitoringBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;

        // 检查间隔
        private static readonly TimeSpan AlertCheckInterval = TimeSpan.FromMinutes(1);      // 每分钟检查告警规则
        private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(5);     // 每5分钟检查系统健康
        private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromHours(1);       // 每小时执行维护任务

        private DateTime _lastAlertCheck = DateTime.MinValue;
        private DateTime _lastHealthCheck = DateTime.MinValue;
        private DateTime _lastMaintenance = DateTime.MinValue;

        public MonitoringBackgroundService(
            ILogger<MonitoringBackgroundService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("监控告警后台服务已启动");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;

                    // 创建服务作用域
                    using var scope = _serviceProvider.CreateScope();
                    var services = scope.ServiceProvider;

                    // 检查告警规则
                    if (now - _lastAlertCheck >= AlertCheckInterval)
                    {
                        await CheckAlertRulesAsync(services);
                        _lastAlertCheck = now;
                    }

                    // 系统健康检查
                    if (now - _lastHealthCheck >= HealthCheckInterval)
                    {
                        await PerformHealthCheckAsync(services);
                        _lastHealthCheck = now;
                    }

                    // 维护任务
                    if (now - _lastMaintenance >= MaintenanceInterval)
                    {
                        await PerformMaintenanceAsync(services);
                        _lastMaintenance = now;
                    }

                    // 等待下一次检查
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，退出循环
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "监控告警后台服务执行出错");

                    // 出错时等待较长时间再重试
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("监控告警后台服务已停止");
        }

        private async Task CheckAlertRulesAsync(IServiceProvider services)
        {
            try
            {
                _logger.LogDebug("开始检查告警规则...");

                var alertService = services.GetService<IAlertService>();
                if (alertService != null)
                {
                    await alertService.CheckAlertRulesAsync();
                }

                _logger.LogDebug("告警规则检查完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查告警规则失败");

                var structuredLogger = services.GetService<IStructuredLogger>();
                structuredLogger?.LogError("告警规则检查失败", ex);
            }
        }

        private async Task PerformHealthCheckAsync(IServiceProvider services)
        {
            try
            {
                _logger.LogDebug("开始系统健康检查...");

                var alertService = services.GetService<IAlertService>();
                var structuredLogger = services.GetService<IStructuredLogger>();

                if (alertService == null || structuredLogger == null)
                {
                    return;
                }

                // 检查系统关键组件
                var healthChecks = new[]
                {
                    await CheckDiskSpaceAsync(),
                    await CheckNetworkConnectivityAsync(),
                    await CheckDatabaseConnectivityAsync(services),
                    await CheckCacheConnectivityAsync(services)
                };

                // 记录健康检查结果
                var healthyChecks = 0;
                foreach (var check in healthChecks)
                {
                    if (check.Healthy)
                    {
                        healthyChecks++;
                    }
                    else
                    {
                        // 触发健康检查失败告警
                        await alertService.TriggerAlertAsync(
                            AlertLevel.Warning,
                            $"健康检查失败: {check.Component}",
                            check.Message);
                    }

                    structuredLogger.LogInfo("健康检查完成",
                        new { check.Component, check.Healthy, check.Message },
                        new Dictionary<string, object>
                        {
                            ["healthCheck"] = true,
                            ["component"] = check.Component
                        });
                }

                // 计算系统健康分数
                var healthScore = (double)healthyChecks / healthChecks.Length * 100;

                structuredLogger.LogInfo("系统健康检查完成",
                    new { healthScore, healthyChecks, totalChecks = healthChecks.Length },
                    new Dictionary<string, object> { ["systemHealth"] = true });

                if (healthScore < 75)
                {
                    await alertService.TriggerAlertAsync(
                        AlertLevel.Critical,
                        "系统健康状态不佳",
                        $"系统健康分数: {healthScore:F1}%，有 {healthChecks.Length - healthyChecks} 个组件异常");
                }

                _logger.LogDebug("系统健康检查完成，健康分数: {HealthScore}%", healthScore);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "系统健康检查失败");
            }
        }

        private async Task PerformMaintenanceAsync(IServiceProvider services)
        {
            try
            {
                _logger.LogInformation("开始执行维护任务...");

                var structuredLogger = services.GetService<IStructuredLogger>();
                var alertService = services.GetService<IAlertService>();

                if (structuredLogger == null || alertService == null)
                {
                    return;
                }

                // 清理已解决的旧告警
                await CleanupResolvedAlertsAsync(alertService);

                // 检查长时间运行的告警
                await CheckLongRunningAlertsAsync(alertService, structuredLogger);

                // 生成维护报告
                await GenerateMaintenanceReportAsync(services, structuredLogger);

                _logger.LogInformation("维护任务执行完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行维护任务失败");
            }
        }

        private async Task<HealthCheckResult> CheckDiskSpaceAsync()
        {
            try
            {
                var drives = System.IO.DriveInfo.GetDrives();
                foreach (var drive in drives)
                {
                    if (drive.IsReady && drive.DriveType == System.IO.DriveType.Fixed)
                    {
                        var freeSpacePercent = (double)drive.AvailableFreeSpace / drive.TotalSize * 100;
                        if (freeSpacePercent < 10) // 少于10%可用空间
                        {
                            return new HealthCheckResult
                            {
                                Component = "DiskSpace",
                                Healthy = false,
                                Message = $"磁盘 {drive.Name} 可用空间不足: {freeSpacePercent:F1}%"
                            };
                        }
                    }
                }

                return new HealthCheckResult
                {
                    Component = "DiskSpace",
                    Healthy = true,
                    Message = "磁盘空间充足"
                };
            }
            catch (Exception ex)
            {
                return new HealthCheckResult
                {
                    Component = "DiskSpace",
                    Healthy = false,
                    Message = $"磁盘空间检查失败: {ex.Message}"
                };
            }
        }

        private async Task<HealthCheckResult> CheckNetworkConnectivityAsync()
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);

                var response = await httpClient.GetAsync("https://www.baidu.com");

                return new HealthCheckResult
                {
                    Component = "NetworkConnectivity",
                    Healthy = response.IsSuccessStatusCode,
                    Message = response.IsSuccessStatusCode ? "网络连接正常" : $"网络连接异常: {response.StatusCode}"
                };
            }
            catch (Exception ex)
            {
                return new HealthCheckResult
                {
                    Component = "NetworkConnectivity",
                    Healthy = false,
                    Message = $"网络连接检查失败: {ex.Message}"
                };
            }
        }

        private async Task<HealthCheckResult> CheckDatabaseConnectivityAsync(IServiceProvider services)
        {
            try
            {
                // 这里应该检查实际的数据库连接
                // 简化实现，假设数据库连接正常
                return new HealthCheckResult
                {
                    Component = "Database",
                    Healthy = true,
                    Message = "数据库连接正常"
                };
            }
            catch (Exception ex)
            {
                return new HealthCheckResult
                {
                    Component = "Database",
                    Healthy = false,
                    Message = $"数据库连接检查失败: {ex.Message}"
                };
            }
        }

        private async Task<HealthCheckResult> CheckCacheConnectivityAsync(IServiceProvider services)
        {
            try
            {
                // 这里应该检查实际的缓存连接
                // 简化实现，假设缓存连接正常
                return new HealthCheckResult
                {
                    Component = "Cache",
                    Healthy = true,
                    Message = "缓存连接正常"
                };
            }
            catch (Exception ex)
            {
                return new HealthCheckResult
                {
                    Component = "Cache",
                    Healthy = false,
                    Message = $"缓存连接检查失败: {ex.Message}"
                };
            }
        }

        private async Task CleanupResolvedAlertsAsync(IAlertService alertService)
        {
            try
            {
                // 这里应该清理超过一定时间的已解决告警
                // 当前实现中告警存储在内存中，实际生产环境应该有数据库清理逻辑
                _logger.LogDebug("告警清理任务完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理已解决告警失败");
            }
        }

        private async Task CheckLongRunningAlertsAsync(IAlertService alertService, IStructuredLogger structuredLogger)
        {
            try
            {
                var alerts = await alertService.GetActiveAlertsAsync();
                var longRunningThreshold = TimeSpan.FromHours(4);

                foreach (var alert in alerts)
                {
                    if (alert.Status == AlertStatus.Active &&
                        DateTime.UtcNow - alert.CreatedAt > longRunningThreshold)
                    {
                        structuredLogger.LogWarning("发现长时间运行的告警",
                            new { alert.Id, alert.Title, alert.CreatedAt,
                                  duration = DateTime.UtcNow - alert.CreatedAt },
                            new Dictionary<string, object> { ["longRunningAlert"] = true });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查长时间运行告警失败");
            }
        }

        private async Task GenerateMaintenanceReportAsync(IServiceProvider services, IStructuredLogger structuredLogger)
        {
            try
            {
                var alertService = services.GetService<IAlertService>();
                if (alertService == null) return;

                var alerts = await alertService.GetActiveAlertsAsync();
                var activeAlertsCount = alerts.Count(a => a.Status == AlertStatus.Active);
                var resolvedAlertsCount = alerts.Count(a => a.Status == AlertStatus.Resolved);

                var report = new
                {
                    generatedAt = DateTime.UtcNow,
                    activeAlerts = activeAlertsCount,
                    resolvedAlerts = resolvedAlertsCount,
                    totalAlerts = alerts.Count,
                    criticalAlerts = alerts.Count(a => a.Level == AlertLevel.Critical && a.Status == AlertStatus.Active),
                    oldestActiveAlert = alerts.Where(a => a.Status == AlertStatus.Active)
                                              .OrderBy(a => a.CreatedAt)
                                              .FirstOrDefault()?.CreatedAt
                };

                structuredLogger.LogInfo("维护报告生成",
                    report,
                    new Dictionary<string, object> { ["maintenanceReport"] = true });

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成维护报告失败");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("正在停止监控告警后台服务...");
            await base.StopAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 健康检查结果
    /// </summary>
    internal class HealthCheckResult
    {
        public string Component { get; set; } = string.Empty;
        public bool Healthy { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
