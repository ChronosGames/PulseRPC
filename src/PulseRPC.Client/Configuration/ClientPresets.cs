using PulseRPC.Client.Reliability;

namespace PulseRPC.Client.Configuration;

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
