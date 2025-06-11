namespace PulseServiceDiscovery.Abstractions.Models;

/// <summary>
/// 服务端点
/// </summary>
/// <param name="Id">端点唯一标识</param>
/// <param name="ServiceName">服务名称</param>
/// <param name="Host">主机地址</param>
/// <param name="Port">端口号</param>
/// <param name="Protocol">协议类型</param>
/// <param name="Metadata">元数据</param>
/// <param name="Health">健康状态</param>
/// <param name="Weight">权重</param>
/// <param name="Tags">标签</param>
public record ServiceEndpoint(
    string Id,
    string ServiceName,
    string Host,
    int Port,
    string Protocol = "tcp",
    ServiceMetadata? Metadata = null,
    HealthStatus Health = HealthStatus.Unknown,
    int Weight = 1,
    IReadOnlyList<string>? Tags = null)
{
    /// <summary>
    /// 获取完整的服务地址
    /// </summary>
    public string Address => $"{Host}:{Port}";

    /// <summary>
    /// 获取完整的服务URL
    /// </summary>
    public string Url => $"{Protocol}://{Host}:{Port}";

    /// <summary>
    /// 检查端点是否健康
    /// </summary>
    public bool IsHealthy => Health == HealthStatus.Healthy;

    /// <summary>
    /// 检查端点是否有指定标签
    /// </summary>
    /// <param name="tag">标签名称</param>
    /// <returns>是否包含标签</returns>
    public bool HasTag(string tag) => Tags?.Contains(tag, StringComparer.OrdinalIgnoreCase) == true;

    /// <summary>
    /// 获取元数据值
    /// </summary>
    /// <param name="key">键</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>元数据值</returns>
    public string? GetMetadata(string key, string? defaultValue = null) =>
        Metadata?.GetValue(key, defaultValue);
}
