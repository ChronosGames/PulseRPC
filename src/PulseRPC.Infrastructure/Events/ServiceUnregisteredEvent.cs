namespace PulseRPC.Infrastructure;

/// <summary>
/// 服务注销事件
/// </summary>
public class ServiceUnregisteredEvent : ServiceEvent
{
    /// <summary>
    /// 注销是否成功
    /// </summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// 错误信息（如果注销失败）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 注销原因
    /// </summary>
    public UnregistrationReason Reason { get; init; } = UnregistrationReason.Manual;

    /// <summary>
    /// 创建成功的服务注销事件
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="reason">注销原因</param>
    /// <param name="source">事件源</param>
    /// <returns>服务注销事件</returns>
    public static ServiceUnregisteredEvent CreateSuccess(ServiceEndpoint endpoint, UnregistrationReason reason = UnregistrationReason.Manual, string source = "") => new()
    {
        Endpoint = endpoint,
        Success = true,
        Reason = reason,
        Source = source
    };

    /// <summary>
    /// 创建失败的服务注销事件
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="errorMessage">错误信息</param>
    /// <param name="source">事件源</param>
    /// <returns>服务注销事件</returns>
    public static ServiceUnregisteredEvent CreateFailed(ServiceEndpoint endpoint, string errorMessage, string source = "") => new()
    {
        Endpoint = endpoint,
        Success = false,
        ErrorMessage = errorMessage,
        Source = source
    };
}

/// <summary>
/// 注销原因
/// </summary>
public enum UnregistrationReason
{
    /// <summary>手动注销</summary>
    Manual,
    /// <summary>健康检查失败</summary>
    HealthCheckFailure,
    /// <summary>服务过期</summary>
    Expired,
    /// <summary>连接断开</summary>
    ConnectionLost,
    /// <summary>系统关闭</summary>
    Shutdown
}
