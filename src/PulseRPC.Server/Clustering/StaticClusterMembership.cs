using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// <see cref="StaticClusterMembership"/> 的配置项（P7 故障接管的失败累计 / 隔离恢复参数）。
/// </summary>
public sealed class StaticClusterMembershipOptions
{
    /// <summary>连续失败达到该阈值即把节点移出存活集。默认 3。</summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>
    /// 节点被移出存活集后的隔离时长；隔离到期后自动"半开"重新纳入存活集以供重试
    /// （若再次失败会被重新隔离）。默认 30 秒。
    /// </summary>
    public TimeSpan QuarantineDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// <see cref="IClusterMembership"/> 的静态成员实现（P8 首版）—— 成员集合来自
/// <see cref="ClusterTopologyOptions"/>，并结合节点健康（连续失败累计 + 隔离半开恢复）在运行时把
/// 不可达节点移出存活集，为 P7 故障接管提供"故障节点的 Actor 键重新映射到存活节点"的依据。
/// </summary>
/// <remarks>
/// <para>
/// 本节点<strong>永不</strong>被移出存活集（本地投递不经节点间链路，不存在"到自己不可达"）。
/// </para>
/// <para>
/// 线程安全：所有对存活集/失败计数/隔离表的变更都在同一把锁下进行，读取走 <c>volatile</c> 快照，无锁。
/// 隔离到期由内部计时器周期性检查并"半开"重新纳入（触发 <see cref="Changed"/>，使环重建后可再次尝试该节点）。
/// </para>
/// </remarks>
public sealed class StaticClusterMembership : IClusterMembership, IDisposable
{
    private readonly string _localNodeId;
    private readonly IReadOnlyList<string> _allNodeIds;
    private readonly int _failureThreshold;
    private readonly long _quarantineTicks;

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _consecutiveFailures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _quarantinedUntilTicks = new(StringComparer.Ordinal);
    private readonly Timer _recoveryTimer;
    private volatile IReadOnlyList<string> _liveSnapshot;
    private bool _disposed;

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>创建静态集群成员视图。</summary>
    public StaticClusterMembership(
        IOptions<ClusterTopologyOptions> topologyOptions,
        IOptions<StaticClusterMembershipOptions>? membershipOptions = null)
    {
        ArgumentNullException.ThrowIfNull(topologyOptions);
        var topology = topologyOptions.Value ?? throw new ArgumentNullException(nameof(topologyOptions));

        _localNodeId = topology.LocalNodeId ?? string.Empty;
        _allNodeIds = topology.Members
            .Select(m => m.NodeId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var options = membershipOptions?.Value ?? new StaticClusterMembershipOptions();
        _failureThreshold = Math.Max(1, options.FailureThreshold);
        var quarantine = options.QuarantineDuration > TimeSpan.Zero ? options.QuarantineDuration : TimeSpan.FromSeconds(30);
        _quarantineTicks = quarantine.Ticks;

        _liveSnapshot = _allNodeIds;

        // 隔离到期检查周期：取隔离时长的一半，限制在 [1s, 30s]，避免过于频繁或过于迟钝。
        var period = TimeSpan.FromMilliseconds(Math.Clamp(quarantine.TotalMilliseconds / 2, 1000, 30000));
        _recoveryTimer = new Timer(_ => RecoverExpiredQuarantines(), null, period, period);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> LiveNodeIds => _liveSnapshot;

    /// <inheritdoc/>
    public void ReportNodeFailure(string nodeId)
    {
        ArgumentNullException.ThrowIfNull(nodeId);

        // 本节点与非成员节点不参与失败累计。
        if (string.Equals(nodeId, _localNodeId, StringComparison.Ordinal) || !_allNodeIds.Contains(nodeId, StringComparer.Ordinal))
        {
            return;
        }

        var changed = false;
        lock (_gate)
        {
            if (_quarantinedUntilTicks.ContainsKey(nodeId))
            {
                // 已在隔离中：刷新隔离到期时间（半开重试又失败视作继续隔离）。
                _quarantinedUntilTicks[nodeId] = DateTime.UtcNow.Ticks + _quarantineTicks;
                return;
            }

            var failures = _consecutiveFailures.TryGetValue(nodeId, out var current) ? current + 1 : 1;
            _consecutiveFailures[nodeId] = failures;

            if (failures >= _failureThreshold)
            {
                _quarantinedUntilTicks[nodeId] = DateTime.UtcNow.Ticks + _quarantineTicks;
                _consecutiveFailures.Remove(nodeId);
                changed = RebuildSnapshotLocked();
            }
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    /// <inheritdoc/>
    public void ReportNodeSuccess(string nodeId)
    {
        ArgumentNullException.ThrowIfNull(nodeId);

        if (string.Equals(nodeId, _localNodeId, StringComparison.Ordinal))
        {
            return;
        }

        var changed = false;
        lock (_gate)
        {
            _consecutiveFailures.Remove(nodeId);
            if (_quarantinedUntilTicks.Remove(nodeId))
            {
                changed = RebuildSnapshotLocked();
            }
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    private void RecoverExpiredQuarantines()
    {
        var changed = false;
        lock (_gate)
        {
            if (_quarantinedUntilTicks.Count == 0)
            {
                return;
            }

            var now = DateTime.UtcNow.Ticks;
            var expired = _quarantinedUntilTicks
                .Where(kvp => kvp.Value <= now)
                .Select(kvp => kvp.Key)
                .ToArray();

            foreach (var nodeId in expired)
            {
                // 半开：移出隔离表重新纳入存活集；若该节点仍不可达，下一次调用失败会重新隔离。
                _quarantinedUntilTicks.Remove(nodeId);
            }

            if (expired.Length > 0)
            {
                changed = RebuildSnapshotLocked();
            }
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    /// <summary>在锁内重建存活快照；返回快照是否发生变化。</summary>
    private bool RebuildSnapshotLocked()
    {
        var live = _allNodeIds.Where(id => !_quarantinedUntilTicks.ContainsKey(id)).ToArray();

        // 保证本节点始终存活（即便理论上不会被隔离，也作为兜底不变式）。
        if (live.Length == 0)
        {
            live = string.IsNullOrEmpty(_localNodeId) ? _allNodeIds.ToArray() : new[] { _localNodeId };
        }

        var previous = _liveSnapshot;
        if (previous.Count == live.Length && previous.SequenceEqual(live, StringComparer.Ordinal))
        {
            return false;
        }

        _liveSnapshot = live;
        return true;
    }

    private void RaiseChanged()
    {
        // 复制委托引用后调用，避免竞态；单个订阅者异常不影响其它订阅者（与 backplane 订阅约定一致）。
        var handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch
            {
                // 订阅者（环重建）异常不应影响健康上报路径。
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _recoveryTimer.Dispose();
    }
}
