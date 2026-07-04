using System.Threading.Tasks;
using PulseRPC;

namespace PulseRPC.Server.Gateway;

/// <summary>
/// Gateway 对外暴露给<strong>外部客户端</strong>的中转契约（§6：桥接远程客户端进入 Actor 网格）。
/// </summary>
/// <remarks>
/// <para>
/// 客户端（叶子应用，经普通 <c>PulseRPC.Client.SourceGenerator</c> 生成调用桩，与 <c>IChatRoomHub</c> 等
/// 业务 Hub 完全相同的使用方式）经此契约把「对某个 Actor(hub,key) 的调用」转发给 Gateway；Gateway 侧的
/// <see cref="GatewayFrontHub"/> 实现注入 <see cref="PulseRPC.Routing.IPulseRouter"/>（在 Gateway 节点上
/// 通常是集群感知的 <c>ClusterPulseRouter</c>），按 <see cref="PulseRPC.Routing.PulseAddress.Actor"/>
/// 寻址：属主在本地则本地执行，属主在远端则经 <c>INodeLink</c> 转发给拥有该实例的后端节点
/// （见 <see cref="PulseRPC.Server.Clustering.ClusterInternalHub.AskActorAsync"/>）。
/// </para>
/// <para>
/// 与 <see cref="PulseRPC.Server.Clustering.IClusterInternalHub"/>（节点↔节点内部契约，手写调用桩）不同，
/// 本契约面向<strong>外部客户端</strong>，走标准的服务端骨架 + 客户端生成器管线（无需手写调用桩），
/// 因为消费方是叶子客户端应用而非被广泛引用的 <c>PulseRPC.Server</c> 库本身。
/// </para>
/// </remarks>
public interface IGatewayFrontHub : IPulseHub
{
    /// <summary>
    /// 请求/响应：把一次对 <c>Actor(hub,key)</c> 的调用经网关中转到拥有该实例的节点并等待结果。
    /// </summary>
    /// <param name="hub">目标 Hub 名称。</param>
    /// <param name="key">目标 Actor 实例键。</param>
    /// <param name="protocolId">方法协议号（与目标 Actor 方法在其自身接口上的协议号一致）。</param>
    /// <param name="body">已序列化的请求体。</param>
    /// <param name="hopLimit">
    /// 剩余转发跳数上限（防止网关拓扑配置错误导致的转发环路），默认 4；达到 0 时拒绝转发。
    /// </param>
    /// <returns>已序列化的响应体。</returns>
    Task<byte[]> RelayAskAsync(string hub, string key, ushort protocolId, byte[] body, byte hopLimit);

    /// <summary>
    /// 单向：把一次对 <c>Actor(hub,key)</c> 的调用经网关中转到拥有该实例的节点，不等待返回值。
    /// </summary>
    Task RelaySendAsync(string hub, string key, ushort protocolId, byte[] body, byte hopLimit);
}
