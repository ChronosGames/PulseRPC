using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC;

/// <summary>
/// 重试配置选项
/// </summary>
public class RetryOptions
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 基础延迟
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 最大延迟
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 使用指数退避
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;
}
