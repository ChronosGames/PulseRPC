using Microsoft.Extensions.Logging;
using PulseRPC.Client.ServiceDiscovery;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Routing;

/// <summary>
/// 路由策略类型
/// </summary>
public enum RoutingStrategy
{
    /// <summary>
    /// 基于标签的路由
    /// </summary>
    TagBased,

    /// <summary>
    /// 基于区域的路由
    /// </summary>
    RegionBased,

    /// <summary>
    /// 基于用户类型的路由
    /// </summary>
    UserTypeBased,

    /// <summary>
    /// 基于负载的路由
    /// </summary>
    LoadBased,

    /// <summary>
    /// 基于权重的路由
    /// </summary>
    WeightBased,

    /// <summary>
    /// 基于版本的路由
    /// </summary>
    VersionBased,

    /// <summary>
    /// 自定义规则路由
    /// </summary>
    CustomRule,

    /// <summary>
    /// 混合路由
    /// </summary>
    Hybrid
}

/// <summary>
/// 路由匹配条件
/// </summary>
public sealed class RoutingCondition
{
    /// <summary>
    /// 条件名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 匹配操作符
    /// </summary>
    public RoutingOperator Operator { get; set; } = RoutingOperator.Equals;

    /// <summary>
    /// 期望值
    /// </summary>
    public object? ExpectedValue { get; set; }

    /// <summary>
    /// 是否大小写敏感
    /// </summary>
    public bool CaseSensitive { get; set; } = false;

    /// <summary>
    /// 权重
    /// </summary>
    public double Weight { get; set; } = 1.0;
}

/// <summary>
/// 路由操作符
/// </summary>
public enum RoutingOperator
{
    /// <summary>
    /// 等于
    /// </summary>
    Equals,

    /// <summary>
    /// 不等于
    /// </summary>
    NotEquals,

    /// <summary>
    /// 包含
    /// </summary>
    Contains,

    /// <summary>
    /// 不包含
    /// </summary>
    NotContains,

    /// <summary>
    /// 开始于
    /// </summary>
    StartsWith,

    /// <summary>
    /// 结束于
    /// </summary>
    EndsWith,

    /// <summary>
    /// 正则表达式
    /// </summary>
    Regex,

    /// <summary>
    /// 在范围内
    /// </summary>
    InRange,

    /// <summary>
    /// 不在范围内
    /// </summary>
    NotInRange,

    /// <summary>
    /// 存在
    /// </summary>
    Exists,

    /// <summary>
    /// 不存在
    /// </summary>
    NotExists
}

/// <summary>
/// 路由规则
/// </summary>
public sealed class RoutingRule
{
    /// <summary>
    /// 规则ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 规则名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 规则描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 优先级（越小越高）
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 路由策略
    /// </summary>
    public RoutingStrategy Strategy { get; set; } = RoutingStrategy.TagBased;

    /// <summary>
    /// 匹配条件列表
    /// </summary>
    public List<RoutingCondition> Conditions { get; set; } = new();

    /// <summary>
    /// 条件逻辑关系（AND 或 OR）
    /// </summary>
    public ConditionLogic Logic { get; set; } = ConditionLogic.And;

    /// <summary>
    /// 目标选择器
    /// </summary>
    public Func<IReadOnlyList<IConnection>, RoutingContext, IConnection?> Selector { get; set; } =
        (connections, _) => connections.FirstOrDefault();

    /// <summary>
    /// 目标过滤器
    /// </summary>
    public Func<IConnection, RoutingContext, bool>? Filter { get; set; }

    /// <summary>
    /// 路由权重
    /// </summary>
    public double Weight { get; set; } = 1.0;

    /// <summary>
    /// 超时时间
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// 重试次数
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// 标签
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 使用统计
    /// </summary>
    public RoutingRuleStatistics Statistics { get; set; } = new();
}

/// <summary>
/// 条件逻辑关系
/// </summary>
public enum ConditionLogic
{
    /// <summary>
    /// 逻辑与
    /// </summary>
    And,

    /// <summary>
    /// 逻辑或
    /// </summary>
    Or
}

/// <summary>
/// 路由上下文
/// </summary>
public sealed class RoutingContext
{
    /// <summary>
    /// 路由键
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// 服务名称
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// 方法名称
    /// </summary>
    public string? MethodName { get; set; }

    /// <summary>
    /// 客户端标识
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// 会话标识
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// 用户标识
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 用户类型
    /// </summary>
    public string? UserType { get; set; }

    /// <summary>
    /// 区域
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// 可用区
    /// </summary>
    public string? Zone { get; set; }

    /// <summary>
    /// 版本
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// 标签
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// 请求时间
    /// </summary>
    public DateTime RequestTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 超时时间
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// 取消令牌
    /// </summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public RoutingContext(string key)
    {
        Key = key;
    }

    /// <summary>
    /// 获取标签值
    /// </summary>
    public string? GetTag(string key) => Tags.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// 设置标签
    /// </summary>
    public void SetTag(string key, string value) => Tags[key] = value;

    /// <summary>
    /// 获取元数据
    /// </summary>
    public T? GetMetadata<T>(string key) => Metadata.TryGetValue(key, out var value) && value is T result ? result : default;

    /// <summary>
    /// 设置元数据
    /// </summary>
    public void SetMetadata(string key, object value) => Metadata[key] = value;
}

/// <summary>
/// 路由结果
/// </summary>
public sealed class RoutingResult
{
    /// <summary>
    /// 选择的连接
    /// </summary>
    public IConnection? SelectedConnection { get; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess => SelectedConnection != null;

    /// <summary>
    /// 匹配的规则
    /// </summary>
    public RoutingRule? MatchedRule { get; }

    /// <summary>
    /// 候选连接数量
    /// </summary>
    public int CandidateCount { get; }

    /// <summary>
    /// 路由耗时
    /// </summary>
    public TimeSpan RoutingTime { get; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// 路由原因
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// 调试信息
    /// </summary>
    public Dictionary<string, object> DebugInfo { get; }

    /// <summary>
    /// 构造函数 - 成功
    /// </summary>
    public RoutingResult(IConnection selectedConnection, RoutingRule? matchedRule, int candidateCount, TimeSpan routingTime, string? reason = null)
    {
        SelectedConnection = selectedConnection;
        MatchedRule = matchedRule;
        CandidateCount = candidateCount;
        RoutingTime = routingTime;
        Reason = reason;
        DebugInfo = new Dictionary<string, object>();
    }

    /// <summary>
    /// 构造函数 - 失败
    /// </summary>
    public RoutingResult(string errorMessage, int candidateCount, TimeSpan routingTime)
    {
        ErrorMessage = errorMessage;
        CandidateCount = candidateCount;
        RoutingTime = routingTime;
        DebugInfo = new Dictionary<string, object>();
    }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static RoutingResult Success(IConnection connection, RoutingRule? rule, int candidateCount, TimeSpan routingTime, string? reason = null)
        => new(connection, rule, candidateCount, routingTime, reason);

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static RoutingResult Failure(string errorMessage, int candidateCount, TimeSpan routingTime)
        => new(errorMessage, candidateCount, routingTime);
}

/// <summary>
/// 路由规则统计信息
/// </summary>
public sealed class RoutingRuleStatistics
{
    /// <summary>
    /// 总匹配次数
    /// </summary>
    public long TotalMatches { get; set; }

    /// <summary>
    /// 成功路由次数
    /// </summary>
    public long SuccessfulRoutes { get; set; }

    /// <summary>
    /// 失败路由次数
    /// </summary>
    public long FailedRoutes { get; set; }

    /// <summary>
    /// 平均路由时间
    /// </summary>
    public TimeSpan AverageRoutingTime { get; set; }

    /// <summary>
    /// 最大路由时间
    /// </summary>
    public TimeSpan MaxRoutingTime { get; set; }

    /// <summary>
    /// 最后匹配时间
    /// </summary>
    public DateTime? LastMatchedAt { get; set; }

    /// <summary>
    /// 成功率
    /// </summary>
    public double SuccessRate => TotalMatches > 0 ? (double)SuccessfulRoutes / TotalMatches * 100 : 100;
}

/// <summary>
/// 连接路由器接口
/// </summary>
public interface IConnectionRouter
{
    /// <summary>
    /// 路由器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 是否已启动
    /// </summary>
    bool IsStarted { get; }

    /// <summary>
    /// 启动路由器
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止路由器
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加路由规则
    /// </summary>
    void AddRule(RoutingRule rule);

    /// <summary>
    /// 移除路由规则
    /// </summary>
    bool RemoveRule(string ruleId);

    /// <summary>
    /// 更新路由规则
    /// </summary>
    bool UpdateRule(RoutingRule rule);

    /// <summary>
    /// 获取路由规则
    /// </summary>
    RoutingRule? GetRule(string ruleId);

    /// <summary>
    /// 获取所有路由规则
    /// </summary>
    IReadOnlyList<RoutingRule> GetAllRules();

    /// <summary>
    /// 清空所有规则
    /// </summary>
    void ClearRules();

    /// <summary>
    /// 路由连接
    /// </summary>
    Task<RoutingResult> RouteAsync(string key, RoutingContext? context = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量路由
    /// </summary>
    Task<IReadOnlyList<RoutingResult>> RouteBatchAsync(IReadOnlyList<string> keys, RoutingContext? context = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取路由统计信息
    /// </summary>
    RoutingStatistics GetStatistics();

    /// <summary>
    /// 重置统计信息
    /// </summary>
    void ResetStatistics();

    /// <summary>
    /// 路由规则变更事件
    /// </summary>
    event EventHandler<RoutingRuleChangedEventArgs> RuleChanged;

    /// <summary>
    /// 路由事件
    /// </summary>
    event EventHandler<RoutingEventArgs> Routed;
}

/// <summary>
/// 路由统计信息
/// </summary>
public sealed class RoutingStatistics
{
    /// <summary>
    /// 路由器名称
    /// </summary>
    public string RouterName { get; set; } = string.Empty;

    /// <summary>
    /// 总路由次数
    /// </summary>
    public long TotalRoutes { get; set; }

    /// <summary>
    /// 成功路由次数
    /// </summary>
    public long SuccessfulRoutes { get; set; }

    /// <summary>
    /// 失败路由次数
    /// </summary>
    public long FailedRoutes { get; set; }

    /// <summary>
    /// 平均路由时间
    /// </summary>
    public TimeSpan AverageRoutingTime { get; set; }

    /// <summary>
    /// 最大路由时间
    /// </summary>
    public TimeSpan MaxRoutingTime { get; set; }

    /// <summary>
    /// 规则匹配分布
    /// </summary>
    public Dictionary<string, long> RuleMatchCounts { get; set; } = new();

    /// <summary>
    /// 连接选择分布
    /// </summary>
    public Dictionary<string, long> ConnectionSelectionCounts { get; set; } = new();

    /// <summary>
    /// 最后统计时间
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 成功率
    /// </summary>
    public double SuccessRate => TotalRoutes > 0 ? (double)SuccessfulRoutes / TotalRoutes * 100 : 100;
}

/// <summary>
/// 路由规则变更事件参数
/// </summary>
public sealed class RoutingRuleChangedEventArgs : EventArgs
{
    /// <summary>
    /// 变更类型
    /// </summary>
    public RoutingRuleChangeType ChangeType { get; }

    /// <summary>
    /// 路由规则
    /// </summary>
    public RoutingRule Rule { get; }

    /// <summary>
    /// 旧规则（更新时使用）
    /// </summary>
    public RoutingRule? OldRule { get; }

    /// <summary>
    /// 变更时间
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public RoutingRuleChangedEventArgs(RoutingRuleChangeType changeType, RoutingRule rule, RoutingRule? oldRule = null)
    {
        ChangeType = changeType;
        Rule = rule;
        OldRule = oldRule;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// 路由规则变更类型
/// </summary>
public enum RoutingRuleChangeType
{
    /// <summary>
    /// 添加
    /// </summary>
    Added,

    /// <summary>
    /// 更新
    /// </summary>
    Updated,

    /// <summary>
    /// 移除
    /// </summary>
    Removed,

    /// <summary>
    /// 清空
    /// </summary>
    Cleared
}

/// <summary>
/// 路由事件参数
/// </summary>
public sealed class RoutingEventArgs : EventArgs
{
    /// <summary>
    /// 路由键
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// 路由上下文
    /// </summary>
    public RoutingContext? Context { get; }

    /// <summary>
    /// 路由结果
    /// </summary>
    public RoutingResult Result { get; }

    /// <summary>
    /// 路由时间
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public RoutingEventArgs(string key, RoutingContext? context, RoutingResult result)
    {
        Key = key;
        Context = context;
        Result = result;
        Timestamp = DateTime.UtcNow;
    }
}
