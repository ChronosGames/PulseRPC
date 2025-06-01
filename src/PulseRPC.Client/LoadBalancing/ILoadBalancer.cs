using PulseRPC.ServiceDiscovery;

namespace PulseRPC.Client.LoadBalancing
{
    /// <summary>
    /// 负载均衡器接口
    /// </summary>
    public interface ILoadBalancer
    {
        /// <summary>
        /// 负载均衡策略
        /// </summary>
        LoadBalancingStrategy Strategy { get; }

        /// <summary>
        /// 选择服务端点
        /// </summary>
        /// <param name="endpoints">可用端点列表</param>
        /// <param name="context">负载均衡上下文</param>
        /// <returns>选中的端点，如果没有可用端点则返回null</returns>
        Task<ServiceEndpoint?> SelectAsync(IReadOnlyList<ServiceEndpoint> endpoints, LoadBalancingContext context);

        /// <summary>
        /// 报告请求结果 (用于动态调整)
        /// </summary>
        /// <param name="endpoint">使用的端点</param>
        /// <param name="result">请求结果</param>
        /// <param name="responseTime">响应时间</param>
        void ReportResult(ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime);

        /// <summary>
        /// 重置负载均衡器状态
        /// </summary>
        void Reset();

        /// <summary>
        /// 获取当前负载均衡统计信息
        /// </summary>
        /// <returns>统计信息字典</returns>
        Dictionary<string, object> GetStatistics();
    }
} 