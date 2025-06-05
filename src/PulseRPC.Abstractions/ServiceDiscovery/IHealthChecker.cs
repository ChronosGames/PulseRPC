using PulseRPC.ServiceDiscovery;

namespace PulseRPC.ServiceDiscovery
{
    /// <summary>
    /// 健康检查结果
    /// </summary>
    public class HealthCheckResult
    {
        public string ServiceId { get; set; } = string.Empty;

        /// <summary>
        /// 健康状态
        /// </summary>
        public HealthStatus Status { get; set; }

        /// <summary>
        /// 检查时间
        /// </summary>
        public DateTime CheckTime { get; set; }

        /// <summary>
        /// 响应时间
        /// </summary>
        public TimeSpan ResponseTime { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 响应时间 (毫秒)
        /// </summary>
        public long ResponseTimeMs => (long)ResponseTime.TotalMilliseconds;

        public int Attempts { get; set; }

        /// <summary>
        /// 额外的检查数据
        /// </summary>
        public Dictionary<string, object> Data { get; set; } = new();
    }

    /// <summary>
    /// 健康检查接口
    /// </summary>
    public interface IHealthChecker
    {
        /// <summary>
        /// 开始监控服务健康状态
        /// </summary>
        /// <param name="endpoint">服务端点</param>
        /// <param name="interval">检查间隔</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>健康检查结果流</returns>
        IAsyncEnumerable<HealthCheckResult> MonitorHealthAsync(
            ServiceEndpoint endpoint,
            TimeSpan interval,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 执行健康检查
        /// </summary>
        /// <param name="endpoint">服务端点</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>健康检查结果</returns>
        Task<HealthCheckResult> CheckHealthAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量健康检查
        /// </summary>
        /// <param name="endpoints">服务端点列表</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>健康检查结果字典</returns>
        Task<Dictionary<string, HealthCheckResult>> CheckHealthBatchAsync(
            IEnumerable<ServiceEndpoint> endpoints,
            CancellationToken cancellationToken = default);
    }
}
