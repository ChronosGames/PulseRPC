namespace PulseServiceDiscovery.Abstractions.Enums;

/// <summary>
/// 负载均衡结果枚举
/// </summary>
public enum LoadBalancingResult
{
    /// <summary>
    /// 成功
    /// </summary>
    Success = 0,

    /// <summary>
    /// 失败
    /// </summary>
    Failed = 1,

    /// <summary>
    /// 超时
    /// </summary>
    Timeout = 2,

    /// <summary>
    /// 连接错误
    /// </summary>
    ConnectionError = 3,

    /// <summary>
    /// 服务不可用
    /// </summary>
    ServiceUnavailable = 4,

    /// <summary>
    /// 限流
    /// </summary>
    RateLimited = 5,

    /// <summary>
    /// 熔断器打开
    /// </summary>
    CircuitBreakerOpen = 6
}
