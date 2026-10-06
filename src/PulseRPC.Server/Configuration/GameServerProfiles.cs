namespace PulseRPC.Server.Configuration;

/// <summary>游戏服务端的显式起始配置；容量仍须根据目标机器和业务负载验证。</summary>
public static class GameServerProfiles
{
    /// <summary>
    /// 玩家入口：开启 ClientFacing 门闸、单连接串行、小排队预算及 30 秒期限。
    /// 仍须注册业务认证、资源授权和 TLS 入口。
    /// </summary>
    public static PulseServerOptions UseGameGatewayProfile(this PulseServerOptions options)
    {
        Configure(options, 8, 1, 32, 4 * 1024 * 1024);
        return options;
    }

    /// <summary>
    /// 内部节点：允许同一节点连接多路并发，开启 ClientFacing 门闸及有界资源预算。
    /// 仍须配置真实 mTLS、节点证书和共享租约后端。
    /// </summary>
    public static PulseServerOptions UseGameNodeProfile(this PulseServerOptions options)
    {
        Configure(options, 32, 16, 128, 16 * 1024 * 1024);
        return options;
    }

    private static void Configure(PulseServerOptions options, int shardConcurrency,
        int connectionConcurrency, int queuedPerConnection, long bytesPerConnection)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnableClientFacingGate = true;
        options.MaxConcurrentMessagesPerShard = shardConcurrency;
        options.MaxConcurrentMessagesPerConnection = connectionConcurrency;
        options.MessageQueueCapacityPerShard = 256;
        options.MaxQueuedMessagesPerConnection = queuedPerConnection;
        options.MaxPendingMessageBytesPerConnection = bytesPerConnection;
        options.MaxPendingMessageBytesPerShard = 64 * 1024 * 1024;
        options.MaxRequestTimeoutMs = 30_000;
    }
}
