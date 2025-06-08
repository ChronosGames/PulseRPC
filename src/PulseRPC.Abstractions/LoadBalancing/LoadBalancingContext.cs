namespace PulseRPC.LoadBalancing;

/// <summary>
/// 负载均衡上下文
/// </summary>
public class LoadBalancingContext
{
    /// <summary>
    /// 请求标识
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 客户端标识
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// 会话标识 (用于会话粘滞)
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// 一致性哈希键
    /// </summary>
    public string? HashKey { get; set; }

    /// <summary>
    /// 请求权重
    /// </summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    /// 上下文数据
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new();

    /// <summary>
    /// 请求时间
    /// </summary>
    public DateTime RequestTime { get; set; } = DateTime.UtcNow;
}
