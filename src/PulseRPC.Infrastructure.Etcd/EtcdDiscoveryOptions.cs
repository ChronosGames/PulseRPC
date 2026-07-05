using System;

namespace PulseRPC.Infrastructure.Etcd;

/// <summary>
/// etcd 服务发现后端配置。
/// </summary>
public sealed class EtcdDiscoveryOptions
{
    /// <summary>etcd 连接字符串（如 <c>http://127.0.0.1:2379</c>，多个用逗号分隔）。</summary>
    public string ConnectionString { get; set; } = "http://127.0.0.1:2379";

    /// <summary>集群成员键前缀（同一集群的所有节点写入 <c>{Prefix}/{nodeId}</c>）。默认 <c>/pulserpc/nodes/</c>。</summary>
    public string KeyPrefix { get; set; } = "/pulserpc/nodes/";

    /// <summary>节点注册租约的 TTL（秒）。进程存活期间自动续租；进程死亡后租约过期，键被 etcd 自动删除。默认 15 秒。</summary>
    public long LeaseTtlSeconds { get; set; } = 15;

    /// <summary>是否启用前缀 watch 以加速成员变更收敛。默认 true。</summary>
    public bool EnableWatch { get; set; } = true;
}
