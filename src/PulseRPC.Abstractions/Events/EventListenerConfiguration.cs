using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PulseRPC.Client;

/// <summary>
/// 事件监听器配置
/// </summary>
public class EventListenerConfiguration
{
    /// <summary>
    /// 目标通道名称
    /// </summary>
    public string? ChannelName { get; set; }

    /// <summary>
    /// 事件过滤器 - 基于事件名称过滤
    /// </summary>
    public Func<string, bool>? EventFilter { get; set; }

    /// <summary>
    /// 错误处理策略
    /// </summary>
    public ErrorHandlingStrategy ErrorHandling { get; set; } = ErrorHandlingStrategy.LogAndContinue;

    /// <summary>
    /// 自定义错误处理器
    /// </summary>
    public EventErrorHandler? ErrorHandler { get; set; }

    /// <summary>
    /// 重试配置
    /// </summary>
    public RetryConfiguration? RetryConfig { get; set; }

    /// <summary>
    /// 超时配置
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// 是否启用批量事件处理
    /// </summary>
    public bool EnableBatchProcessing { get; set; } = false;

    /// <summary>
    /// 批量处理大小
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// 特定事件的数据过滤器
    /// </summary>
    public Dictionary<string, Func<object, bool>> DataFilters { get; set; } = new();

    /// <summary>
    /// 事件优先级配置
    /// </summary>
    public Dictionary<string, EventPriority> EventPriorities { get; set; } = new();

    /// <summary>
    /// 是否启用性能监控
    /// </summary>
    public bool EnablePerformanceMonitoring { get; set; } = false;
}

/// <summary>
/// 错误处理策略
/// </summary>
public enum ErrorHandlingStrategy
{
    /// <summary>
    /// 记录日志并继续处理其他事件
    /// </summary>
    LogAndContinue,

    /// <summary>
    /// 重试然后跳过
    /// </summary>
    RetryThenSkip,

    /// <summary>
    /// 重试然后抛出异常
    /// </summary>
    RetryThenThrow,

    /// <summary>
    /// 立即抛出异常
    /// </summary>
    ThrowImmediately,

    /// <summary>
    /// 自定义处理
    /// </summary>
    Custom
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

/// <summary>
/// 重试配置
/// </summary>
public class RetryConfiguration
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// 重试间隔
    /// </summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 是否使用指数退避
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// 退避倍数
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// 最大重试间隔
    /// </summary>
    public TimeSpan MaxRetryInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 重试条件判断器
    /// </summary>
    public Func<Exception, bool>? ShouldRetry { get; set; }
}

/// <summary>
/// 事件错误处理委托
/// </summary>
/// <param name="exception">发生的异常</param>
/// <param name="eventName">事件名称</param>
/// <param name="eventData">事件数据</param>
/// <param name="attemptNumber">当前尝试次数</param>
/// <returns>是否继续处理</returns>
public delegate Task<bool> EventErrorHandler(Exception exception, string eventName, object? eventData, int attemptNumber);

/// <summary>
/// 事件数据过滤器委托
/// </summary>
/// <typeparam name="T">事件数据类型</typeparam>
/// <param name="eventData">事件数据</param>
/// <returns>是否应该处理此事件</returns>
public delegate bool EventDataFilter<in T>(T eventData);

/// <summary>
/// 预定义的重试配置
/// </summary>
public static class RetryConfigurations
{
    /// <summary>
    /// 无重试
    /// </summary>
    public static readonly RetryConfiguration None = new() { MaxAttempts = 1 };

    /// <summary>
    /// 快速重试 - 适用于临时网络问题
    /// </summary>
    public static readonly RetryConfiguration Fast = new()
    {
        MaxAttempts = 3,
        RetryInterval = TimeSpan.FromMilliseconds(100),
        UseExponentialBackoff = true,
        BackoffMultiplier = 2.0
    };

    /// <summary>
    /// 标准重试 - 平衡重试次数和间隔
    /// </summary>
    public static readonly RetryConfiguration Standard = new()
    {
        MaxAttempts = 3,
        RetryInterval = TimeSpan.FromSeconds(1),
        UseExponentialBackoff = true,
        BackoffMultiplier = 2.0
    };

    /// <summary>
    /// 持久重试 - 适用于重要事件
    /// </summary>
    public static readonly RetryConfiguration Persistent = new()
    {
        MaxAttempts = 5,
        RetryInterval = TimeSpan.FromSeconds(2),
        UseExponentialBackoff = true,
        BackoffMultiplier = 1.5,
        MaxRetryInterval = TimeSpan.FromMinutes(1)
    };
}
