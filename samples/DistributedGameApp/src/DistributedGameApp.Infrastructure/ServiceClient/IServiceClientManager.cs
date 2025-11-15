namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// 服务客户端管理器接口
/// </summary>
public interface IServiceClientManager
{
    /// <summary>
    /// 获取 Hub 代理（通过服务ID/分片键路由）
    /// </summary>
    /// <typeparam name="THub">Hub 接口类型</typeparam>
    /// <param name="serviceId">服务ID/分片键 (如 userId, roomId 等)</param>
    /// <returns>Hub 代理实例，可以直接调用 Hub 方法</returns>
    THub? GetHub<THub>(string serviceId) where THub : class;

    /// <summary>
    /// 获取 Hub 代理（通过节点ID路由到特定节点）
    /// </summary>
    /// <typeparam name="THub">Hub 接口类型</typeparam>
    /// <param name="nodeId">节点ID</param>
    /// <returns>Hub 代理实例，可以直接调用 Hub 方法</returns>
    THub? GetHub<THub>(int nodeId) where THub : class;

    /// <summary>
    /// 获取服务连接（底层接口，用于高级场景）
    /// </summary>
    IServiceConnection? GetConnection(ServerType serverType, string serviceId);

    /// <summary>
    /// 获取所有服务连接
    /// </summary>
    List<IServiceConnection> GetAllConnections(ServerType serverType);

    /// <summary>
    /// 广播消息到指定服务类型的所有实例
    /// </summary>
    Task<Dictionary<string, bool>> BroadcastAsync<TRequest>(
        ServerType serverType,
        string hubName,
        string methodName,
        TRequest? request);
}
