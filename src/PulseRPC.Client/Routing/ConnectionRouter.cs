using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Client.ServiceDiscovery;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using PulseRPC.Client;
using PulseRPC.Client;

namespace PulseRPC.Client.Routing;

/// <summary>
/// 连接路由器实现
/// </summary>
public sealed class ConnectionRouter : IConnectionRouter, IDisposable
{
    private readonly ILogger<ConnectionRouter> _logger;
    private readonly IConnectionRegistry _connectionRegistry;
    private readonly ConcurrentDictionary<string, RoutingRule> _rules = new();
    private readonly RoutingStatistics _statistics = new();
    private readonly object _statisticsLock = new();
    private volatile bool _isStarted;
    private volatile bool _disposed;

    /// <summary>
    /// 路由器名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 是否已启动
    /// </summary>
    public bool IsStarted => _isStarted;

    /// <summary>
    /// 路由规则变更事件
    /// </summary>
    public event EventHandler<RoutingRuleChangedEventArgs>? RuleChanged;

    /// <summary>
    /// 路由事件
    /// </summary>
    public event EventHandler<RoutingEventArgs>? Routed;

    /// <summary>
    /// 构造函数
    /// </summary>
    public ConnectionRouter(
        IConnectionRegistry connectionRegistry,
        string name = "DefaultConnectionRouter",
        ILoggerFactory? loggerFactory = null)
    {
        _connectionRegistry = connectionRegistry ?? throw new ArgumentNullException(nameof(connectionRegistry));
        Name = name;
        _logger = loggerFactory?.CreateLogger<ConnectionRouter>() ?? NullLogger<ConnectionRouter>.Instance;

        _statistics.RouterName = Name;
    }

    /// <summary>
    /// 启动路由器
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_isStarted)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("启动连接路由器: {RouterName}", Name);

        // 添加默认规则
        AddDefaultRules();

        _isStarted = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止路由器
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_isStarted)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("停止连接路由器: {RouterName}", Name);
        _isStarted = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 添加路由规则
    /// </summary>
    public void AddRule(RoutingRule rule)
    {
        ThrowIfDisposed();

        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        rule.LastUpdatedAt = DateTime.UtcNow;

        if (_rules.TryAdd(rule.Id, rule))
        {
            _logger.LogInformation("添加路由规则: {RuleId} ({RuleName})", rule.Id, rule.Name);
            OnRuleChanged(new RoutingRuleChangedEventArgs(RoutingRuleChangeType.Added, rule));
        }
        else
        {
            _logger.LogWarning("路由规则ID已存在: {RuleId}", rule.Id);
            throw new InvalidOperationException($"路由规则ID已存在: {rule.Id}");
        }
    }

    /// <summary>
    /// 移除路由规则
    /// </summary>
    public bool RemoveRule(string ruleId)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(ruleId))
            return false;

        if (_rules.TryRemove(ruleId, out var removedRule))
        {
            _logger.LogInformation("移除路由规则: {RuleId} ({RuleName})", ruleId, removedRule.Name);
            OnRuleChanged(new RoutingRuleChangedEventArgs(RoutingRuleChangeType.Removed, removedRule));
            return true;
        }

        return false;
    }

    /// <summary>
    /// 更新路由规则
    /// </summary>
    public bool UpdateRule(RoutingRule rule)
    {
        ThrowIfDisposed();

        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        rule.LastUpdatedAt = DateTime.UtcNow;

        if (_rules.TryGetValue(rule.Id, out var oldRule))
        {
            _rules[rule.Id] = rule;
            _logger.LogInformation("更新路由规则: {RuleId} ({RuleName})", rule.Id, rule.Name);
            OnRuleChanged(new RoutingRuleChangedEventArgs(RoutingRuleChangeType.Updated, rule, oldRule));
            return true;
        }

        return false;
    }

    /// <summary>
    /// 获取路由规则
    /// </summary>
    public RoutingRule? GetRule(string ruleId)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(ruleId))
            return null;

        return _rules.TryGetValue(ruleId, out var rule) ? rule : null;
    }

    /// <summary>
    /// 获取所有路由规则
    /// </summary>
    public IReadOnlyList<RoutingRule> GetAllRules()
    {
        ThrowIfDisposed();
        return _rules.Values.OrderBy(r => r.Priority).ThenBy(r => r.CreatedAt).ToList();
    }

    /// <summary>
    /// 清空所有规则
    /// </summary>
    public void ClearRules()
    {
        ThrowIfDisposed();

        var count = _rules.Count;
        _rules.Clear();

        _logger.LogInformation("清空所有路由规则，共 {Count} 条", count);
        OnRuleChanged(new RoutingRuleChangedEventArgs(RoutingRuleChangeType.Cleared, new RoutingRule { Name = "ClearAll" }));
    }

    /// <summary>
    /// 路由连接
    /// </summary>
    public async Task<RoutingResult> RouteAsync(string key, RoutingContext? context = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(key))
        {
            return RoutingResult.Failure("路由键不能为空", 0, TimeSpan.Zero);
        }

        var stopwatch = Stopwatch.StartNew();
        context ??= new RoutingContext(key);

        try
        {
            _logger.LogDebug("开始路由连接: {Key}", key);

            // 获取所有可用连接
            var availableConnections = _connectionRegistry.GetAllConnections()
                .Where(c => c.State == ExtendedConnectionState.Connected || c.State == ExtendedConnectionState.Active)
                .ToList();

            if (availableConnections.Count == 0)
            {
                var result = RoutingResult.Failure("没有可用的连接", 0, stopwatch.Elapsed);
                await RecordRoutingResult(key, context, result);
                return result;
            }

            // 按优先级排序的规则
            var sortedRules = GetAllRules().Where(r => r.Enabled).ToList();

            // 尝试匹配规则
            foreach (var rule in sortedRules)
            {
                if (await TryMatchRule(rule, context, availableConnections, stopwatch.Elapsed) is { } matchResult)
                {
                    await RecordRoutingResult(key, context, matchResult);
                    return matchResult;
                }
            }

            // 没有匹配的规则，使用默认选择
            var defaultConnection = availableConnections.First();
            var defaultResult = RoutingResult.Success(
                defaultConnection,
                null,
                availableConnections.Count,
                stopwatch.Elapsed,
                "默认选择");

            await RecordRoutingResult(key, context, defaultResult);
            return defaultResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "路由连接时发生错误: {Key}", key);
            var errorResult = RoutingResult.Failure($"路由错误: {ex.Message}", 0, stopwatch.Elapsed);
            await RecordRoutingResult(key, context, errorResult);
            return errorResult;
        }
    }

    /// <summary>
    /// 批量路由
    /// </summary>
    public async Task<IReadOnlyList<RoutingResult>> RouteBatchAsync(IReadOnlyList<string> keys, RoutingContext? context = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (keys == null || keys.Count == 0)
        {
            return Array.Empty<RoutingResult>();
        }

        var tasks = keys.Select(key => RouteAsync(key, context, cancellationToken));
        return await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 获取路由统计信息
    /// </summary>
    public RoutingStatistics GetStatistics()
    {
        ThrowIfDisposed();

        lock (_statisticsLock)
        {
            var stats = new RoutingStatistics
            {
                RouterName = _statistics.RouterName,
                TotalRoutes = _statistics.TotalRoutes,
                SuccessfulRoutes = _statistics.SuccessfulRoutes,
                FailedRoutes = _statistics.FailedRoutes,
                AverageRoutingTime = _statistics.AverageRoutingTime,
                MaxRoutingTime = _statistics.MaxRoutingTime,
                RuleMatchCounts = new Dictionary<string, long>(_statistics.RuleMatchCounts),
                ConnectionSelectionCounts = new Dictionary<string, long>(_statistics.ConnectionSelectionCounts),
                LastUpdatedAt = DateTime.UtcNow
            };

            return stats;
        }
    }

    /// <summary>
    /// 重置统计信息
    /// </summary>
    public void ResetStatistics()
    {
        ThrowIfDisposed();

        lock (_statisticsLock)
        {
            _statistics.TotalRoutes = 0;
            _statistics.SuccessfulRoutes = 0;
            _statistics.FailedRoutes = 0;
            _statistics.AverageRoutingTime = TimeSpan.Zero;
            _statistics.MaxRoutingTime = TimeSpan.Zero;
            _statistics.RuleMatchCounts.Clear();
            _statistics.ConnectionSelectionCounts.Clear();
            _statistics.LastUpdatedAt = DateTime.UtcNow;
        }

        _logger.LogInformation("重置路由统计信息: {RouterName}", Name);
    }

    /// <summary>
    /// 添加默认规则
    /// </summary>
    private void AddDefaultRules()
    {
        // 基于标签的路由规则
        var tagBasedRule = new RoutingRule
        {
            Name = "TagBasedRouting",
            Description = "基于连接标签的路由",
            Priority = 10,
            Strategy = RoutingStrategy.TagBased,
            Selector = SelectByTag
        };

        // 基于区域的路由规则
        var regionBasedRule = new RoutingRule
        {
            Name = "RegionBasedRouting",
            Description = "基于区域的路由",
            Priority = 20,
            Strategy = RoutingStrategy.RegionBased,
            Selector = SelectByRegion
        };

        // 基于负载的路由规则
        var loadBasedRule = new RoutingRule
        {
            Name = "LoadBasedRouting",
            Description = "基于负载的路由",
            Priority = 30,
            Strategy = RoutingStrategy.LoadBased,
            Selector = SelectByLoad
        };

        if (_rules.TryAdd(tagBasedRule.Id, tagBasedRule))
        {
            _logger.LogDebug("添加默认标签路由规则");
        }

        if (_rules.TryAdd(regionBasedRule.Id, regionBasedRule))
        {
            _logger.LogDebug("添加默认区域路由规则");
        }

        if (_rules.TryAdd(loadBasedRule.Id, loadBasedRule))
        {
            _logger.LogDebug("添加默认负载路由规则");
        }
    }

    /// <summary>
    /// 尝试匹配规则
    /// </summary>
    private async Task<RoutingResult?> TryMatchRule(RoutingRule rule, RoutingContext context, IReadOnlyList<IConnection> availableConnections, TimeSpan elapsed)
    {
        try
        {
            // 检查规则条件
            if (!await EvaluateConditions(rule, context))
            {
                return null;
            }

            // 过滤连接
            var filteredConnections = rule.Filter != null
                ? availableConnections.Where(c => rule.Filter(c, context)).ToList()
                : availableConnections.ToList();

            if (filteredConnections.Count == 0)
            {
                return null;
            }

            // 选择连接
            var selectedConnection = rule.Selector(filteredConnections, context);
            if (selectedConnection == null)
            {
                return null;
            }

            // 更新规则统计
            UpdateRuleStatistics(rule, true, elapsed);

            return RoutingResult.Success(
                selectedConnection,
                rule,
                availableConnections.Count,
                elapsed,
                $"匹配规则: {rule.Name}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "匹配路由规则时发生错误: {RuleId}", rule.Id);
            UpdateRuleStatistics(rule, false, elapsed);
            return null;
        }
    }

    /// <summary>
    /// 评估规则条件
    /// </summary>
    private async Task<bool> EvaluateConditions(RoutingRule rule, RoutingContext context)
    {
        if (rule.Conditions.Count == 0)
        {
            return true;
        }

        var results = new List<bool>();

        foreach (var condition in rule.Conditions)
        {
            var result = await EvaluateCondition(condition, context);
            results.Add(result);
        }

        return rule.Logic == ConditionLogic.And
            ? results.All(r => r)
            : results.Any(r => r);
    }

    /// <summary>
    /// 评估单个条件
    /// </summary>
    private async Task<bool> EvaluateCondition(RoutingCondition condition, RoutingContext context)
    {
        var actualValue = GetContextValue(condition.Name, context);
        var expectedValue = condition.ExpectedValue;

        return condition.Operator switch
        {
            RoutingOperator.Equals => CompareValues(actualValue, expectedValue, condition.CaseSensitive) == 0,
            RoutingOperator.NotEquals => CompareValues(actualValue, expectedValue, condition.CaseSensitive) != 0,
            RoutingOperator.Contains => ContainsValue(actualValue, expectedValue, condition.CaseSensitive),
            RoutingOperator.NotContains => !ContainsValue(actualValue, expectedValue, condition.CaseSensitive),
            RoutingOperator.StartsWith => StartsWithValue(actualValue, expectedValue, condition.CaseSensitive),
            RoutingOperator.EndsWith => EndsWithValue(actualValue, expectedValue, condition.CaseSensitive),
            RoutingOperator.Regex => MatchesRegex(actualValue, expectedValue),
            RoutingOperator.Exists => actualValue != null,
            RoutingOperator.NotExists => actualValue == null,
            _ => false
        };
    }

    /// <summary>
    /// 从上下文获取值
    /// </summary>
    private object? GetContextValue(string name, RoutingContext context)
    {
        return name.ToLowerInvariant() switch
        {
            "servicename" => context.ServiceName,
            "methodname" => context.MethodName,
            "clientid" => context.ClientId,
            "sessionid" => context.SessionId,
            "userid" => context.UserId,
            "usertype" => context.UserType,
            "region" => context.Region,
            "zone" => context.Zone,
            "version" => context.Version,
            _ => context.GetTag(name) ?? context.GetMetadata<object>(name)
        };
    }

    /// <summary>
    /// 比较值
    /// </summary>
    private int CompareValues(object? actual, object? expected, bool caseSensitive)
    {
        if (actual == null && expected == null) return 0;
        if (actual == null) return -1;
        if (expected == null) return 1;

        var actualStr = actual.ToString() ?? string.Empty;
        var expectedStr = expected.ToString() ?? string.Empty;

        return caseSensitive
            ? string.Compare(actualStr, expectedStr, StringComparison.Ordinal)
            : string.Compare(actualStr, expectedStr, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查包含
    /// </summary>
    private bool ContainsValue(object? actual, object? expected, bool caseSensitive)
    {
        if (actual == null || expected == null) return false;

        var actualStr = actual.ToString() ?? string.Empty;
        var expectedStr = expected.ToString() ?? string.Empty;

        return actualStr.Contains(expectedStr, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查开始
    /// </summary>
    private bool StartsWithValue(object? actual, object? expected, bool caseSensitive)
    {
        if (actual == null || expected == null) return false;

        var actualStr = actual.ToString() ?? string.Empty;
        var expectedStr = expected.ToString() ?? string.Empty;

        return actualStr.StartsWith(expectedStr, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查结束
    /// </summary>
    private bool EndsWithValue(object? actual, object? expected, bool caseSensitive)
    {
        if (actual == null || expected == null) return false;

        var actualStr = actual.ToString() ?? string.Empty;
        var expectedStr = expected.ToString() ?? string.Empty;

        return actualStr.EndsWith(expectedStr, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 正则表达式匹配
    /// </summary>
    private bool MatchesRegex(object? actual, object? expected)
    {
        if (actual == null || expected == null) return false;

        var actualStr = actual.ToString() ?? string.Empty;
        var pattern = expected.ToString() ?? string.Empty;

        try
        {
            return Regex.IsMatch(actualStr, pattern);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 基于标签选择连接
    /// </summary>
    private IConnection? SelectByTag(IReadOnlyList<IConnection> connections, RoutingContext context)
    {
        // 简单实现：选择第一个匹配标签的连接
        foreach (var connection in connections)
        {
            foreach (var tag in context.Tags)
            {
                if (connection.Tags.TryGetValue(tag.Key, out var value) && value == tag.Value)
                {
                    return connection;
                }
            }
        }

        return connections.FirstOrDefault();
    }

    /// <summary>
    /// 基于区域选择连接
    /// </summary>
    private IConnection? SelectByRegion(IReadOnlyList<IConnection> connections, RoutingContext context)
    {
        if (string.IsNullOrEmpty(context.Region))
        {
            return connections.FirstOrDefault();
        }

        // 优先选择相同区域的连接
        var sameRegionConnections = connections.Where(c =>
            c.Tags.TryGetValue("region", out var region) && region == context.Region).ToList();

        return sameRegionConnections.FirstOrDefault() ?? connections.FirstOrDefault();
    }

    /// <summary>
    /// 基于负载选择连接
    /// </summary>
    private IConnection? SelectByLoad(IReadOnlyList<IConnection> connections, RoutingContext context)
    {
        // 简单实现：选择负载最轻的连接（基于连接数）
        return connections.OrderBy(c => c.Statistics?.ActiveRequests ?? 0).FirstOrDefault();
    }

    /// <summary>
    /// 更新规则统计
    /// </summary>
    private void UpdateRuleStatistics(RoutingRule rule, bool success, TimeSpan elapsed)
    {
        rule.Statistics.TotalMatches++;
        if (success)
        {
            rule.Statistics.SuccessfulRoutes++;
        }
        else
        {
            rule.Statistics.FailedRoutes++;
        }

        rule.Statistics.LastMatchedAt = DateTime.UtcNow;

        // 更新平均时间
        var totalTime = rule.Statistics.AverageRoutingTime.TotalMilliseconds * (rule.Statistics.TotalMatches - 1) + elapsed.TotalMilliseconds;
        rule.Statistics.AverageRoutingTime = TimeSpan.FromMilliseconds(totalTime / rule.Statistics.TotalMatches);

        if (elapsed > rule.Statistics.MaxRoutingTime)
        {
            rule.Statistics.MaxRoutingTime = elapsed;
        }
    }

    /// <summary>
    /// 记录路由结果
    /// </summary>
    private async Task RecordRoutingResult(string key, RoutingContext context, RoutingResult result)
    {
        lock (_statisticsLock)
        {
            _statistics.TotalRoutes++;

            if (result.IsSuccess)
            {
                _statistics.SuccessfulRoutes++;

                if (result.MatchedRule != null)
                {
                    _statistics.RuleMatchCounts.TryGetValue(result.MatchedRule.Id, out var count);
                    _statistics.RuleMatchCounts[result.MatchedRule.Id] = count + 1;
                }

                if (result.SelectedConnection != null)
                {
                    _statistics.ConnectionSelectionCounts.TryGetValue(result.SelectedConnection.Id, out var count);
                    _statistics.ConnectionSelectionCounts[result.SelectedConnection.Id] = count + 1;
                }
            }
            else
            {
                _statistics.FailedRoutes++;
            }

            // 更新平均时间
            var totalTime = _statistics.AverageRoutingTime.TotalMilliseconds * (_statistics.TotalRoutes - 1) + result.RoutingTime.TotalMilliseconds;
            _statistics.AverageRoutingTime = TimeSpan.FromMilliseconds(totalTime / _statistics.TotalRoutes);

            if (result.RoutingTime > _statistics.MaxRoutingTime)
            {
                _statistics.MaxRoutingTime = result.RoutingTime;
            }

            _statistics.LastUpdatedAt = DateTime.UtcNow;
        }

        // 触发路由事件
        OnRouted(new RoutingEventArgs(key, context, result));
    }

    /// <summary>
    /// 触发规则变更事件
    /// </summary>
    private void OnRuleChanged(RoutingRuleChangedEventArgs e)
    {
        try
        {
            RuleChanged?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发路由规则变更事件时发生错误");
        }
    }

    /// <summary>
    /// 触发路由事件
    /// </summary>
    private void OnRouted(RoutingEventArgs e)
    {
        try
        {
            Routed?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发路由事件时发生错误");
        }
    }

    /// <summary>
    /// 检查是否已释放
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ConnectionRouter));
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (_isStarted)
            {
                StopAsync().Wait(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止连接路由器时发生错误");
        }

        _rules.Clear();

        _logger.LogInformation("连接路由器已释放: {RouterName}", Name);
    }
}
