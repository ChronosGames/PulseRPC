using System;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Clustering;
using PulseRPC.Messaging;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// 基于 <see cref="INodeTransport"/> 的最小节点链路实现。
/// </summary>
/// <remarks>
/// Phase B 的目标是先打通 Actor Ask/Send 的节点间 request-response 边界；本实现只把语义化
/// <see cref="INodeLink"/> 调用转换为 PulseRPC 原始帧，具体连接复用、鉴权和收包分发由注入的
/// <see cref="INodeTransport"/> 实现承担。
/// </remarks>
public sealed class TransportBackedNodeLink : INodeLink
{
    private readonly INodeTransport _transport;

    /// <summary>创建基于原始帧传输的节点链路。</summary>
    public TransportBackedNodeLink(INodeTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <inheritdoc/>
    public ValueTask<ReadOnlyMemory<byte>> AskActorAsync(
        string targetNodeId,
        string hub,
        string key,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        string sourceNodeId = "",
        string replyTo = "",
        CancellationToken cancellationToken = default)
    {
        var frame = BuildFrame(MessageType.Request, hub, key, protocolId, body, MessageFlags.RequireResponse, sourceNodeId, replyTo);
        return _transport.AskFrameAsync(targetNodeId, frame, cancellationToken);
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
        Guid messageId = default)
    {
        var frame = BuildFrame(MessageType.OneWay, hub, key, protocolId, body, MessageFlags.None, sourceNodeId, replyTo, messageId);
        return _transport.SendFrameAsync(targetNodeId, frame, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask SendToConnectionAsync(
        string targetNodeId,
        string connectionId,
        ReadOnlyMemory<byte> framedPacket,
        CancellationToken cancellationToken = default)
        => _transport.SendFrameAsync(targetNodeId, framedPacket, cancellationToken);

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

        var frame = BuildFrame(MessageType.ReverseRequest, "CLIENT", connectionId, protocolId, payload, MessageFlags.RequireResponse);
        return await _transport.AskFrameAsync(targetNodeId, frame, effectiveToken).ConfigureAwait(false);
    }

    private static byte[] BuildFrame(
        MessageType type,
        string hub,
        string key,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        MessageFlags flags,
        string sourceNodeId = "",
        string replyTo = "",
        Guid messageId = default)
    {
        var header = new MessageHeader(type, hub, string.Empty)
        {
            MessageId = messageId == Guid.Empty ? Guid.NewGuid() : messageId,
            ProtocolId = protocolId,
            ServiceKey = key ?? string.Empty,
            Flags = flags,
            SourceNodeId = sourceNodeId ?? string.Empty,
            ReplyTo = replyTo ?? string.Empty,
        };

        var packet = new MessagePacket(header, body);
        var buffer = new byte[packet.EstimateSize()];
        var written = packet.WriteTo(buffer);
        if (written == buffer.Length)
        {
            return buffer;
        }

        Array.Resize(ref buffer, written);
        return buffer;
    }
}
