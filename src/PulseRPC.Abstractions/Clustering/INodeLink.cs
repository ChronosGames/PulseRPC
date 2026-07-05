using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Clustering;

/// <summary>
/// 节点↔节点全双工链路 —— 把一次 Actor 调用转发给拥有该实例的远端节点执行，
/// 或把一次针对「网关虚拟连接」的推送/反向 Ask 转发给持有真实客户端连接的网关节点。
/// </summary>
/// <remarks>
/// <para>
/// 本接口只表达节点间转发语义，不绑定具体传输实现。PulseRPC.Server 不内置客户端运行时；
/// 部署集群时应由独立模块注册 <see cref="INodeLink"/> 实现，建立节点间出站连接并完成互信鉴权。
/// </para>
/// <para>
/// <see cref="SourceNodeId"/>/<see cref="ReplyTo"/> 语义对应《统一 IPulseHub 全链路寻址与集群架构设计》
/// §4.1/§10：当调用经由网关中转（见路线图 P5 Gateway）时，<c>sourceNodeId</c> 为发起网关的节点标识，
/// <c>replyTo</c> 为该网关上真实客户端连接的连接标识；接收方节点可据此为该「虚拟连接」建立本地代理
/// （见 <c>GatewayVirtualChannel</c>），使被调 Actor 之后能像对待本地连接一样对其 <c>Send/Ask</c>。
/// 非网关场景（普通 Actor↔Actor 直连）留空即可，退化为单跳、无回执寻径。
/// </para>
/// <para>
/// 仅负责 Actor 语义与网关虚拟连接的跨节点转发；Fan-out（Group/User/AllClients）跨节点扩散属于
/// <see cref="IPulseBackplane"/> 的职责（见路线图 P6）。
/// </para>
/// </remarks>
public interface INodeLink
{
    /// <summary>
    /// 请求/响应：把一次 Actor 调用转发给 <paramref name="targetNodeId"/> 执行并等待结果。
    /// </summary>
    /// <param name="targetNodeId">目标节点标识（必须在集群静态成员列表中）。</param>
    /// <param name="hub">目标 Hub 名称。</param>
    /// <param name="key">目标 Actor 实例键。</param>
    /// <param name="protocolId">方法协议号。</param>
    /// <param name="body">已序列化的请求体。</param>
    /// <param name="sourceNodeId">发起节点标识（经网关中转时为网关节点 Id）；留空表示本地节点直接发起。</param>
    /// <param name="replyTo">回执地址（经网关中转时为网关上的真实客户端连接 Id）；留空表示无需回执寻径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已序列化的响应体。</returns>
    ValueTask<ReadOnlyMemory<byte>> AskActorAsync(
        string targetNodeId,
        string hub,
        string key,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        string sourceNodeId = "",
        string replyTo = "",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 单向发送：把一次 Actor 调用转发给 <paramref name="targetNodeId"/> 执行，不等待返回值。
    /// </summary>
    /// <param name="messageId">
    /// 全局幂等键（§4.1/§10.3）；<see cref="Guid.Empty"/> 表示未指定。精确一次投递时据此在
    /// 目标节点的 Actor 侧去重（跨越本次节点间转发）。
    /// </param>
    ValueTask SendActorAsync(
        string targetNodeId,
        string hub,
        string key,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        string sourceNodeId = "",
        string replyTo = "",
        CancellationToken cancellationToken = default,
        Guid messageId = default);

    /// <summary>
    /// 单向：把一段已组帧的原始字节推送给 <paramref name="targetNodeId"/>（网关节点）上
    /// 由 <paramref name="connectionId"/> 标识的真实客户端连接（供 <c>GatewayVirtualChannel.SendAsync</c> 使用）。
    /// </summary>
    ValueTask SendToConnectionAsync(
        string targetNodeId,
        string connectionId,
        ReadOnlyMemory<byte> framedPacket,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 请求/响应：向 <paramref name="targetNodeId"/>（网关节点）上由 <paramref name="connectionId"/> 标识的
    /// 真实客户端连接发起反向 Ask（服务端→客户端），并等待其应答（供 <c>GatewayVirtualChannel.InvokeClientAsync</c> 使用）。
    /// </summary>
    ValueTask<ReadOnlyMemory<byte>> AskConnectionAsync(
        string targetNodeId,
        string connectionId,
        ushort protocolId,
        ReadOnlyMemory<byte> payload,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
