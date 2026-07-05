using System;

namespace PulseRPC.Infrastructure.Consul;

/// <summary>
/// Consul 服务发现后端配置。
/// </summary>
public sealed class ConsulDiscoveryOptions
{
    /// <summary>Consul agent HTTP 地址。默认本机 agent。</summary>
    public string Address { get; set; } = "http://127.0.0.1:8500";

    /// <summary>在 Consul 中注册的服务名（同一集群的所有节点使用同一服务名）。默认 <c>pulserpc</c>。</summary>
    public string ServiceName { get; set; } = "pulserpc";

    /// <summary>可选的 ACL Token。</summary>
    public string? Token { get; set; }

    /// <summary>Consul 存放本节点标识的 Meta 键名。默认 <c>pulse-node-id</c>。</summary>
    public string NodeIdMetaKey { get; set; } = "pulse-node-id";

    /// <summary>TCP 健康检查间隔。默认 5 秒。</summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>健康检查持续 critical 多久后自动注销该服务实例。默认 30 秒。</summary>
    public TimeSpan DeregisterCriticalAfter { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>是否启用基于阻塞查询（blocking query）的 watch 以加速成员变更收敛。默认 true。</summary>
    public bool EnableWatch { get; set; } = true;
}
