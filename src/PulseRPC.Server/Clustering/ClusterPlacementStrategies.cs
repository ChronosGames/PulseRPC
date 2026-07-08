using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// 支持在成员视图变化时更新内部节点集合的 placement 策略。
/// </summary>
public interface IClusterMembershipAwarePlacementStrategy : IActorPlacementStrategy
{
    /// <summary>按最新成员环更新策略内部视图。</summary>
    void UpdateMembers(NodeConsistentHashRing hashRing);
}

/// <summary>
/// 集群节点负载指标，用于 least-loaded placement。
/// </summary>
public interface IClusterLoadMetrics
{
    /// <summary>返回节点当前负载；数值越小越适合作为新 owner。</summary>
    double GetLoad(string nodeId);
}

/// <summary>
/// 固定放置规则配置。
/// </summary>
public sealed class PinnedPlacementOptions
{
    /// <summary>精确 Actor identity 到节点的固定映射，key 格式为 <c>Hub:Key</c>。</summary>
    public Dictionary<string, string> ActorPins { get; } = new(StringComparer.Ordinal);

    /// <summary>Hub 到节点的固定映射，作为精确映射未命中时的后备。</summary>
    public Dictionary<string, string> HubPins { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 本地优先放置策略：本节点仍在成员环内时优先选择本节点，否则回退到 hash placement。
/// </summary>
public sealed class LocalAffinityPlacementStrategy : IClusterMembershipAwarePlacementStrategy
{
    private readonly string _localNodeId;
    private volatile NodeConsistentHashRing _hashRing;

    /// <summary>创建本地优先策略。</summary>
    public LocalAffinityPlacementStrategy(string localNodeId, NodeConsistentHashRing hashRing)
    {
        _localNodeId = localNodeId ?? string.Empty;
        _hashRing = hashRing ?? throw new ArgumentNullException(nameof(hashRing));
    }

    /// <inheritdoc/>
    public string SelectOwner(string hub, string key)
        => _hashRing.Nodes.Contains(_localNodeId, StringComparer.Ordinal)
            ? _localNodeId
            : _hashRing.GetOwner(HashPlacementStrategy.BuildIdentity(hub, key));

    /// <inheritdoc/>
    public void UpdateMembers(NodeConsistentHashRing hashRing)
    {
        _hashRing = hashRing ?? throw new ArgumentNullException(nameof(hashRing));
    }
}

/// <summary>
/// 最低负载优先放置策略：在当前成员中选择负载最低节点，负载相同时用 hash owner 做稳定 tie-breaker。
/// </summary>
public sealed class LeastLoadedPlacementStrategy : IClusterMembershipAwarePlacementStrategy
{
    private readonly IClusterLoadMetrics _metrics;
    private volatile NodeConsistentHashRing _hashRing;

    /// <summary>创建最低负载优先策略。</summary>
    public LeastLoadedPlacementStrategy(NodeConsistentHashRing hashRing, IClusterLoadMetrics metrics)
    {
        _hashRing = hashRing ?? throw new ArgumentNullException(nameof(hashRing));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    /// <inheritdoc/>
    public string SelectOwner(string hub, string key)
    {
        var ring = _hashRing;
        var hashOwner = ring.GetOwner(HashPlacementStrategy.BuildIdentity(hub, key));
        return ring.Nodes
            .OrderBy(node => _metrics.GetLoad(node))
            .ThenBy(node => string.Equals(node, hashOwner, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(node => node, StringComparer.Ordinal)
            .First();
    }

    /// <inheritdoc/>
    public void UpdateMembers(NodeConsistentHashRing hashRing)
    {
        _hashRing = hashRing ?? throw new ArgumentNullException(nameof(hashRing));
    }
}

/// <summary>
/// 固定放置策略：优先使用精确 Actor pin，其次使用 Hub pin，最后回退到 hash placement。
/// </summary>
public sealed class PinnedPlacementStrategy : IClusterMembershipAwarePlacementStrategy
{
    private readonly PinnedPlacementOptions _options;
    private volatile NodeConsistentHashRing _hashRing;

    /// <summary>创建固定放置策略。</summary>
    public PinnedPlacementStrategy(NodeConsistentHashRing hashRing, IOptions<PinnedPlacementOptions> options)
    {
        _hashRing = hashRing ?? throw new ArgumentNullException(nameof(hashRing));
        _options = options?.Value ?? new PinnedPlacementOptions();
    }

    /// <inheritdoc/>
    public string SelectOwner(string hub, string key)
    {
        var ring = _hashRing;
        var identity = HashPlacementStrategy.BuildIdentity(hub, key);
        if (_options.ActorPins.TryGetValue(identity, out var exact) && ring.Nodes.Contains(exact, StringComparer.Ordinal))
        {
            return exact;
        }

        if (_options.HubPins.TryGetValue(hub, out var hubOwner) && ring.Nodes.Contains(hubOwner, StringComparer.Ordinal))
        {
            return hubOwner;
        }

        return ring.GetOwner(identity);
    }

    /// <inheritdoc/>
    public void UpdateMembers(NodeConsistentHashRing hashRing)
    {
        _hashRing = hashRing ?? throw new ArgumentNullException(nameof(hashRing));
    }
}
