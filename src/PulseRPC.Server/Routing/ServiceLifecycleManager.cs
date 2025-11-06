using Microsoft.Extensions.Logging;

namespace PulseRPC.Server.Routing;

/// <summary>
/// Service生命周期管理器
/// 负责处理Service的创建、下线等生命周期事件
/// </summary>
public sealed class ServiceLifecycleManager
{
    private readonly IServiceRouter _router;
    private readonly ILogger<ServiceLifecycleManager> _logger;

    public ServiceLifecycleManager(
        IServiceRouter router,
        ILogger<ServiceLifecycleManager> logger)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Service创建时调用
    /// </summary>
    /// <param name="serviceIdHash">Service标识符的哈希值</param>
    /// <param name="reason">创建原因</param>
    public async Task<ushort> OnServiceCreatedAsync(ulong serviceIdHash, string reason = "")
    {
        _logger.LogInformation(
            "Service创建: ServiceIdHash={Hash:X16}, 原因: {Reason}",
            serviceIdHash, reason);

        // 为新Service选择节点（使用最新的哈希环）
        var nodeId = await _router.SelectNodeForNewServiceAsync(serviceIdHash);

        _logger.LogInformation(
            "Service ServiceIdHash={Hash:X16} 分配到节点 {NodeId}",
            serviceIdHash, nodeId);

        return nodeId;
    }

    /// <summary>
    /// Service正常下线时调用
    /// </summary>
    /// <param name="serviceIdHash">Service标识符的哈希值</param>
    /// <param name="reason">下线原因</param>
    public async Task OnServiceShutdownAsync(ulong serviceIdHash, ShutdownReason reason)
    {
        _logger.LogInformation(
            "Service下线: ServiceIdHash={Hash:X16}, 原因: {Reason}",
            serviceIdHash, reason);

        // 清理固定映射
        await _router.OnServiceShutdownAsync(serviceIdHash);

        // 这里可以添加其他清理逻辑
        // 例如：保存状态到MongoDB、清理缓存等

        _logger.LogDebug(
            "Service ServiceIdHash={Hash:X16} 下线处理完成",
            serviceIdHash);
    }

    /// <summary>
    /// 批量处理Service下线
    /// </summary>
    /// <param name="serviceIdHashes">Service标识符哈希列表</param>
    /// <param name="reason">下线原因</param>
    public async Task OnBatchServiceShutdownAsync(List<ulong> serviceIdHashes, ShutdownReason reason)
    {
        _logger.LogInformation(
            "批量Service下线: 数量={Count}, 原因: {Reason}",
            serviceIdHashes.Count, reason);

        var tasks = serviceIdHashes.Select(hash => _router.OnServiceShutdownAsync(hash));
        await Task.WhenAll(tasks);

        _logger.LogInformation(
            "批量Service下线处理完成: 数量={Count}",
            serviceIdHashes.Count);
    }

    /// <summary>
    /// Service超时时调用
    /// </summary>
    /// <param name="serviceIdHash">Service标识符的哈希值</param>
    /// <param name="timeout">超时时长</param>
    public async Task OnServiceTimeoutAsync(ulong serviceIdHash, TimeSpan timeout)
    {
        _logger.LogWarning(
            "Service超时: ServiceIdHash={Hash:X16}, 超时时长: {Timeout}",
            serviceIdHash, timeout);

        await OnServiceShutdownAsync(serviceIdHash, ShutdownReason.Timeout);
    }

    /// <summary>
    /// 节点关闭时，批量处理该节点上的所有Service
    /// </summary>
    /// <param name="nodeId">节点ID</param>
    public async Task OnNodeShutdownAsync(ushort nodeId)
    {
        _logger.LogWarning(
            "节点关闭: NodeId={NodeId}, 该节点上的Service将下线",
            nodeId);

        // 这里需要获取该节点上的所有Service
        // 实际实现时需要ServiceLocator提供按节点查询的API

        _logger.LogInformation(
            "节点 {NodeId} 关闭处理完成",
            nodeId);
    }
}

/// <summary>
/// Service生命周期事件监听器接口（可选）
/// </summary>
public interface IServiceLifecycleListener
{
    Task OnServiceCreatedAsync(ulong serviceIdHash, ushort nodeId);
    Task OnServiceShutdownAsync(ulong serviceIdHash, ShutdownReason reason);
}

/// <summary>
/// 默认的生命周期事件记录器
/// </summary>
public class LoggingServiceLifecycleListener : IServiceLifecycleListener
{
    private readonly ILogger<LoggingServiceLifecycleListener> _logger;

    public LoggingServiceLifecycleListener(ILogger<LoggingServiceLifecycleListener> logger)
    {
        _logger = logger;
    }

    public Task OnServiceCreatedAsync(ulong serviceIdHash, ushort nodeId)
    {
        _logger.LogInformation(
            "ServiceLifecycle: Created - ServiceIdHash={Hash:X16}, NodeId={NodeId}",
            serviceIdHash, nodeId);
        return Task.CompletedTask;
    }

    public Task OnServiceShutdownAsync(ulong serviceIdHash, ShutdownReason reason)
    {
        _logger.LogInformation(
            "ServiceLifecycle: Shutdown - ServiceIdHash={Hash:X16}, Reason={Reason}",
            serviceIdHash, reason);
        return Task.CompletedTask;
    }
}
