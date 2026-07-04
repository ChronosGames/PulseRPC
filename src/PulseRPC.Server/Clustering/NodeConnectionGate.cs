using PulseRPC.Server.Contexts;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// 集群节点互信鉴权门槛 —— 供 <see cref="ClusterInternalHub"/> 与 <c>GatewayRelayHub</c> 共用，
/// 校验当前连接是否已通过 <see cref="ClusterInternalHub.AuthenticateAsync"/> 标记为「节点连接」。
/// </summary>
/// <remarks>
/// 鉴权状态挂在 <see cref="PulseRPC.Authentication.IAuthenticationContext"/> 上，按<strong>连接</strong>生效
/// （而非按 Hub），因此同一物理连接上的其它内部 Hub（如网关的 <c>IGatewayRelayHub</c>）可复用同一次鉴权结果。
/// </remarks>
public static class NodeConnectionGate
{
    /// <summary>标记一个已通过节点互信鉴权的连接的鉴权 Scope。</summary>
    public const string NodeConnectionScope = "cluster-node";

    /// <summary>
    /// 校验当前连接已通过节点互信鉴权，防止外部/客户端伪造成内部节点；未通过则抛出。
    /// </summary>
    /// <exception cref="System.UnauthorizedAccessException">当前连接未通过节点互信鉴权。</exception>
    public static void Require()
    {
        var authContext = PulseContext.Current?.AuthenticationContext;
        if (authContext is null || !authContext.IsAuthenticated || !authContext.HasScope(NodeConnectionScope))
        {
            throw new System.UnauthorizedAccessException(
                "该连接未通过集群节点互信鉴权，禁止调用集群内部转发接口。请先调用 IClusterInternalHub.AuthenticateAsync。");
        }
    }
}
