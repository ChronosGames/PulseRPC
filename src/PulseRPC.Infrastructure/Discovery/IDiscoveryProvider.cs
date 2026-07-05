using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Clustering;

namespace PulseRPC.Infrastructure.Discovery;

/// <summary>
/// 一个被发现的集群节点：标识 + 端点。
/// </summary>
public readonly struct DiscoveredNode : IEquatable<DiscoveredNode>
{
    /// <summary>节点标识（与一致性哈希环 / <see cref="IClusterMembership.LiveNodeIds"/> 同命名空间）。</summary>
    public string NodeId { get; }

    /// <summary>节点网络端点（供 <see cref="INodeEndpointResolver"/> 解析）。</summary>
    public NodeEndpoint Endpoint { get; }

    /// <summary>创建被发现节点。</summary>
    public DiscoveredNode(string nodeId, NodeEndpoint endpoint)
    {
        NodeId = nodeId ?? string.Empty;
        Endpoint = endpoint;
    }

    /// <inheritdoc/>
    public bool Equals(DiscoveredNode other)
        => string.Equals(NodeId, other.NodeId, StringComparison.Ordinal) && Endpoint.Equals(other.Endpoint);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is DiscoveredNode other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(NodeId is null ? 0 : StringComparer.Ordinal.GetHashCode(NodeId), Endpoint);

    /// <inheritdoc/>
    public override string ToString() => $"{NodeId}@{Endpoint}";
}

/// <summary>
/// 服务发现后端抽象 —— 各后端（Consul/Etcd/Kubernetes）只需实现"注册本节点 / 注销本节点 /
/// 获取当前节点集"这三件事，复杂的"轮询-变更检测-存活集-端点缓存-健康提示"逻辑由
/// <see cref="DiscoveryClusterMembership"/> 统一承载。
/// </summary>
/// <remarks>
/// <para>
/// 设计目标：把所有后端共有的分布式发现机制沉淀到 <see cref="DiscoveryClusterMembership"/>，使每个后端
/// 的适配代码尽可能薄、易于正确实现与测试（对应设计文档路线图 P8）。
/// </para>
/// <para>
/// <see cref="FetchNodesAsync"/> 返回"后端视角下当前应视为存活的全部节点"（含本节点）。基础设施层会
/// 周期性调用它并做变更检测；后端若支持原生 watch，可在内部触发 <see cref="Changed"/> 以加速收敛
/// （否则纯轮询也能正确工作，只是收敛延迟等于轮询周期）。
/// </para>
/// </remarks>
public interface IDiscoveryProvider
{
    /// <summary>
    /// 向发现后端注册本节点（幂等）。通常在集群成员启动时调用一次；后端若使用租约（如 etcd/Consul TTL），
    /// 实现内部负责续租/心跳。
    /// </summary>
    Task RegisterAsync(DiscoveredNode self, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从发现后端注销本节点（优雅下线）。
    /// </summary>
    Task DeregisterAsync(DiscoveredNode self, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取后端视角下当前存活的全部节点（含本节点）。
    /// </summary>
    Task<IReadOnlyList<DiscoveredNode>> FetchNodesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 后端原生 watch 检测到成员变化时触发（可选）；<see cref="DiscoveryClusterMembership"/> 收到后会立即
    /// 拉取一次而不必等下个轮询周期。不支持 watch 的后端可不触发本事件（纯轮询）。
    /// </summary>
    event Action? Changed;
}
