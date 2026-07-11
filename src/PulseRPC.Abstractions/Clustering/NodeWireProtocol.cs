using System;
using System.Collections.Generic;
using MemoryPack;
using PulseRPC.Messaging;

namespace PulseRPC.Clustering;

/// <summary>
/// 节点链路协商出的能力位。
/// </summary>
[Flags]
public enum NodeTransportCapabilities : ulong
{
    /// <summary>未协商任何可选能力。</summary>
    None = 0,

    /// <summary>支持带显式版本号的 Actor 调用信封。</summary>
    VersionedActorEnvelope = 1UL << 0,

    /// <summary>支持完整 <see cref="System.Security.Claims.ClaimsPrincipal"/> 快照。</summary>
    ClaimsPrincipal = 1UL << 1,

    /// <summary>支持 Actor lease id fencing。</summary>
    LeaseFencing = 1UL << 2,

    /// <summary>接收方会强制校验 Hub 与 ProtocolId 的归属关系。</summary>
    HubProtocolValidation = 1UL << 3,

    /// <summary>能力协商响应携带可由发起方校验的远端节点凭据。</summary>
    MutualNodeAuthentication = 1UL << 4,
}

/// <summary>
/// 一个已认证、已完成 wire 能力协商的节点会话。
/// </summary>
public sealed class NodeTransportSession
{
    /// <summary>创建节点会话描述。</summary>
    public NodeTransportSession(
        string nodeId,
        byte wireVersion,
        NodeTransportCapabilities capabilities)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            throw new ArgumentException("Node id cannot be null or whitespace.", nameof(nodeId));
        }

        NodeId = nodeId;
        WireVersion = wireVersion;
        Capabilities = capabilities;
    }

    /// <summary>已认证的远端节点标识。</summary>
    public string NodeId { get; }

    /// <summary>本会话选定的 node wire 版本。</summary>
    public byte WireVersion { get; }

    /// <summary>双方能力交集。</summary>
    public NodeTransportCapabilities Capabilities { get; }

    /// <summary>检查本会话是否同时具备指定能力。</summary>
    public bool Supports(NodeTransportCapabilities capabilities)
        => (Capabilities & capabilities) == capabilities;
}

/// <summary>
/// 在 <see cref="INodeTransport"/> 之上提供连接级认证/版本协商结果的节点传输。
/// </summary>
public interface IVersionedNodeTransport : INodeTransport
{
    /// <summary>
    /// 取得或建立到目标节点的已认证、已协商会话。
    /// </summary>
    ValueTask<NodeTransportSession> GetSessionAsync(
        string targetNodeId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 节点 wire 的固定协议号、版本与序列化辅助方法。
/// </summary>
public static class NodeWireProtocol
{
    /// <summary>节点控制契约的 canonical Hub 名。</summary>
    public const string ClusterInternalHubName = "ClusterInternalHub";

    /// <summary>Gateway 最后一跳契约的 canonical Hub 名。</summary>
    public const string GatewayRelayHubName = "GatewayRelayHub";

    /// <summary>隐式、无能力协商的历史节点协议版本。</summary>
    public const byte LegacyWireVersion = 1;

    /// <summary>当前版本化 Actor 信封的 wire 版本。</summary>
    public const byte CurrentWireVersion = 2;

    /// <summary>当前实现可提供的全部节点能力。</summary>
    public const NodeTransportCapabilities SupportedCapabilities =
        NodeTransportCapabilities.VersionedActorEnvelope |
        NodeTransportCapabilities.ClaimsPrincipal |
        NodeTransportCapabilities.LeaseFencing |
        NodeTransportCapabilities.HubProtocolValidation |
        NodeTransportCapabilities.MutualNodeAuthentication;

    /// <summary>历史 Authenticate 固定协议号。</summary>
    public const ushort AuthenticateProtocolId = 0xD524;

    /// <summary>历史 AskActor 固定协议号。</summary>
    public const ushort AskActorProtocolId = 0xFD7F;

    /// <summary>历史 SendActor 固定协议号。</summary>
    public const ushort SendActorProtocolId = 0x33A0;

    /// <summary>Authenticate 成功后执行能力协商的固定协议号。</summary>
    public const ushort NegotiateProtocolId = 0xD525;

    /// <summary>版本化 AskActor 固定协议号。</summary>
    public const ushort AskActorV2ProtocolId = 0xFD80;

    /// <summary>版本化 SendActor 固定协议号。</summary>
    public const ushort SendActorV2ProtocolId = 0x33A1;

    /// <summary>服务端连接属性：已协商 wire 版本。</summary>
    public const string NegotiatedWireVersionPropertyName = "PulseRPC.Cluster.WireVersion";

    /// <summary>服务端连接属性：已协商能力。</summary>
    public const string NegotiatedCapabilitiesPropertyName = "PulseRPC.Cluster.Capabilities";

    /// <summary>服务端连接属性：已认证节点标识。</summary>
    public const string NegotiatedNodeIdPropertyName = "PulseRPC.Cluster.NodeId";

    /// <summary>构建一个标准 PulseRPC 节点控制/调用帧。</summary>
    public static byte[] BuildFrame(
        MessageType type,
        string hub,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        MessageFlags flags)
    {
        var header = new MessageHeader(type, hub, string.Empty)
        {
            MessageId = Guid.NewGuid(),
            ProtocolId = protocolId,
            Flags = flags,
        };

        return EnvelopeRelay.WriteFrame(ReadOnlyEnvelopeHeader.FromHeader(header), body.Span);
    }

    /// <summary>序列化历史 Authenticate 调用参数。</summary>
    public static byte[] SerializeAuthenticationRequest(string nodeId, byte[] credential)
        => MemoryPackSerializer.Serialize((nodeId ?? string.Empty, credential ?? Array.Empty<byte>()));

    /// <summary>
    /// 把一段内部 wire payload 编码为单个 <c>byte[]</c> Hub 参数的请求体。
    /// </summary>
    public static byte[] SerializeByteArrayArgument(ReadOnlySpan<byte> payload)
        => MemoryPackSerializer.Serialize(payload.ToArray());

    /// <summary>
    /// 从返回类型为 <c>byte[]</c> 的 Hub 响应 payload 中取出内部 wire payload。
    /// </summary>
    public static byte[] ParseByteArrayResult(ReadOnlySpan<byte> payload)
        => MemoryPackSerializer.Deserialize<byte[]>(payload) ?? Array.Empty<byte>();

    /// <summary>创建并序列化协商请求。</summary>
    public static byte[] SerializeNegotiationRequest(
        string nodeId,
        NodeTransportCapabilities capabilities = SupportedCapabilities,
        byte minWireVersion = CurrentWireVersion,
        byte maxWireVersion = CurrentWireVersion)
        => MemoryPackSerializer.Serialize(new NodeNegotiationRequest
        {
            NodeId = nodeId ?? string.Empty,
            MinWireVersion = minWireVersion,
            MaxWireVersion = maxWireVersion,
            Capabilities = capabilities,
        });

    /// <summary>反序列化协商请求。</summary>
    public static NodeNegotiationRequest ParseNegotiationRequest(ReadOnlySpan<byte> payload)
        => MemoryPackSerializer.Deserialize<NodeNegotiationRequest>(payload)
           ?? throw new InvalidOperationException("Node negotiation request payload is empty.");

    /// <summary>序列化协商响应。</summary>
    public static byte[] SerializeNegotiationResponse(NodeNegotiationResponse response)
        => MemoryPackSerializer.Serialize(response ?? throw new ArgumentNullException(nameof(response)));

    /// <summary>反序列化协商响应。</summary>
    public static NodeNegotiationResponse ParseNegotiationResponse(ReadOnlySpan<byte> payload)
        => MemoryPackSerializer.Deserialize<NodeNegotiationResponse>(payload)
           ?? throw new InvalidOperationException("Node negotiation response payload is empty.");

    /// <summary>序列化版本化 Actor 调用信封。</summary>
    public static byte[] SerializeActorInvocation(NodeActorInvocationEnvelope envelope)
        => MemoryPackSerializer.Serialize(envelope ?? throw new ArgumentNullException(nameof(envelope)));

    /// <summary>反序列化版本化 Actor 调用信封。</summary>
    public static NodeActorInvocationEnvelope ParseActorInvocation(ReadOnlySpan<byte> payload)
        => MemoryPackSerializer.Deserialize<NodeActorInvocationEnvelope>(payload)
           ?? throw new InvalidOperationException("Node actor invocation payload is empty.");
}

/// <summary>节点能力协商请求。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class NodeNegotiationRequest
{
    /// <summary>发起协商的节点标识。</summary>
    [MemoryPackOrder(0)]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>支持的最小 wire 版本。</summary>
    [MemoryPackOrder(1)]
    public byte MinWireVersion { get; set; }

    /// <summary>支持的最大 wire 版本。</summary>
    [MemoryPackOrder(2)]
    public byte MaxWireVersion { get; set; }

    /// <summary>本端能力。</summary>
    [MemoryPackOrder(3)]
    public NodeTransportCapabilities Capabilities { get; set; }
}

/// <summary>节点能力协商响应。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class NodeNegotiationResponse
{
    /// <summary>协商是否成功。</summary>
    [MemoryPackOrder(0)]
    public bool Accepted { get; set; }

    /// <summary>选定的 wire 版本。</summary>
    [MemoryPackOrder(1)]
    public byte SelectedWireVersion { get; set; }

    /// <summary>双方能力交集。</summary>
    [MemoryPackOrder(2)]
    public NodeTransportCapabilities Capabilities { get; set; }

    /// <summary>响应节点标识。</summary>
    [MemoryPackOrder(3)]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>协商失败原因。</summary>
    [MemoryPackOrder(4)]
    public string Error { get; set; } = string.Empty;

    /// <summary>响应节点对自身身份生成的凭据，供发起节点完成反向认证。</summary>
    [MemoryPackOrder(5)]
    public byte[] Credential { get; set; } = Array.Empty<byte>();
}

/// <summary>版本化的跨节点 Actor 调用信封。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class NodeActorInvocationEnvelope
{
    /// <summary>信封 wire 版本。</summary>
    [MemoryPackOrder(0)]
    public byte WireVersion { get; set; }

    /// <summary>目标 Hub。</summary>
    [MemoryPackOrder(1)]
    public string Hub { get; set; } = string.Empty;

    /// <summary>目标 Actor key。</summary>
    [MemoryPackOrder(2)]
    public string Key { get; set; } = string.Empty;

    /// <summary>目标方法协议号。</summary>
    [MemoryPackOrder(3)]
    public ushort ProtocolId { get; set; }

    /// <summary>业务请求体。</summary>
    [MemoryPackOrder(4)]
    public byte[] Body { get; set; } = Array.Empty<byte>();

    /// <summary>Gateway 来源节点。</summary>
    [MemoryPackOrder(5)]
    public string SourceNodeId { get; set; } = string.Empty;

    /// <summary>Gateway 上真实客户端连接。</summary>
    [MemoryPackOrder(6)]
    public string ReplyTo { get; set; } = string.Empty;

    /// <summary>幂等/关联消息标识。</summary>
    [MemoryPackOrder(7)]
    public Guid MessageId { get; set; }

    /// <summary>目标 Actor 当前 lease id，用于拒绝陈旧 owner 帧。</summary>
    [MemoryPackOrder(8)]
    public string LeaseId { get; set; } = string.Empty;

    /// <summary>原始外部调用者快照；普通节点内部调用为空。</summary>
    [MemoryPackOrder(9)]
    public NodeCallerContextSnapshot? Caller { get; set; }
}

/// <summary>可安全跨节点传播的外部调用者快照。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class NodeCallerContextSnapshot
{
    /// <summary>业务用户标识。</summary>
    [MemoryPackOrder(0)]
    public string? UserId { get; set; }

    /// <summary>框架调用者标识。</summary>
    [MemoryPackOrder(1)]
    public string CallerId { get; set; } = string.Empty;

    /// <summary>入口认证后得到的权限集合。</summary>
    [MemoryPackOrder(2)]
    public string[] Permissions { get; set; } = Array.Empty<string>();

    /// <summary>入口认证后得到的角色集合。</summary>
    [MemoryPackOrder(3)]
    public string[] Roles { get; set; } = Array.Empty<string>();

    /// <summary>身份过期 UTC ticks；空表示未知。</summary>
    [MemoryPackOrder(4)]
    public long? ExpiresAtUtcTicks { get; set; }

    /// <summary>ClaimsPrincipal 中按顺序保存的 identities。</summary>
    [MemoryPackOrder(5)]
    public NodeClaimsIdentitySnapshot[] Identities { get; set; } = Array.Empty<NodeClaimsIdentitySnapshot>();
}

/// <summary>一个 ClaimsIdentity 的 wire 快照。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class NodeClaimsIdentitySnapshot
{
    /// <summary>认证类型。</summary>
    [MemoryPackOrder(0)]
    public string? AuthenticationType { get; set; }

    /// <summary>名称 claim type。</summary>
    [MemoryPackOrder(1)]
    public string NameClaimType { get; set; } = string.Empty;

    /// <summary>角色 claim type。</summary>
    [MemoryPackOrder(2)]
    public string RoleClaimType { get; set; } = string.Empty;

    /// <summary>Identity claims，保留重复项与顺序。</summary>
    [MemoryPackOrder(3)]
    public NodeClaimSnapshot[] Claims { get; set; } = Array.Empty<NodeClaimSnapshot>();
}

/// <summary>一个 Claim 的完整 wire 快照。</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class NodeClaimSnapshot
{
    /// <summary>Claim type。</summary>
    [MemoryPackOrder(0)]
    public string Type { get; set; } = string.Empty;

    /// <summary>Claim value。</summary>
    [MemoryPackOrder(1)]
    public string Value { get; set; } = string.Empty;

    /// <summary>Claim value type。</summary>
    [MemoryPackOrder(2)]
    public string ValueType { get; set; } = string.Empty;

    /// <summary>Claim issuer。</summary>
    [MemoryPackOrder(3)]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Claim original issuer。</summary>
    [MemoryPackOrder(4)]
    public string OriginalIssuer { get; set; } = string.Empty;

    /// <summary>Claim properties。</summary>
    [MemoryPackOrder(5)]
    public Dictionary<string, string> Properties { get; set; } = new();
}
