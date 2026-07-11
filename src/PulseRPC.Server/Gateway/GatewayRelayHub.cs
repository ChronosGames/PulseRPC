using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Gateway;

/// <summary>
/// <see cref="IGatewayRelayHub"/> 的服务端实现 —— 运行在 Gateway 节点上，接受来自后端节点的推送/反向 Ask
/// 转发请求，投递给本机上真实的客户端连接。
/// </summary>
public sealed class GatewayRelayHub : IGatewayRelayHub
{
    private readonly IServerChannelManager _channelManager;
    private readonly ILogger<GatewayRelayHub> _logger;

    /// <summary>创建网关中继 Hub。</summary>
    public GatewayRelayHub(IServerChannelManager channelManager, ILogger<GatewayRelayHub> logger)
    {
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task PushRawFrameAsync(string connectionId, byte[] framedPacket)
    {
        NodeConnectionGate.Require();

        var channel = _channelManager.GetChannel(connectionId);
        if (channel is null)
        {
            _logger.LogWarning("网关推送目标连接 '{ConnectionId}' 不存在或已断开，丢弃该帧。", connectionId);
            return;
        }

        await channel.SendAsync(
            framedPacket,
            PulseContext.Current?.CancellationToken ?? default).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<byte[]> AskConnectionAsync(string connectionId, ushort protocolId, byte[] payload, int timeoutMs)
    {
        NodeConnectionGate.Require();

        var channel = _channelManager.GetChannel(connectionId)
            ?? throw new InvalidOperationException($"网关反向 Ask 目标连接 '{connectionId}' 不存在或已断开。");

        var timeout = timeoutMs > 0 ? TimeSpan.FromMilliseconds(timeoutMs) : TimeSpan.Zero;
        var result = await channel.InvokeClientAsync(
            protocolId,
            payload,
            timeout,
            PulseContext.Current?.CancellationToken ?? default).ConfigureAwait(false);
        return result.ToArray();
    }
}
