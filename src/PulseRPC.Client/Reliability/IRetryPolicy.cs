using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Reliability;

/// <summary>
/// 重试策略类型
/// </summary>
public enum RetryStrategyType
{
    /// <summary>
    /// 固定延迟
    /// </summary>
    FixedDelay,

    /// <summary>
    /// 线性增长
    /// </summary>
    LinearBackoff,

    /// <summary>
    /// 指数退避
    /// </summary>
    ExponentialBackoff,

    /// <summary>
    /// 自定义策略
    /// </summary>
    Custom
}

/// <summary>
/// 重试结果
/// </summary>
public sealed class RetryResult<T>
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// 结果值
    /// </summary>
    public T? Result { get; }

    /// <summary>
    /// 异常信息
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// 重试次数
    /// </summary>
    public int AttemptCount { get; }

    /// <summary>
    /// 总耗时
    /// </summary>
    public TimeSpan TotalTime { get; }

    /// <summary>
    /// 构造函数 - 成功
    /// </summary>
    public RetryResult(T result, int attemptCount, TimeSpan totalTime)
    {
        IsSuccess = true;
        Result = result;
        AttemptCount = attemptCount;
        TotalTime = totalTime;
    }

    /// <summary>
    /// 构造函数 - 失败
    /// </summary>
    public RetryResult(Exception exception, int attemptCount, TimeSpan totalTime)
    {
        IsSuccess = false;
        Exception = exception;
        AttemptCount = attemptCount;
        TotalTime = totalTime;
    }
}

/// <summary>
/// 重试上下文
/// </summary>
public sealed class RetryContext
{
    /// <summary>
    /// 当前重试次数
    /// </summary>
    public int AttemptNumber { get; }

    /// <summary>
    /// 上次异常
    /// </summary>
    public Exception? LastException { get; }

    /// <summary>
    /// 上次尝试时间
    /// </summary>
    public DateTime LastAttemptTime { get; }

    /// <summary>
    /// 总耗时
    /// </summary>
    public TimeSpan TotalElapsedTime { get; }

    /// <summary>
    /// 取消令牌
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// 扩展数据
    /// </summary>
    public Dictionary<string, object> Data { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public RetryContext(
        int attemptNumber,
        Exception? lastException,
        DateTime lastAttemptTime,
        TimeSpan totalElapsedTime,
        CancellationToken cancellationToken)
    {
        AttemptNumber = attemptNumber;
        LastException = lastException;
        LastAttemptTime = lastAttemptTime;
        TotalElapsedTime = totalElapsedTime;
        CancellationToken = cancellationToken;
        Data = new Dictionary<string, object>();
    }
}

/// <summary>
/// 重试事件参数
/// </summary>
public sealed class RetryEventArgs : EventArgs
{
    /// <summary>
    /// 重试上下文
    /// </summary>
    public RetryContext Context { get; }

    /// <summary>
    /// 下次重试延迟
    /// </summary>
    public TimeSpan NextDelay { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public RetryEventArgs(RetryContext context, TimeSpan nextDelay)
    {
        Context = context;
        NextDelay = nextDelay;
    }
}

/// <summary>
/// 重试策略接口
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// 策略名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 最大重试次数
    /// </summary>
    int MaxRetryCount { get; }

    /// <summary>
    /// 最大总耗时
    /// </summary>
    TimeSpan? MaxTotalTime { get; }

    /// <summary>
    /// 是否应该重试
    /// </summary>
    bool ShouldRetry(RetryContext context);

    /// <summary>
    /// 计算下次重试延迟
    /// </summary>
    TimeSpan CalculateDelay(RetryContext context);

    /// <summary>
    /// 执行带重试的操作
    /// </summary>
    Task<RetryResult<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行带重试的操作（无返回值）
    /// </summary>
    Task<RetryResult<object?>> ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 重试开始事件
    /// </summary>
    event EventHandler<RetryEventArgs>? RetryStarted;

    /// <summary>
    /// 重试失败事件
    /// </summary>
    event EventHandler<RetryEventArgs>? RetryFailed;

    /// <summary>
    /// 重试成功事件
    /// </summary>
    event EventHandler<RetryEventArgs>? RetrySucceeded;
}

/// <summary>
/// 重试策略配置
/// </summary>
public sealed class RetryPolicyConfiguration
{
    /// <summary>
    /// 策略名称
    /// </summary>
    public string Name { get; set; } = "DefaultRetryPolicy";

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// 基础延迟时间
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 最大延迟时间
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 最大总耗时
    /// </summary>
    public TimeSpan? MaxTotalTime { get; set; }

    /// <summary>
    /// 重试策略类型
    /// </summary>
    public RetryStrategyType StrategyType { get; set; } = RetryStrategyType.ExponentialBackoff;

    /// <summary>
    /// 退避倍数（用于指数退避）
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// 线性增长步长（用于线性退避）
    /// </summary>
    public TimeSpan LinearStep { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 是否启用抖动
    /// </summary>
    public bool EnableJitter { get; set; } = true;

    /// <summary>
    /// 抖动因子（0-1之间）
    /// </summary>
    public double JitterFactor { get; set; } = 0.1;

    /// <summary>
    /// 异常过滤器（决定哪些异常应该重试）
    /// </summary>
    public Func<Exception, bool>? ExceptionFilter { get; set; }

    /// <summary>
    /// 自定义延迟计算器
    /// </summary>
    public Func<RetryContext, TimeSpan>? CustomDelayCalculator { get; set; }

    /// <summary>
    /// 重试条件评估器
    /// </summary>
    public Func<RetryContext, bool>? RetryConditionEvaluator { get; set; }

    /// <summary>
    /// 扩展配置
    /// </summary>
    public Dictionary<string, object> ExtendedConfiguration { get; set; } = new();
}

/// <summary>
/// 重试策略统计信息
/// </summary>
public sealed class RetryPolicyStatistics
{
    /// <summary>
    /// 策略名称
    /// </summary>
    public string PolicyName { get; set; } = string.Empty;

    /// <summary>
    /// 总执行次数
    /// </summary>
    public long TotalExecutions { get; set; }

    /// <summary>
    /// 成功次数
    /// </summary>
    public long SuccessfulExecutions { get; set; }

    /// <summary>
    /// 失败次数
    /// </summary>
    public long FailedExecutions { get; set; }

    /// <summary>
    /// 总重试次数
    /// </summary>
    public long TotalRetries { get; set; }

    /// <summary>
    /// 平均重试次数
    /// </summary>
    public double AverageRetries { get; set; }

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; }

    /// <summary>
    /// 平均执行时间
    /// </summary>
    public TimeSpan AverageExecutionTime { get; set; }

    /// <summary>
    /// 最大执行时间
    /// </summary>
    public TimeSpan MaxExecutionTime { get; set; }

    /// <summary>
    /// 成功率
    /// </summary>
    public double SuccessRate => TotalExecutions > 0 ? (double)SuccessfulExecutions / TotalExecutions * 100 : 100;

    /// <summary>
    /// 异常分布
    /// </summary>
    public Dictionary<string, long> ExceptionDistribution { get; set; } = new();

    /// <summary>
    /// 重试分布
    /// </summary>
    public Dictionary<int, long> RetryDistribution { get; set; } = new();

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 重试策略构建器
/// </summary>
public interface IRetryPolicyBuilder
{
    /// <summary>
    /// 设置最大重试次数
    /// </summary>
    IRetryPolicyBuilder WithMaxRetryCount(int maxRetryCount);

    /// <summary>
    /// 设置基础延迟
    /// </summary>
    IRetryPolicyBuilder WithBaseDelay(TimeSpan baseDelay);

    /// <summary>
    /// 设置最大延迟
    /// </summary>
    IRetryPolicyBuilder WithMaxDelay(TimeSpan maxDelay);

    /// <summary>
    /// 设置最大总耗时
    /// </summary>
    IRetryPolicyBuilder WithMaxTotalTime(TimeSpan maxTotalTime);

    /// <summary>
    /// 使用固定延迟策略
    /// </summary>
    IRetryPolicyBuilder WithFixedDelay();

    /// <summary>
    /// 使用线性退避策略
    /// </summary>
    IRetryPolicyBuilder WithLinearBackoff(TimeSpan step);

    /// <summary>
    /// 使用指数退避策略
    /// </summary>
    IRetryPolicyBuilder WithExponentialBackoff(double multiplier = 2.0);

    /// <summary>
    /// 启用抖动
    /// </summary>
    IRetryPolicyBuilder WithJitter(double factor = 0.1);

    /// <summary>
    /// 设置异常过滤器
    /// </summary>
    IRetryPolicyBuilder WithExceptionFilter(Func<Exception, bool> filter);

    /// <summary>
    /// 设置自定义延迟计算器
    /// </summary>
    IRetryPolicyBuilder WithCustomDelayCalculator(Func<RetryContext, TimeSpan> calculator);

    /// <summary>
    /// 设置重试条件评估器
    /// </summary>
    IRetryPolicyBuilder WithRetryCondition(Func<RetryContext, bool> evaluator);

    /// <summary>
    /// 构建重试策略
    /// </summary>
    IRetryPolicy Build();
}
