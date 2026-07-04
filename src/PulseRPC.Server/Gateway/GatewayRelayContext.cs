namespace PulseRPC.Server.Gateway;

/// <summary>
/// 网关中转的环境（Ambient）上下文 —— 让 <c>ClusterPulseRouter</c> 在把一次 <c>IPulseRouter.AskAsync</c>/
/// <c>SendAsync</c>（Actor 地址）转发给远端节点时，能够附带「本次调用是经由哪个网关、代表哪个真实客户端连接
/// 发起的」信息，而无需改动广泛使用的 <see cref="PulseRPC.Routing.IPulseRouter"/> 接口签名。
/// </summary>
/// <remarks>
/// <para>
/// 用法：<c>GatewayFrontHub</c>（网关对外暴露给客户端的中转 Hub）在调用 <c>IPulseRouter.AskAsync/SendAsync</c>
/// 前用 <see cref="SetScope"/> 建立一个作用域（基于 <see cref="AsyncLocal{T}"/>，随异步调用链自动传播），
/// <c>ClusterPulseRouter</c> 转发到 <c>INodeLink.AskActorAsync/SendActorAsync</c> 时读取
/// <see cref="Current"/> 作为 <c>sourceNodeId</c>/<c>replyTo</c> 参数；未建立作用域（非网关中转的普通
/// Actor↔Actor 调用）时 <see cref="Current"/> 为 <c>null</c>，退化为「无回执寻径」的单跳语义。
/// </para>
/// </remarks>
public static class GatewayRelayContext
{
    private static readonly AsyncLocal<GatewayRelayInfo?> AmbientInfo = new();

    /// <summary>当前网关中转信息；未处于网关中转调用链中时为 <c>null</c>。</summary>
    public static GatewayRelayInfo? Current => AmbientInfo.Value;

    /// <summary>
    /// 建立一个网关中转作用域，返回的 <see cref="IDisposable"/> 释放时恢复此前的作用域。
    /// </summary>
    /// <param name="gatewayNodeId">发起中转的网关节点标识。</param>
    /// <param name="clientConnectionId">该网关上真实客户端连接的连接 Id。</param>
    public static IDisposable SetScope(string gatewayNodeId, string clientConnectionId)
    {
        var previous = AmbientInfo.Value;
        AmbientInfo.Value = new GatewayRelayInfo(gatewayNodeId ?? string.Empty, clientConnectionId ?? string.Empty);
        return new Scope(previous);
    }

    private sealed class Scope(GatewayRelayInfo? previous) : IDisposable
    {
        public void Dispose() => AmbientInfo.Value = previous;
    }
}

/// <summary>网关中转信息：发起网关节点标识 + 该网关上真实客户端连接的连接 Id。</summary>
public readonly record struct GatewayRelayInfo(string GatewayNodeId, string ClientConnectionId);
