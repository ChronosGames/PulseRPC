using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;
using PulseRPC.Server.Services;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// L3 Actor 状态迁移协调器 —— 在 keyed Actor 的属主从本节点<strong>优雅转移</strong>到目标节点时，
/// 完成"静默排空在途消息 → 捕获状态快照 → 跨节点搬运 → 目标恢复并激活 → 释放本地租约"的编排，
/// 实现<strong>跨激活状态保留</strong>（超越 L2"重新激活即空状态"的语义）。
/// </summary>
/// <remarks>
/// <para>
/// <strong>在途消息接管</strong>由三条既有机制协同保证，无需侵入邮箱内部：
/// </para>
/// <list type="number">
/// <item><description><em>迁出侧已入队消息</em>：<see cref="MigrateOutAsync"/> 先 <c>StopAsync</c>，其语义即
/// "等待队列中的消息处理完成"——排空后再捕获快照，故这些消息的效果已包含在快照中；</description></item>
/// <item><description><em>迁移窗口内新到达的消息</em>：由 <c>ClusterPulseRouter</c> 的属主重解析 + 故障接管
/// 重路由到新属主（配合至少一次投递与 Actor 侧去重），不会丢失；</description></item>
/// <item><description><em>状态连续性</em>：由 <see cref="IActorStateSnapshot"/> 快照在迁入节点恢复。</description></item>
/// </list>
/// <para>
/// 本协调器与具体传输解耦（<see cref="IActorStateTransport"/>）；生产环境把该传输绑定到
/// <see cref="INodeLink"/>（新增集群内部 Hub 方法接收快照），即可完成端到端跨节点迁移。
/// </para>
/// </remarks>
public sealed class ActorMigrationCoordinator
{
    private readonly IActorDirectory _directory;
    private readonly IActorStateTransport _stateTransport;
    private readonly ILogger<ActorMigrationCoordinator> _logger;
    private readonly IActorLeaseHeartbeat? _leaseHeartbeat;
    private readonly string _localNodeId;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _migrationGates = new(StringComparer.Ordinal);

    /// <summary>创建迁移协调器。</summary>
    public ActorMigrationCoordinator(
        IActorDirectory directory,
        IActorStateTransport stateTransport,
        IOptions<ClusterTopologyOptions> topologyOptions,
        ILogger<ActorMigrationCoordinator> logger)
        : this(directory, stateTransport, topologyOptions, null, logger)
    {
    }

    /// <summary>创建迁移协调器，并在迁入成功后接管 Actor 租约心跳。</summary>
    public ActorMigrationCoordinator(
        IActorDirectory directory,
        IActorStateTransport stateTransport,
        IOptions<ClusterTopologyOptions> topologyOptions,
        IActorLeaseHeartbeat? leaseHeartbeat,
        ILogger<ActorMigrationCoordinator> logger)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _stateTransport = stateTransport ?? throw new ArgumentNullException(nameof(stateTransport));
        _leaseHeartbeat = leaseHeartbeat;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ArgumentNullException.ThrowIfNull(topologyOptions);
        _localNodeId = topologyOptions.Value?.LocalNodeId ?? string.Empty;
        if (string.IsNullOrEmpty(_localNodeId))
        {
            throw new InvalidOperationException("ClusterTopologyOptions.LocalNodeId 未配置。");
        }
    }

    /// <summary>
    /// 把本节点持有的 <paramref name="localActor"/>（<c>(hub, key)</c>）优雅迁出到 <paramref name="targetNodeId"/>。
    /// </summary>
    /// <param name="hub">目标 Hub 名称。</param>
    /// <param name="key">Actor 实例键。</param>
    /// <param name="targetNodeId">迁入目标节点。</param>
    /// <param name="localActor">本地 Actor 实例（其 <c>StopAsync</c> 用于排空在途消息；若实现
    /// <see cref="IActorStateSnapshot"/> 则捕获其状态一并迁移）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task MigrateOutAsync(string hub, string key, string targetNodeId, IPulseService localActor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(hub);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(targetNodeId);
        ArgumentNullException.ThrowIfNull(localActor);

        if (string.Equals(targetNodeId, _localNodeId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("迁出目标不能是本节点。");
        }

        var migrationGate = GetMigrationGate(hub, key);
        await migrationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("开始迁出 Actor (Hub={Hub}, Key={Key}) 到节点 '{Target}'", hub, key, targetNodeId);

            // 1. 静默：StopAsync 语义为"等待队列在途消息处理完成"，排空后 Actor 不再接受新消息，状态稳定。
            await localActor.StopAsync(cancellationToken).ConfigureAwait(false);

            // 2. 捕获快照（Actor 已静默，无并发）。
            var snapshot = Array.Empty<byte>();
            if (localActor is IActorStateSnapshot snapshottable)
            {
                snapshot = await snapshottable.CaptureStateAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogInformation(
                    "Actor (Hub={Hub}, Key={Key}) 未实现 IActorStateSnapshot，迁入后状态为空（等价 L2 重新激活）", hub, key);
            }

            // 3. 跨节点搬运快照（目标节点据此恢复并激活）。
            await _stateTransport.SendSnapshotAsync(targetNodeId, hub, key, snapshot, cancellationToken).ConfigureAwait(false);

            // 4. 实例必须先彻底释放，再停止心跳并释放 fencing lease；否则新 owner 可能与旧实例短暂并存。
            await localActor.DisposeAsync().ConfigureAwait(false);
            var placement = await _directory.ResolveAsync(hub, key, CancellationToken.None).ConfigureAwait(false);
            if (placement is { } p && string.Equals(p.NodeId, _localNodeId, StringComparison.Ordinal))
            {
                _leaseHeartbeat?.Untrack(hub, key, p.LeaseId);
                await _directory.ReleaseAsync(hub, key, _localNodeId, p.LeaseId, CancellationToken.None).ConfigureAwait(false);
            }

            _logger.LogInformation("已完成迁出 Actor (Hub={Hub}, Key={Key}) -> '{Target}'（快照 {Bytes} 字节）", hub, key, targetNodeId, snapshot.Length);
        }
        finally
        {
            migrationGate.Release();
        }
    }

    /// <summary>
    /// 在本节点（迁入目标）恢复并激活一个迁移而来的 Actor。
    /// </summary>
    /// <param name="hub">目标 Hub 名称。</param>
    /// <param name="key">Actor 实例键。</param>
    /// <param name="snapshot">迁出节点捕获的状态快照（可能为空）。</param>
    /// <param name="localActor">本节点新建的 Actor 实例（若实现 <see cref="IActorStateSnapshot"/> 则恢复状态）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>迁入后取得的放置信息。</returns>
    public async Task<ActorPlacement> MigrateInAsync(string hub, string key, byte[] snapshot, IPulseService localActor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(hub);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(localActor);

        var migrationGate = GetMigrationGate(hub, key);
        await migrationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 1. 在目录激活并取得本节点租约（单一激活保证；若被他人持有会返回既有放置，调用方据此放弃）。
            var placement = await _directory.ActivateAsync(hub, key, _localNodeId, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(placement.NodeId, _localNodeId, StringComparison.Ordinal))
            {
                await DisposeAfterFailedMigrationAsync(hub, key, localActor).ConfigureAwait(false);
                _logger.LogWarning(
                    "迁入 Actor (Hub={Hub}, Key={Key}) 时目录显示属主为 '{Owner}'，放弃本地恢复以避免双激活", hub, key, placement.NodeId);
                return placement;
            }

            try
            {
                // 2. 恢复状态（在开始处理消息前）。
                if (snapshot.Length > 0 && localActor is IActorStateSnapshot snapshottable)
                {
                    await snapshottable.RestoreStateAsync(snapshot, cancellationToken).ConfigureAwait(false);
                }

                // 3. 启动成功后才纳入续租，避免为未发布/故障实例保活。
                await localActor.StartAsync(cancellationToken).ConfigureAwait(false);
                _leaseHeartbeat?.Track(hub, key, placement);
            }
            catch
            {
                await DisposeAfterFailedMigrationAsync(hub, key, localActor).ConfigureAwait(false);
                try
                {
                    await _directory.ReleaseAsync(
                        hub,
                        key,
                        _localNodeId,
                        placement.LeaseId,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception releaseException)
                {
                    _logger.LogWarning(
                        releaseException,
                        "迁入失败后释放 Actor 租约失败 (Hub={Hub}, Key={Key}, Lease={Lease})",
                        hub,
                        key,
                        placement.LeaseId);
                }

                throw;
            }

            _logger.LogInformation("已完成迁入 Actor (Hub={Hub}, Key={Key})（快照 {Bytes} 字节）", hub, key, snapshot.Length);
            return placement;
        }
        finally
        {
            migrationGate.Release();
        }
    }

    private SemaphoreSlim GetMigrationGate(string hub, string key)
        => _migrationGates.GetOrAdd($"{hub}:{key}", static _ => new SemaphoreSlim(1, 1));

    private async ValueTask DisposeAfterFailedMigrationAsync(string hub, string key, IPulseService actor)
    {
        try
        {
            await actor.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception disposeException)
        {
            _logger.LogWarning(
                disposeException,
                "清理未激活的迁入 Actor 失败 (Hub={Hub}, Key={Key})",
                hub,
                key);
        }
    }
}
