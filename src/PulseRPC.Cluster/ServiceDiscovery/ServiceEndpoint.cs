using System.Net;
using PulseRPC.HealthCheck;

namespace PulseRPC.ServiceDiscovery;

/// <summary>
/// 服务端点
/// </summary>
public class ServiceEndpoint
{
    /// <summary>
    /// 服务ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// 主机地址
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 端口号
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// 权重
    /// </summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    /// 元数据
    /// </summary>
    public ServiceMetadata Metadata { get; set; } = new();

    /// <summary>
    /// 健康状态
    /// </summary>
    public HealthStatus Health { get; set; } = HealthStatus.Unknown;

    public string Address => $"{Host}:{Port}";

    /// <summary>
    /// 创建服务端点
    /// </summary>
    public ServiceEndpoint()
    {
    }

    /// <summary>
    /// 创建服务端点
    /// </summary>
    /// <param name="id">服务ID</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="host">主机地址</param>
    /// <param name="port">端口号</param>
    /// <param name="weight">权重</param>
    /// <param name="metadata">元数据</param>
    /// <param name="health">健康状态</param>
    public ServiceEndpoint(
        string id,
        string serviceName,
        string host,
        int port,
        int weight = 1,
        ServiceMetadata? metadata = null,
        HealthStatus health = HealthStatus.Unknown)
    {
        Id = id;
        ServiceName = serviceName;
        Host = host;
        Port = port;
        Weight = weight;
        Metadata = metadata ?? new ServiceMetadata();
        Health = health;
    }

    /// <summary>
    /// 转换为字符串
    /// </summary>
    public override string ToString()
    {
        return $"{ServiceName} ({Id}) at {Host}:{Port}";
    }

    public override bool Equals(object? obj)
    {
        return obj is ServiceEndpoint endpoint &&
               Id == endpoint.Id;
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }
}
