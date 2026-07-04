using System;

namespace PulseRPC.Messaging;

/// <summary>
/// 只读信封头视图 —— 仅暴露「地址中转」所需的头部字段，不含消息体（body）。
/// </summary>
/// <remarks>
/// <para>
/// 该结构是 <see cref="MessageHeader"/> 的一个 <strong>只读投影</strong>，专为网关（Gateway）「纯中转」场景设计：
/// 网关只需读取寻址三元组 <c>Hub / Key / MethodId</c>（外加消息语义所需的 <see cref="Type"/> / <see cref="MessageId"/> /
/// <see cref="Flags"/>）即可决定目标节点/实例并转发原始帧，<strong>无需反序列化消息体</strong>。
/// </para>
/// <para>
/// 与 <see cref="MessageHeader"/> 相比，本视图刻意省略了 <see cref="MessageHeader.MethodName"/>、
/// <see cref="MessageHeader.Timestamp"/>、<see cref="MessageHeader.SequenceNumber"/> 等非寻址字段：
/// 框架的路由完全基于 <see cref="MethodId"/>（协议号），字符串方法名路由已废弃。
/// </para>
/// </remarks>
/// <seealso cref="EnvelopeRelay"/>
public readonly struct ReadOnlyEnvelopeHeader : IEquatable<ReadOnlyEnvelopeHeader>
{
    /// <summary>消息类型。</summary>
    public MessageType Type { get; }

    /// <summary>消息唯一标识符（用于请求/响应关联）。</summary>
    public Guid MessageId { get; }

    /// <summary>Hub 名称（对应 <see cref="MessageHeader.ServiceName"/>）。</summary>
    public string Hub { get; }

    /// <summary>
    /// 目标实例键（对应 <see cref="MessageHeader.ServiceKey"/>）。
    /// 空字符串表示无实例键（非 keyed 服务，或由目标节点自行解析实例）。
    /// </summary>
    public string Key { get; }

    /// <summary>方法号 / 协议号（对应 <see cref="MessageHeader.ProtocolId"/>），路由的唯一依据。</summary>
    public ushort MethodId { get; }

    /// <summary>消息标志位。</summary>
    public MessageFlags Flags { get; }

    /// <summary>
    /// 发起节点标识（对应 <see cref="MessageHeader.SourceNodeId"/>），供网关/多跳回执寻径。
    /// 空字符串表示"未跨节点"。
    /// </summary>
    public string SourceNodeId { get; }

    /// <summary>
    /// 显式回执地址（对应 <see cref="MessageHeader.ReplyTo"/>），覆盖"沿原连接返回"默认行为。
    /// 空字符串表示使用默认回执路径。
    /// </summary>
    public string ReplyTo { get; }

    /// <summary>
    /// 剩余转发跳数上限（对应 <see cref="MessageHeader.HopLimit"/>），防止多跳转发环路。
    /// </summary>
    public byte HopLimit { get; }

    /// <summary>
    /// 构造一个只读信封头视图。
    /// </summary>
    public ReadOnlyEnvelopeHeader(
        MessageType type,
        Guid messageId,
        string hub,
        string key,
        ushort methodId,
        MessageFlags flags,
        string sourceNodeId = "",
        string replyTo = "",
        byte hopLimit = 0)
    {
        Type = type;
        MessageId = messageId;
        Hub = hub ?? string.Empty;
        Key = key ?? string.Empty;
        MethodId = methodId;
        Flags = flags;
        SourceNodeId = sourceNodeId ?? string.Empty;
        ReplyTo = replyTo ?? string.Empty;
        HopLimit = hopLimit;
    }

    /// <summary>
    /// 从完整的 <see cref="MessageHeader"/> 创建只读视图。
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="header"/> 为 <c>null</c>。</exception>
    public static ReadOnlyEnvelopeHeader FromHeader(MessageHeader header)
    {
        if (header is null)
        {
            throw new ArgumentNullException(nameof(header));
        }

        return new ReadOnlyEnvelopeHeader(
            header.Type,
            header.MessageId,
            header.ServiceName,
            header.ServiceKey,
            header.ProtocolId,
            header.Flags,
            header.SourceNodeId,
            header.ReplyTo,
            header.HopLimit);
    }

    /// <summary>
    /// 依据本视图重建一个 <see cref="MessageHeader"/>（用于重新组帧）。
    /// </summary>
    /// <remarks>
    /// 由于本视图不携带 <see cref="MessageHeader.MethodName"/>、<see cref="MessageHeader.Timestamp"/>、
    /// <see cref="MessageHeader.SequenceNumber"/>，重建后的头部这些字段为默认值（方法名为空、时间戳/序号为 0）。
    /// 这与「基于协议号（<see cref="MethodId"/>）寻址」的纯中转模型一致。
    /// </remarks>
    public MessageHeader ToHeader()
    {
        return new MessageHeader
        {
            Type = Type,
            MessageId = MessageId,
            ServiceName = Hub,
            ServiceKey = Key,
            ProtocolId = MethodId,
            Flags = Flags,
            SourceNodeId = SourceNodeId,
            ReplyTo = ReplyTo,
            HopLimit = HopLimit,
        };
    }

    /// <summary>
    /// 返回一个替换了实例键（<see cref="Key"/>）的新视图，其余字段不变（供网关改写目标实例）。
    /// </summary>
    public ReadOnlyEnvelopeHeader WithKey(string key)
        => new(Type, MessageId, Hub, key, MethodId, Flags, SourceNodeId, ReplyTo, HopLimit);

    /// <summary>
    /// 返回一个替换了 Hub（<see cref="Hub"/>）的新视图，其余字段不变（供网关改写目标 Hub）。
    /// </summary>
    public ReadOnlyEnvelopeHeader WithHub(string hub)
        => new(Type, MessageId, hub, Key, MethodId, Flags, SourceNodeId, ReplyTo, HopLimit);

    /// <summary>
    /// 返回一个替换了 <see cref="SourceNodeId"/> 的新视图，其余字段不变（供网关标记转发来源节点）。
    /// </summary>
    public ReadOnlyEnvelopeHeader WithSourceNodeId(string sourceNodeId)
        => new(Type, MessageId, Hub, Key, MethodId, Flags, sourceNodeId, ReplyTo, HopLimit);

    /// <summary>
    /// 返回一个替换了 <see cref="ReplyTo"/> 的新视图，其余字段不变（供网关标记回执地址）。
    /// </summary>
    public ReadOnlyEnvelopeHeader WithReplyTo(string replyTo)
        => new(Type, MessageId, Hub, Key, MethodId, Flags, SourceNodeId, replyTo, HopLimit);

    /// <summary>
    /// 返回一个替换了 <see cref="HopLimit"/> 的新视图，其余字段不变（供网关递减防环跳数）。
    /// </summary>
    public ReadOnlyEnvelopeHeader WithHopLimit(byte hopLimit)
        => new(Type, MessageId, Hub, Key, MethodId, Flags, SourceNodeId, ReplyTo, hopLimit);

    /// <inheritdoc/>
    public bool Equals(ReadOnlyEnvelopeHeader other)
        => Type == other.Type
           && MessageId == other.MessageId
           && string.Equals(Hub, other.Hub, StringComparison.Ordinal)
           && string.Equals(Key, other.Key, StringComparison.Ordinal)
           && MethodId == other.MethodId
           && Flags == other.Flags
           && string.Equals(SourceNodeId, other.SourceNodeId, StringComparison.Ordinal)
           && string.Equals(ReplyTo, other.ReplyTo, StringComparison.Ordinal)
           && HopLimit == other.HopLimit;

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is ReadOnlyEnvelopeHeader other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Type);
        hash.Add(MessageId);
        hash.Add(Hub, StringComparer.Ordinal);
        hash.Add(Key, StringComparer.Ordinal);
        hash.Add(MethodId);
        hash.Add(Flags);
        hash.Add(SourceNodeId, StringComparer.Ordinal);
        hash.Add(ReplyTo, StringComparer.Ordinal);
        hash.Add(HopLimit);
        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"Envelope(Hub='{Hub}', Key='{Key}', MethodId=0x{MethodId:X4}, Type={Type}, MessageId={MessageId})";
}
