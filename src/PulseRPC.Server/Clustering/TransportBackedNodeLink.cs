using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC.Clustering;
using PulseRPC.Gateway;
using PulseRPC.Messaging;
using PulseRPC.Server.Contexts;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// 基于 <see cref="INodeTransport"/> 的最小节点链路实现。
/// </summary>
/// <remarks>
/// 本实现把语义化 <see cref="INodeLink"/> 调用包装为集群内部 Hub / Gateway Relay Hub 的固定协议帧，
/// 确保远端调用经过节点鉴权门禁和 Gateway 最后一跳。具体连接复用、节点鉴权握手和收包分发由注入的
/// <see cref="INodeTransport"/> 实现承担。
/// </remarks>
public sealed class TransportBackedNodeLink : INodeLink
{
    private const NodeTransportCapabilities VersionedActorRequirements =
        NodeTransportCapabilities.VersionedActorEnvelope |
        NodeTransportCapabilities.LeaseFencing |
        NodeTransportCapabilities.HubProtocolValidation;

    private readonly INodeTransport _transport;

    /// <summary>创建基于原始帧传输的节点链路。</summary>
    public TransportBackedNodeLink(INodeTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <inheritdoc/>
    public async ValueTask<ReadOnlyMemory<byte>> AskActorAsync(
        string targetNodeId,
        string hub,
        string key,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        string sourceNodeId = "",
        string replyTo = "",
        CancellationToken cancellationToken = default,
        string leaseId = "")
    {
        var session = await GetSessionAsync(targetNodeId, cancellationToken).ConfigureAwait(false);
        EnsureCallerContextCanBeForwarded(targetNodeId, session);
        if (CanUseVersionedActorProtocol(session))
        {
            var envelope = CreateActorEnvelope(
                hub, key, protocolId, body, sourceNodeId, replyTo,
                messageId: Guid.NewGuid(), leaseId, session!);
            var wirePayload = NodeWireProtocol.SerializeActorInvocation(envelope);
            var versionedFrame = NodeWireProtocol.BuildFrame(
                MessageType.Request,
                NodeWireProtocol.ClusterInternalHubName,
                NodeWireProtocol.AskActorV2ProtocolId,
                NodeWireProtocol.SerializeByteArrayArgument(wirePayload),
                MessageFlags.RequireResponse);
            var versionedResponse = await _transport.AskFrameAsync(targetNodeId, versionedFrame, cancellationToken).ConfigureAwait(false);
            return UnwrapByteArrayResponse(versionedResponse);
        }

        var request = (hub, key, protocolId, body.ToArray(), sourceNodeId, replyTo);
        var frame = NodeWireProtocol.BuildFrame(
            MessageType.Request,
            NodeWireProtocol.ClusterInternalHubName,
            NodeWireProtocol.AskActorProtocolId,
            MemoryPackSerializer.Serialize(request),
            MessageFlags.RequireResponse);
        var response = await _transport.AskFrameAsync(targetNodeId, frame, cancellationToken).ConfigureAwait(false);
        return UnwrapByteArrayResponse(response);
    }

    /// <inheritdoc/>
    public ValueTask SendActorAsync(
        string targetNodeId,
        string hub,
        string key,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        string sourceNodeId = "",
        string replyTo = "",
        CancellationToken cancellationToken = default,
        Guid messageId = default,
        string leaseId = "")
        => SendActorCoreAsync(
            targetNodeId, hub, key, protocolId, body,
            sourceNodeId, replyTo, cancellationToken, messageId, leaseId);

    private async ValueTask SendActorCoreAsync(
        string targetNodeId,
        string hub,
        string key,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        string sourceNodeId,
        string replyTo,
        CancellationToken cancellationToken,
        Guid messageId,
        string leaseId)
    {
        var session = await GetSessionAsync(targetNodeId, cancellationToken).ConfigureAwait(false);
        EnsureCallerContextCanBeForwarded(targetNodeId, session);
        if (CanUseVersionedActorProtocol(session))
        {
            var envelope = CreateActorEnvelope(
                hub, key, protocolId, body, sourceNodeId, replyTo,
                messageId, leaseId, session!);
            var wirePayload = NodeWireProtocol.SerializeActorInvocation(envelope);
            var versionedFrame = NodeWireProtocol.BuildFrame(
                MessageType.Request,
                NodeWireProtocol.ClusterInternalHubName,
                NodeWireProtocol.SendActorV2ProtocolId,
                NodeWireProtocol.SerializeByteArrayArgument(wirePayload),
                MessageFlags.RequireResponse);

            // V2 Send 必须等待执行端 ACK。仅等待 socket write 不能支撑 AtLeastOnce/ExactlyOnce：
            // 对端可能在内核收包后、业务执行前崩溃。稳定 MessageId 让调用方安全重试，接收端去重。
            _ = await _transport.AskFrameAsync(targetNodeId, versionedFrame, cancellationToken).ConfigureAwait(false);
            return;
        }

        var command = (hub, key, protocolId, body.ToArray(), sourceNodeId, replyTo, messageId);
        var frame = NodeWireProtocol.BuildFrame(
            MessageType.OneWay,
            NodeWireProtocol.ClusterInternalHubName,
            NodeWireProtocol.SendActorProtocolId,
            MemoryPackSerializer.Serialize(command),
            MessageFlags.None);
        await _transport.SendFrameAsync(targetNodeId, frame, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask SendToConnectionAsync(
        string targetNodeId,
        string connectionId,
        ReadOnlyMemory<byte> framedPacket,
        CancellationToken cancellationToken = default)
    {
        var command = (connectionId, framedPacket.ToArray());
        var frame = NodeWireProtocol.BuildFrame(
            MessageType.OneWay,
            NodeWireProtocol.GatewayRelayHubName,
            GatewayProtocolIds.RelayPushFrame,
            MemoryPackSerializer.Serialize(command),
            MessageFlags.None);
        return _transport.SendFrameAsync(targetNodeId, frame, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<ReadOnlyMemory<byte>> AskConnectionAsync(
        string targetNodeId,
        string connectionId,
        ushort protocolId,
        ReadOnlyMemory<byte> payload,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = timeout > TimeSpan.Zero ? new CancellationTokenSource(timeout) : null;
        using var linkedCts = timeoutCts is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var effectiveToken = linkedCts?.Token ?? cancellationToken;

        var timeoutMs = timeout > TimeSpan.Zero
            ? (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue)
            : 0;
        var request = (connectionId, protocolId, payload.ToArray(), timeoutMs);
        var frame = NodeWireProtocol.BuildFrame(
            MessageType.Request,
            NodeWireProtocol.GatewayRelayHubName,
            GatewayProtocolIds.RelayAskConnection,
            MemoryPackSerializer.Serialize(request),
            MessageFlags.RequireResponse);
        var response = await _transport.AskFrameAsync(targetNodeId, frame, effectiveToken).ConfigureAwait(false);
        return UnwrapByteArrayResponse(response);
    }

    private async ValueTask<NodeTransportSession?> GetSessionAsync(
        string targetNodeId,
        CancellationToken cancellationToken)
    {
        if (_transport is not IVersionedNodeTransport versionedTransport)
        {
            return null;
        }

        var session = await versionedTransport.GetSessionAsync(targetNodeId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(session.NodeId, targetNodeId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                $"节点传输为目标 '{targetNodeId}' 返回了不匹配的已认证节点会话 '{session.NodeId}'。");
        }

        return session;
    }

    private static bool CanUseVersionedActorProtocol(NodeTransportSession? session)
        => session is not null
           && session.WireVersion == NodeWireProtocol.CurrentWireVersion
           && session.Supports(VersionedActorRequirements);

    private static void EnsureCallerContextCanBeForwarded(
        string targetNodeId,
        NodeTransportSession? session)
    {
        if (PulseContext.Current?.SourceType != CallSourceType.ExternalUser)
        {
            return;
        }

        if (!CanUseVersionedActorProtocol(session) ||
            !session!.Supports(NodeTransportCapabilities.ClaimsPrincipal))
        {
            throw new InvalidOperationException(
                $"目标节点 '{targetNodeId}' 未协商 ClaimsPrincipal 版本化转发能力；拒绝把外部用户调用静默降级为匿名 legacy 调用。");
        }
    }

    private static NodeActorInvocationEnvelope CreateActorEnvelope(
        string hub,
        string key,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        string sourceNodeId,
        string replyTo,
        Guid messageId,
        string leaseId,
        NodeTransportSession session)
    {
        var context = PulseContext.Current;
        return new NodeActorInvocationEnvelope
        {
            WireVersion = NodeWireProtocol.CurrentWireVersion,
            Hub = hub ?? string.Empty,
            Key = key ?? string.Empty,
            ProtocolId = protocolId,
            Body = body.ToArray(),
            SourceNodeId = sourceNodeId ?? string.Empty,
            ReplyTo = replyTo ?? string.Empty,
            MessageId = messageId,
            LeaseId = leaseId ?? string.Empty,
            Caller = context?.SourceType == CallSourceType.ExternalUser &&
                     session.Supports(NodeTransportCapabilities.ClaimsPrincipal)
                ? CaptureCaller(context)
                : null,
        };
    }

    private static NodeCallerContextSnapshot CaptureCaller(IPulseContext context)
    {
        var identities = context.User?.Identities
            .Select(identity => new NodeClaimsIdentitySnapshot
            {
                AuthenticationType = identity.AuthenticationType,
                NameClaimType = identity.NameClaimType,
                RoleClaimType = identity.RoleClaimType,
                Claims = identity.Claims.Select(claim =>
                {
                    var properties = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var property in claim.Properties)
                    {
                        properties[property.Key] = property.Value;
                    }

                    return new NodeClaimSnapshot
                    {
                        Type = claim.Type,
                        Value = claim.Value,
                        ValueType = claim.ValueType,
                        Issuer = claim.Issuer,
                        OriginalIssuer = claim.OriginalIssuer,
                        Properties = properties,
                    };
                }).ToArray(),
            })
            .ToArray() ?? Array.Empty<NodeClaimsIdentitySnapshot>();

        return new NodeCallerContextSnapshot
        {
            UserId = context.UserId,
            CallerId = context.CallerId,
            Permissions = context.Permissions.ToArray(),
            Roles = context.Roles.ToArray(),
            ExpiresAtUtcTicks = context.ExpiresAt?.ToUniversalTime().Ticks,
            Identities = identities,
        };
    }

    private static ReadOnlyMemory<byte> UnwrapByteArrayResponse(ReadOnlyMemory<byte> response)
    {
        if (response.IsEmpty)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        return MemoryPackSerializer.Deserialize<byte[]>(response.Span) ?? Array.Empty<byte>();
    }
}
