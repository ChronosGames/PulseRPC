using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;
using PulseRPC.Messaging;
using PulseRPC.Shared;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// 内置生产节点 TCP 传输：每目标节点复用一条已认证连接，并提供并发请求关联、超时、断线淘汰与 wire 能力协商。
/// </summary>
/// <remarks>
/// 传输建立后先完成 PulseRPC 底层握手，再调用集群内部 Authenticate，最后协商 node wire 版本与能力；
/// 在三步全部成功前不会发送业务节点帧。网络机密性、完整性和防中间人保护必须由 mTLS service mesh
/// 或 TLS 终止层提供；<see cref="INodeAuthenticator"/>（共享密钥或证书签名）是额外身份检查，不替代 TLS。
/// </remarks>
public sealed class TcpNodeTransport : IVersionedNodeTransport, IDisposable
{
    private readonly INodeEndpointResolver _endpointResolver;
    private readonly INodeAuthenticator _authenticator;
    private readonly TcpNodeTransportOptions _options;
    private readonly ILogger<TcpNodeTransport> _logger;
    private readonly string _localNodeId;
    private readonly ConcurrentDictionary<string, PeerState> _peers = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private int _disposed;

    /// <summary>创建 TCP 节点传输。</summary>
    public TcpNodeTransport(
        INodeEndpointResolver endpointResolver,
        INodeAuthenticator authenticator,
        IOptions<ClusterTopologyOptions> topologyOptions,
        IOptions<TcpNodeTransportOptions> options,
        ILogger<TcpNodeTransport> logger)
    {
        _endpointResolver = endpointResolver ?? throw new ArgumentNullException(nameof(endpointResolver));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        ArgumentNullException.ThrowIfNull(topologyOptions);
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _localNodeId = topologyOptions.Value?.LocalNodeId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_localNodeId))
        {
            throw new InvalidOperationException("ClusterTopologyOptions.LocalNodeId 未配置，无法建立节点出站连接。");
        }

        _options = options.Value ?? new TcpNodeTransportOptions();
        ValidateOptions(_options);
        if (_options.SecurityMode == NodeTransportSecurityMode.InsecureDevelopment)
        {
            _logger.LogWarning(
                "TcpNodeTransport 正在使用 InsecureDevelopment 明文模式；此模式仅允许 loopback 测试，禁止生产部署。");
        }
    }

    /// <inheritdoc/>
    public async ValueTask<NodeTransportSession> GetSessionAsync(
        string targetNodeId,
        CancellationToken cancellationToken = default)
    {
        var peer = await GetPeerAsync(targetNodeId, cancellationToken).ConfigureAwait(false);
        return peer.Session;
    }

    /// <inheritdoc/>
    public async ValueTask SendFrameAsync(
        string targetNodeId,
        ReadOnlyMemory<byte> framedPacket,
        CancellationToken cancellationToken = default)
    {
        var peer = await GetPeerAsync(targetNodeId, cancellationToken).ConfigureAwait(false);
        try
        {
            await peer.Connection.SendFrameAsync(framedPacket, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            await InvalidateAsync(targetNodeId, peer).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ReadOnlyMemory<byte>> AskFrameAsync(
        string targetNodeId,
        ReadOnlyMemory<byte> framedPacket,
        CancellationToken cancellationToken = default)
    {
        var peer = await GetPeerAsync(targetNodeId, cancellationToken).ConfigureAwait(false);
        try
        {
            return await peer.Connection.AskFrameAsync(framedPacket, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            await InvalidateAsync(targetNodeId, peer).ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<ConnectedPeer> GetPeerAsync(
        string targetNodeId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidateTargetNode(targetNodeId);

        var state = _peers.GetOrAdd(targetNodeId, static _ => new PeerState());
        var current = Volatile.Read(ref state.Current);
        if (current is not null && current.Connection.IsConnected)
        {
            return current;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCts.Token);
        var effectiveToken = linkedCts.Token;
        await state.Gate.WaitAsync(effectiveToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            current = state.Current;
            if (current is not null && current.Connection.IsConnected)
            {
                return current;
            }

            current?.Connection.Dispose();
            state.Current = null;

            var nowTicks = DateTime.UtcNow.Ticks;
            if (state.NextConnectAttemptUtcTicks > nowTicks)
            {
                var remaining = TimeSpan.FromTicks(state.NextConnectAttemptUtcTicks - nowTicks);
                throw new IOException(
                    $"节点 '{targetNodeId}' 处于建连失败冷却期，{remaining.TotalMilliseconds:F0}ms 后可重试。",
                    state.LastConnectFailure);
            }

            try
            {
                var connected = await ConnectPeerAsync(targetNodeId, effectiveToken).ConfigureAwait(false);
                try
                {
                    ThrowIfDisposed();
                }
                catch
                {
                    connected.Connection.Dispose();
                    throw;
                }

                state.LastConnectFailure = null;
                state.NextConnectAttemptUtcTicks = 0;
                state.Current = connected;
                return connected;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && !_lifetimeCts.IsCancellationRequested)
            {
                state.LastConnectFailure = ex;
                state.NextConnectAttemptUtcTicks = DateTime.UtcNow.Add(_options.ReconnectBackoff).Ticks;
                throw;
            }
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private async ValueTask<ConnectedPeer> ConnectPeerAsync(
        string targetNodeId,
        CancellationToken cancellationToken)
    {
        if (!_endpointResolver.TryResolve(targetNodeId, out var endpoint) || !endpoint.IsValid)
        {
            throw new InvalidOperationException($"无法解析集群节点 '{targetNodeId}' 的有效端点。");
        }

        if (_options.SecurityMode == NodeTransportSecurityMode.InsecureDevelopment &&
            !IsLoopbackHost(endpoint.Host))
        {
            throw new InvalidOperationException(
                $"InsecureDevelopment 只允许 loopback 节点端点，拒绝明文连接 '{endpoint.Host}:{endpoint.Port}'。");
        }

        var transportOptions = new TcpTransportOptions
        {
            ConnectionTimeout = ToTimeoutMilliseconds(_options.ConnectTimeout),
            MaxPacketSize = _options.MaxFrameSize,
            SendQueueCapacity = _options.SendQueueCapacity,
            NoDelay = _options.NoDelay,
            AutoReconnect = false,
            KeepAlive = true,
        };
        var client = new TcpNodeClient(
            $"{_localNodeId}->{targetNodeId}:{Guid.NewGuid():N}",
            transportOptions,
            _options.ConnectTimeout,
            _logger);
        var connection = new TcpNodeTransportConnection(
            client,
            _options.RequestTimeout,
            _options.SendQueueCapacity,
            _logger);

        try
        {
            await client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken).ConfigureAwait(false);
            await AuthenticateAsync(connection, targetNodeId, cancellationToken).ConfigureAwait(false);
            var session = await NegotiateAsync(connection, targetNodeId, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "节点 TCP 会话已就绪：Local={LocalNodeId}, Remote={RemoteNodeId}, Endpoint={Endpoint}, Wire={WireVersion}, Capabilities={Capabilities}",
                _localNodeId,
                targetNodeId,
                endpoint,
                session.WireVersion,
                session.Capabilities);
            return new ConnectedPeer(connection, session);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private async ValueTask AuthenticateAsync(
        TcpNodeTransportConnection connection,
        string targetNodeId,
        CancellationToken cancellationToken)
    {
        var credential = await _authenticator
            .CreateCredentialAsync(_localNodeId, cancellationToken)
            .ConfigureAwait(false);
        var frame = NodeWireProtocol.BuildFrame(
            MessageType.Request,
            NodeWireProtocol.ClusterInternalHubName,
            NodeWireProtocol.AuthenticateProtocolId,
            NodeWireProtocol.SerializeAuthenticationRequest(_localNodeId, credential.ToArray()),
            MessageFlags.RequireResponse);
        var payload = await connection.AskFrameAsync(frame, cancellationToken).ConfigureAwait(false);
        if (!MemoryPackSerializer.Deserialize<bool>(payload.Span))
        {
            throw new UnauthorizedAccessException($"目标节点 '{targetNodeId}' 拒绝节点身份凭据。");
        }
    }

    private async ValueTask<NodeTransportSession> NegotiateAsync(
        TcpNodeTransportConnection connection,
        string targetNodeId,
        CancellationToken cancellationToken)
    {
        var request = NodeWireProtocol.SerializeNegotiationRequest(_localNodeId);
        var frame = NodeWireProtocol.BuildFrame(
            MessageType.Request,
            NodeWireProtocol.ClusterInternalHubName,
            NodeWireProtocol.NegotiateProtocolId,
            NodeWireProtocol.SerializeByteArrayArgument(request),
            MessageFlags.RequireResponse);
        var responsePayload = await connection.AskFrameAsync(frame, cancellationToken).ConfigureAwait(false);
        var responseBytes = NodeWireProtocol.ParseByteArrayResult(responsePayload.Span);
        var response = NodeWireProtocol.ParseNegotiationResponse(responseBytes);

        if (!response.Accepted)
        {
            throw new InvalidOperationException(
                $"目标节点 '{targetNodeId}' 拒绝 node wire 协商：{response.Error}");
        }

        if (!string.Equals(response.NodeId, targetNodeId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"端点 '{targetNodeId}' 在 wire 协商中声明了不匹配的节点身份 '{response.NodeId}'。");
        }

        if ((response.Capabilities & NodeTransportCapabilities.MutualNodeAuthentication) == 0 ||
            response.Credential is null ||
            response.Credential.Length == 0)
        {
            throw new UnauthorizedAccessException(
                $"目标节点 '{targetNodeId}' 未提供协商所要求的反向身份凭据。");
        }

        var remoteAuthentication = await _authenticator.ValidateAsync(
            response.NodeId,
            response.Credential,
            cancellationToken).ConfigureAwait(false);
        if (!remoteAuthentication.IsAuthenticated)
        {
            throw new UnauthorizedAccessException(
                $"目标节点 '{targetNodeId}' 的反向身份凭据校验失败：{remoteAuthentication.FailureReason}");
        }

        if (response.SelectedWireVersion != NodeWireProtocol.CurrentWireVersion)
        {
            throw new NotSupportedException(
                $"目标节点 '{targetNodeId}' 协商到不受支持的 wire v{response.SelectedWireVersion}；" +
                $"本端生产传输要求 v{NodeWireProtocol.CurrentWireVersion}。");
        }

        if ((response.Capabilities & _options.RequiredCapabilities) != _options.RequiredCapabilities)
        {
            var missing = _options.RequiredCapabilities & ~response.Capabilities;
            throw new NotSupportedException(
                $"目标节点 '{targetNodeId}' 缺少必需 node wire 能力：{missing}。");
        }

        return new NodeTransportSession(response.NodeId, response.SelectedWireVersion, response.Capabilities);
    }

    private async ValueTask InvalidateAsync(string targetNodeId, ConnectedPeer failedPeer)
    {
        if (!_peers.TryGetValue(targetNodeId, out var state))
        {
            failedPeer.Connection.Dispose();
            return;
        }

        await state.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(state.Current, failedPeer))
            {
                state.Current = null;
            }
        }
        finally
        {
            state.Gate.Release();
        }

        failedPeer.Connection.Dispose();
    }

    private void ValidateTargetNode(string targetNodeId)
    {
        if (string.IsNullOrWhiteSpace(targetNodeId))
        {
            throw new ArgumentException("目标节点标识不能为空或空白。", nameof(targetNodeId));
        }

        if (string.Equals(targetNodeId, _localNodeId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("TcpNodeTransport 不接受发往本节点的帧；本地调用必须走 LocalPulseRouter。");
        }
    }

    private static void ValidateOptions(TcpNodeTransportOptions options)
    {
        if (options.SecurityMode == NodeTransportSecurityMode.Unspecified)
        {
            throw new InvalidOperationException(
                "必须显式配置 TcpNodeTransportOptions.SecurityMode。生产节点需由外部 mTLS/TLS 层保护并选择 " +
                "ExternalMutualTls；仅 loopback 测试可选择 InsecureDevelopment。");
        }

        if (options.ConnectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "节点 TCP 建连超时必须大于零。");
        }

        if (options.RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "节点请求超时必须大于零。");
        }

        if (options.MaxFrameSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "节点最大帧必须大于零。");
        }

        if (options.SendQueueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "节点发送队列容量必须大于零。");
        }

        if (options.ReconnectBackoff < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "节点重连冷却时间不能为负数。");
        }
    }

    private static int ToTimeoutMilliseconds(TimeSpan timeout)
        => (int)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue);

    private static bool IsLoopbackHost(string host)
        => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
           (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpNodeTransport));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetimeCts.Cancel();
        foreach (var state in _peers.Values)
        {
            Interlocked.Exchange(ref state.Current, null)?.Connection.Dispose();
        }

        _peers.Clear();
    }

    private sealed class PeerState
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public ConnectedPeer? Current;
        public Exception? LastConnectFailure;
        public long NextConnectAttemptUtcTicks;
    }

    private sealed class ConnectedPeer
    {
        public ConnectedPeer(TcpNodeTransportConnection connection, NodeTransportSession session)
        {
            Connection = connection;
            Session = session;
        }

        public TcpNodeTransportConnection Connection { get; }
        public NodeTransportSession Session { get; }
    }
}
