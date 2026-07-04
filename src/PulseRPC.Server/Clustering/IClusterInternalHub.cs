using System.Threading.Tasks;
using PulseRPC;
using PulseRPC.Protocol;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// 节点↔节点内部 RPC 契约 —— 由每个 PulseRPC 服务端节点提供，供其它节点经 <see cref="PulseNodeLink"/> 调用。
/// </summary>
/// <remarks>
/// <para>
/// 服务端一侧复用现有服务端源生成管线生成骨架（<see cref="ClusterInternalHub"/> 为业务实现）；
/// 客户端一侧（<see cref="PulseNodeLink"/> 内部持有的 <see cref="PulseRPC.Client.IPulseClient"/>）
/// 由 <see cref="ClusterInternalHubClientStub"/> 手写调用桩，不经过
/// <c>PulseRPC.Client.SourceGenerator</c>（该生成器按编译单元生成全局聚合扩展方法，
/// 若在被广泛引用的 <c>PulseRPC.Server</c> 库中启用，会与下游同样使用该生成器的应用产生同签名扩展方法二义性）。
/// 因此本接口方法全部以 <see cref="ProtocolAttribute"/> 显式固定协议号，使手写客户端桩与服务端生成骨架天然一致。
/// </para>
/// <para>
/// 不面向业务代码；业务侧应通过 <see cref="PulseRPC.Routing.IPulseRouter"/>（Actor 地址）间接触发跨节点调用。
/// </para>
/// </remarks>
public interface IClusterInternalHub : IPulseHub
{
    /// <summary>
    /// 节点互信鉴权：校验来自 <paramref name="nodeId"/> 的凭据，通过后把当前连接标记为「节点连接」。
    /// </summary>
    [Protocol(0xD524)]
    Task<bool> AuthenticateAsync(string nodeId, byte[] credential);

    /// <summary>
    /// 请求/响应：在本节点本地路由表中执行 <paramref name="protocolId"/> 对应的 Actor 方法并返回结果。
    /// </summary>
    /// <param name="hub">目标 Hub 名称。</param>
    /// <param name="key">目标 Actor 实例键。</param>
    /// <param name="protocolId">方法协议号。</param>
    /// <param name="body">已序列化的请求体。</param>
    /// <param name="sourceNodeId">
    /// 发起节点标识；经网关中转时为网关节点 Id，用于与 <paramref name="replyTo"/> 组合出「网关虚拟连接」标识。
    /// </param>
    /// <param name="replyTo">
    /// 回执地址；经网关中转时为网关上真实客户端连接的连接 Id。非空时，本节点会为
    /// <c>(sourceNodeId, replyTo)</c> 惰性注册一个 <c>GatewayVirtualChannel</c>，并把它作为
    /// <see cref="PulseRPC.Server.Contexts.PulseContext.CurrentConnectionId"/> 注入被调 Actor 方法的执行上下文，
    /// 使其之后可以像对待本地连接一样对该虚拟连接 <c>Send/Ask</c>（见 §10 多跳回执）。
    /// </param>
    [Protocol(0xFD7F)]
    Task<byte[]> AskActorAsync(string hub, string key, ushort protocolId, byte[] body, string sourceNodeId = "", string replyTo = "");

    /// <summary>
    /// 单向：在本节点本地路由表中执行 <paramref name="protocolId"/> 对应的 Actor 方法，丢弃返回值。
    /// </summary>
    /// <param name="messageId">
    /// 全局幂等键（§4.1/§10.3）；<see cref="Guid.Empty"/> 表示未指定。精确一次投递时，接收方按
    /// <c>(hub, key, messageId)</c> 去重，重复消息直接跳过执行（"效果幂等"）。
    /// </param>
    [Protocol(0x33A0)]
    Task SendActorAsync(string hub, string key, ushort protocolId, byte[] body, string sourceNodeId = "", string replyTo = "", System.Guid messageId = default);
}
