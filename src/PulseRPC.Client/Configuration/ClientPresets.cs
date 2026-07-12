using PulseRPC.Client.Channels;
using PulseRPC.Client.Reliability;

namespace PulseRPC.Client.Configuration;

/// <summary>
/// Legacy client presets retained for compatibility.
/// </summary>
[Obsolete("Client presets configure legacy ClientOptions fields that are not consumed. Configure effective load-balancing and per-connection transport options explicitly.", false)]
public static class ClientPresets
{
    /// <summary>
    /// 默认配置 - 适用于大多数场景
    /// </summary>
    public static Action<ClientOptions> Default => options =>
    {
        options.Name = "PulseRPC-Client";
        options.DefaultTimeout = TimeSpan.FromSeconds(30);
        options.MaxConcurrentConnections = 100;
        options.EnableStatistics = true;
    };

    /// <summary>
    /// 游戏客户端配置 - 低延迟优化
    /// </summary>
    public static Action<ClientOptions> GameClient => options =>
    {
        options.Name = "Game-Client";
        options.DefaultTimeout = TimeSpan.FromSeconds(10);
        options.MaxConcurrentConnections = 20;
        options.EnableStatistics = false; // 减少开销
    };

    /// <summary>
    /// 高吞吐配置 - 服务端到服务端通信
    /// </summary>
    public static Action<ClientOptions> HighThroughput => options =>
    {
        options.Name = "Service-Client";
        options.DefaultTimeout = TimeSpan.FromSeconds(60);
        options.MaxConcurrentConnections = 500;
        options.EnableStatistics = true;
    };

    /// <summary>
    /// 开发/调试配置
    /// </summary>
    public static Action<ClientOptions> Development => options =>
    {
        options.Name = "Dev-Client";
        options.DefaultTimeout = TimeSpan.FromSeconds(120); // 长超时便于调试
        options.MaxConcurrentConnections = 10;
        options.EnableDebugMode = true;
        options.EnableStatistics = true;
    };
}

/// <summary>
/// Legacy presets for an options type that has no public connection wiring.
/// </summary>
[Obsolete("ChannelPresets cannot be applied through the public client API. Configure ConnectionDescriptor.TransportOptions instead.", false)]
public static class ChannelPresets
{
    /// <summary>
    /// 默认通道配置
    /// </summary>
    public static TransportChannelOptions Default => new()
    {
        DefaultTimeout = TimeSpan.FromSeconds(30),
        MessageQueueCapacity = 1000,
        MessageProcessingConcurrency = 4,
        HeartbeatInterval = TimeSpan.FromSeconds(30),
        EnableAutoReconnect = true,
        MaxReconnectAttempts = 5
    };

    /// <summary>
    /// 低延迟配置 - 适用于游戏客户端
    /// </summary>
    public static TransportChannelOptions LowLatency => new()
    {
        DefaultTimeout = TimeSpan.FromSeconds(10),
        MessageQueueCapacity = 500,
        MessageProcessingConcurrency = 2,
        HeartbeatInterval = TimeSpan.FromSeconds(15),
        EnableAutoReconnect = true,
        MaxReconnectAttempts = 3
    };

    /// <summary>
    /// 高可靠配置 - 适用于关键业务
    /// </summary>
    public static TransportChannelOptions HighReliability => new()
    {
        DefaultTimeout = TimeSpan.FromSeconds(60),
        MessageQueueCapacity = 2000,
        MessageProcessingConcurrency = 8,
        HeartbeatInterval = TimeSpan.FromSeconds(10),
        EnableAutoReconnect = true,
        MaxReconnectAttempts = 10
    };
}

/// <summary>
/// 重试策略预设
/// </summary>
public static class RetryPresets
{
    /// <summary>
    /// 默认策略 - 3次重试，指数退避
    /// </summary>
    public static Reliability.RetryPolicy Default => Reliability.RetryPolicy.Default();

    /// <summary>
    /// 激进策略 - 5次重试，更短初始延迟
    /// </summary>
    public static Reliability.RetryPolicy Aggressive => Reliability.RetryPolicy.Exponential(
        maxRetries: 5,
        initialDelay: TimeSpan.FromMilliseconds(200),
        maxDelay: TimeSpan.FromSeconds(30));

    /// <summary>
    /// 保守策略 - 10次重试，更长延迟
    /// </summary>
    public static Reliability.RetryPolicy Conservative => Reliability.RetryPolicy.Exponential(
        maxRetries: 10,
        initialDelay: TimeSpan.FromSeconds(1),
        maxDelay: TimeSpan.FromMinutes(2));

    /// <summary>
    /// 无重试
    /// </summary>
    public static Reliability.RetryPolicy None => Reliability.RetryPolicy.NoRetry();
}
