using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameApp.Infrastructure.Monitoring
{
    /// <summary>
    /// 告警服务接口
    /// </summary>
    public interface IAlertService
    {
        /// <summary>
        /// 触发告警
        /// </summary>
        Task TriggerAlertAsync(AlertLevel level, string title, string message, Dictionary<string, object>? metadata = null);

        /// <summary>
        /// 检查告警规则
        /// </summary>
        Task CheckAlertRulesAsync();

        /// <summary>
        /// 获取活跃告警
        /// </summary>
        Task<List<Alert>> GetActiveAlertsAsync();

        /// <summary>
        /// 解决告警
        /// </summary>
        Task ResolveAlertAsync(string alertId, string resolvedBy, string resolution);

        /// <summary>
        /// 添加告警规则
        /// </summary>
        Task AddAlertRuleAsync(AlertRule rule);

        /// <summary>
        /// 移除告警规则
        /// </summary>
        Task RemoveAlertRuleAsync(string ruleId);
    }

    /// <summary>
    /// 告警级别
    /// </summary>
    public enum AlertLevel
    {
        Info,
        Warning,
        Critical,
        Emergency
    }

    /// <summary>
    /// 告警状态
    /// </summary>
    public enum AlertStatus
    {
        Active,
        Resolved,
        Suppressed
    }

    /// <summary>
    /// 告警信息
    /// </summary>
    public class Alert
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public AlertLevel Level { get; set; }
        public AlertStatus Status { get; set; } = AlertStatus.Active;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }
        public string? ResolvedBy { get; set; }
        public string? Resolution { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        public int NotificationCount { get; set; }
        public DateTime? LastNotificationAt { get; set; }
    }

    /// <summary>
    /// 告警规则
    /// </summary>
    public class AlertRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public AlertLevel Level { get; set; }
        public string MetricName { get; set; } = string.Empty;
        public AlertCondition Condition { get; set; }
        public double Threshold { get; set; }
        public TimeSpan EvaluationWindow { get; set; } = TimeSpan.FromMinutes(5);
        public int ConsecutiveFailures { get; set; } = 1;
        public TimeSpan SuppressFor { get; set; } = TimeSpan.FromMinutes(10);
        public bool Enabled { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// 告警条件
    /// </summary>
    public enum AlertCondition
    {
        GreaterThan,
        LessThan,
        Equals,
        NotEquals,
        GreaterThanOrEqual,
        LessThanOrEqual
    }
}
