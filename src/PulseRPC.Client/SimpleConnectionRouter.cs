using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using PulseRPC.Messaging;

namespace PulseRPC.Client;

/// <summary>
/// 简单连接路由器实现 - Stage 1 基础版本
/// </summary>
public sealed class SimpleConnectionRouter : IConnectionRouter
{
    private readonly ILogger<SimpleConnectionRouter> _logger;
    private readonly IConnectionRegistry _connectionRegistry;
    private readonly ConcurrentDictionary<string, RoutingRule> _rules = new();
    private readonly object _lock = new();

    /// <summary>
    /// 构造函数
    /// </summary>
    public SimpleConnectionRouter(IConnectionRegistry connectionRegistry, ILogger<SimpleConnectionRouter>? logger = null)
    {
        _connectionRegistry = connectionRegistry ?? throw new ArgumentNullException(nameof(connectionRegistry));
        _logger = logger ?? NullLogger<SimpleConnectionRouter>.Instance;
    }

    /// <summary>
    /// 注册路由规则
    /// </summary>
    public void RegisterRule(RoutingRule rule)
    {
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        if (string.IsNullOrEmpty(rule.Id))
            throw new ArgumentException("路由规则ID不能为空", nameof(rule));

        _rules.AddOrUpdate(rule.Id, rule, (_, _) => rule);
        _logger.LogDebug("注册路由规则: {RuleId} ({RuleName})", rule.Id, rule.Name);
    }

    /// <summary>
    /// 移除路由规则
    /// </summary>
    public bool RemoveRule(string ruleId)
    {
        if (string.IsNullOrEmpty(ruleId))
            return false;

        var removed = _rules.TryRemove(ruleId, out _);
        if (removed)
        {
            _logger.LogDebug("移除路由规则: {RuleId}", ruleId);
        }
        return removed;
    }

    /// <summary>
    /// 路由到最佳连接
    /// </summary>
    public async Task<IClientChannel> RouteAsync(string routingKey, RoutingContext? context = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(routingKey))
            throw new ArgumentException("路由键不能为空", nameof(routingKey));

        // 获取所有匹配的连接
        var matchingConnections = GetMatchingConnections(routingKey, context);
        if (matchingConnections.Count == 0)
        {
            throw new InvalidOperationException($"未找到匹配的连接: {routingKey}");
        }

        // 应用路由规则选择最佳连接
        var selectedConnection = ApplyRoutingRules(routingKey, matchingConnections, context);
        if (selectedConnection == null)
        {
            // 如果没有规则匹配，默认选择第一个可用连接
            selectedConnection = matchingConnections.FirstOrDefault(c =>
                c.State == ExtendedConnectionState.Connected ||
                c.State == ExtendedConnectionState.Active);
        }

        if (selectedConnection == null)
        {
            throw new InvalidOperationException($"没有可用的连接: {routingKey}");
        }

        _logger.LogDebug("路由选择连接: {RoutingKey} -> {ConnectionId}", routingKey, selectedConnection.Id);
        return await Task.FromResult(selectedConnection);
    }

    /// <summary>
    /// 获取所有匹配的连接
    /// </summary>
    public IReadOnlyList<IClientChannel> GetMatchingConnections(string routingKey, RoutingContext? context = null)
    {
        if (string.IsNullOrEmpty(routingKey))
            return Array.Empty<IClientChannel>();

        // 尝试通过连接ID匹配
        var connectionById = _connectionRegistry.GetConnection(routingKey);
        if (connectionById != null)
        {
            _logger.LogDebug("通过连接ID找到连接: {ConnectionId}", routingKey);
            return new[] { connectionById };
        }

        // 如果没有找到，返回所有连接作为后备选项
        var allConnections = _connectionRegistry.GetAllConnections();
        if (allConnections.Count > 0)
        {
            // 过滤出健康的连接
            var healthyConnections = allConnections
                .Where(c => c.State == ExtendedConnectionState.Connected || c.State == ExtendedConnectionState.Active)
                .ToList();

            if (healthyConnections.Count > 0)
            {
                _logger.LogDebug("使用所有健康连接作为后备选项，连接数: {Count}", healthyConnections.Count);
                return healthyConnections;
            }

            _logger.LogDebug("使用所有连接作为后备选项，连接数: {Count}", allConnections.Count);
            return allConnections;
        }

        _logger.LogWarning("未找到匹配的连接: {RoutingKey}", routingKey);
        return Array.Empty<IClientChannel>();
    }

    /// <summary>
    /// 应用路由规则选择连接
    /// </summary>
    private IClientChannel? ApplyRoutingRules(string routingKey, IReadOnlyList<IClientChannel> connections, RoutingContext? context)
    {
        if (connections.Count == 0)
            return null;

        // 获取所有启用的规则，按优先级排序
        var enabledRules = _rules.Values
            .Where(r => r.Enabled)
            .OrderByDescending(r => r.Priority)
            .ToList();

        // 遍历规则，找到第一个匹配的
        foreach (var rule in enabledRules)
        {
            try
            {
                if (rule.Matcher(routingKey, context))
                {
                    var selectedConnection = rule.Selector(connections, context);
                    if (selectedConnection != null)
                    {
                        _logger.LogDebug("应用路由规则: {RuleId} 选择连接: {ConnectionId}",
                            rule.Id, selectedConnection.Id);
                        return selectedConnection;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "路由规则执行失败: {RuleId}", rule.Id);
            }
        }

        return null;
    }

    /// <summary>
    /// 添加默认路由规则
    /// </summary>
    public void AddDefaultRules()
    {
        // 服务名称匹配规则
        RegisterRule(new RoutingRule
        {
            Id = "service-name-match",
            Name = "服务名称匹配",
            Priority = 100,
            Matcher = (routingKey, context) =>
            {
                // 如果路由键包含服务名称，尝试匹配
                return !string.IsNullOrEmpty(routingKey);
            },
            Selector = (connections, context) =>
            {
                // 选择第一个健康的连接
                return connections.FirstOrDefault(c =>
                    c.State == ExtendedConnectionState.Connected ||
                    c.State == ExtendedConnectionState.Active);
            }
        });

        // 负载均衡规则
        RegisterRule(new RoutingRule
        {
            Id = "load-balance",
            Name = "负载均衡",
            Priority = 50,
            Matcher = (_, _) => true, // 总是匹配
            Selector = (connections, context) =>
            {
                var healthyConnections = connections.Where(c =>
                    c.State == ExtendedConnectionState.Connected ||
                    c.State == ExtendedConnectionState.Active).ToList();

                if (healthyConnections.Count == 0)
                    return null;

                // 简单轮询选择
                var index = Environment.TickCount % healthyConnections.Count;
                return healthyConnections[index];
            }
        });

        _logger.LogInformation("添加默认路由规则完成");
    }
}
