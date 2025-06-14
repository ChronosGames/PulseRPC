using PulseRPC.ServiceDiscovery;

namespace PulseRPC.LoadBalancing;

/// <summary>
/// 端点选择器接口
/// </summary>
public interface IEndpointSelector
{
    /// <summary>
    /// 从健康的端点中选择一个
    /// </summary>
    /// <param name="healthyEndpoints">健康的端点列表</param>
    /// <param name="context">选择上下文</param>
    /// <returns>选中的端点</returns>
    ServiceEndpoint? SelectEndpoint(IReadOnlyList<ServiceEndpoint> healthyEndpoints, LoadBalancingContext context);

    /// <summary>
    /// 过滤可用的端点
    /// </summary>
    /// <param name="endpoints">所有端点</param>
    /// <returns>可用的端点列表</returns>
    IReadOnlyList<ServiceEndpoint> FilterAvailableEndpoints(IReadOnlyList<ServiceEndpoint> endpoints);
}

/// <summary>
/// 负载均衡策略接口
/// </summary>
public interface ILoadBalancingStrategy
{
    /// <summary>
    /// 策略名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 选择服务端点
    /// </summary>
    /// <param name="endpoints">可用端点列表</param>
    /// <param name="context">负载均衡上下文</param>
    /// <returns>选中的端点，如果没有可用端点则返回null</returns>
    ServiceEndpoint? Select(IReadOnlyList<ServiceEndpoint> endpoints, LoadBalancingContext context);

    /// <summary>
    /// 报告请求结果
    /// </summary>
    /// <param name="endpoint">使用的端点</param>
    /// <param name="result">请求结果</param>
    /// <param name="responseTime">响应时间</param>
    void ReportResult(ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime);

    /// <summary>
    /// 重置策略状态
    /// </summary>
    void Reset();

    /// <summary>
    /// 获取策略统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    Dictionary<string, object> GetStatistics();
}
