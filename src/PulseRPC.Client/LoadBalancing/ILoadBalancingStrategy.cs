using PulseRPC.LoadBalancing;
using PulseRPC.ServiceDiscovery;

namespace PulseRPC.Client.LoadBalancing
{
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
}
