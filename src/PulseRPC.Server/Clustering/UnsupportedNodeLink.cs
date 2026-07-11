using System;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Clustering;

namespace PulseRPC.Server.Clustering;

internal sealed class UnsupportedNodeLink : INodeLink
{
    public ValueTask<ReadOnlyMemory<byte>> AskActorAsync(
        string targetNodeId,
        string hub,
        string key,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        string sourceNodeId = "",
        string replyTo = "",
        CancellationToken cancellationToken = default,
        string leaseId = "")
        => throw CreateException();

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
        => throw CreateException();

    public ValueTask SendToConnectionAsync(
        string targetNodeId,
        string connectionId,
        ReadOnlyMemory<byte> framedPacket,
        CancellationToken cancellationToken = default)
        => throw CreateException();

    public ValueTask<ReadOnlyMemory<byte>> AskConnectionAsync(
        string targetNodeId,
        string connectionId,
        ushort protocolId,
        ReadOnlyMemory<byte> payload,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => throw CreateException();

    private static NotSupportedException CreateException()
        => new("跨节点出站链路未配置。PulseRPC.Server 不再依赖 PulseRPC.Client；请注册一个 INodeLink 实现来提供节点间传输。");
}
