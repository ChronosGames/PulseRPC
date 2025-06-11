using PulseServiceDiscovery.Abstractions.Models;
using PulseServiceDiscovery.Abstractions.Enums;

namespace PulseServiceDiscovery.Abstractions;

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
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>选中的端点</returns>
    Task<ServiceEndpoint?> SelectAsync(IReadOnlyList<ServiceEndpoint> endpoints, LoadBalancingContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 报告请求结果
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
    /// 获取统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    Dictionary<string, object> GetStatistics();
}
