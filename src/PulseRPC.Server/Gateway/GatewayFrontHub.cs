using System;
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

    /// <summary>创建网关前端中转 Hub。</summary>
    public GatewayFrontHub(
        IPulseRouter router,
        IOptions<ClusterTopologyOptions> topologyOptions,
        ILogger<GatewayFrontHub> logger)
        : this(router, topologyOptions, logger, connectionDirectory: null)
    {
    }

    /// <summary>创建网关前端中转 Hub。</summary>
    public GatewayFrontHub(
        IPulseRouter router,
        IOptions<ClusterTopologyOptions> topologyOptions,
        ILogger<GatewayFrontHub> logger,
        IConnectionDirectory? connectionDirectory)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        ArgumentNullException.ThrowIfNull(topologyOptions);
        _localNodeId = topologyOptions.Value?.LocalNodeId ?? string.Empty;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionDirectory = connectionDirectory;
    }

    /// <inheritdoc/>
    public async Task<byte[]> RelayAskAsync(string hub, string key, ushort protocolId, byte[] body, byte hopLimit)
    {
        RequireHop(hopLimit);

        var clientConnectionId = PulseContext.CurrentConnectionId ?? string.Empty;
        await RegisterGatewayVirtualConnectionAsync(clientConnectionId).ConfigureAwait(false);
        using (GatewayRelayContext.SetScope(_localNodeId, clientConnectionId))
        {
            var result = await _router.AskAsync(PulseAddress.Actor(hub, key), protocolId, body).ConfigureAwait(false);
            return result.ToArray();
        }
    }

    /// <inheritdoc/>
    public async Task RelaySendAsync(string hub, string key, ushort protocolId, byte[] body, byte hopLimit)
    {
        RequireHop(hopLimit);

        var clientConnectionId = PulseContext.CurrentConnectionId ?? string.Empty;
        await RegisterGatewayVirtualConnectionAsync(clientConnectionId).ConfigureAwait(false);
        using (GatewayRelayContext.SetScope(_localNodeId, clientConnectionId))
        {
            await _router.SendAsync(PulseAddress.Actor(hub, key), protocolId, body).ConfigureAwait(false);
        }
    }

    private async ValueTask RegisterGatewayVirtualConnectionAsync(string clientConnectionId)
    {
        if (_connectionDirectory is null || string.IsNullOrEmpty(_localNodeId) || string.IsNullOrEmpty(clientConnectionId))
        {
            return;
        }

        var virtualConnectionId = GatewayVirtualChannel.ComposeId(_localNodeId, clientConnectionId);
        await _connectionDirectory.RegisterConnectionAsync(
            virtualConnectionId,
            new ConnectionPlacement(_localNodeId, clientConnectionId)).ConfigureAwait(false);
    }

    private void RequireHop(byte hopLimit)
    {
        if (hopLimit == 0)
        {
            _logger.LogWarning("网关中转 HopLimit 已耗尽，拒绝转发（可能存在网关拓扑配置环路）。");
            throw new InvalidOperationException("HopLimit 已耗尽，拒绝转发：可能存在网关转发环路。");
        }
    }
}
