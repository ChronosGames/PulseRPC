using Microsoft.Extensions.Logging;
using PulseRPC.Client.ServiceDiscovery;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PulseRPC.Client.LoadBalancing;

/// <summary>
/// 最少连接数负载均衡器
/// </summary>
public sealed class LeastConnectionsLoadBalancer : LoadBalancerBase
{
    /// <summary>
    /// 负载均衡器名称
    /// </summary>
    public override string Name => "LeastConnections";

    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public override LoadBalancingStrategy Strategy => LoadBalancingStrategy.LeastConnections;

    /// <summary>
    /// 构造函数
    /// </summary>
    public LeastConnectionsLoadBalancer(
        LoadBalancingConfiguration? configuration = null,
        ILogger<LeastConnectionsLoadBalancer>? logger = null)
        : base(configuration, logger)
    { }

    /// <summary>
    /// 内部选择实例实现
    /// </summary>
    protected override Task<LoadBalancingResult> SelectInstanceInternalAsync(
        LoadBalancingContext context,
        IReadOnlyList<ServiceInstance> eligibleInstances)
    {
        if (eligibleInstances.Count == 0)
        {
            return Task.FromResult(LoadBalancingResult.Failure("没有符合条件的实例", 0));
        }

        if (eligibleInstances.Count == 1)
        {
            var singleInstance = eligibleInstances[0];
            return Task.FromResult(LoadBalancingResult.Success(
                singleInstance,
                "唯一可用实例",
                eligibleInstances.Count));
        }

        // 选择连接数最少的实例
        ServiceInstance? selectedInstance = null;
        int minConnections = int.MaxValue;
        int selectedIndex = -1;
        string reason = "";

        for (int i = 0; i < eligibleInstances.Count; i++)
        {
            var instance = eligibleInstances[i];
            var connections = GetCurrentConnections(instance.Id);

            if (connections < minConnections)
            {
                minConnections = connections;
                selectedInstance = instance;
                selectedIndex = i;
            }
            else if (connections == minConnections && selectedInstance != null)
            {
                // 如果连接数相同，可以使用其他因素进行选择
                if (ShouldPreferInstance(instance, selectedInstance))
                {
                    selectedInstance = instance;
                    selectedIndex = i;
                }
            }
        }

        if (selectedInstance == null)
        {
            return Task.FromResult(LoadBalancingResult.Failure("无法选择实例", eligibleInstances.Count));
        }

        reason = $"最少连接数选择 (连接数: {minConnections}, 索引: {selectedIndex})";

        var result = LoadBalancingResult.Success(selectedInstance, reason, eligibleInstances.Count);

        // 添加扩展数据
        result.ExtendedData["LeastConnectionsIndex"] = selectedIndex;
        result.ExtendedData["MinConnections"] = minConnections;
        result.ExtendedData["ConnectionDistribution"] = GetConnectionDistribution(eligibleInstances);

        _logger.LogTrace("最少连接数选择实例: {InstanceId} (连接数: {Connections}, 索引: {Index}/{Total})",
            selectedInstance.Id, minConnections, selectedIndex, eligibleInstances.Count);

        return Task.FromResult(result);
    }

    /// <summary>
    /// 获取当前连接数
    /// </summary>
    private int GetCurrentConnections(string instanceId)
    {
        if (_instanceHealth.TryGetValue(instanceId, out var healthInfo))
        {
            return healthInfo.CurrentConnections;
        }

        return 0;
    }

    /// <summary>
    /// 当连接数相同时，判断是否应该优先选择某个实例
    /// </summary>
    private bool ShouldPreferInstance(ServiceInstance candidate, ServiceInstance current)
    {
        // 优先选择权重更高的实例
        if (candidate.Weight != current.Weight)
        {
            return candidate.Weight > current.Weight;
        }

        // 优先选择响应时间更短的实例
        if (_instanceHealth.TryGetValue(candidate.Id, out var candidateHealth) &&
            _instanceHealth.TryGetValue(current.Id, out var currentHealth))
        {
            if (Math.Abs(candidateHealth.AverageResponseTime - currentHealth.AverageResponseTime) > 1.0) // 1ms差异
            {
                return candidateHealth.AverageResponseTime < currentHealth.AverageResponseTime;
            }

            // 优先选择错误率更低的实例
            if (Math.Abs(candidateHealth.ErrorRate - currentHealth.ErrorRate) > 1.0) // 1%差异
            {
                return candidateHealth.ErrorRate < currentHealth.ErrorRate;
            }

            // 优先选择负载评分更低的实例
            return candidateHealth.LoadScore < currentHealth.LoadScore;
        }

        // 最后按实例ID排序（确保一致性）
        return string.Compare(candidate.Id, current.Id, StringComparison.Ordinal) < 0;
    }

    /// <summary>
    /// 获取连接分布信息
    /// </summary>
    private Dictionary<string, int> GetConnectionDistribution(IReadOnlyList<ServiceInstance> instances)
    {
        var distribution = new Dictionary<string, int>();

        foreach (var instance in instances)
        {
            distribution[instance.Id] = GetCurrentConnections(instance.Id);
        }

        return distribution;
    }

    /// <summary>
    /// 报告请求结果时更新连接数
    /// </summary>
    public override async Task ReportRequestResultAsync(string instanceId, bool success, TimeSpan responseTime, System.Threading.CancellationToken cancellationToken = default)
    {
        await base.ReportRequestResultAsync(instanceId, success, responseTime, cancellationToken);

        // 对于最少连接数算法，连接数的管理通常由外部系统负责
        // 这里可以记录一些额外的统计信息
        _logger.LogTrace("请求结果报告: {InstanceId}, 成功: {Success}, 响应时间: {ResponseTime}ms, 当前连接数: {Connections}",
            instanceId, success, responseTime.TotalMilliseconds, GetCurrentConnections(instanceId));
    }

    /// <summary>
    /// 获取负载均衡统计信息（扩展版本）
    /// </summary>
    public async Task<LeastConnectionsStatistics> GetLeastConnectionsStatisticsAsync(System.Threading.CancellationToken cancellationToken = default)
    {
        var baseStats = await GetStatisticsAsync(cancellationToken);

        var connectionStats = new Dictionary<string, ConnectionStatistics>();

        foreach (var kvp in _instanceHealth)
        {
            var healthInfo = kvp.Value;
            connectionStats[kvp.Key] = new ConnectionStatistics
            {
                InstanceId = kvp.Key,
                CurrentConnections = healthInfo.CurrentConnections,
                AverageResponseTime = healthInfo.AverageResponseTime,
                ErrorRate = healthInfo.ErrorRate,
                LoadScore = healthInfo.LoadScore,
                Weight = healthInfo.Weight
            };
        }

        return new LeastConnectionsStatistics
        {
            BaseStatistics = baseStats,
            ConnectionStatistics = connectionStats,
            TotalActiveConnections = connectionStats.Values.Sum(c => c.CurrentConnections),
            AverageConnectionsPerInstance = connectionStats.Count > 0
                ? (double)connectionStats.Values.Sum(c => c.CurrentConnections) / connectionStats.Count
                : 0,
            MaxConnectionsPerInstance = connectionStats.Values.Any()
                ? connectionStats.Values.Max(c => c.CurrentConnections)
                : 0,
            MinConnectionsPerInstance = connectionStats.Values.Any()
                ? connectionStats.Values.Min(c => c.CurrentConnections)
                : 0
        };
    }
}

/// <summary>
/// 连接统计信息
/// </summary>
public sealed class ConnectionStatistics
{
    /// <summary>
    /// 实例ID
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 当前连接数
    /// </summary>
    public int CurrentConnections { get; set; }

    /// <summary>
    /// 平均响应时间
    /// </summary>
    public double AverageResponseTime { get; set; }

    /// <summary>
    /// 错误率
    /// </summary>
    public double ErrorRate { get; set; }

    /// <summary>
    /// 负载评分
    /// </summary>
    public double LoadScore { get; set; }

    /// <summary>
    /// 权重
    /// </summary>
    public int Weight { get; set; }
}

/// <summary>
/// 最少连接数负载均衡统计信息
/// </summary>
public sealed class LeastConnectionsStatistics
{
    /// <summary>
    /// 基础统计信息
    /// </summary>
    public LoadBalancingStatistics BaseStatistics { get; set; } = new();

    /// <summary>
    /// 连接统计信息
    /// </summary>
    public Dictionary<string, ConnectionStatistics> ConnectionStatistics { get; set; } = new();

    /// <summary>
    /// 总活跃连接数
    /// </summary>
    public int TotalActiveConnections { get; set; }

    /// <summary>
    /// 每个实例平均连接数
    /// </summary>
    public double AverageConnectionsPerInstance { get; set; }

    /// <summary>
    /// 每个实例最大连接数
    /// </summary>
    public int MaxConnectionsPerInstance { get; set; }

    /// <summary>
    /// 每个实例最小连接数
    /// </summary>
    public int MinConnectionsPerInstance { get; set; }

    /// <summary>
    /// 连接数分布方差（用于衡量负载均衡效果）
    /// </summary>
    public double ConnectionDistributionVariance
    {
        get
        {
            if (!ConnectionStatistics.Any()) return 0;

            var connections = ConnectionStatistics.Values.Select(c => (double)c.CurrentConnections).ToArray();
            var mean = connections.Average();
            var variance = connections.Sum(x => Math.Pow(x - mean, 2)) / connections.Length;
            return variance;
        }
    }

    /// <summary>
    /// 负载均衡效果评分（0-1，1为最佳）
    /// </summary>
    public double LoadBalancingEffectiveness
    {
        get
        {
            if (!ConnectionStatistics.Any()) return 1.0;

            var variance = ConnectionDistributionVariance;
            var maxPossibleVariance = Math.Pow(MaxConnectionsPerInstance, 2);

            if (maxPossibleVariance == 0) return 1.0;

            return Math.Max(0, 1.0 - variance / maxPossibleVariance);
        }
    }
}
