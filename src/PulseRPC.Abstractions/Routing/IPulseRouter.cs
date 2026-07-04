using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Routing;

/// <summary>
/// 统一出站路由 —— 所有调用方代理（ClientStub / Fan-out 代理 / Actor 代理）的公共出口。
/// </summary>
/// <remarks>
/// <para>
/// 生成的调用方代理只负责"序列化参数 + 构造 <see cref="PulseAddress"/> + 反序列化结果"，
/// 把"目标解析（本地 Actor 邮箱 / 本地连接 / Fan-out / 远程节点转发）"交给本接口的实现，
/// 使同一份生成代理在本地/远程下行为一致、对业务透明。
/// </para>
/// <para>
/// 单节点默认实现直投本地；集群实现负责经目录 / Backplane / 节点链路完成跨节点投递（见路线图 P4+）。
/// </para>
/// </remarks>
public interface IPulseRouter
{
    /// <summary>
    /// 单向发送（不等待应答）。
    /// </summary>
    /// <param name="address">目标地址。</param>
    /// <param name="protocolId">方法协议号。</param>
    /// <param name="body">已序列化的消息体。</param>
    /// <param name="delivery">
    /// 投递保证级别，默认 <see cref="DeliveryMode.AtMostOnce"/>：<see cref="DeliveryMode.AtLeastOnce"/>/
    /// <see cref="DeliveryMode.ExactlyOnce"/> 时失败自动重试（有界次数 + 退避）；
    /// <see cref="DeliveryMode.ExactlyOnce"/> 且地址为 <see cref="AddressKind.Actor"/> 时额外基于
    /// <paramref name="messageId"/> 在目标 Actor 侧去重（§10.3）。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="messageId">
    /// 全局幂等键，跨跳不变（§4.1）。默认 <see cref="Guid.Empty"/> 表示"未显式指定"——
    /// 实现应在需要时（<see cref="DeliveryMode.ExactlyOnce"/>）内部生成一个新的；
    /// 调用方也可显式传入同一个值以在自身发起的重试之间去重。
    /// </param>
    ValueTask SendAsync(
        in PulseAddress address,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        DeliveryMode delivery = DeliveryMode.AtMostOnce,
        CancellationToken cancellationToken = default,
        Guid messageId = default);

    /// <summary>
    /// 请求/响应（等待应答）。适用于单连接 RPC、Actor 请求响应与反向 Ask；跨节点由实现负责寻径与回执关联。
    /// </summary>
    /// <param name="address">目标地址（必须解析为单一目标）。</param>
    /// <param name="protocolId">方法协议号。</param>
    /// <param name="body">已序列化的请求体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已序列化的响应体。</returns>
    ValueTask<ReadOnlyMemory<byte>> AskAsync(
        in PulseAddress address,
        ushort protocolId,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default);
}
