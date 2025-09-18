using PulseRPC.Client.ServiceDiscovery;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Reliability;

/// <summary>
/// 故障转移策略
/// </summary>
public enum FailoverStrategy
{
    /// <summary>
    /// 快速故障转移
    /// </summary>
    FastFailover,

    /// <summary>
    /// 渐进式故障转移
    /// </summary>
    GradualFailover,

    /// <summary>
    /// 智能故障转移
    /// </summary>
    IntelligentFailover,

    /// <summary>
    /// 优先级故障转移
    /// </summary>
    PriorityFailover,

    /// <summary>
    /// 区域故障转移
    /// </summary>
    RegionalFailover,

    /// <summary>
    /// 自定义故障转移
    /// </summary>
    Custom
}

/// <summary>
/// 故障转移触发条件
/// </summary>
public enum FailoverTrigger
{
    /// <summary>
    /// 连接失败
    /// </summary>
    ConnectionFailure,

    /// <summary>
    /// 超时
    /// </summary>
    Timeout,

    /// <summary>
    /// 熔断器打开
    /// </summary>
    CircuitBreakerOpen,

    /// <summary>
    /// 健康检查失败
    /// </summary>
    HealthCheckFailure,

    /// <summary>
    /// 响应时间过长
    /// </summary>
    SlowResponse,

    /// <summary>
    /// 错误率过高
    /// </summary>
    HighErrorRate,

    /// <summary>
    /// 手动触发
    /// </summary>
    Manual,

    /// <summary>
    /// 自定义条件
    /// </summary>
    Custom
}

/// <summary>
/// 故障转移结果
/// </summary>
public sealed class FailoverResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// 原始连接
    /// </summary>
    public IConnection? OriginalConnection { get; }

    /// <summary>
    /// 备选连接
    /// </summary>
    public IConnection? FailoverConnection { get; }

    /// <summary>
    /// 故障转移策略
    /// </summary>
    public FailoverStrategy Strategy { get; }

    /// <summary>
    /// 触发原因
    /// </summary>
    public FailoverTrigger Trigger { get; }

    /// <summary>
    /// 故障转移耗时
    /// </summary>
    public TimeSpan FailoverTime { get; }

    /// <summary>
    /// 尝试的连接数
    /// </summary>
    public int AttemptedConnections { get; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// 故障转移原因
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// 扩展数据
    /// </summary>
    public Dictionary<string, object> ExtendedData { get; }

    /// <summary>
    /// 构造函数 - 成功
    /// </summary>
    public FailoverResult(
        IConnection originalConnection,
        IConnection failoverConnection,
        FailoverStrategy strategy,
        FailoverTrigger trigger,
        TimeSpan failoverTime,
        int attemptedConnections,
        string? reason = null)
    {
        IsSuccess = true;
        OriginalConnection = originalConnection;
        FailoverConnection = failoverConnection;
        Strategy = strategy;
        Trigger = trigger;
        FailoverTime = failoverTime;
        AttemptedConnections = attemptedConnections;
        Reason = reason;
        ExtendedData = new Dictionary<string, object>();
    }

    /// <summary>
    /// 构造函数 - 失败
    /// </summary>
    public FailoverResult(
        IConnection? originalConnection,
        FailoverStrategy strategy,
        FailoverTrigger trigger,
        TimeSpan failoverTime,
        int attemptedConnections,
        string errorMessage)
    {
        IsSuccess = false;
        OriginalConnection = originalConnection;
        Strategy = strategy;
        Trigger = trigger;
        FailoverTime = failoverTime;
        AttemptedConnections = attemptedConnections;
        ErrorMessage = errorMessage;
        ExtendedData = new Dictionary<string, object>();
    }
}

/// <summary>
/// 故障转移配置
/// </summary>
public sealed class FailoverConfiguration
{
    /// <summary>
    /// 故障转移策略
    /// </summary>
    public FailoverStrategy Strategy { get; set; } = FailoverStrategy.FastFailover;

    /// <summary>
    /// 最大故障转移尝试次数
    /// </summary>
    public int MaxFailoverAttempts { get; set; } = 3;

    /// <summary>
    /// 故障转移超时时间
    /// </summary>
    public TimeSpan FailoverTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 故障检测超时时间
    /// </summary>
    public TimeSpan FailureDetectionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 自动回切是否启用
    /// </summary>
    public bool EnableAutoFailback { get; set; } = true;

    /// <summary>
    /// 回切延迟时间
    /// </summary>
    public TimeSpan FailbackDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 连续成功次数阈值（用于回切）
    /// </summary>
    public int FailbackSuccessThreshold { get; set; } = 3;

    /// <summary>
    /// 错误率阈值
    /// </summary>
    public double ErrorRateThreshold { get; set; } = 50.0;

    /// <summary>
    /// 响应时间阈值
    /// </summary>
    public TimeSpan ResponseTimeThreshold { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 故障转移条件评估器
    /// </summary>
    public Func<IConnection, FailoverContext, bool>? FailoverConditionEvaluator { get; set; }

    /// <summary>
    /// 连接选择器
    /// </summary>
    public Func<IReadOnlyList<IConnection>, FailoverContext, IConnection?>? ConnectionSelector { get; set; }

    /// <summary>
    /// 连接健康检查器
    /// </summary>
    public Func<IConnection, CancellationToken, Task<bool>>? HealthChecker { get; set; }

    /// <summary>
    /// 扩展配置
    /// </summary>
    public Dictionary<string, object> ExtendedConfiguration { get; set; } = new();
}

/// <summary>
/// 故障转移上下文
/// </summary>
public sealed class FailoverContext
{
    /// <summary>
    /// 原始连接
    /// </summary>
    public IConnection OriginalConnection { get; }

    /// <summary>
    /// 候选连接列表
    /// </summary>
    public IReadOnlyList<IConnection> CandidateConnections { get; }

    /// <summary>
    /// 触发原因
    /// </summary>
    public FailoverTrigger Trigger { get; set; }

    /// <summary>
    /// 触发异常
    /// </summary>
    public Exception? TriggerException { get; set; }

    /// <summary>
    /// 触发时间
    /// </summary>
    public DateTime TriggerTime { get; }

    /// <summary>
    /// 当前尝试次数
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// 服务名称
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// 区域偏好
    /// </summary>
    public string? PreferredRegion { get; set; }

    /// <summary>
    /// 优先级偏好
    /// </summary>
    public int? PreferredPriority { get; set; }

    /// <summary>
    /// 标签过滤器
    /// </summary>
    public Dictionary<string, string> TagFilters { get; set; } = new();

    /// <summary>
    /// 排除的连接
    /// </summary>
    public HashSet<string> ExcludedConnections { get; set; } = new();

    /// <summary>
    /// 扩展数据
    /// </summary>
    public Dictionary<string, object> ExtendedData { get; set; } = new();

    /// <summary>
    /// 取消令牌
    /// </summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public FailoverContext(IConnection originalConnection, IReadOnlyList<IConnection> candidateConnections)
    {
        OriginalConnection = originalConnection;
        CandidateConnections = candidateConnections;
        TriggerTime = DateTime.UtcNow;
    }
}

/// <summary>
/// 故障转移事件参数
/// </summary>
public sealed class FailoverEventArgs : EventArgs
{
    /// <summary>
    /// 故障转移结果
    /// </summary>
    public FailoverResult Result { get; }

    /// <summary>
    /// 故障转移上下文
    /// </summary>
    public FailoverContext Context { get; }

    /// <summary>
    /// 事件时间
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public FailoverEventArgs(FailoverResult result, FailoverContext context)
    {
        Result = result;
        Context = context;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// 故障回切事件参数
/// </summary>
public sealed class FailbackEventArgs : EventArgs
{
    /// <summary>
    /// 当前连接
    /// </summary>
    public IConnection CurrentConnection { get; }

    /// <summary>
    /// 原始连接
    /// </summary>
    public IConnection OriginalConnection { get; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// 回切原因
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// 事件时间
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public FailbackEventArgs(IConnection currentConnection, IConnection originalConnection, bool isSuccess, string? reason = null)
    {
        CurrentConnection = currentConnection;
        OriginalConnection = originalConnection;
        IsSuccess = isSuccess;
        Reason = reason;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// 故障转移统计信息
/// </summary>
public sealed class FailoverStatistics
{
    /// <summary>
    /// 总故障转移次数
    /// </summary>
    public long TotalFailovers { get; set; }

    /// <summary>
    /// 成功故障转移次数
    /// </summary>
    public long SuccessfulFailovers { get; set; }

    /// <summary>
    /// 失败故障转移次数
    /// </summary>
    public long FailedFailovers { get; set; }

    /// <summary>
    /// 总回切次数
    /// </summary>
    public long TotalFailbacks { get; set; }

    /// <summary>
    /// 成功回切次数
    /// </summary>
    public long SuccessfulFailbacks { get; set; }

    /// <summary>
    /// 平均故障转移时间
    /// </summary>
    public TimeSpan AverageFailoverTime { get; set; }

    /// <summary>
    /// 最大故障转移时间
    /// </summary>
    public TimeSpan MaxFailoverTime { get; set; }

    /// <summary>
    /// 触发原因分布
    /// </summary>
    public Dictionary<FailoverTrigger, long> TriggerDistribution { get; set; } = new();

    /// <summary>
    /// 策略使用分布
    /// </summary>
    public Dictionary<FailoverStrategy, long> StrategyDistribution { get; set; } = new();

    /// <summary>
    /// 连接故障转移计数
    /// </summary>
    public Dictionary<string, long> ConnectionFailoverCounts { get; set; } = new();

    /// <summary>
    /// 最后故障转移时间
    /// </summary>
    public DateTime? LastFailoverAt { get; set; }

    /// <summary>
    /// 最后回切时间
    /// </summary>
    public DateTime? LastFailbackAt { get; set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 故障转移成功率
    /// </summary>
    public double FailoverSuccessRate => TotalFailovers > 0 ? (double)SuccessfulFailovers / TotalFailovers * 100 : 100;

    /// <summary>
    /// 回切成功率
    /// </summary>
    public double FailbackSuccessRate => TotalFailbacks > 0 ? (double)SuccessfulFailbacks / TotalFailbacks * 100 : 100;
}

/// <summary>
/// 故障转移管理器接口
/// </summary>
public interface IFailoverManager
{
    /// <summary>
    /// 管理器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 是否已启动
    /// </summary>
    bool IsStarted { get; }

    /// <summary>
    /// 启动故障转移管理器
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止故障转移管理器
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行故障转移
    /// </summary>
    Task<FailoverResult> FailoverAsync(FailoverContext context);

    /// <summary>
    /// 检查是否需要故障转移
    /// </summary>
    Task<bool> ShouldFailoverAsync(IConnection connection, FailoverTrigger trigger, Exception? exception = null);

    /// <summary>
    /// 尝试回切到原始连接
    /// </summary>
    Task<bool> TryFailbackAsync(IConnection currentConnection, IConnection originalConnection, CancellationToken cancellationToken = default);

    /// <summary>
    /// 手动触发故障转移
    /// </summary>
    Task<FailoverResult> ManualFailoverAsync(IConnection connection, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册连接监控
    /// </summary>
    void RegisterConnectionMonitoring(IConnection connection);

    /// <summary>
    /// 取消连接监控
    /// </summary>
    void UnregisterConnectionMonitoring(string connectionId);

    /// <summary>
    /// 获取统计信息
    /// </summary>
    FailoverStatistics GetStatistics();

    /// <summary>
    /// 重置统计信息
    /// </summary>
    void ResetStatistics();

    /// <summary>
    /// 故障转移事件
    /// </summary>
    event EventHandler<FailoverEventArgs>? FailoverOccurred;

    /// <summary>
    /// 故障回切事件
    /// </summary>
    event EventHandler<FailbackEventArgs>? FailbackOccurred;
}

/// <summary>
/// 故障转移管理器构建器接口
/// </summary>
public interface IFailoverManagerBuilder
{
    /// <summary>
    /// 设置名称
    /// </summary>
    IFailoverManagerBuilder WithName(string name);

    /// <summary>
    /// 设置故障转移策略
    /// </summary>
    IFailoverManagerBuilder WithStrategy(FailoverStrategy strategy);

    /// <summary>
    /// 设置最大尝试次数
    /// </summary>
    IFailoverManagerBuilder WithMaxAttempts(int maxAttempts);

    /// <summary>
    /// 设置故障转移超时时间
    /// </summary>
    IFailoverManagerBuilder WithFailoverTimeout(TimeSpan timeout);

    /// <summary>
    /// 设置故障检测超时时间
    /// </summary>
    IFailoverManagerBuilder WithFailureDetectionTimeout(TimeSpan timeout);

    /// <summary>
    /// 启用自动回切
    /// </summary>
    IFailoverManagerBuilder WithAutoFailback(bool enabled = true, TimeSpan? delay = null, int successThreshold = 3);

    /// <summary>
    /// 设置健康检查间隔
    /// </summary>
    IFailoverManagerBuilder WithHealthCheckInterval(TimeSpan interval);

    /// <summary>
    /// 设置错误率阈值
    /// </summary>
    IFailoverManagerBuilder WithErrorRateThreshold(double threshold);

    /// <summary>
    /// 设置响应时间阈值
    /// </summary>
    IFailoverManagerBuilder WithResponseTimeThreshold(TimeSpan threshold);

    /// <summary>
    /// 设置故障转移条件评估器
    /// </summary>
    IFailoverManagerBuilder WithFailoverCondition(Func<IConnection, FailoverContext, bool> evaluator);

    /// <summary>
    /// 设置连接选择器
    /// </summary>
    IFailoverManagerBuilder WithConnectionSelector(Func<IReadOnlyList<IConnection>, FailoverContext, IConnection?> selector);

    /// <summary>
    /// 设置健康检查器
    /// </summary>
    IFailoverManagerBuilder WithHealthChecker(Func<IConnection, CancellationToken, Task<bool>> healthChecker);

    /// <summary>
    /// 构建故障转移管理器
    /// </summary>
    IFailoverManager Build();
}
