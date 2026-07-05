using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Client;
using PulseRPC.Client.Configuration;
using PulseRPC.Clustering;
using PulseRPC.Server.Gateway;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// <see cref="INodeLink"/> 首版实现 —— 复用 <see cref="PulseRPC.Client"/> 的全双工连接层
/// 向对端节点建立出站连接，经生成的 <see cref="IClusterInternalHub"/>/<see cref="IGatewayRelayHub"/>
/// 客户端桩转发 Actor 调用与网关虚拟连接推送/反向 Ask。
/// </summary>
/// <remarks>
/// <para>
/// 出站连接按目标 <c>NodeId</c> 惰性建立并缓存（懒连接 + 复用，避免每次调用都重连）；
/// 建连后立即经 <see cref="INodeAuthenticator"/> 完成节点互信鉴权（见 <see cref="ClusterInternalHub.AuthenticateAsync"/>），
/// 失败或连接断开时从缓存移除，下次调用重新建连。缓存的是<strong>已鉴权的连接</strong>本身（而非某个固定 Hub 的
/// 调用桩），因为同一物理连接上可能需要同时调用 <see cref="IClusterInternalHub"/>（Actor 转发）与
/// <see cref="IGatewayRelayHub"/>（网关虚拟连接的最后一跳投递）。
/// </para>
/// <para>
/// P4 首版仅支持《静态成员》拓扑（<see cref="ClusterTopologyOptions"/>），目标节点必须在配置中登记端点。
/// </para>
/// </remarks>
public sealed class PulseNodeLink : INodeLink, IDisposable
{
    private readonly ClusterTopologyOptions _topology;
    private readonly INodeEndpointResolver _endpointResolver;
    private readonly INodeAuthenticator _authenticator;
    private readonly ILogger<PulseNodeLink> _logger;
    private readonly IPulseClient _client;

    private readonly ConcurrentDictionary<string, Lazy<Task<IClientChannel>>> _channels = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private volatile bool _clientStarted;
    private bool _disposed;

    /// <summary>创建节点间链路。</summary>
    public PulseNodeLink(
        IOptions<ClusterTopologyOptions> topologyOptions,
        INodeEndpointResolver endpointResolver,
        INodeAuthenticator authenticator,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(topologyOptions);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _topology = topologyOptions.Value ?? throw new ArgumentNullException(nameof(topologyOptions));
        _endpointResolver = endpointResolver ?? throw new ArgumentNullException(nameof(endpointResolver));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _logger = loggerFactory.CreateLogger<PulseNodeLink>();
        _client = new PulseClientBuilder().WithLogging(loggerFactory).Build();
    }

    /// <inheritdoc/>
    public async ValueTask<ReadOnlyMemory<byte>> AskActorAsync(
        string targetNodeId, string hub, string key, ushort protocolId, ReadOnlyMemory<byte> body,
        string sourceNodeId = "", string replyTo = "", CancellationToken cancellationToken = default)
    {
        var channel = await GetOrConnectChannelAsync(targetNodeId, cancellationToken).ConfigureAwait(false);
        var hubProxy = new ClusterInternalHubClientStub(channel);
        return await hubProxy.AskActorAsync(hub, key, protocolId, body.ToArray(), sourceNodeId, replyTo).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask SendActorAsync(
        string targetNodeId, string hub, string key, ushort protocolId, ReadOnlyMemory<byte> body,
        string sourceNodeId = "", string replyTo = "", CancellationToken cancellationToken = default, Guid messageId = default)
    {
        var channel = await GetOrConnectChannelAsync(targetNodeId, cancellationToken).ConfigureAwait(false);
        var hubProxy = new ClusterInternalHubClientStub(channel);
        await hubProxy.SendActorAsync(hub, key, protocolId, body.ToArray(), sourceNodeId, replyTo, messageId).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask SendToConnectionAsync(
        string targetNodeId, string connectionId, ReadOnlyMemory<byte> framedPacket, CancellationToken cancellationToken = default)
    {
        var channel = await GetOrConnectChannelAsync(targetNodeId, cancellationToken).ConfigureAwait(false);
        var hubProxy = new GatewayRelayHubClientStub(channel);
        await hubProxy.PushRawFrameAsync(connectionId, framedPacket.ToArray()).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<ReadOnlyMemory<byte>> AskConnectionAsync(
        string targetNodeId, string connectionId, ushort protocolId, ReadOnlyMemory<byte> payload, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var channel = await GetOrConnectChannelAsync(targetNodeId, cancellationToken).ConfigureAwait(false);
        var hubProxy = new GatewayRelayHubClientStub(channel);
        return await hubProxy.AskConnectionAsync(connectionId, protocolId, payload.ToArray(), (int)timeout.TotalMilliseconds).ConfigureAwait(false);
    }

    private async Task<IClientChannel> GetOrConnectChannelAsync(string targetNodeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetNodeId);
        await EnsureClientStartedAsync(cancellationToken).ConfigureAwait(false);

        var lazy = _channels.GetOrAdd(
            targetNodeId,
            id => new Lazy<Task<IClientChannel>>(() => ConnectAndAuthenticateAsync(id, cancellationToken)));

        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            // 建连/鉴权失败：移除缓存条目，避免后续调用一直复用一个失败的 Lazy<Task>。
            _channels.TryRemove(targetNodeId, out _);
            throw;
        }
    }

    private async Task<IClientChannel> ConnectAndAuthenticateAsync(string targetNodeId, CancellationToken cancellationToken)
    {
        if (!_endpointResolver.TryResolve(targetNodeId, out var endpoint) || !endpoint.IsValid)
        {
            throw new InvalidOperationException(
                $"无法解析节点 '{targetNodeId}' 的端点（INodeEndpointResolver 未命中或端点无效）——" +
                "静态部署需在 ClusterTopologyOptions.Members 中登记；动态部署需确保该节点已被服务发现后端发现，无法建立节点间链路。");
        }

        var channel = await _client.ConnectToServerAsync(
            endpoint.Host,
            endpoint.Port,
            serverId: $"cluster-node-{targetNodeId}",
            name: $"cluster-link-{targetNodeId}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var hubProxy = new ClusterInternalHubClientStub(channel);

        var credential = await _authenticator.CreateCredentialAsync(_topology.LocalNodeId, cancellationToken).ConfigureAwait(false);
        var authenticated = await hubProxy.AuthenticateAsync(_topology.LocalNodeId, credential.ToArray()).ConfigureAwait(false);
        if (!authenticated)
        {
            throw new UnauthorizedAccessException(
                $"节点互信鉴权失败：本地节点 '{_topology.LocalNodeId}' 未能通过目标节点 '{targetNodeId}' 的鉴权校验。");
        }

        _logger.LogInformation(
            "已建立到集群节点 '{TargetNodeId}' ({Host}:{Port}) 的出站链路并完成互信鉴权", targetNodeId, endpoint.Host, endpoint.Port);

        return channel;
    }

    private async Task EnsureClientStartedAsync(CancellationToken cancellationToken)
    {
        if (_clientStarted)
        {
            return;
        }

        await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_clientStarted)
            {
                return;
            }

            await _client.InitializeAsync(cancellationToken).ConfigureAwait(false);
            _clientStarted = true;
        }
        finally
        {
            _startLock.Release();
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
        _startLock.Dispose();
        _client.Dispose();
    }
}
