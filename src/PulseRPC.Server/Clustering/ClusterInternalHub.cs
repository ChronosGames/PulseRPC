using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly IActorDirectory? _actorDirectory;
    private readonly string _localNodeId;
    private readonly ClusterNodeWireOptions _wireOptions;
    private readonly IActorLeaseHeartbeat? _leaseHeartbeat;

    /// <summary>创建集群内部 Hub。</summary>
    public ClusterInternalHub(
        INodeAuthenticator authenticator,
        IServerChannelManager channelManager,
        INodeLink nodeLink,
        IServiceProvider serviceProvider,
        ILogger<ClusterInternalHub> logger,
        IServiceRoutingTable? routingTable = null,
        IResponseSerializerRegistry? responseSerializerRegistry = null,
        MessageDeduplicationCache? deduplicationCache = null,
        IActorDirectory? actorDirectory = null,
        IOptions<ClusterTopologyOptions>? topologyOptions = null,
        IOptions<ClusterNodeWireOptions>? wireOptions = null,
        IActorLeaseHeartbeat? leaseHeartbeat = null)
    {
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _nodeLink = nodeLink ?? throw new ArgumentNullException(nameof(nodeLink));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _routingTable = routingTable;
        _responseSerializerRegistry = responseSerializerRegistry;
        _deduplicationCache = deduplicationCache ?? new MessageDeduplicationCache();
        _actorDirectory = actorDirectory;
        _localNodeId = topologyOptions?.Value?.LocalNodeId ?? string.Empty;
        _wireOptions = wireOptions?.Value ?? new ClusterNodeWireOptions();
        _leaseHeartbeat = leaseHeartbeat;
        ValidateWireOptions(_wireOptions);
    }

    /// <inheritdoc/>
    public async Task<bool> AuthenticateAsync(string nodeId, byte[] credential)
    {
        if (string.IsNullOrWhiteSpace(nodeId) ||
            nodeId.Length > _wireOptions.MaxFieldLength ||
            credential is null ||
            credential.Length == 0 ||
            credential.Length > _wireOptions.MaxNodeCredentialSize)
        {
            _logger.LogWarning("拒绝格式或长度不合法的节点认证请求。");
            return false;
        }

        var result = await _authenticator.ValidateAsync(
            nodeId,
            credential,
            PulseContext.Current?.CancellationToken ?? default).ConfigureAwait(false);
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
    public async Task<byte[]> NegotiateAsync(byte[] request)
    {
        NodeConnectionGate.Require();

        NodeNegotiationRequest negotiation;
        try
        {
            negotiation = NodeWireProtocol.ParseNegotiationRequest(request);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "节点 wire 协商请求无法解析。");
            return NodeWireProtocol.SerializeNegotiationResponse(new NodeNegotiationResponse
            {
                Accepted = false,
                NodeId = _localNodeId,
                Error = "Invalid node negotiation payload.",
            });
        }

        var physicalIdentity = PulseContext.Current?.AuthenticationContext?.Identity;
        if (string.IsNullOrEmpty(negotiation.NodeId) ||
            !string.Equals(negotiation.NodeId, physicalIdentity, StringComparison.Ordinal))
        {
            return NodeWireProtocol.SerializeNegotiationResponse(new NodeNegotiationResponse
            {
                Accepted = false,
                NodeId = _localNodeId,
                Error = "Negotiation node id does not match the authenticated connection.",
            });
        }

        if (negotiation.MinWireVersion > negotiation.MaxWireVersion ||
            negotiation.MinWireVersion > NodeWireProtocol.CurrentWireVersion ||
            negotiation.MaxWireVersion < NodeWireProtocol.CurrentWireVersion)
        {
            return NodeWireProtocol.SerializeNegotiationResponse(new NodeNegotiationResponse
            {
                Accepted = false,
                NodeId = _localNodeId,
                Error = $"No common node wire version. Local={NodeWireProtocol.CurrentWireVersion}, " +
                        $"Remote={negotiation.MinWireVersion}-{negotiation.MaxWireVersion}.",
            });
        }

        var connectionId = PulseContext.CurrentConnectionId;
        var channel = string.IsNullOrEmpty(connectionId) ? null : _channelManager.GetChannel(connectionId);
        if (channel is null)
        {
            return NodeWireProtocol.SerializeNegotiationResponse(new NodeNegotiationResponse
            {
                Accepted = false,
                NodeId = _localNodeId,
                Error = "The authenticated node connection is no longer active.",
            });
        }

        var capabilities = negotiation.Capabilities & NodeWireProtocol.SupportedCapabilities;
        ReadOnlyMemory<byte> localCredential;
        try
        {
            localCredential = await _authenticator.CreateCredentialAsync(
                _localNodeId,
                PulseContext.Current?.CancellationToken ?? default).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "无法为 node wire 协商创建本节点身份凭据。");
            return NodeWireProtocol.SerializeNegotiationResponse(new NodeNegotiationResponse
            {
                Accepted = false,
                NodeId = _localNodeId,
                Error = "The responding node could not create a mutual-authentication credential.",
            });
        }

        if (localCredential.IsEmpty)
        {
            return NodeWireProtocol.SerializeNegotiationResponse(new NodeNegotiationResponse
            {
                Accepted = false,
                NodeId = _localNodeId,
                Error = "The responding node produced an empty mutual-authentication credential.",
            });
        }

        RecordNegotiatedSession(channel, negotiation.NodeId, NodeWireProtocol.CurrentWireVersion, capabilities);

        return NodeWireProtocol.SerializeNegotiationResponse(new NodeNegotiationResponse
        {
            Accepted = true,
            SelectedWireVersion = NodeWireProtocol.CurrentWireVersion,
            Capabilities = capabilities,
            NodeId = _localNodeId,
            Credential = localCredential.ToArray(),
        });
    }

    /// <inheritdoc/>
    public async Task<byte[]> AskActorAsync(string hub, string key, ushort protocolId, byte[] body, string sourceNodeId = "", string replyTo = "")
    {
        NodeConnectionGate.Require();
        RequireLegacyActorProtocol();

        var routingTable = RequireRoutingTable();
        ValidateHubProtocol(routingTable, hub, protocolId);
        using var virtualScope = EnterVirtualConnectionScopeIfNeeded(hub, key, sourceNodeId, replyTo);

        var result = await routingTable.RouteByProtocolIdAsync(
            _serviceProvider,
            hub,
            protocolId,
            key,
            body,
            PulseContext.Current?.CancellationToken ?? default).ConfigureAwait(false);

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
    public async Task<byte[]> AskActorV2Async(byte[] envelope)
    {
        NodeConnectionGate.Require();
        var negotiatedCapabilities = RequireVersionedActorSession();
        var invocation = ParseAndValidateInvocation(envelope, negotiatedCapabilities);
        var routingTable = RequireRoutingTable();
        ValidateHubProtocol(routingTable, invocation.Hub, invocation.ProtocolId);
        await ValidateLeaseAsync(invocation, PulseContext.Current?.CancellationToken ?? default).ConfigureAwait(false);

        using var callerScope = EnterInvocationScope(invocation);
        var result = await routingTable.RouteByProtocolIdAsync(
            _serviceProvider,
            invocation.Hub,
            invocation.ProtocolId,
            invocation.Key,
            invocation.Body,
            PulseContext.Current?.CancellationToken ?? default).ConfigureAwait(false);

        if (result is null)
        {
            return Array.Empty<byte>();
        }

        var registry = _responseSerializerRegistry ?? ResponseSerializerRegistry.Instance;
        if (registry != null && registry.TryGetSerializer(invocation.ProtocolId, out var serializer))
        {
            var writer = new ArrayBufferWriter<byte>();
            serializer.Serialize(result, writer);
            return writer.WrittenSpan.ToArray();
        }

        throw new InvalidOperationException(
            $"未找到协议号 0x{invocation.ProtocolId:X4} 对应的响应序列化器（IResponseSerializerRegistry），无法完成跨节点 Actor Ask 调用的结果序列化。");
    }

    /// <inheritdoc/>
    public async Task SendActorAsync(string hub, string key, ushort protocolId, byte[] body, string sourceNodeId = "", string replyTo = "", Guid messageId = default)
    {
        NodeConnectionGate.Require();
        RequireLegacyActorProtocol();

        var routingTable = RequireRoutingTable();
        ValidateHubProtocol(routingTable, hub, protocolId);

        // messageId 非空即代表调用方要求本跳去重（对应 ClusterPulseRouter 在 ExactlyOnce 时的转发）：
        // 与本地 LocalPulseRouter.SendToLocalActorAsync 一致，按 (hub,key,messageId) 在本节点去重。
        var scopeKey = $"{hub}:{key}";
        if (messageId != Guid.Empty && !_deduplicationCache.TryReserve(scopeKey, messageId))
        {
            _logger.LogDebug("跨节点 Actor '{ScopeKey}' 收到重复消息 MessageId={MessageId}，按精确一次语义跳过执行。", scopeKey, messageId);
            return;
        }

        using var virtualScope = EnterVirtualConnectionScopeIfNeeded(hub, key, sourceNodeId, replyTo);

        try
        {
            await routingTable.RouteByProtocolIdAsync(
                _serviceProvider,
                hub,
                protocolId,
                key,
                body,
                PulseContext.Current?.CancellationToken ?? default).ConfigureAwait(false);
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

    /// <inheritdoc/>
    public async Task SendActorV2Async(byte[] envelope)
    {
        NodeConnectionGate.Require();
        var negotiatedCapabilities = RequireVersionedActorSession();
        var invocation = ParseAndValidateInvocation(envelope, negotiatedCapabilities);
        var routingTable = RequireRoutingTable();
        ValidateHubProtocol(routingTable, invocation.Hub, invocation.ProtocolId);
        await ValidateLeaseAsync(invocation, PulseContext.Current?.CancellationToken ?? default).ConfigureAwait(false);

        var scopeKey = $"{invocation.Hub}:{invocation.Key}";
        if (invocation.MessageId != Guid.Empty &&
            !_deduplicationCache.TryReserve(scopeKey, invocation.MessageId))
        {
            _logger.LogDebug(
                "跨节点 Actor '{ScopeKey}' 收到重复 V2 消息 MessageId={MessageId}，跳过执行。",
                scopeKey,
                invocation.MessageId);
            return;
        }

        using var callerScope = EnterInvocationScope(invocation);
        try
        {
            await routingTable.RouteByProtocolIdAsync(
                _serviceProvider,
                invocation.Hub,
                invocation.ProtocolId,
                invocation.Key,
                invocation.Body,
                PulseContext.Current?.CancellationToken ?? default).ConfigureAwait(false);
        }
        catch
        {
            if (invocation.MessageId != Guid.Empty)
            {
                _deduplicationCache.Release(scopeKey, invocation.MessageId);
            }

            throw;
        }
    }

    private static void ValidateWireOptions(ClusterNodeWireOptions options)
    {
        if (options.MaxNodeCredentialSize <= 0 ||
            options.MaxIdentityCount <= 0 ||
            options.MaxClaimCount <= 0 ||
            options.MaxClaimPropertyCount <= 0 ||
            options.MaxAuthorizationValueCount <= 0 ||
            options.MaxFieldLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Node wire caller limits must all be greater than zero.");
        }
    }

    private static void RecordNegotiatedSession(
        IServerChannel channel,
        string nodeId,
        byte wireVersion,
        NodeTransportCapabilities capabilities)
    {
        static void Record(
            IDictionary<string, object> properties,
            string authenticatedNodeId,
            byte selectedWireVersion,
            NodeTransportCapabilities selectedCapabilities)
        {
            properties[NodeWireProtocol.NegotiatedNodeIdPropertyName] = authenticatedNodeId;
            properties[NodeWireProtocol.NegotiatedWireVersionPropertyName] = selectedWireVersion;
            properties[NodeWireProtocol.NegotiatedCapabilitiesPropertyName] = selectedCapabilities;
        }

        if (channel is ServerTransportChannel serverChannel)
        {
            Record(serverChannel.Properties, nodeId, wireVersion, capabilities);
        }

        if (channel.AuthenticationContext is { } authentication)
        {
            Record(authentication.Properties, nodeId, wireVersion, capabilities);
        }
    }

    private void RequireLegacyActorProtocol()
    {
        if (!_wireOptions.AllowLegacyActorProtocol)
        {
            throw new NotSupportedException(
                "Legacy node Actor protocol is disabled. Complete node wire negotiation and use AskActorV2/SendActorV2.");
        }
    }

    private NodeTransportCapabilities RequireVersionedActorSession()
    {
        var context = PulseContext.Current
            ?? throw new UnauthorizedAccessException("Versioned node invocation requires an active node connection context.");
        var authentication = context.AuthenticationContext
            ?? throw new UnauthorizedAccessException("Versioned node invocation requires an authenticated node connection.");

        var properties = authentication.Properties;
        if (!properties.TryGetValue(NodeWireProtocol.NegotiatedWireVersionPropertyName, out var versionValue) ||
            versionValue is not byte version ||
            version != NodeWireProtocol.CurrentWireVersion)
        {
            throw new UnauthorizedAccessException("The node connection has not negotiated the current wire version.");
        }

        if (!properties.TryGetValue(NodeWireProtocol.NegotiatedCapabilitiesPropertyName, out var capabilitiesValue) ||
            capabilitiesValue is not NodeTransportCapabilities capabilities)
        {
            throw new UnauthorizedAccessException("The node connection has no negotiated capability set.");
        }

        if (!properties.TryGetValue(NodeWireProtocol.NegotiatedNodeIdPropertyName, out var nodeIdValue) ||
            nodeIdValue is not string nodeId ||
            !string.Equals(nodeId, authentication.Identity, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The negotiated node identity does not match the authenticated connection.");
        }

        const NodeTransportCapabilities required =
            NodeTransportCapabilities.VersionedActorEnvelope |
            NodeTransportCapabilities.LeaseFencing |
            NodeTransportCapabilities.HubProtocolValidation |
            NodeTransportCapabilities.MutualNodeAuthentication;
        if ((capabilities & required) != required)
        {
            throw new NotSupportedException(
                $"The node connection is missing required versioned Actor capabilities: {required & ~capabilities}.");
        }

        return capabilities;
    }

    private NodeActorInvocationEnvelope ParseAndValidateInvocation(
        byte[] envelope,
        NodeTransportCapabilities negotiatedCapabilities)
    {
        NodeActorInvocationEnvelope invocation;
        try
        {
            invocation = NodeWireProtocol.ParseActorInvocation(envelope);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("Invalid versioned Actor invocation envelope.", ex);
        }

        if (invocation.WireVersion != NodeWireProtocol.CurrentWireVersion)
        {
            throw new NotSupportedException(
                $"Unsupported node Actor wire version {invocation.WireVersion}; expected {NodeWireProtocol.CurrentWireVersion}.");
        }

        ValidateRequiredField(invocation.Hub, nameof(invocation.Hub));
        ValidateRequiredField(invocation.Key, nameof(invocation.Key));
        ValidateRequiredField(invocation.LeaseId, nameof(invocation.LeaseId));
        ValidateOptionalField(invocation.SourceNodeId, nameof(invocation.SourceNodeId));
        ValidateOptionalField(invocation.ReplyTo, nameof(invocation.ReplyTo));

        if (invocation.ProtocolId == 0)
        {
            throw new InvalidOperationException("Versioned Actor invocation ProtocolId cannot be zero.");
        }

        if (invocation.Body is null)
        {
            throw new InvalidOperationException("Versioned Actor invocation Body cannot be null.");
        }

        var hasSource = !string.IsNullOrEmpty(invocation.SourceNodeId);
        var hasReply = !string.IsNullOrEmpty(invocation.ReplyTo);
        if (hasSource != hasReply)
        {
            throw new InvalidOperationException("SourceNodeId and ReplyTo must either both be present or both be empty.");
        }

        if (invocation.Caller is not null)
        {
            if ((negotiatedCapabilities & NodeTransportCapabilities.ClaimsPrincipal) == 0)
            {
                throw new NotSupportedException(
                    "The invocation contains a caller snapshot but ClaimsPrincipal was not negotiated for this connection.");
            }

            ValidateCallerSnapshot(invocation.Caller);
        }

        return invocation;
    }

    private void ValidateCallerSnapshot(NodeCallerContextSnapshot caller)
    {
        ValidateOptionalField(caller.UserId, nameof(caller.UserId));
        ValidateRequiredField(caller.CallerId, nameof(caller.CallerId));

        var permissions = caller.Permissions
            ?? throw new InvalidOperationException("Caller Permissions cannot be null.");
        var roles = caller.Roles
            ?? throw new InvalidOperationException("Caller Roles cannot be null.");
        if (permissions.Length > _wireOptions.MaxAuthorizationValueCount ||
            roles.Length > _wireOptions.MaxAuthorizationValueCount)
        {
            throw new InvalidOperationException("Caller permissions or roles exceed the configured count limit.");
        }

        foreach (var permission in permissions)
        {
            ValidateRequiredField(permission, "Permission");
        }

        foreach (var role in roles)
        {
            ValidateRequiredField(role, "Role");
        }

        if (caller.ExpiresAtUtcTicks is { } expiresAtTicks)
        {
            if (expiresAtTicks <= 0 || expiresAtTicks > DateTime.MaxValue.Ticks)
            {
                throw new InvalidOperationException("Caller ExpiresAt is outside the valid DateTime range.");
            }

            if (expiresAtTicks <= DateTime.UtcNow.Ticks)
            {
                throw new UnauthorizedAccessException("The forwarded caller identity has expired.");
            }
        }

        var identities = caller.Identities
            ?? throw new InvalidOperationException("Caller Identities cannot be null.");
        if (identities.Length > _wireOptions.MaxIdentityCount)
        {
            throw new InvalidOperationException("Caller identity count exceeds the configured limit.");
        }

        var claimCount = 0;
        foreach (var identity in identities)
        {
            if (identity is null)
            {
                throw new InvalidOperationException("Caller identity cannot be null.");
            }

            ValidateOptionalField(identity.AuthenticationType, nameof(identity.AuthenticationType));
            ValidateRequiredField(identity.NameClaimType, nameof(identity.NameClaimType));
            ValidateRequiredField(identity.RoleClaimType, nameof(identity.RoleClaimType));

            var claims = identity.Claims
                ?? throw new InvalidOperationException("Identity Claims cannot be null.");
            claimCount += claims.Length;
            if (claimCount > _wireOptions.MaxClaimCount)
            {
                throw new InvalidOperationException("Caller claim count exceeds the configured limit.");
            }

            foreach (var claim in claims)
            {
                if (claim is null)
                {
                    throw new InvalidOperationException("Caller claim cannot be null.");
                }

                ValidateRequiredField(claim.Type, nameof(claim.Type));
                ValidateOptionalField(claim.Value, nameof(claim.Value));
                ValidateRequiredField(claim.ValueType, nameof(claim.ValueType));
                ValidateRequiredField(claim.Issuer, nameof(claim.Issuer));
                ValidateRequiredField(claim.OriginalIssuer, nameof(claim.OriginalIssuer));

                var claimProperties = claim.Properties
                    ?? throw new InvalidOperationException("Claim Properties cannot be null.");
                if (claimProperties.Count > _wireOptions.MaxClaimPropertyCount)
                {
                    throw new InvalidOperationException("Claim property count exceeds the configured limit.");
                }

                foreach (var property in claimProperties)
                {
                    ValidateRequiredField(property.Key, "Claim property key");
                    ValidateOptionalField(property.Value, "Claim property value");
                }
            }
        }
    }

    private void ValidateRequiredField(string? value, string fieldName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"{fieldName} cannot be null or empty.");
        }

        ValidateOptionalField(value, fieldName);
    }

    private void ValidateOptionalField(string? value, string fieldName)
    {
        if (value is not null && value.Length > _wireOptions.MaxFieldLength)
        {
            throw new InvalidOperationException(
                $"{fieldName} exceeds the configured {_wireOptions.MaxFieldLength}-character limit.");
        }
    }

    private static void ValidateHubProtocol(
        IServiceRoutingTable routingTable,
        string hub,
        ushort protocolId)
    {
        if (!routingTable.IsProtocolIdValid(hub, protocolId))
        {
            throw new InvalidOperationException(
                $"ProtocolId 0x{protocolId:X4} does not belong to Hub '{hub}'.");
        }
    }

    private async ValueTask ValidateLeaseAsync(
        NodeActorInvocationEnvelope invocation,
        CancellationToken cancellationToken)
    {
        var actorDirectory = _actorDirectory
            ?? throw new InvalidOperationException("IActorDirectory is required for versioned Actor lease fencing.");
        if (string.IsNullOrEmpty(_localNodeId))
        {
            throw new InvalidOperationException("ClusterTopologyOptions.LocalNodeId is required for versioned Actor lease fencing.");
        }

        var placement = await actorDirectory.ResolveAsync(
            invocation.Hub,
            invocation.Key,
            cancellationToken).ConfigureAwait(false);
        if (placement is null ||
            !placement.Value.IsValidAt(DateTime.UtcNow) ||
            !string.Equals(placement.Value.NodeId, _localNodeId, StringComparison.Ordinal) ||
            !string.Equals(placement.Value.LeaseId, invocation.LeaseId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Actor lease fencing rejected '{invocation.Hub}:{invocation.Key}' for local node '{_localNodeId}'.");
        }

        _leaseHeartbeat?.Track(invocation.Hub, invocation.Key, placement.Value);
    }

    private IDisposable? EnterInvocationScope(NodeActorInvocationEnvelope invocation)
    {
        var hasVirtualConnection =
            !string.IsNullOrEmpty(invocation.SourceNodeId) &&
            !string.IsNullOrEmpty(invocation.ReplyTo);
        string? virtualConnectionId = null;
        if (hasVirtualConnection)
        {
            virtualConnectionId = GatewayVirtualChannel.ComposeId(invocation.SourceNodeId, invocation.ReplyTo);
            _channelManager.GetOrRegisterVirtualChannel(
                virtualConnectionId,
                id => new GatewayVirtualChannel(invocation.SourceNodeId, invocation.ReplyTo, _nodeLink));
        }

        var ambient = PulseContext.Current;
        if (invocation.Caller is null)
        {
            if (!hasVirtualConnection)
            {
                return null;
            }

            var anonymousContext = PulseContextData.CreateAnonymousClientContext(
                virtualConnectionId,
                transport: null,
                ambient?.CancellationToken ?? default) with
            {
                RequestId = invocation.MessageId,
                ServiceName = invocation.Hub,
                ServiceKey = invocation.Key,
                TraceContext = ambient?.TraceContext ?? default,
            };
            return PulseContext.SetContext(anonymousContext);
        }

        var caller = invocation.Caller;
        var identities = caller.Identities.Select(RestoreIdentity).ToArray();
        var principal = identities.Length == 0 ? null : new ClaimsPrincipal(identities);
        AuthenticationContext? authentication = null;
        if (!string.IsNullOrEmpty(caller.UserId))
        {
            var authenticationConnectionId = virtualConnectionId ?? $"forwarded:{caller.CallerId}";
            var name = principal?.Identity?.Name;
            if (string.IsNullOrEmpty(name))
            {
                name = string.IsNullOrEmpty(caller.CallerId) ? caller.UserId : caller.CallerId;
            }

            authentication = new AuthenticationContext(authenticationConnectionId);
            authentication.SetClientAuthentication(
                caller.UserId,
                name!,
                token: null,
                principal: principal);
        }

        var claims = new Dictionary<string, string>(StringComparer.Ordinal);
        if (principal is not null)
        {
            foreach (var claim in principal.Claims)
            {
                if (claim.Type != ClaimTypes.Role)
                {
                    claims[claim.Type] = claim.Value;
                }
            }
        }

        var restoredContext = new PulseContextData
        {
            RequestId = invocation.MessageId,
            ConnectionId = virtualConnectionId,
            ServiceName = invocation.Hub,
            ServiceKey = invocation.Key,
            CancellationToken = ambient?.CancellationToken ?? default,
            StartTimestamp = Stopwatch.GetTimestamp(),
            TraceContext = ambient?.TraceContext ?? default,
            User = principal,
            SourceType = CallSourceType.ExternalUser,
            CallerId = caller.CallerId,
            UserId = caller.UserId,
            Token = null,
            Permissions = new HashSet<string>(caller.Permissions, StringComparer.Ordinal),
            Roles = new HashSet<string>(caller.Roles, StringComparer.Ordinal),
            Claims = claims,
            ExpiresAt = caller.ExpiresAtUtcTicks is { } ticks
                ? new DateTime(ticks, DateTimeKind.Utc)
                : null,
            AuthenticationContext = authentication,
        };

        return PulseContext.SetContext(restoredContext);
    }

    private static ClaimsIdentity RestoreIdentity(NodeClaimsIdentitySnapshot snapshot)
    {
        var claims = snapshot.Claims.Select(claimSnapshot =>
        {
            var claim = new Claim(
                claimSnapshot.Type,
                claimSnapshot.Value,
                claimSnapshot.ValueType,
                claimSnapshot.Issuer,
                claimSnapshot.OriginalIssuer);
            foreach (var property in claimSnapshot.Properties)
            {
                claim.Properties[property.Key] = property.Value;
            }

            return claim;
        });

        return new ClaimsIdentity(
            claims,
            snapshot.AuthenticationType,
            snapshot.NameClaimType,
            snapshot.RoleClaimType);
    }

    /// <summary>
    /// 当 <paramref name="replyTo"/> 非空（本次调用经网关中转）时：为 <c>(sourceNodeId, replyTo)</c>
    /// 惰性注册一个 <see cref="GatewayVirtualChannel"/>（幂等），并建立一个把
    /// <see cref="PulseContext.CurrentConnectionId"/> 覆盖为该虚拟连接标识的嵌套上下文作用域，
    /// 使被调 Actor 方法之后可以经 <c>Clients.Client(PulseContext.CurrentConnectionId)</c> 等寻址方式
    /// 把消息推回真实客户端（§10 多跳回执）。非网关中转时返回 <c>null</c>，不改变环境上下文。
    /// </summary>
    private IDisposable? EnterVirtualConnectionScopeIfNeeded(
        string hub,
        string key,
        string sourceNodeId,
        string replyTo)
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
        // 这是仅供显式兼容窗口使用的 legacy wire；它不携带 caller snapshot，因此远程 Gateway
        // 调用恢复为最小权限的匿名 ExternalUser。wire v2 使用 EnterInvocationScope 恢复完整 caller：
        // ClientFacingGate 仍会生效，依赖 UserId/角色/权限的业务则自然 fail closed。
        var virtualContext = PulseContextData.CreateAnonymousClientContext(
            virtualConnectionId,
            transport: null,
            ambient?.CancellationToken ?? default) with
        {
            RequestId = ambient?.RequestId ?? Guid.Empty,
            ServiceName = hub,
            ServiceKey = key,
            MethodName = ambient?.MethodName ?? string.Empty,
            TraceContext = ambient?.TraceContext ?? default,
        };

        return PulseContext.SetContext(virtualContext);
    }

    private IServiceRoutingTable RequireRoutingTable()
    {
        return _routingTable
            ?? throw new InvalidOperationException(
                "IServiceRoutingTable 未注册，无法完成跨节点 Actor 转发。请确认已引用带 [Channel] Hub 接口的程序集。");
    }
}
