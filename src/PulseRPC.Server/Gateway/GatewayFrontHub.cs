using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;
using PulseRPC.Routing;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Contexts;

namespace PulseRPC.Server.Gateway;

/// <summary>
/// <see cref="IGatewayFrontHub"/> 的服务端实现 —— 运行在 Gateway 节点上，把外部客户端的调用
/// 经 <see cref="IPulseRouter"/> 转发给拥有目标 Actor 实例的节点（见 §6.3 数据面）。
/// </summary>
public sealed class GatewayFrontHub : IGatewayFrontHub
{
    private readonly IPulseRouter _router;
    private readonly string _localNodeId;
    private readonly ILogger<GatewayFrontHub> _logger;
    private readonly IConnectionDirectory? _connectionDirectory;
    private readonly IServiceRoutingTable? _routingTable;
    private readonly IEnumerable<IGatewayActorInvocationPolicy> _invocationPolicies;

    /// <summary>创建网关前端中转 Hub。</summary>
    public GatewayFrontHub(
        IPulseRouter router,
        IOptions<ClusterTopologyOptions> topologyOptions,
        ILogger<GatewayFrontHub> logger)
        : this(
            router,
            topologyOptions,
            logger,
            connectionDirectory: null,
            routingTable: null,
            invocationPolicies: Array.Empty<IGatewayActorInvocationPolicy>())
    {
    }

    /// <summary>创建网关前端中转 Hub。</summary>
    public GatewayFrontHub(
        IPulseRouter router,
        IOptions<ClusterTopologyOptions> topologyOptions,
        ILogger<GatewayFrontHub> logger,
        IConnectionDirectory? connectionDirectory,
        IServiceRoutingTable? routingTable = null)
        : this(
            router,
            topologyOptions,
            logger,
            connectionDirectory,
            routingTable,
            Array.Empty<IGatewayActorInvocationPolicy>())
    {
    }

    internal GatewayFrontHub(
        IPulseRouter router,
        IOptions<ClusterTopologyOptions> topologyOptions,
        ILogger<GatewayFrontHub> logger,
        IConnectionDirectory? connectionDirectory,
        IServiceRoutingTable? routingTable,
        IEnumerable<IGatewayActorInvocationPolicy> invocationPolicies)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        ArgumentNullException.ThrowIfNull(topologyOptions);
        _localNodeId = topologyOptions.Value?.LocalNodeId ?? string.Empty;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionDirectory = connectionDirectory;
        _routingTable = routingTable;
        _invocationPolicies = invocationPolicies ?? throw new ArgumentNullException(nameof(invocationPolicies));
    }

    /// <inheritdoc/>
    public async Task<byte[]> RelayAskAsync(string hub, string key, ushort protocolId, byte[] body, byte hopLimit)
    {
        RequireHop(hopLimit);
        RequireHubProtocol(hub, protocolId);

        var context = RequireExternalClientContext();
        var cancellationToken = context.CancellationToken;
        var clientConnectionId = context.ConnectionId!;
        await EvaluateInvocationPoliciesAsync(
            hub,
            key,
            protocolId,
            GatewayActorInvocationKind.Ask,
            context,
            cancellationToken).ConfigureAwait(false);
        await RegisterGatewayVirtualConnectionAsync(clientConnectionId, cancellationToken).ConfigureAwait(false);
        using (GatewayRelayContext.SetScope(_localNodeId, clientConnectionId))
        {
            var result = await _router.AskAsync(
                PulseAddress.Actor(hub, key), protocolId, body, cancellationToken).ConfigureAwait(false);
            return result.ToArray();
        }
    }

    /// <inheritdoc/>
    public async Task RelaySendAsync(string hub, string key, ushort protocolId, byte[] body, byte hopLimit)
    {
        RequireHop(hopLimit);
        RequireHubProtocol(hub, protocolId);

        var context = RequireExternalClientContext();
        var cancellationToken = context.CancellationToken;
        var clientConnectionId = context.ConnectionId!;
        await EvaluateInvocationPoliciesAsync(
            hub,
            key,
            protocolId,
            GatewayActorInvocationKind.Send,
            context,
            cancellationToken).ConfigureAwait(false);
        await RegisterGatewayVirtualConnectionAsync(clientConnectionId, cancellationToken).ConfigureAwait(false);
        using (GatewayRelayContext.SetScope(_localNodeId, clientConnectionId))
        {
            await _router.SendAsync(
                PulseAddress.Actor(hub, key), protocolId, body,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask EvaluateInvocationPoliciesAsync(
        string hub,
        string key,
        ushort protocolId,
        GatewayActorInvocationKind invocationKind,
        IPulseContext callerContext,
        System.Threading.CancellationToken cancellationToken)
    {
        var invocationContext = new GatewayActorInvocationContext(
            hub,
            key,
            protocolId,
            invocationKind,
            callerContext);

        foreach (var policy in _invocationPolicies)
        {
            await policy.EvaluateAsync(invocationContext, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask RegisterGatewayVirtualConnectionAsync(
        string clientConnectionId,
        System.Threading.CancellationToken cancellationToken)
    {
        if (_connectionDirectory is null || string.IsNullOrEmpty(_localNodeId) || string.IsNullOrEmpty(clientConnectionId))
        {
            return;
        }

        var virtualConnectionId = GatewayVirtualChannel.ComposeId(_localNodeId, clientConnectionId);
        await _connectionDirectory.RegisterConnectionAsync(
            virtualConnectionId,
            new ConnectionPlacement(_localNodeId, clientConnectionId),
            cancellationToken).ConfigureAwait(false);
    }

    private void RequireHop(byte hopLimit)
    {
        if (hopLimit == 0)
        {
            _logger.LogWarning("网关中转 HopLimit 已耗尽，拒绝转发（可能存在网关拓扑配置环路）。");
            throw new InvalidOperationException("HopLimit 已耗尽，拒绝转发：可能存在网关转发环路。");
        }
    }

    private void RequireHubProtocol(string hub, ushort protocolId)
    {
        if (_routingTable is not null && !_routingTable.IsProtocolIdValid(hub, protocolId))
        {
            throw new InvalidOperationException(
                $"协议号 0x{protocolId:X4} 不属于 canonical Hub '{hub}'，网关拒绝在 placement 前转发。");
        }
    }

    private static IPulseContext RequireExternalClientContext()
    {
        var context = PulseContext.Current;
        if (context?.SourceType != CallSourceType.ExternalUser)
        {
            throw new UnauthorizedAccessException("Gateway Front only accepts external client calls.");
        }

        if (string.IsNullOrEmpty(context.ConnectionId))
        {
            throw new InvalidOperationException("Gateway Front requires an active client connection id.");
        }

        return context;
    }
}
