using System.Collections.Generic;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// 集群静态成员端点：节点标识 + 供 <see cref="PulseNodeLink"/> 建立出站连接的地址。
/// </summary>
public sealed class ClusterNodeEndpoint
{
    /// <summary>节点标识（与 <see cref="ClusterTopologyOptions.LocalNodeId"/> 及一致性哈希环使用同一命名空间）。</summary>
    public required string NodeId { get; init; }

    /// <summary>节点 PulseRPC 服务端监听地址（主机名/IP）。</summary>
    public required string Host { get; init; }

    /// <summary>节点 PulseRPC 服务端监听端口。</summary>
    public required int Port { get; init; }
}

/// <summary>
/// 集群拓扑配置（P4 首版 = 静态成员列表，动态加入/退出属于后续增量）。
/// </summary>
/// <remarks>
/// 通过 <c>services.AddPulseClustering(options => ...)</c> 配置；<see cref="Members"/> 应在所有节点上保持一致，
/// 使 <see cref="NodeConsistentHashRing"/> 在各节点上算出相同的属主映射。
/// </remarks>
public sealed class ClusterTopologyOptions
{
    /// <summary>本节点标识（必须与 <see cref="Members"/> 中某一项的 <see cref="ClusterNodeEndpoint.NodeId"/> 一致）。</summary>
    public string LocalNodeId { get; set; } = string.Empty;

    /// <summary>集群全部静态成员（包含本节点）。</summary>
    public List<ClusterNodeEndpoint> Members { get; set; } = new();
}
