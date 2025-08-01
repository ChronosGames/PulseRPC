using GameApp.Infrastructure.Logging;
using GameApp.Infrastructure.Performance;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GameApp.Infrastructure.Monitoring
{
    /// <summary>
    /// 告警服务实现
    /// </summary>
    public class AlertService : IAlertService
    {
        private readonly ILogger<AlertService> _logger;
        private readonly IStructuredLogger _structuredLogger;
        private readonly IPerformanceService _performanceService;

        // 内存存储（生产环境应使用数据库）
        private readonly ConcurrentDictionary<string, Alert> _activeAlerts = new();
        private readonly ConcurrentDictionary<string, AlertRule> _alertRules = new();
        private readonly ConcurrentDictionary<string, AlertRuleState> _ruleStates = new();

        public AlertService(
            ILogger<AlertService> logger,
            IStructuredLogger structuredLogger,
            IPerformanceService performanceService)
        {
            _logger = logger;
            _structuredLogger = structuredLogger;
            _performanceService = performanceService;

            // 初始化默认告警规则
            InitializeDefaultRules();
        }

        public async Task TriggerAlertAsync(AlertLevel level, string title, string message, Dictionary<string, object>? metadata = null)
        {
            var alert = new Alert
            {
                Level = level,
                Title = title,
                Message = message,
                Metadata = metadata ?? new Dictionary<string, object>()
            };

            // 检查是否已存在相同的告警（防止重复告警）
            var existingAlert = _activeAlerts.Values.FirstOrDefault(a =>
                a.Title == title &&
                a.Status == AlertStatus.Active &&
                DateTime.UtcNow - a.CreatedAt < TimeSpan.FromMinutes(5));

            if (existingAlert != null)
            {
                // 更新现有告警
                existingAlert.NotificationCount++;
                existingAlert.LastNotificationAt = DateTime.UtcNow;
                _logger.LogDebug("更新现有告警: {AlertId}", existingAlert.Id);
                return;
            }

            // 添加新告警
            _activeAlerts[alert.Id] = alert;

            // 记录告警日志
            _structuredLogger.LogSecurity(SecurityEventType.SuspiciousActivity,
                $"告警触发: {title}",
                new { alert.Id, alert.Level, alert.Message },
                new Dictionary<string, object> { ["alertLevel"] = level.ToString() });

            // 发送通知
            await SendNotificationAsync(alert);

            _logger.LogWarning("告警已触发: {Level} - {Title} | {Message}", level, title, message);
        }

        public async Task CheckAlertRulesAsync()
        {
            try
            {
                _logger.LogDebug("开始检查告警规则...");

                var stats = await _performanceService.GetStatsAsync(TimeSpan.FromMinutes(5));
                var resources = _performanceService.GetSystemResourceUsage();

                foreach (var rule in _alertRules.Values.Where(r => r.Enabled))
                {
                    await EvaluateAlertRuleAsync(rule, stats, resources);
                }

                _logger.LogDebug("告警规则检查完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查告警规则失败");
                _structuredLogger.LogError("告警规则检查失败", ex);
            }
        }

        public Task<List<Alert>> GetActiveAlertsAsync()
        {
            var activeAlerts = _activeAlerts.Values
                .Where(a => a.Status == AlertStatus.Active)
                .OrderByDescending(a => a.CreatedAt)
                .ToList();

            return Task.FromResult(activeAlerts);
        }

        public async Task ResolveAlertAsync(string alertId, string resolvedBy, string resolution)
        {
            if (_activeAlerts.TryGetValue(alertId, out var alert))
            {
                alert.Status = AlertStatus.Resolved;
                alert.ResolvedAt = DateTime.UtcNow;
                alert.ResolvedBy = resolvedBy;
                alert.Resolution = resolution;

                _structuredLogger.LogInfo("告警已解决",
                    new { alertId, resolvedBy, resolution },
                    new Dictionary<string, object> { ["alertTitle"] = alert.Title });

                _logger.LogInformation("告警已解决: {AlertId} - {Title}", alertId, alert.Title);
            }
        }

        public Task AddAlertRuleAsync(AlertRule rule)
        {
            _alertRules[rule.Id] = rule;
            _ruleStates[rule.Id] = new AlertRuleState();

            _structuredLogger.LogInfo("添加告警规则",
                new { rule.Id, rule.Name, rule.MetricName, rule.Threshold });

            return Task.CompletedTask;
        }

        public Task RemoveAlertRuleAsync(string ruleId)
        {
            _alertRules.TryRemove(ruleId, out _);
            _ruleStates.TryRemove(ruleId, out _);

            _structuredLogger.LogInfo("移除告警规则", new { ruleId });

            return Task.CompletedTask;
        }

        private void InitializeDefaultRules()
        {
            // CPU使用率告警
            var cpuRule = new AlertRule
            {
                Name = "高CPU使用率",
                Description = "CPU使用率超过80%",
                Level = AlertLevel.Warning,
                MetricName = "system.cpu_usage",
                Condition = AlertCondition.GreaterThan,
                Threshold = 80,
                ConsecutiveFailures = 2,
                CreatedBy = "System"
            };
            _alertRules[cpuRule.Id] = cpuRule;
            _ruleStates[cpuRule.Id] = new AlertRuleState();

            // 内存使用率告警
            var memoryRule = new AlertRule
            {
                Name = "高内存使用率",
                Description = "内存使用率超过85%",
                Level = AlertLevel.Critical,
                MetricName = "system.memory_usage",
                Condition = AlertCondition.GreaterThan,
                Threshold = 85,
                ConsecutiveFailures = 1,
                CreatedBy = "System"
            };
            _alertRules[memoryRule.Id] = memoryRule;
            _ruleStates[memoryRule.Id] = new AlertRuleState();

            // 错误率告警
            var errorRateRule = new AlertRule
            {
                Name = "高错误率",
                Description = "系统错误率超过5%",
                Level = AlertLevel.Critical,
                MetricName = "system.error_rate",
                Condition = AlertCondition.GreaterThan,
                Threshold = 5,
                ConsecutiveFailures = 1,
                CreatedBy = "System"
            };
            _alertRules[errorRateRule.Id] = errorRateRule;
            _ruleStates[errorRateRule.Id] = new AlertRuleState();

            // 响应时间告警
            var responseTimeRule = new AlertRule
            {
                Name = "高响应时间",
                Description = "平均响应时间超过1秒",
                Level = AlertLevel.Warning,
                MetricName = "system.response_time",
                Condition = AlertCondition.GreaterThan,
                Threshold = 1000,
                ConsecutiveFailures = 3,
                CreatedBy = "System"
            };
            _alertRules[responseTimeRule.Id] = responseTimeRule;
            _ruleStates[responseTimeRule.Id] = new AlertRuleState();
        }

        private async Task EvaluateAlertRuleAsync(AlertRule rule, PerformanceStats stats, SystemResourceUsage resources)
        {
            try
            {
                var currentValue = GetMetricValue(rule.MetricName, stats, resources);
                if (!currentValue.HasValue) return;

                var ruleState = _ruleStates[rule.Id];
                var isTriggered = EvaluateCondition(rule.Condition, currentValue.Value, rule.Threshold);

                if (isTriggered)
                {
                    ruleState.ConsecutiveFailures++;
                    ruleState.LastFailureAt = DateTime.UtcNow;

                    // 检查是否达到连续失败次数
                    if (ruleState.ConsecutiveFailures >= rule.ConsecutiveFailures)
                    {
                        // 检查抑制期
                        if (ruleState.LastAlertAt.HasValue &&
                            DateTime.UtcNow - ruleState.LastAlertAt.Value < rule.SuppressFor)
                        {
                            return; // 在抑制期内，不发送告警
                        }

                        // 触发告警
                        await TriggerAlertAsync(rule.Level, rule.Name,
                            $"{rule.Description} 当前值: {currentValue:F2}, 阈值: {rule.Threshold}",
                            new Dictionary<string, object>
                            {
                                ["ruleId"] = rule.Id,
                                ["metricName"] = rule.MetricName,
                                ["currentValue"] = currentValue.Value,
                                ["threshold"] = rule.Threshold,
                                ["condition"] = rule.Condition.ToString()
                            });

                        ruleState.LastAlertAt = DateTime.UtcNow;
                        ruleState.ConsecutiveFailures = 0; // 重置计数器
                    }
                }
                else
                {
                    // 条件不满足，重置失败计数
                    ruleState.ConsecutiveFailures = 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "评估告警规则失败: {RuleId}", rule.Id);
            }
        }

        private double? GetMetricValue(string metricName, PerformanceStats stats, SystemResourceUsage resources)
        {
            return metricName switch
            {
                "system.cpu_usage" => resources.CpuUsagePercent,
                "system.memory_usage" => resources.MemoryUsagePercent,
                "system.error_rate" => stats.ErrorRate * 100,
                "system.response_time" => stats.AverageResponseTime,
                "system.requests_per_second" => stats.RequestsPerSecond,
                _ => null
            };
        }

        private bool EvaluateCondition(AlertCondition condition, double currentValue, double threshold)
        {
            return condition switch
            {
                AlertCondition.GreaterThan => currentValue > threshold,
                AlertCondition.LessThan => currentValue < threshold,
                AlertCondition.Equals => Math.Abs(currentValue - threshold) < 0.001,
                AlertCondition.NotEquals => Math.Abs(currentValue - threshold) >= 0.001,
                AlertCondition.GreaterThanOrEqual => currentValue >= threshold,
                AlertCondition.LessThanOrEqual => currentValue <= threshold,
                _ => false
            };
        }

        private async Task SendNotificationAsync(Alert alert)
        {
            try
            {
                // 这里可以集成各种通知渠道：邮件、短信、Webhook、Slack等
                _logger.LogInformation("发送告警通知: {Level} - {Title}", alert.Level, alert.Title);

                // 示例：记录到结构化日志
                _structuredLogger.LogInfo("告警通知已发送",
                    new { alert.Id, alert.Level, alert.Title, alert.Message },
                    new Dictionary<string, object>
                    {
                        ["notificationType"] = "alert",
                        ["alertLevel"] = alert.Level.ToString()
                    });

                alert.NotificationCount++;
                alert.LastNotificationAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送告警通知失败: {AlertId}", alert.Id);
            }
        }
    }

    /// <summary>
    /// 告警规则状态
    /// </summary>
    internal class AlertRuleState
    {
        public int ConsecutiveFailures { get; set; }
        public DateTime? LastFailureAt { get; set; }
        public DateTime? LastAlertAt { get; set; }
    }
}
