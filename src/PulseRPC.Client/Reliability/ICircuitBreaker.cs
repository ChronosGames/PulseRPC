using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Reliability;

/// <summary>
/// 熔断器状态
/// </summary>
public enum CircuitBreakerState
{
    /// <summary>
    /// 关闭状态（正常）
    /// </summary>
    Closed,

    /// <summary>
    /// 打开状态（熔断）
    /// </summary>
    Open,

    /// <summary>
    /// 半开状态（试探）
    /// </summary>
    HalfOpen
}

/// <summary>
/// 熔断器结果
/// </summary>
public sealed class CircuitBreakerResult<T>
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
    /// 是否被熔断器拒绝
    /// </summary>
    public bool IsRejectedByCircuitBreaker { get; }

    /// <summary>
    /// 熔断器状态
    /// </summary>
    public CircuitBreakerState CircuitBreakerState { get; }

    /// <summary>
    /// 执行时间
    /// </summary>
    public TimeSpan ExecutionTime { get; }

    /// <summary>
    /// 构造函数 - 成功
    /// </summary>
    public CircuitBreakerResult(T result, CircuitBreakerState state, TimeSpan executionTime)
    {
        IsSuccess = true;
        Result = result;
        CircuitBreakerState = state;
        ExecutionTime = executionTime;
    }

    /// <summary>
    /// 构造函数 - 失败
    /// </summary>
    public CircuitBreakerResult(Exception exception, CircuitBreakerState state, TimeSpan executionTime, bool isRejected = false)
    {
        IsSuccess = false;
        Exception = exception;
        CircuitBreakerState = state;
        ExecutionTime = executionTime;
        IsRejectedByCircuitBreaker = isRejected;
    }
}

/// <summary>
/// 熔断器配置
/// </summary>
public sealed class CircuitBreakerConfiguration
{
    /// <summary>
    /// 熔断器名称
    /// </summary>
    public string Name { get; set; } = "DefaultCircuitBreaker";

    /// <summary>
    /// 失败阈值（连续失败次数）
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// 失败率阈值（百分比）
    /// </summary>
    public double FailureRateThreshold { get; set; } = 50.0;

    /// <summary>
    /// 最小请求数量（在统计窗口内）
    /// </summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>
    /// 统计窗口大小
    /// </summary>
    public TimeSpan StatisticsWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 熔断持续时间
    /// </summary>
    public TimeSpan OpenTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 半开状态最大尝试次数
    /// </summary>
    public int HalfOpenMaxAttempts { get; set; } = 3;

    /// <summary>
    /// 半开状态成功阈值
    /// </summary>
    public int HalfOpenSuccessThreshold { get; set; } = 2;

    /// <summary>
    /// 超时时间
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// 异常断言器（判断异常是否应该计入失败）
    /// </summary>
    public Func<Exception, bool>? ExceptionPredicate { get; set; }

    /// <summary>
    /// 结果断言器（判断结果是否应该计入失败）
    /// </summary>
    public Func<object?, bool>? ResultPredicate { get; set; }

    /// <summary>
    /// 启用详细统计
    /// </summary>
    public bool EnableDetailedStatistics { get; set; } = true;

    /// <summary>
    /// 扩展配置
    /// </summary>
    public Dictionary<string, object> ExtendedConfiguration { get; set; } = new();
}

/// <summary>
/// 熔断器统计信息
/// </summary>
public sealed class CircuitBreakerStatistics
{
    /// <summary>
    /// 熔断器名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 当前状态
    /// </summary>
    public CircuitBreakerState State { get; set; }

    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// 成功请求数
    /// </summary>
    public long SuccessfulRequests { get; set; }

    /// <summary>
    /// 失败请求数
    /// </summary>
    public long FailedRequests { get; set; }

    /// <summary>
    /// 被拒绝请求数
    /// </summary>
    public long RejectedRequests { get; set; }

    /// <summary>
    /// 连续失败次数
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// 当前失败率
    /// </summary>
    public double CurrentFailureRate { get; set; }

    /// <summary>
    /// 熔断次数
    /// </summary>
    public long CircuitOpenedCount { get; set; }

    /// <summary>
    /// 最后熔断时间
    /// </summary>
    public DateTime? LastCircuitOpenedAt { get; set; }

    /// <summary>
    /// 最后状态变更时间
    /// </summary>
    public DateTime LastStateChangedAt { get; set; }

    /// <summary>
    /// 平均响应时间
    /// </summary>
    public TimeSpan AverageResponseTime { get; set; }

    /// <summary>
    /// 最大响应时间
    /// </summary>
    public TimeSpan MaxResponseTime { get; set; }

    /// <summary>
    /// 半开状态尝试次数
    /// </summary>
    public int HalfOpenAttempts { get; set; }

    /// <summary>
    /// 半开状态成功次数
    /// </summary>
    public int HalfOpenSuccesses { get; set; }

    /// <summary>
    /// 异常分布
    /// </summary>
    public Dictionary<string, long> ExceptionDistribution { get; set; } = new();

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 成功率
    /// </summary>
    public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests * 100 : 100;
}

/// <summary>
/// 熔断器状态变更事件参数
/// </summary>
public sealed class CircuitBreakerStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 熔断器名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 之前的状态
    /// </summary>
    public CircuitBreakerState PreviousState { get; }

    /// <summary>
    /// 当前状态
    /// </summary>
    public CircuitBreakerState CurrentState { get; }

    /// <summary>
    /// 状态变更原因
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// 触发异常
    /// </summary>
    public Exception? TriggerException { get; }

    /// <summary>
    /// 变更时间
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public CircuitBreakerStateChangedEventArgs(
        string name,
        CircuitBreakerState previousState,
        CircuitBreakerState currentState,
        string? reason = null,
        Exception? triggerException = null)
    {
        Name = name;
        PreviousState = previousState;
        CurrentState = currentState;
        Reason = reason;
        TriggerException = triggerException;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// 熔断器接口
/// </summary>
public interface ICircuitBreaker
{
    /// <summary>
    /// 熔断器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 当前状态
    /// </summary>
    CircuitBreakerState State { get; }

    /// <summary>
    /// 是否允许请求通过
    /// </summary>
    bool IsRequestAllowed { get; }

    /// <summary>
    /// 执行操作
    /// </summary>
    Task<CircuitBreakerResult<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行操作（无返回值）
    /// </summary>
    Task<CircuitBreakerResult<object?>> ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 手动打开熔断器
    /// </summary>
    void Open(string? reason = null);

    /// <summary>
    /// 手动关闭熔断器
    /// </summary>
    void Close(string? reason = null);

    /// <summary>
    /// 手动设置为半开状态
    /// </summary>
    void HalfOpen(string? reason = null);

    /// <summary>
    /// 重置熔断器
    /// </summary>
    void Reset();

    /// <summary>
    /// 获取统计信息
    /// </summary>
    CircuitBreakerStatistics GetStatistics();

    /// <summary>
    /// 重置统计信息
    /// </summary>
    void ResetStatistics();

    /// <summary>
    /// 状态变更事件
    /// </summary>
    event EventHandler<CircuitBreakerStateChangedEventArgs>? StateChanged;
}

/// <summary>
/// 熔断器构建器接口
/// </summary>
public interface ICircuitBreakerBuilder
{
    /// <summary>
    /// 设置名称
    /// </summary>
    ICircuitBreakerBuilder WithName(string name);

    /// <summary>
    /// 设置失败阈值
    /// </summary>
    ICircuitBreakerBuilder WithFailureThreshold(int threshold);

    /// <summary>
    /// 设置失败率阈值
    /// </summary>
    ICircuitBreakerBuilder WithFailureRateThreshold(double threshold);

    /// <summary>
    /// 设置最小吞吐量
    /// </summary>
    ICircuitBreakerBuilder WithMinimumThroughput(int throughput);

    /// <summary>
    /// 设置统计窗口
    /// </summary>
    ICircuitBreakerBuilder WithStatisticsWindow(TimeSpan window);

    /// <summary>
    /// 设置打开超时时间
    /// </summary>
    ICircuitBreakerBuilder WithOpenTimeout(TimeSpan timeout);

    /// <summary>
    /// 设置半开状态配置
    /// </summary>
    ICircuitBreakerBuilder WithHalfOpenConfig(int maxAttempts, int successThreshold);

    /// <summary>
    /// 设置超时时间
    /// </summary>
    ICircuitBreakerBuilder WithTimeout(TimeSpan timeout);

    /// <summary>
    /// 设置异常断言器
    /// </summary>
    ICircuitBreakerBuilder WithExceptionPredicate(Func<Exception, bool> predicate);

    /// <summary>
    /// 设置结果断言器
    /// </summary>
    ICircuitBreakerBuilder WithResultPredicate(Func<object?, bool> predicate);

    /// <summary>
    /// 启用详细统计
    /// </summary>
    ICircuitBreakerBuilder WithDetailedStatistics(bool enabled = true);

    /// <summary>
    /// 构建熔断器
    /// </summary>
    ICircuitBreaker Build();
}
