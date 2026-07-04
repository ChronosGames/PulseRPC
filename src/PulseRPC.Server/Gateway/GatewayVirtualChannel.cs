using System.Net;
using PulseRPC.Authentication;
using PulseRPC.Clustering;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;

namespace PulseRPC.Server.Gateway;

/// <summary>
/// 虚拟连接（Virtual Connection，§6.2）—— 在<strong>持有 Actor 的后端节点</strong>上代表
/// 「经 Gateway 桥接的远程客户端」的 <see cref="IServerChannel"/>。
/// </summary>
/// <remarks>
/// <para>
/// 对该通道的 <see cref="SendAsync"/>/<see cref="InvokeClientAsync"/> 并不直接写底层网络传输，
/// 而是经 <see cref="INodeLink"/> 打包成 node↔node 帧转发给持有真实客户端连接的网关节点
/// （<c>IGatewayRelayHub</c>），由网关完成最后一跳投递/反向 Ask。
/// </para>
/// <para>
/// 好处（见设计 §6.2）：后端上的 Fan-out 选择器（<c>IHubClients</c>/<c>ServerChannelManager</c>）
/// 无需感知客户端是否远程 —— 对该虚拟连接 <c>Send</c> 即可，网关负责最后一跳。
/// </para>
/// </remarks>
public sealed class GatewayVirtualChannel : IServerChannel
{
    private static readonly EndPoint PlaceholderEndPoint = new IPEndPoint(IPAddress.None, 0);

    private readonly INodeLink _nodeLink;
    private readonly string _gatewayNodeId;
    private readonly string _originalConnectionId;

    /// <summary>
    /// 按设计 §6.2 组合虚拟连接标识：<c>gatewayNodeId + ':' + originalConnectionId</c>。
    /// </summary>
    public static string ComposeId(string gatewayNodeId, string originalConnectionId)
        => $"{gatewayNodeId}:{originalConnectionId}";

    /// <summary>创建一个虚拟连接。</summary>
    /// <param name="gatewayNodeId">持有真实客户端连接的网关节点标识。</param>
    /// <param name="originalConnectionId">该客户端在网关上的真实连接 Id。</param>
    /// <param name="nodeLink">用于向网关节点转发的节点间链路。</param>
    public GatewayVirtualChannel(string gatewayNodeId, string originalConnectionId, INodeLink nodeLink)
    {
        _gatewayNodeId = gatewayNodeId ?? throw new ArgumentNullException(nameof(gatewayNodeId));
        _originalConnectionId = originalConnectionId ?? throw new ArgumentNullException(nameof(originalConnectionId));
        _nodeLink = nodeLink ?? throw new ArgumentNullException(nameof(nodeLink));

        Id = ComposeId(gatewayNodeId, originalConnectionId);
        ConnectedAt = DateTime.UtcNow;
        LastActiveTime = ConnectedAt;
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public DateTime ConnectedAt { get; }

    /// <inheritdoc/>
    public DateTime LastActiveTime { get; private set; }

    /// <inheritdoc/>
    public EndPoint LocalEndPoint => PlaceholderEndPoint;

    /// <inheritdoc/>
    public EndPoint RemoteEndPoint => PlaceholderEndPoint;

    /// <inheritdoc/>
    public TransportType Type => TransportType.TCP;

    /// <inheritdoc/>
    public bool IsAuthenticated => AuthenticationContext?.IsAuthenticated ?? false;

    /// <inheritdoc/>
    public IAuthenticationContext? AuthenticationContext { get; set; }

    /// <inheritdoc/>
    public void SetAuthentication(IAuthenticationContext authContext) => AuthenticationContext = authContext;

    /// <inheritdoc/>
    public void ClearAuthentication() => AuthenticationContext = null;

    /// <inheritdoc/>
    public async Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        try
        {
            await _nodeLink.SendToConnectionAsync(_gatewayNodeId, _originalConnectionId, data, cancellationToken)
                .ConfigureAwait(false);
            LastActiveTime = DateTime.UtcNow;
            return true;
        }
        catch
        {
            // 与真实传输通道保持一致的容错语义：单次投递失败不抛出，由上层 Fan-out 逻辑忽略。
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<ReadOnlyMemory<byte>> InvokeClientAsync(
        ushort protocolId,
        ReadOnlyMemory<byte> payload,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        LastActiveTime = DateTime.UtcNow;
        return await _nodeLink.AskConnectionAsync(_gatewayNodeId, _originalConnectionId, protocolId, payload, timeout, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public event EventHandler<TransportStateEventArgs>? StateChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public event EventHandler<MessageParsedEventArgs>? MessageParsed
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public event EventHandler<MessageProcessedEventArgs>? MessageProcessed
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // 虚拟通道不持有任何底层网络资源，无需释放；生命周期由 ServerChannelManager.RemoveChannel 驱动。
    }
}
