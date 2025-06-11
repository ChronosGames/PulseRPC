namespace PulseServiceDiscovery.Abstractions.Models;

/// <summary>
/// 负载均衡上下文
/// </summary>
public class LoadBalancingContext
{
    /// <summary>
    /// 请求标识
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// 客户端标识
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// 会话标识（用于粘性会话）
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// 一致性哈希键
    /// </summary>
    public string? HashKey { get; set; }

    /// <summary>
    /// 用户标识
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 请求权重
    /// </summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    /// 上下文数据
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new();

    /// <summary>
    /// 创建默认上下文
    /// </summary>
    /// <returns>默认上下文</returns>
    public static LoadBalancingContext Default() => new();

    /// <summary>
    /// 创建带请求ID的上下文
    /// </summary>
    /// <param name="requestId">请求ID</param>
    /// <returns>上下文</returns>
    public static LoadBalancingContext WithRequestId(string requestId) => new() { RequestId = requestId };

    /// <summary>
    /// 创建带会话ID的上下文
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>上下文</returns>
    public static LoadBalancingContext WithSessionId(string sessionId) => new() { SessionId = sessionId };

    /// <summary>
    /// 创建带哈希键的上下文
    /// </summary>
    /// <param name="hashKey">哈希键</param>
    /// <returns>上下文</returns>
    public static LoadBalancingContext WithHashKey(string hashKey) => new() { HashKey = hashKey };
}
