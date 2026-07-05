using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// <see cref="INodeEndpointResolver"/> 的静态实现 —— 从 <see cref="ClusterTopologyOptions.Members"/>
/// 固定端点表解析节点端点（P8 首版 / 静态成员部署）。
/// </summary>
/// <remarks>
/// 动态部署（Consul/Etcd/K8s）由服务发现后端提供的解析器（同时实现 <see cref="IClusterMembership"/>）覆盖本注册。
/// </remarks>
public sealed class StaticNodeEndpointResolver : INodeEndpointResolver
{
    private readonly Dictionary<string, NodeEndpoint> _endpoints;

    /// <summary>创建静态端点解析器。</summary>
    public StaticNodeEndpointResolver(IOptions<ClusterTopologyOptions> topologyOptions)
    {
        ArgumentNullException.ThrowIfNull(topologyOptions);
        var topology = topologyOptions.Value ?? throw new ArgumentNullException(nameof(topologyOptions));

        _endpoints = new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal);
        foreach (var member in topology.Members)
        {
            if (member is null || string.IsNullOrEmpty(member.NodeId))
            {
                continue;
            }

            _endpoints[member.NodeId] = new NodeEndpoint(member.Host, member.Port);
        }
    }

    /// <inheritdoc/>
    public bool TryResolve(string nodeId, out NodeEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        return _endpoints.TryGetValue(nodeId, out endpoint);
    }
}
