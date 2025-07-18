using PulseRPC.HealthCheck;

namespace PulseRPC.Cluster;

/// <summary>
/// 服务端点 - 表示一个服务实例及其承载的传输通道
/// </summary>
public class ServiceEndpoint
{
    /// <summary>
    /// 服务唯一标识
    /// </summary>
    public required string ServiceId { get; init; }

    /// <summary>
    /// 服务类型名称（IPulseHub的接口名）
    /// </summary>
    public required string ServiceType { get; init; }

    /// <summary>
    /// 服务实例名称（用于多实例场景）
    /// </summary>
    public string? InstanceName { get; init; }

    /// <summary>
    /// 承载此服务的通道端点
    /// </summary>
    public required ChannelEndpoint Channel { get; init; }

    /// <summary>
    /// 服务元数据
    /// </summary>
    public ServiceMetadata Metadata { get; init; } = new();

    /// <summary>
    /// 服务健康状态
    /// </summary>
    public HealthStatus Health { get; set; } = HealthStatus.Unknown;

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTime RegisteredAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 最后健康检查时间
    /// </summary>
    public DateTime LastHealthCheck { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 获取服务的完整地址
    /// </summary>
    public string GetServiceAddress() => $"{ServiceType}@{Channel}";

    public override string ToString() => GetServiceAddress();
}

/// <summary>
/// 服务元数据
/// </summary>
public class ServiceMetadata
{
    /// <summary>
    /// 服务版本
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// 服务标签（用于分组和过滤）
    /// </summary>
    public Dictionary<string, string> Tags { get; init; } = new();

    /// <summary>
    /// 支持的方法列表
    /// </summary>
    public List<ServiceMethodInfo> Methods { get; init; } = new();

    /// <summary>
    /// 自定义属性
    /// </summary>
    public Dictionary<string, object> Properties { get; init; } = new();

    /// <summary>
    /// 服务描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 服务维护者信息
    /// </summary>
    public string? Maintainer { get; init; }

    public ServiceMetadata(Dictionary<string, string>? metadata = null)
    {
        if (metadata != null)
        {
            foreach (var kvp in metadata)
            {
                Properties[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// 获取属性值
    /// </summary>
    /// <param name="key">属性键</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>属性值</returns>
    public string? GetValue(string key, string? defaultValue = null)
    {
        if (Properties.TryGetValue(key, out var value))
        {
            return value?.ToString();
        }
        return defaultValue;
    }
}

/// <summary>
/// 服务方法信息
/// </summary>
public class ServiceMethodInfo
{
    /// <summary>
    /// 方法名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 方法描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 请求类型
    /// </summary>
    public string? RequestType { get; init; }

    /// <summary>
    /// 响应类型
    /// </summary>
    public string? ResponseType { get; init; }

    /// <summary>
    /// 是否为流式方法
    /// </summary>
    public bool IsStreaming { get; init; } = false;

    /// <summary>
    /// 方法标签
    /// </summary>
    public Dictionary<string, string> Tags { get; init; } = new();
}
