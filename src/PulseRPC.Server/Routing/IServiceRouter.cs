namespace PulseRPC.Server.Routing;

/// <summary>
/// Service路由器接口
/// 负责根据ServiceId确定Service应该运行在哪个节点
/// </summary>
public interface IServiceRouter
{
    /// <summary>
    /// 定位Service所在节点（固定映射优先，否则使用一致性哈希）
    /// </summary>
    /// <param name="serviceIdHash">Service标识符的哈希值</param>
    /// <returns>节点ID</returns>
    Task<ushort> LocateServiceAsync(ulong serviceIdHash);

    /// <summary>
    /// 为新Service选择节点（总是使用最新的一致性哈希环）
    /// </summary>
    /// <param name="serviceIdHash">Service标识符的哈希值</param>
    /// <returns>节点ID</returns>
    Task<ushort> SelectNodeForNewServiceAsync(ulong serviceIdHash);

    /// <summary>
    /// Service下线时通知路由器清理固定映射
    /// </summary>
    /// <param name="serviceIdHash">Service标识符的哈希值</param>
    Task OnServiceShutdownAsync(ulong serviceIdHash);

    /// <summary>
    /// 获取当前哈希环的活跃节点列表
    /// </summary>
    List<ushort> GetActiveNodes();

    /// <summary>
    /// 获取路由指标（用于监控）
    /// </summary>
    ServiceRoutingMetrics GetMetrics();
}
