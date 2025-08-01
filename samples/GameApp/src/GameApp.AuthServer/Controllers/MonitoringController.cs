using GameApp.Infrastructure.Monitoring;
using GameApp.Infrastructure.Performance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GameApp.AuthServer.Controllers
{
    /// <summary>
    /// 监控仪表板控制器 - 提供监控数据和告警管理接口
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // 生产环境应该有适当的权限控制
    public class MonitoringController : ControllerBase
    {
        private readonly IPerformanceService _performanceService;
        private readonly IAlertService _alertService;

        public MonitoringController(
            IPerformanceService performanceService,
            IAlertService alertService)
        {
            _performanceService = performanceService;
            _alertService = alertService;
        }

        /// <summary>
        /// 获取监控仪表板数据
        /// </summary>
        [HttpGet("dashboard")]
        public async Task<ActionResult<MonitoringDashboard>> GetDashboard([FromQuery] int hours = 1)
        {
            if (hours < 1 || hours > 24)
            {
                return BadRequest("时间范围必须在1-24小时之间");
            }

            var timeSpan = TimeSpan.FromHours(hours);
            var stats = await _performanceService.GetStatsAsync(timeSpan);
            var resources = _performanceService.GetSystemResourceUsage();
            var alerts = await _alertService.GetActiveAlertsAsync();

            var dashboard = new MonitoringDashboard
            {
                TimeRange = timeSpan,
                GeneratedAt = DateTime.UtcNow,
                SystemHealth = DetermineSystemHealth(stats, resources, alerts),
                PerformanceStats = stats,
                ResourceUsage = resources,
                ActiveAlerts = alerts,
                Summary = CreateSummary(stats, resources, alerts)
            };

            return Ok(dashboard);
        }

        /// <summary>
        /// 获取系统健康状态
        /// </summary>
        [HttpGet("health")]
        public async Task<ActionResult<SystemHealthStatus>> GetSystemHealth()
        {
            var stats = await _performanceService.GetStatsAsync(TimeSpan.FromMinutes(5));
            var resources = _performanceService.GetSystemResourceUsage();
            var alerts = await _alertService.GetActiveAlertsAsync();

            var health = new SystemHealthStatus
            {
                Status = DetermineSystemHealth(stats, resources, alerts),
                CheckedAt = DateTime.UtcNow,
                Details = CreateHealthDetails(stats, resources, alerts)
            };

            var statusCode = health.Status switch
            {
                MonitoringHealthStatus.Healthy => 200,
                MonitoringHealthStatus.Degraded => 200,
                MonitoringHealthStatus.Unhealthy => 503,
                _ => 500
            };

            return StatusCode(statusCode, health);
        }

        /// <summary>
        /// 获取性能指标趋势
        /// </summary>
        [HttpGet("metrics/trends")]
        public async Task<ActionResult<MetricsTrends>> GetMetricsTrends([FromQuery] int hours = 6)
        {
            if (hours < 1 || hours > 24)
            {
                return BadRequest("时间范围必须在1-24小时之间");
            }

            // 获取多个时间点的数据来构建趋势
            var intervals = 12; // 12个数据点
            var intervalDuration = TimeSpan.FromMinutes(hours * 60.0 / intervals);
            var trends = new List<MetricPoint>();

            for (int i = intervals - 1; i >= 0; i--)
            {
                var endTime = DateTime.UtcNow.AddMinutes(-i * intervalDuration.TotalMinutes);
                var stats = await _performanceService.GetStatsAsync(intervalDuration);
                var resources = _performanceService.GetSystemResourceUsage();

                trends.Add(new MetricPoint
                {
                    Timestamp = endTime,
                    CpuUsage = resources.CpuUsagePercent,
                    MemoryUsage = resources.MemoryUsagePercent,
                    RequestsPerSecond = stats.RequestsPerSecond,
                    AverageResponseTime = stats.AverageResponseTime,
                    ErrorRate = stats.ErrorRate * 100
                });
            }

            return Ok(new MetricsTrends
            {
                TimeRange = TimeSpan.FromHours(hours),
                Intervals = intervals,
                DataPoints = trends
            });
        }

        /// <summary>
        /// 获取活跃告警列表
        /// </summary>
        [HttpGet("alerts")]
        public async Task<ActionResult<List<Alert>>> GetAlerts([FromQuery] AlertStatus? status = null)
        {
            var alerts = await _alertService.GetActiveAlertsAsync();

            if (status.HasValue)
            {
                alerts = alerts.Where(a => a.Status == status.Value).ToList();
            }

            return Ok(alerts);
        }

        /// <summary>
        /// 解决告警
        /// </summary>
        [HttpPost("alerts/{alertId}/resolve")]
        public async Task<ActionResult> ResolveAlert(string alertId, [FromBody] ResolveAlertRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ResolvedBy))
            {
                return BadRequest("必须指定解决人");
            }

            await _alertService.ResolveAlertAsync(alertId, request.ResolvedBy, request.Resolution ?? "");
            return Ok(new { message = "告警已解决" });
        }

        /// <summary>
        /// 创建自定义告警
        /// </summary>
        [HttpPost("alerts")]
        public async Task<ActionResult> CreateAlert([FromBody] CreateAlertRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest("标题和消息不能为空");
            }

            await _alertService.TriggerAlertAsync(request.Level, request.Title, request.Message, request.Metadata);
            return Ok(new { message = "告警已创建" });
        }

        /// <summary>
        /// 获取操作统计
        /// </summary>
        [HttpGet("operations")]
        public async Task<ActionResult<List<OperationSummary>>> GetOperationStats([FromQuery] int hours = 1)
        {
            var stats = await _performanceService.GetStatsAsync(TimeSpan.FromHours(hours));

            var operations = stats.Operations.Values.Select(op => new OperationSummary
            {
                Name = op.Name,
                TotalRequests = op.TotalRequests,
                SuccessRate = op.SuccessRate * 100,
                ErrorRate = op.ErrorRate * 100,
                AverageResponseTime = op.AverageResponseTime,
                P95ResponseTime = op.P95ResponseTime,
                MinResponseTime = op.MinResponseTime,
                MaxResponseTime = op.MaxResponseTime
            }).OrderByDescending(o => o.TotalRequests).ToList();

            return Ok(operations);
        }

        /// <summary>
        /// 获取系统事件日志
        /// </summary>
        [HttpGet("events")]
        public ActionResult<List<SystemEvent>> GetSystemEvents([FromQuery] int limit = 100)
        {
            // 这里应该从实际的日志系统获取事件
            // 目前返回模拟数据
            var events = GenerateMockSystemEvents(limit);
            return Ok(events);
        }

        private MonitoringHealthStatus DetermineSystemHealth(PerformanceStats stats, SystemResourceUsage resources, List<Alert> alerts)
        {
            // 检查是否有紧急或严重告警
            if (alerts.Any(a => a.Level == AlertLevel.Emergency && a.Status == AlertStatus.Active))
                return MonitoringHealthStatus.Unhealthy;

            if (alerts.Any(a => a.Level == AlertLevel.Critical && a.Status == AlertStatus.Active))
                return MonitoringHealthStatus.Unhealthy;

            // 检查系统资源
            if (resources.CpuUsagePercent > 90 || resources.MemoryUsagePercent > 90)
                return MonitoringHealthStatus.Unhealthy;

            if (resources.CpuUsagePercent > 70 || resources.MemoryUsagePercent > 80)
                return MonitoringHealthStatus.Degraded;

            // 检查错误率
            if (stats.ErrorRate > 0.1) // 10%
                return MonitoringHealthStatus.Unhealthy;

            if (stats.ErrorRate > 0.05) // 5%
                return MonitoringHealthStatus.Degraded;

            // 检查响应时间
            if (stats.AverageResponseTime > 2000) // 2秒
                return MonitoringHealthStatus.Degraded;

            return MonitoringHealthStatus.Healthy;
        }

        private Dictionary<string, object> CreateHealthDetails(PerformanceStats stats, SystemResourceUsage resources, List<Alert> alerts)
        {
            return new Dictionary<string, object>
            {
                ["cpuUsage"] = resources.CpuUsagePercent,
                ["memoryUsage"] = resources.MemoryUsagePercent,
                ["errorRate"] = stats.ErrorRate * 100,
                ["averageResponseTime"] = stats.AverageResponseTime,
                ["activeAlerts"] = alerts.Count(a => a.Status == AlertStatus.Active),
                ["criticalAlerts"] = alerts.Count(a => a.Level == AlertLevel.Critical && a.Status == AlertStatus.Active),
                ["uptime"] = resources.Uptime.TotalHours
            };
        }

        private MonitoringSummary CreateSummary(PerformanceStats stats, SystemResourceUsage resources, List<Alert> alerts)
        {
            return new MonitoringSummary
            {
                TotalRequests = stats.Operations.Values.Sum(o => o.TotalRequests),
                AverageResponseTime = stats.AverageResponseTime,
                ErrorRate = stats.ErrorRate * 100,
                ActiveAlerts = alerts.Count(a => a.Status == AlertStatus.Active),
                CpuUsage = resources.CpuUsagePercent,
                MemoryUsage = resources.MemoryUsagePercent,
                Uptime = resources.Uptime
            };
        }

        private List<SystemEvent> GenerateMockSystemEvents(int limit)
        {
            var events = new List<SystemEvent>();
            var random = new Random();
            var eventTypes = new[] { "LOGIN", "ERROR", "PERFORMANCE", "SECURITY", "SYSTEM" };

            for (int i = 0; i < Math.Min(limit, 50); i++)
            {
                events.Add(new SystemEvent
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = eventTypes[random.Next(eventTypes.Length)],
                    Message = $"系统事件 {i + 1}",
                    Timestamp = DateTime.UtcNow.AddMinutes(-random.Next(0, 60)),
                    Severity = (EventSeverity)random.Next(0, 4)
                });
            }

            return events.OrderByDescending(e => e.Timestamp).ToList();
        }
    }

    // DTOs
    public class MonitoringDashboard
    {
        public TimeSpan TimeRange { get; set; }
        public DateTime GeneratedAt { get; set; }
        public MonitoringHealthStatus SystemHealth { get; set; }
        public PerformanceStats PerformanceStats { get; set; } = new();
        public SystemResourceUsage ResourceUsage { get; set; } = new();
        public List<Alert> ActiveAlerts { get; set; } = new();
        public MonitoringSummary Summary { get; set; } = new();
    }

    public class SystemHealthStatus
    {
        public MonitoringHealthStatus Status { get; set; }
        public DateTime CheckedAt { get; set; }
        public Dictionary<string, object> Details { get; set; } = new();
    }

    public enum MonitoringHealthStatus
    {
        Healthy,
        Degraded,
        Unhealthy
    }

    public class MetricsTrends
    {
        public TimeSpan TimeRange { get; set; }
        public int Intervals { get; set; }
        public List<MetricPoint> DataPoints { get; set; } = new();
    }

    public class MetricPoint
    {
        public DateTime Timestamp { get; set; }
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public double RequestsPerSecond { get; set; }
        public double AverageResponseTime { get; set; }
        public double ErrorRate { get; set; }
    }

    public class OperationSummary
    {
        public string Name { get; set; } = string.Empty;
        public long TotalRequests { get; set; }
        public double SuccessRate { get; set; }
        public double ErrorRate { get; set; }
        public double AverageResponseTime { get; set; }
        public double P95ResponseTime { get; set; }
        public double MinResponseTime { get; set; }
        public double MaxResponseTime { get; set; }
    }

    public class MonitoringSummary
    {
        public long TotalRequests { get; set; }
        public double AverageResponseTime { get; set; }
        public double ErrorRate { get; set; }
        public int ActiveAlerts { get; set; }
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public TimeSpan Uptime { get; set; }
    }

    public class SystemEvent
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public EventSeverity Severity { get; set; }
    }

    public enum EventSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public class ResolveAlertRequest
    {
        public string ResolvedBy { get; set; } = string.Empty;
        public string? Resolution { get; set; }
    }

    public class CreateAlertRequest
    {
        public AlertLevel Level { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
