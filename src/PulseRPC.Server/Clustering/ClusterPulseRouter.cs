using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;
using PulseRPC.Routing;
using PulseRPC.Server.Gateway;
using PulseRPC.Server.Routing;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// 集群感知的 <see cref="IPulseRouter"/> 实现 —— 在 <see cref="LocalPulseRouter"/>（本地投递）之上
/// 为 <see cref="AddressKind.Actor"/> 增加跨节点转发能力。
/// </summary>
/// <remarks>
/// <para>
/// 属主解析：<see cref="PulseAddress.NodeId"/> 显式指定时直接采用；否则经
/// <see cref="NodeConsistentHashRing"/> 算出候选属主（所有节点对同一 Key 算出相同候选，无需协调）。
/// 无论候选属主是本地还是远端，都必须先经 <see cref="IActorDirectory"/> 的共享 CAS + TTL 目录取得租约；
/// 远端调用同时携带该 lease id，由执行端在业务反序列化前做 fencing 校验。
/// </para>
/// <para>
/// <c>Connection</c> 地址全部委派给 <see cref="LocalPulseRouter"/>，单节点行为完全不变；
/// <c>AllClients/Group/User/Except</c> Fan-out 地址在本地投递（<see cref="LocalPulseRouter"/>）之后，
/// 额外经 <see cref="IPulseBackplane"/> 模型 X（<see cref="IPulseBackplane.PublishAsync"/>/
/// <see cref="IPulseBackplane.Subscribe"/>）扩散到集群其它节点，修复"跨节点广播静默丢消息"
/// （设计文档 §9、§15.1 风险 #2）；<c>Node</c> 显式跨节点寻址暂不支持。
/// </para>
/// </remarks>
public sealed class ClusterPulseRouter : IPulseRouter, IDisposable
{
    private readonly LocalPulseRouter _local;
    private readonly IActorDirectory _actorDirectory;
    private readonly INodeLink _nodeLink;
    private readonly IPulseBackplane _backplane;
    private readonly string _localNodeId;
    private readonly ILogger<ClusterPulseRouter> _logger;
    private readonly IDisposable _backplaneSubscription;
    private readonly DeliveryRetryOptions _retryOptions;
    private readonly IActorLeaseHeartbeat? _leaseHeartbeat;
    private readonly IClusterDiagnostics _diagnostics;

    // P7 故障接管：当注入 IClusterMembership 时，属主解析基于「存活成员」动态重建的环，
    // 且跨节点调用失败会上报健康、把故障节点移出存活集并重新映射属主。未注入时 _currentRing 恒为构造时
    // 传入的静态环，行为与单节点/静态环完全一致（向后兼容）。
    private readonly IClusterMembership? _membership;
    private volatile NodeConsistentHashRing _currentRing;
    private volatile IActorPlacementStrategy _placementStrategy;

    /// <summary>创建集群路由器。</summary>
    public ClusterPulseRouter(
        LocalPulseRouter local,
        NodeConsistentHashRing hashRing,
        IActorDirectory actorDirectory,
        INodeLink nodeLink,
        IPulseBackplane backplane,
        IOptions<ClusterTopologyOptions> topologyOptions,
        ILogger<ClusterPulseRouter> logger,
        DeliveryRetryOptions? retryOptions = null,
        IClusterMembership? membership = null,
        IActorPlacementStrategy? placementStrategy = null,
        IActorLeaseHeartbeat? leaseHeartbeat = null,
        IClusterDiagnostics? diagnostics = null)
    {
        _local = local ?? throw new ArgumentNullException(nameof(local));
        _currentRing = hashRing ?? throw new ArgumentNullException(nameof(hashRing));
        _placementStrategy = placementStrategy ?? new HashPlacementStrategy(_currentRing);
        _actorDirectory = actorDirectory ?? throw new ArgumentNullException(nameof(actorDirectory));
        _nodeLink = nodeLink ?? throw new ArgumentNullException(nameof(nodeLink));
        _backplane = backplane ?? throw new ArgumentNullException(nameof(backplane));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _retryOptions = retryOptions ?? new DeliveryRetryOptions();
        _leaseHeartbeat = leaseHeartbeat;
        _diagnostics = diagnostics ?? new NoopClusterDiagnostics();

        ArgumentNullException.ThrowIfNull(topologyOptions);
        _localNodeId = topologyOptions.Value?.LocalNodeId ?? string.Empty;
        if (string.IsNullOrEmpty(_localNodeId))
        {
            throw new InvalidOperationException(
                "ClusterTopologyOptions.LocalNodeId 未配置。请在 services.AddPulseClustering(o => o.LocalNodeId = ...) 中设置本节点标识。");
        }

        _membership = membership;
        if (_membership is not null)
        {
            _membership.Changed += OnMembershipChanged;
            OnMembershipChanged(); // 以当前存活成员为准初始化环（可能已有节点在启动前被判定下线）。
        }

        _backplaneSubscription = _backplane.Subscribe(OnBackplaneMessageAsync);
    }

    /// <summary>
    /// 存活成员变化时按最新存活集重建一致性哈希环（P7）：故障节点被移出后，其原本拥有的
    /// <c>(Hub, Key)</c> 会重新映射到存活节点，从而实现属主接管。
    /// </summary>
    private void OnMembershipChanged()
    {
        var live = _membership!.LiveNodeIds;
        if (live is null || live.Count == 0)
        {
            return; // 存活集异常为空时保持上一份环，避免瞬时无属主。
        }

        try
        {
            _currentRing = new NodeConsistentHashRing(live);
            if (_placementStrategy is IClusterMembershipAwarePlacementStrategy membershipAware)
            {
                membershipAware.UpdateMembers(_currentRing);
            }
        }
        catch (ArgumentException)
        {
            // 存活集不含任何有效节点标识：保持上一份环。
        }
    }

    /// <summary>
    /// 收到 <see cref="IPulseBackplane"/> 模型 X 广播时的处理：来自本节点自己发布的广播已在发布前完成
    /// 本地投递，跳过以免重复；来自其它节点的广播对本节点本地成员投递一次（至多一次语义，与本地 Fan-out 一致）。
    /// </summary>
    private ValueTask OnBackplaneMessageAsync(
        PulseAddress fanoutAddress, ushort protocolId, ReadOnlyMemory<byte> body, string originNodeId, CancellationToken cancellationToken)
    {
        if (string.Equals(originNodeId, _localNodeId, StringComparison.Ordinal))
        {
            return default;
        }

        return _local.SendAsync(fanoutAddress, protocolId, body, DeliveryMode.AtMostOnce, cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_membership is not null)
        {
            _membership.Changed -= OnMembershipChanged;
        }

        _backplaneSubscription.Dispose();
    }

    /// <inheritdoc/>
    public ValueTask SendAsync(
        in PulseAddress address,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        DeliveryMode delivery = DeliveryMode.AtMostOnce,
        CancellationToken cancellationToken = default,
        Guid messageId = default)
        // 异步方法不能使用 in/ref/out 参数：先按值捕获地址（只读结构体，值语义安全），再委派给异步实现。
        => SendCoreAsync(address, protocolId, body, delivery, cancellationToken, messageId);

    private async ValueTask SendCoreAsync(
        PulseAddress address,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        DeliveryMode delivery,
        CancellationToken cancellationToken,
        Guid messageId)
    {
        if (address.Kind == AddressKind.Actor)
        {
            var explicitNode = !string.IsNullOrEmpty(address.NodeId);
            var relay = GatewayRelayContext.Current;
            var sourceNodeId = relay?.GatewayNodeId ?? string.Empty;
            var replyTo = relay?.ClientConnectionId ?? string.Empty;

            // 在进入重试/接管循环之前一次性敲定 messageId（未显式指定时生成一个新的），确保本次调用的
            // 所有重试/接管尝试都携带同一个值，使远端节点的 Actor 侧去重（ExactlyOnce）能正确识别"是同一条消息的重投"
            // 而不是每次尝试都被误判为一条新消息。
            var effectiveMessageId = messageId == Guid.Empty ? Guid.NewGuid() : messageId;

            HashSet<string>? triedOwners = null;
            while (true)
            {
                var placement = await ResolveActorPlacementAsync(address, cancellationToken).ConfigureAwait(false);
                var ownerNodeId = placement.NodeId;
                if (string.Equals(ownerNodeId, _localNodeId, StringComparison.Ordinal))
                {
                    break; // 属主为本节点：转本地投递。
                }

                try
                {
                    await DeliveryRetryExecutor.ExecuteAsync(
                        delivery, _retryOptions,
                        ct => _nodeLink.SendActorAsync(
                            ownerNodeId, address.Hub, address.Key, protocolId, body,
                            sourceNodeId: sourceNodeId, replyTo: replyTo, cancellationToken: ct,
                            messageId: effectiveMessageId, leaseId: placement.LeaseId),
                        _logger, $"跨节点 Actor Send '{address.Hub}:{address.Key}' -> '{ownerNodeId}'", cancellationToken).ConfigureAwait(false);
                    _membership?.ReportNodeSuccess(ownerNodeId);
                    return;
                }
                catch (Exception ex) when (IsNodeTransportFailure(ex))
                {
                    if (!TryFailover(ex, ownerNodeId, explicitNode, address, ref triedOwners))
                    {
                        throw;
                    }

                    await WaitForLeaseHandoffAsync(address, placement, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        await _local.SendAsync(address, protocolId, body, delivery, cancellationToken, messageId).ConfigureAwait(false);

        // 模型 X：AllClients/Group/User/Except 都可能存在其它节点上的成员，广播扩散后由各节点的
        // OnBackplaneMessageAsync 对各自的本地成员完成投递，避免"跨节点广播静默丢消息"（§9/§15.1 风险 #2）。
        if (address.Kind is AddressKind.AllClients or AddressKind.Group or AddressKind.User or AddressKind.Except)
        {
            await _backplane.PublishAsync(address, protocolId, body, _localNodeId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public ValueTask<ReadOnlyMemory<byte>> AskAsync(
        in PulseAddress address,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
        // 异步方法不能使用 in/ref/out 参数：先按值捕获地址（只读结构体，值语义安全），再委派给异步实现。
        => AskCoreAsync(address, protocolId, body, cancellationToken);

    private async ValueTask<ReadOnlyMemory<byte>> AskCoreAsync(
        PulseAddress address,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        if (address.Kind == AddressKind.Actor)
        {
            var explicitNode = !string.IsNullOrEmpty(address.NodeId);
            var relay = GatewayRelayContext.Current;
            var sourceNodeId = relay?.GatewayNodeId ?? string.Empty;
            var replyTo = relay?.ClientConnectionId ?? string.Empty;

            HashSet<string>? triedOwners = null;
            while (true)
            {
                var placement = await ResolveActorPlacementAsync(address, cancellationToken).ConfigureAwait(false);
                var ownerNodeId = placement.NodeId;
                if (string.Equals(ownerNodeId, _localNodeId, StringComparison.Ordinal))
                {
                    break; // 属主为本节点：转本地投递。
                }

                try
                {
                    var response = await _nodeLink.AskActorAsync(
                        ownerNodeId, address.Hub, address.Key, protocolId, body,
                        sourceNodeId: sourceNodeId, replyTo: replyTo,
                        cancellationToken: cancellationToken, leaseId: placement.LeaseId).ConfigureAwait(false);
                    _membership?.ReportNodeSuccess(ownerNodeId);
                    return response;
                }
                catch (Exception ex) when (IsNodeTransportFailure(ex))
                {
                    if (!TryFailover(ex, ownerNodeId, explicitNode, address, ref triedOwners))
                    {
                        throw;
                    }

                    await WaitForLeaseHandoffAsync(address, placement, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return await _local.AskAsync(address, protocolId, body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 处理一次跨节点 Actor 调用失败：上报节点健康，并判断是否可故障接管到其它存活属主（P7）。
    /// </summary>
    /// <returns>
    /// <c>true</c> 表示拓扑已找到不同候选，可在旧租约失效后重新解析；
    /// <c>false</c> 表示无可接管路径，调用方应重新抛出原异常。
    /// </returns>
    private bool TryFailover(
        Exception ex,
        string failedOwnerNodeId,
        bool explicitNode,
        PulseAddress address,
        ref HashSet<string>? triedOwners)
    {
        _membership?.ReportNodeFailure(failedOwnerNodeId);

        // 未启用动态成员，或调用方显式指定了目标节点：不做属主接管，直接向上抛出。
        if (_membership is null || explicitNode)
        {
            return false;
        }

        (triedOwners ??= new HashSet<string>(StringComparer.Ordinal)).Add(failedOwnerNodeId);

        // 失败上报可能已把故障节点移出存活集并重建了环；重新解析候选属主。
        var next = !string.IsNullOrEmpty(address.NodeId) ? address.NodeId! : _placementStrategy.SelectOwner(address.Hub, address.Key);

        if (string.Equals(next, failedOwnerNodeId, StringComparison.Ordinal) || triedOwners.Contains(next))
        {
            _logger.LogError(
                ex, "跨节点 Actor 调用失败且无其它可用属主可接管 (Hub={Hub}, Key={Key}, FailedOwner={Owner})",
                address.Hub, address.Key, failedOwnerNodeId);
            return false;
        }

        _logger.LogWarning(
            ex, "跨节点 Actor 调用失败，拓扑候选已切换为 '{NextOwner}'；等待旧租约失效后再接管 (Hub={Hub}, Key={Key})",
            next, address.Hub, address.Key);
        return true;
    }

    /// <summary>
    /// 只有链路级失败才能改变成员健康状态。远端业务异常、授权失败和协议校验失败必须原样返回，
    /// 否则一次确定性的业务错误会错误隔离健康节点，并可能在新 owner 上重复执行请求。
    /// </summary>
    private static bool IsNodeTransportFailure(Exception exception)
        => exception is IOException
            or SocketException
            or TimeoutException
            or ObjectDisposedException;

    /// <summary>
    /// 等待失败 owner 的当前租约失效或被合法替换。不能只因本节点观察到网络失败就主动释放远端租约，
    /// 否则在网络分区而非节点宕机时会产生双 owner。
    /// </summary>
    private async ValueTask WaitForLeaseHandoffAsync(
        PulseAddress address,
        ActorPlacement failedPlacement,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await _actorDirectory.ResolveAsync(address.Hub, address.Key, cancellationToken).ConfigureAwait(false);
            if (current is null
                || !string.Equals(current.Value.NodeId, failedPlacement.NodeId, StringComparison.Ordinal)
                || !string.Equals(current.Value.LeaseId, failedPlacement.LeaseId, StringComparison.Ordinal))
            {
                return;
            }

            var remaining = TimeSpan.FromTicks(Math.Max(
                0,
                current.Value.ExpiresAtUtcTicks - DateTime.UtcNow.Ticks));
            var delay = remaining <= TimeSpan.Zero
                ? TimeSpan.FromMilliseconds(10)
                : TimeSpan.FromMilliseconds(Math.Min(remaining.TotalMilliseconds, 250));
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 解析 <see cref="AddressKind.Actor"/> 地址的当前租约。所有候选节点（本地与远端）都必须先经过
    /// 共享目录 CAS，禁止远端候选绕过租约后直接执行。
    /// </summary>
    private async ValueTask<ActorPlacement> ResolveActorPlacementAsync(
        PulseAddress address,
        CancellationToken cancellationToken)
    {
        var explicitNode = !string.IsNullOrEmpty(address.NodeId);
        var candidate = explicitNode ? address.NodeId! : _placementStrategy.SelectOwner(address.Hub, address.Key);
        _diagnostics.RecordPlacementDecision(
            address.Hub,
            address.Key,
            candidate,
            explicitNode ? "ExplicitNode" : _placementStrategy.GetType().Name);

        var placement = await _actorDirectory
            .ActivateAsync(address.Hub, address.Key, candidate, cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(placement.NodeId, _localNodeId, StringComparison.Ordinal))
        {
            _leaseHeartbeat?.Track(address.Hub, address.Key, placement);
        }

        if (!string.Equals(placement.NodeId, candidate, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "拓扑候选属主为 '{Candidate}'，但 Actor 目录显示 (Hub={Hub}, Key={Key}) 的有效租约由 '{ActualOwner}' 持有；以目录为准。",
                candidate, address.Hub, address.Key, placement.NodeId);
        }

        return placement;
    }
}
