using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameApp.Infrastructure.Performance
{
    /// <summary>
    /// 性能监控和优化服务接口
    /// </summary>
    public interface IPerformanceService
    {
        /// <summary>
        /// 记录性能指标
        /// </summary>
        Task RecordMetricAsync(string name, double value, IDictionary<string, string>? tags = null);

        /// <summary>
        /// 记录响应时间
        /// </summary>
        Task RecordResponseTimeAsync(string operation, TimeSpan duration, bool success = true);

        /// <summary>
        /// 记录错误率
        /// </summary>
        Task RecordErrorAsync(string operation, Exception exception);

        /// <summary>
        /// 获取性能统计信息
        /// </summary>
        Task<PerformanceStats> GetStatsAsync(TimeSpan period);

        /// <summary>
        /// 获取当前系统资源使用情况
        /// </summary>
        SystemResourceUsage GetSystemResourceUsage();
    }

    /// <summary>
    /// 性能统计信息
    /// </summary>
    public class PerformanceStats
    {
        public Dictionary<string, OperationStats> Operations { get; set; } = new();
        public double RequestsPerSecond { get; set; }
        public double AverageResponseTime { get; set; }
        public double ErrorRate { get; set; }
        public SystemResourceUsage ResourceUsage { get; set; } = new();
        public DateTime Period { get; set; }
    }

    /// <summary>
    /// 操作统计信息
    /// </summary>
    public class OperationStats
    {
        public string Name { get; set; } = string.Empty;
        public long TotalRequests { get; set; }
        public long SuccessfulRequests { get; set; }
        public long FailedRequests { get; set; }
        public double AverageResponseTime { get; set; }
        public double MinResponseTime { get; set; }
        public double MaxResponseTime { get; set; }
        public double P95ResponseTime { get; set; }
        public double ErrorRate => TotalRequests > 0 ? (double)FailedRequests / TotalRequests : 0;
        public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 0;
    }

    /// <summary>
    /// 系统资源使用情况
    /// </summary>
    public class SystemResourceUsage
    {
        public double CpuUsagePercent { get; set; }
        public long MemoryUsageBytes { get; set; }
        public double MemoryUsagePercent { get; set; }
        public long AvailableMemoryBytes { get; set; }
        public int ThreadCount { get; set; }
        public long GcCollectionCount { get; set; }
        public TimeSpan Uptime { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
