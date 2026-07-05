using System;

namespace PulseRPC.Clustering;

/// <summary>
/// 一个集群节点的网络端点（供 <c>INodeLink</c> 建立出站连接）。
/// </summary>
public readonly struct NodeEndpoint : IEquatable<NodeEndpoint>
{
    /// <summary>节点 PulseRPC 服务端监听主机名/IP。</summary>
    public string Host { get; }

    /// <summary>节点 PulseRPC 服务端监听端口。</summary>
    public int Port { get; }

    /// <summary>创建节点端点。</summary>
    public NodeEndpoint(string host, int port)
    {
        Host = host ?? string.Empty;
        Port = port;
    }

    /// <summary>是否为有效端点（主机非空、端口在 1..65535）。</summary>
    public bool IsValid => !string.IsNullOrEmpty(Host) && Port is > 0 and <= 65535;

    /// <inheritdoc/>
    public bool Equals(NodeEndpoint other)
        => string.Equals(Host, other.Host, StringComparison.Ordinal) && Port == other.Port;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NodeEndpoint other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(Host is null ? 0 : StringComparer.Ordinal.GetHashCode(Host), Port);

    /// <inheritdoc/>
    public override string ToString() => $"{Host}:{Port}";
}

/// <summary>
/// 节点端点解析器 —— 把节点标识解析为其网络端点（<c>nodeId → host:port</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IClusterMembership"/>（管"哪些节点存活"）互补：本接口管"某节点在哪"，供
/// <c>INodeLink</c> 建立跨节点出站连接。
/// </para>
/// <list type="bullet">
/// <item><description>静态部署：默认实现从 <c>ClusterTopologyOptions.Members</c> 的固定端点解析；</description></item>
/// <item><description>动态部署：服务发现后端（Consul/Etcd/K8s）在注册/监听节点时同时提供端点，实现本接口，
/// 使 <c>PulseNodeLink</c> 无需静态配置即可连接任意被发现的节点。</description></item>
/// </list>
/// <para>
/// 解析为同步（端点在动态实现中已由后台监听缓存到本地），命中返回 <c>true</c>；未知节点返回 <c>false</c>。
/// </para>
/// </remarks>
public interface INodeEndpointResolver
{
    /// <summary>
    /// 尝试解析 <paramref name="nodeId"/> 的网络端点。
    /// </summary>
    /// <param name="nodeId">目标节点标识。</param>
    /// <param name="endpoint">命中时输出端点。</param>
    /// <returns>已知该节点端点返回 <c>true</c>；否则返回 <c>false</c>。</returns>
    bool TryResolve(string nodeId, out NodeEndpoint endpoint);
}
