using System;
using System.Buffers;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Clustering;
using PulseRPC.Serialization;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Gateway;
using PulseRPC.Server.Processing.Pipeline;
using PulseRPC.Server.Routing;
using PulseRPC.Server.Security;
using PulseRPC.Server.Services;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// <see cref="IClusterInternalHub"/> 的服务端实现 —— 接受来自其它节点的鉴权与 Actor 转发调用。
/// </summary>
public sealed class ClusterInternalHub : IClusterInternalHub
{
    /// <summary>标记一个已通过节点互信鉴权的连接的鉴权 Scope。</summary>
    public const string NodeConnectionScope = NodeConnectionGate.NodeConnectionScope;

    private readonly INodeAuthenticator _authenticator;
    private readonly IServerChannelManager _channelManager;
    private readonly INodeLink _nodeLink;
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceRoutingTable? _routingTable;
    private readonly IResponseSerializerRegistry? _responseSerializerRegistry;
    private readonly ILogger<ClusterInternalHub> _logger;
    private readonly MessageDeduplicationCache _deduplicationCache;

    /// <summary>创建集群内部 Hub。</summary>
    public ClusterInternalHub(
        INodeAuthenticator authenticator,
        IServerChannelManager channelManager,
        INodeLink nodeLink,
        IServiceProvider serviceProvider,
        ILogger<ClusterInternalHub> logger,
        IServiceRoutingTable? routingTable = null,
        IResponseSerializerRegistry? responseSerializerRegistry = null,
        MessageDeduplicationCache? deduplicationCache = null)
    {
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _nodeLink = nodeLink ?? throw new ArgumentNullException(nameof(nodeLink));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _routingTable = routingTable;
        _responseSerializerRegistry = responseSerializerRegistry;
        _deduplicationCache = deduplicationCache ?? new MessageDeduplicationCache();
    }

    /// <inheritdoc/>
    public async Task<bool> AuthenticateAsync(string nodeId, byte[] credential)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            return false;
        }

        var result = await _authenticator.ValidateAsync(nodeId, credential, CancellationToken.None).ConfigureAwait(false);
        if (!result.IsAuthenticated)
        {
            _logger.LogWarning("节点鉴权失败：NodeId={NodeId}, Reason={Reason}", nodeId, result.FailureReason);
            return false;
        }

        var connectionId = PulseContext.CurrentConnectionId;
        if (string.IsNullOrEmpty(connectionId))
        {
            return false;
        }

        var channel = _channelManager.GetChannel(connectionId);
        if (channel is null)
        {
            return false;
        }

        var authContext = new AuthenticationContext(connectionId);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Node") }, "cluster"));
        authContext.SetServiceAuthentication(nodeId, nodeId, token: NodeConnectionScope, scopes: new[] { NodeConnectionScope }, principal: principal);
        channel.SetAuthentication(authContext);

        _logger.LogInformation("节点 '{NodeId}' 已通过互信鉴权，连接 '{ConnectionId}' 标记为集群节点连接", nodeId, connectionId);
        return true;
    }

    /// <inheritdoc/>
    public async Task<byte[]> AskActorAsync(string hub, string key, ushort protocolId, byte[] body, string sourceNodeId = "", string replyTo = "")
    {
        NodeConnectionGate.Require();

        using var virtualScope = EnterVirtualConnectionScopeIfNeeded(sourceNodeId, replyTo);

        var routingTable = RequireRoutingTable();
        var result = await routingTable.RouteByProtocolIdAsync(_serviceProvider, protocolId, key, body, CancellationToken.None).ConfigureAwait(false);

        if (result is null)
        {
            return Array.Empty<byte>();
        }

        var registry = _responseSerializerRegistry ?? ResponseSerializerRegistry.Instance;
        if (registry != null && registry.TryGetSerializer(protocolId, out var serializer))
        {
            var writer = new ArrayBufferWriter<byte>();
            serializer.Serialize(result, writer);
            return writer.WrittenSpan.ToArray();
        }

        throw new InvalidOperationException(
            $"未找到协议号 0x{protocolId:X4} 对应的响应序列化器（IResponseSerializerRegistry），无法完成跨节点 Actor Ask 调用的结果序列化。");
    }

    /// <inheritdoc/>
    public async Task SendActorAsync(string hub, string key, ushort protocolId, byte[] body, string sourceNodeId = "", string replyTo = "", Guid messageId = default)
    {
        NodeConnectionGate.Require();

        // messageId 非空即代表调用方要求本跳去重（对应 ClusterPulseRouter 在 ExactlyOnce 时的转发）：
        // 与本地 LocalPulseRouter.SendToLocalActorAsync 一致，按 (hub,key,messageId) 在本节点去重。
        var scopeKey = $"{hub}:{key}";
        if (messageId != Guid.Empty && !_deduplicationCache.TryReserve(scopeKey, messageId))
        {
            _logger.LogDebug("跨节点 Actor '{ScopeKey}' 收到重复消息 MessageId={MessageId}，按精确一次语义跳过执行。", scopeKey, messageId);
            return;
        }

        using var virtualScope = EnterVirtualConnectionScopeIfNeeded(sourceNodeId, replyTo);

        try
        {
            var routingTable = RequireRoutingTable();
            await routingTable.RouteByProtocolIdAsync(_serviceProvider, protocolId, key, body, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            if (messageId != Guid.Empty)
            {
                // 执行失败：释放去重预占，使发起端后续携带同一 messageId 的合法重试不会被永久性地误判为重复。
                _deduplicationCache.Release(scopeKey, messageId);
            }

            throw;
        }
    }

    /// <summary>
    /// 当 <paramref name="replyTo"/> 非空（本次调用经网关中转）时：为 <c>(sourceNodeId, replyTo)</c>
    /// 惰性注册一个 <see cref="GatewayVirtualChannel"/>（幂等），并建立一个把
    /// <see cref="PulseContext.CurrentConnectionId"/> 覆盖为该虚拟连接标识的嵌套上下文作用域，
    /// 使被调 Actor 方法之后可以经 <c>Clients.Client(PulseContext.CurrentConnectionId)</c> 等寻址方式
    /// 把消息推回真实客户端（§10 多跳回执）。非网关中转时返回 <c>null</c>，不改变环境上下文。
    /// </summary>
    private IDisposable? EnterVirtualConnectionScopeIfNeeded(string sourceNodeId, string replyTo)
    {
        if (string.IsNullOrEmpty(replyTo) || string.IsNullOrEmpty(sourceNodeId))
        {
            return null;
        }

        var virtualConnectionId = GatewayVirtualChannel.ComposeId(sourceNodeId, replyTo);
        _channelManager.GetOrRegisterVirtualChannel(
            virtualConnectionId,
            id => new GatewayVirtualChannel(sourceNodeId, replyTo, _nodeLink));

        var ambient = PulseContext.Current;
        var virtualContext = ambient is PulseContextData data
            ? data with { ConnectionId = virtualConnectionId }
            : PulseContextData.CreateServiceContext(serviceType: "Gateway", serviceId: virtualConnectionId) with { ConnectionId = virtualConnectionId };

        return PulseContext.SetContext(virtualContext);
    }

    private IServiceRoutingTable RequireRoutingTable()
    {
        return _routingTable
            ?? throw new InvalidOperationException(
                "IServiceRoutingTable 未注册，无法完成跨节点 Actor 转发。请确认已引用带 [Channel] Hub 接口的程序集。");
    }
}
