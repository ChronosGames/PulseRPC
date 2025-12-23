using System;

namespace PulseRPC.Client.Events;

/// <summary>
/// 智能订阅选项 - 配置高级事件处理特性
/// </summary>
public sealed class SmartSubscriptionOptions
{
    /// <summary>
    /// 默认配置 - 适用于大多数场景
    /// </summary>
    public static SmartSubscriptionOptions Default { get; } = new();

    /// <summary>
    /// 游戏优化配置 - 低延迟、高频处理
    /// </summary>
    public static SmartSubscriptionOptions Game { get; } = new()
    {
        EnableBatchProcessing = true,
        BatchSize = 100,
        BatchTimeout = TimeSpan.FromMilliseconds(10),
        EnablePerformanceMonitoring = true,
        MaxRetries = 0, // 游戏事件不重试
        Timeout = TimeSpan.FromMilliseconds(100)
    };

    /// <summary>
    /// 关键业务配置 - 高可靠性、强一致性
    /// </summary>
    public static SmartSubscriptionOptions Critical { get; } = new()
    {
        EnableBatchProcessing = false,
        EnablePerformanceMonitoring = true,
        MaxRetries = 5,
        RetryDelay = TimeSpan.FromSeconds(1),
        Timeout = TimeSpan.FromSeconds(30),
        EnableCircuitBreaker = true
    };

    /// <summary>启用批量处理</summary>
    public bool EnableBatchProcessing { get; set; } = false;

    /// <summary>批量大小</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>批量超时时间</summary>
    public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>启用性能监控</summary>
    public bool EnablePerformanceMonitoring { get; set; } = true;

    /// <summary>最大重试次数</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>重试延迟</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>事件处理超时时间</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>启用断路器模式</summary>
    public bool EnableCircuitBreaker { get; set; } = false;

    /// <summary>自定义错误处理器</summary>
    public Action<Exception, string>? ErrorHandler { get; set; }

    /// <summary>事件优先级</summary>
    public EventPriority Priority { get; set; } = EventPriority.Normal;
}

/// <summary>
/// 事件优先级
/// </summary>
public enum EventPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}
