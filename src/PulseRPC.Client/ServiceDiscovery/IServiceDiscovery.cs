using PulseRPC.ServiceDiscovery;

namespace PulseRPC.Client.ServiceDiscovery
{
    /// <summary>
    /// 服务发现接口
    /// </summary>
    public interface IServiceDiscovery
    {
        /// <summary>
        /// 发现指定名称的服务
        /// </summary>
        /// <param name="serviceName">服务名称</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>服务端点列表</returns>
        Task<IReadOnlyList<ServiceEndpoint>> DiscoverAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// 监听指定服务的变化
        /// </summary>
        /// <param name="serviceName">服务名称</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>服务端点变化流</returns>
        IAsyncEnumerable<ServiceEndpoint[]> WatchAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取所有可用的服务名称
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>服务名称列表</returns>
        Task<IReadOnlyList<string>> GetServiceNamesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据标签过滤服务
        /// </summary>
        /// <param name="serviceName">服务名称</param>
        /// <param name="tags">标签过滤条件</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>符合条件的服务端点列表</returns>
        Task<IReadOnlyList<ServiceEndpoint>> DiscoverByTagsAsync(
            string serviceName, 
            Dictionary<string, string> tags, 
            CancellationToken cancellationToken = default);
    }
} 