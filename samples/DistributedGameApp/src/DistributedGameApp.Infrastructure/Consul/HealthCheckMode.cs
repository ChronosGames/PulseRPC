namespace DistributedGameApp.Infrastructure.Consul;

/// <summary>
/// Consul 健康检查模式
/// </summary>
public enum HealthCheckMode
{
    /// <summary>
    /// TTL 模式：服务主动向 Consul 推送健康状态
    /// </summary>
    TTL,

    /// <summary>
    /// HTTP 模式：Consul 主动向服务拉取健康状态
    /// </summary>
    HTTP
}