namespace PulseRPC.Server.Routing;

/// <summary>
/// 优雅关闭协调器接口
/// </summary>
public interface IGracefulShutdownCoordinator
{
    /// <summary>
    /// 当前关闭状态
    /// </summary>
    ShutdownState CurrentState { get; }

    /// <summary>
    /// 是否正在关闭
    /// </summary>
    bool IsShuttingDown { get; }

    /// <summary>
    /// 开始优雅关闭流程
    /// </summary>
    /// <param name="reason">关闭原因</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task InitiateShutdownAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// 强制关闭（跳过等待）
    /// </summary>
    Task ForceShutdownAsync();

    /// <summary>
    /// 获取关闭进度
    /// </summary>
    ShutdownProgress GetProgress();

    /// <summary>
    /// 检查是否可以接受新连接
    /// </summary>
    bool CanAcceptNewConnections();

    /// <summary>
    /// 注册待完成的请求
    /// </summary>
    void RegisterPendingRequest();

    /// <summary>
    /// 标记请求已完成
    /// </summary>
    void MarkRequestCompleted();
}
