using PulseRPC.Infrastructure;

namespace PulseRPC.LoadBalancing;

/// <summary>
/// 基于通道的负载均衡器
/// </summary>
public interface IChannelLoadBalancer : ILoadBalancer
{
    /// <summary>
    /// 选择最佳通道端点
    /// </summary>
    Task<ChannelEndpoint?> SelectChannelAsync(
        IReadOnlyList<ChannelEndpoint> channels,
        LoadBalancingContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 选择最佳服务端点（考虑通道负载）
    /// </summary>
    Task<ServiceEndpoint?> SelectServiceAsync(
        IReadOnlyList<ServiceEndpoint> services,
        LoadBalancingContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 报告通道结果
    /// </summary>
    void ReportChannelResult(ChannelEndpoint channel, LoadBalancingResult result, TimeSpan responseTime);

    /// <summary>
    /// 报告服务结果
    /// </summary>
    void ReportServiceResult(ServiceEndpoint service, LoadBalancingResult result, TimeSpan responseTime);
}
