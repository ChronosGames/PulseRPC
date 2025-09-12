using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Transport;

namespace PulseRPC.ServiceDiscovery;

/// <summary>
/// 服务发现接口
/// </summary>
public interface IServiceDiscovery
{
    /// <summary>
    /// 获取指定服务名称的所有服务
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyList<ServiceEndpoint>> GetServicesAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定服务ID的服务
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<ServiceEndpoint?> GetServiceAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查服务是否存在
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> ExistsAsync(string serviceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 服务端点 - 服务发现专用
/// </summary>
public class ServiceEndpoint
{
    /// <summary>
    /// 服务唯一标识
    /// </summary>
    public string ServiceId { get; set; } = "";

    /// <summary>
    /// 服务类型名称
    /// </summary>
    public string ServiceType { get; set; } = "";

    /// <summary>
    /// 主机地址
    /// </summary>
    public string Host { get; set; } = "";

    /// <summary>
    /// 端口号
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// 默认构造函数
    /// </summary>
    public ServiceEndpoint() { }

    /// <summary>
    /// 参数化构造函数
    /// </summary>
    public ServiceEndpoint(string serviceId, string serviceType, string host, int port)
    {
        ServiceId = serviceId;
        ServiceType = serviceType;
        Host = host;
        Port = port;
    }

    /// <summary>
    /// 传输协议
    /// </summary>
    public string Protocol { get; set; } = "Tcp";

    /// <summary>
    /// 传输类型（为了兼容客户端代码）
    /// </summary>
    public TransportType Transport { get; set; } = TransportType.Tcp;

    /// <summary>
    /// 服务元数据
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// 权重（用于负载均衡）
    /// </summary>
    public int Weight { get; set; } = 100;

    /// <summary>
    /// 是否健康
    /// </summary>
    public bool IsHealthy { get; set; } = true;

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后健康检查时间
    /// </summary>
    public DateTime LastHealthCheck { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 获取服务的完整地址
    /// </summary>
    public string GetServiceAddress() => $"{ServiceType}@{Host}:{Port}";

    public override string ToString() => GetServiceAddress();
}
