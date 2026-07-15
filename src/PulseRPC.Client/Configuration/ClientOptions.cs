namespace PulseRPC.Client.Configuration;

/// <summary>
/// 客户端选项。
/// </summary>
public sealed class ClientOptions
{
    /// <summary>
    /// 客户端名称。
    /// </summary>
    public string Name { get; set; } = "PulseRPC-Client";

    /// <summary>
    /// 连接负载均衡设置。
    /// </summary>
    public ConnectionLoadBalancingOptions LoadBalancing { get; set; } = new();
}
