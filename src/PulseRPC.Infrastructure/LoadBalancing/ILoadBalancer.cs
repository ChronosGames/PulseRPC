using PulseRPC.Infrastructure;
using PulseRPC.ServiceDiscovery;
using PulseRPC.Routing;

namespace PulseRPC.LoadBalancing;

/// <summary>
/// 负载均衡器接口
/// </summary>
public interface ILoadBalancer
{
    /// <summary>
    /// 负载均衡策略
    /// </summary>
    LoadBalancingStrategy Strategy { get; }

    /// <summary>
    /// 选择服务端点
    /// </summary>
    /// <param name="endpoints">可用端点列表</param>
    /// <param name="context">负载均衡上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>选中的端点，如果没有可用端点则返回null</returns>
    Task<ServiceEndpoint?> SelectAsync(IReadOnlyList<ServiceEndpoint> endpoints, LoadBalancingContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 报告请求结果 (用于动态调整)
    /// </summary>
    /// <param name="endpoint">使用的端点</param>
    /// <param name="result">请求结果</param>
    /// <param name="responseTime">响应时间</param>
    void ReportResult(ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime);

    /// <summary>
    /// 重置负载均衡器状态
    /// </summary>
    void Reset();

    /// <summary>
    /// 获取当前负载均衡统计信息
    /// </summary>
    /// <returns>统计信息字典</returns>
    Dictionary<string, object> GetStatistics();
}

/// <summary>
/// 负载均衡策略枚举
/// </summary>
public enum LoadBalancingStrategy
{
    /// <summary>
    /// 轮询
    /// </summary>
    RoundRobin,

    /// <summary>
    /// 加权轮询
    /// </summary>
    WeightedRoundRobin,

    /// <summary>
    /// 随机选择
    /// </summary>
    Random,

    /// <summary>
    /// 最少连接数
    /// </summary>
    LeastConnections,
}

/// <summary>
/// 负载均衡结果
/// </summary>
public enum LoadBalancingResult
{
    /// <summary>
    /// 成功
    /// </summary>
    Success,

    /// <summary>
    /// 失败
    /// </summary>
    Failure,

    /// <summary>
    /// 超时
    /// </summary>
    Timeout,

    /// <summary>
    /// 连接拒绝
    /// </summary>
    ConnectionRefused,

    /// <summary>
    /// 连接失败
    /// </summary>
    ConnectionFailed,

    /// <summary>
    /// 服务不可用
    /// </summary>
    ServiceUnavailable,

    /// <summary>
    /// 限流
    /// </summary>
    RateLimited,

    /// <summary>
    /// 熔断器打开
    /// </summary>
    CircuitBreakerOpen,

    /// <summary>
    /// 未知错误
    /// </summary>
    UnknownError,

    /// <summary>
    /// 服务器错误
    /// </summary>
    ServerError,

    /// <summary>
    /// 客户端错误
    /// </summary>
    ClientError,

    /// <summary>
    /// 一般错误
    /// </summary>
    Error,
}
