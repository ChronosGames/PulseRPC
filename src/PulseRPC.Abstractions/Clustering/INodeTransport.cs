using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Clustering;

/// <summary>
/// 节点间原始帧传输抽象。
/// </summary>
/// <remarks>
/// <para>
/// 本接口位于 <see cref="INodeLink"/> 之下：<see cref="INodeLink"/> 表达 Actor / Gateway 虚拟连接等
/// 语义化跨节点调用，<see cref="INodeTransport"/> 只负责把已经组好的协议帧发送到目标节点，或发起一次
/// 请求/响应式帧交换。实现可以基于 TCP、KCP、gRPC stream、QUIC 或消息队列，不应依赖业务 DTO 类型。
/// </para>
/// <para>
/// 帧通常由 <c>MessagePacket</c> / <c>EnvelopeRelay</c> 产生，格式为
/// <c>[4 字节 headerLen][MemoryPack(MessageHeader)][body]</c>；中间节点可只读头部并保持 body opaque。
/// </para>
/// </remarks>
public interface INodeTransport
{
    /// <summary>
    /// 单向发送一段已组帧的原始协议帧到目标节点。
    /// </summary>
    /// <param name="targetNodeId">目标节点标识。</param>
    /// <param name="framedPacket">已组帧的完整协议帧。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask SendFrameAsync(
        string targetNodeId,
        ReadOnlyMemory<byte> framedPacket,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 请求/响应式发送一段已组帧的原始协议帧到目标节点，并返回响应体或响应帧。
    /// </summary>
    /// <param name="targetNodeId">目标节点标识。</param>
    /// <param name="framedPacket">已组帧的完整协议帧。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>响应帧的 payload（不包含 PulseRPC 消息头和长度前缀）。</returns>
    ValueTask<ReadOnlyMemory<byte>> AskFrameAsync(
        string targetNodeId,
        ReadOnlyMemory<byte> framedPacket,
        CancellationToken cancellationToken = default);
}
