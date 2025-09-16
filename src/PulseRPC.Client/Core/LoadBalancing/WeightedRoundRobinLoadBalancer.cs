using Microsoft.Extensions.Logging;
using PulseRPC.Client.Core.ServiceDiscovery;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Core.LoadBalancing;

/// <summary>
/// 加权轮询负载均衡器
/// </summary>
public sealed class WeightedRoundRobinLoadBalancer : LoadBalancerBase
{
    private readonly object _weightLock = new();
    private volatile WeightedInstanceList _weightedInstances = new(Array.Empty<WeightedInstance>());

    /// <summary>
    /// 负载均衡器名称
    /// </summary>
    public override string Name => "WeightedRoundRobin";

    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public override LoadBalancingStrategy Strategy => LoadBalancingStrategy.WeightedRoundRobin;

    /// <summary>
    /// 构造函数
    /// </summary>
    public WeightedRoundRobinLoadBalancer(
        LoadBalancingConfiguration? configuration = null,
        ILogger<WeightedRoundRobinLoadBalancer>? logger = null)
        : base(configuration, logger)
    {
    }

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

        // 构建加权实例列表
        var weightedInstances = BuildWeightedInstances(eligibleInstances);
        if (weightedInstances.Count == 0)
        {
            return Task.FromResult(LoadBalancingResult.Failure("所有实例权重为零", eligibleInstances.Count));
        }

        ServiceInstance selectedInstance;
        string reason;
        int selectedIndex = -1;

        lock (_weightLock)
        {
            // 使用平滑加权轮询算法
            selectedInstance = SelectByWeightedRoundRobin(weightedInstances, out selectedIndex, out reason);
        }

        var result = LoadBalancingResult.Success(selectedInstance, reason, eligibleInstances.Count);

        // 添加扩展数据
        result.ExtendedData["WeightedIndex"] = selectedIndex;
        result.ExtendedData["TotalWeight"] = weightedInstances.Sum(w => w.Weight);
        result.ExtendedData["SelectedWeight"] = selectedInstance.Weight;

        _logger.LogTrace("加权轮询选择实例: {InstanceId} (权重: {Weight}, 索引: {Index})",
            selectedInstance.Id, selectedInstance.Weight, selectedIndex);

        return Task.FromResult(result);
    }

    /// <summary>
    /// 更新实例列表时重建加权列表
    /// </summary>
    public override async Task UpdateInstancesAsync(IReadOnlyList<ServiceInstance> instances, CancellationToken cancellationToken = default)
    {
        await base.UpdateInstancesAsync(instances, cancellationToken);

        // 重建加权实例列表
        lock (_weightLock)
        {
            var weightedInstances = BuildWeightedInstances(instances);
            _weightedInstances = new WeightedInstanceList(weightedInstances);
        }

        _logger.LogDebug("加权实例列表已更新: {Count} 个实例", instances.Count);
    }

    /// <summary>
    /// 重置统计信息时也重置权重状态
    /// </summary>
    public override async Task ResetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await base.ResetStatisticsAsync(cancellationToken);

        lock (_weightLock)
        {
            // 重置所有实例的当前权重
            foreach (var weightedInstance in _weightedInstances.Instances)
            {
                weightedInstance.CurrentWeight = 0;
            }
        }

        _logger.LogDebug("加权轮询权重状态已重置");
    }

    /// <summary>
    /// 构建加权实例列表
    /// </summary>
    private List<WeightedInstance> BuildWeightedInstances(IReadOnlyList<ServiceInstance> instances)
    {
        var weightedInstances = new List<WeightedInstance>();

        foreach (var instance in instances)
        {
            var weight = instance.Weight;

            // 从健康信息获取动态权重调整
            if (_instanceHealth.TryGetValue(instance.Id, out var healthInfo))
            {
                // 根据健康状态调整权重
                weight = AdjustWeightByHealth(weight, healthInfo);
            }

            if (weight > 0)
            {
                weightedInstances.Add(new WeightedInstance(instance, weight));
            }
        }

        return weightedInstances;
    }

    /// <summary>
    /// 根据健康状态调整权重
    /// </summary>
    private int AdjustWeightByHealth(int originalWeight, InstanceHealthInfo healthInfo)
    {
        var adjustedWeight = originalWeight;

        // 根据健康状态调整
        switch (healthInfo.HealthStatus)
        {
            case ServiceInstanceStatus.Unhealthy:
                return 0; // 不健康的实例权重为0

            case ServiceInstanceStatus.Unknown:
                adjustedWeight = Math.Max(1, adjustedWeight / 4); // 未知状态降低到1/4
                break;

            case ServiceInstanceStatus.Maintenance:
                adjustedWeight = Math.Max(1, adjustedWeight / 2); // 维护状态降低到1/2
                break;
        }

        // 根据错误率调整权重
        var errorRate = healthInfo.ErrorRate;
        if (errorRate > 0)
        {
            var errorAdjustment = 1.0 - (errorRate / 100.0 * 0.5); // 错误率每1%减少0.5%权重
            adjustedWeight = Math.Max(1, (int)(adjustedWeight * errorAdjustment));
        }

        // 根据响应时间调整权重
        if (healthInfo.AverageResponseTime > _configuration.ResponseTimeThreshold.TotalMilliseconds)
        {
            var responseTimeRatio = healthInfo.AverageResponseTime / _configuration.ResponseTimeThreshold.TotalMilliseconds;
            var responseTimeAdjustment = 1.0 / Math.Min(responseTimeRatio, 3.0); // 最多降低到1/3
            adjustedWeight = Math.Max(1, (int)(adjustedWeight * responseTimeAdjustment));
        }

        return adjustedWeight;
    }

    /// <summary>
    /// 使用平滑加权轮询算法选择实例
    /// </summary>
    private ServiceInstance SelectByWeightedRoundRobin(List<WeightedInstance> weightedInstances, out int selectedIndex, out string reason)
    {
        if (weightedInstances.Count == 1)
        {
            selectedIndex = 0;
            reason = "唯一加权实例";
            return weightedInstances[0].Instance;
        }

        var totalWeight = weightedInstances.Sum(w => w.Weight);
        WeightedInstance? selectedWeightedInstance = null;
        selectedIndex = -1;

        // 平滑加权轮询算法 (Nginx 风格)
        for (int i = 0; i < weightedInstances.Count; i++)
        {
            var weightedInstance = weightedInstances[i];

            // 增加当前权重
            weightedInstance.CurrentWeight += weightedInstance.Weight;

            // 选择当前权重最大的实例
            if (selectedWeightedInstance == null || weightedInstance.CurrentWeight > selectedWeightedInstance.CurrentWeight)
            {
                selectedWeightedInstance = weightedInstance;
                selectedIndex = i;
            }
        }

        // 减少选中实例的当前权重
        if (selectedWeightedInstance != null)
        {
            selectedWeightedInstance.CurrentWeight -= totalWeight;
            reason = $"加权轮询 (权重: {selectedWeightedInstance.Weight}, 当前权重: {selectedWeightedInstance.CurrentWeight + totalWeight})";
            return selectedWeightedInstance.Instance;
        }

        // 这种情况理论上不应该发生
        throw new InvalidOperationException("加权轮询选择失败：无法选择实例");
    }
}

/// <summary>
/// 加权实例
/// </summary>
internal sealed class WeightedInstance
{
    /// <summary>
    /// 服务实例
    /// </summary>
    public ServiceInstance Instance { get; }

    /// <summary>
    /// 权重
    /// </summary>
    public int Weight { get; }

    /// <summary>
    /// 当前权重（用于平滑加权轮询算法）
    /// </summary>
    public int CurrentWeight { get; set; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public WeightedInstance(ServiceInstance instance, int weight)
    {
        Instance = instance;
        Weight = weight;
        CurrentWeight = 0;
    }

    /// <summary>
    /// 转换为字符串
    /// </summary>
    public override string ToString()
    {
        return $"{Instance.Id} (权重: {Weight}, 当前权重: {CurrentWeight})";
    }
}

/// <summary>
/// 加权实例列表
/// </summary>
internal sealed class WeightedInstanceList
{
    /// <summary>
    /// 加权实例列表
    /// </summary>
    public IReadOnlyList<WeightedInstance> Instances { get; }

    /// <summary>
    /// 总权重
    /// </summary>
    public int TotalWeight { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public WeightedInstanceList(IReadOnlyList<WeightedInstance> instances)
    {
        Instances = instances;
        TotalWeight = instances.Sum(i => i.Weight);
    }

    /// <summary>
    /// 实例数量
    /// </summary>
    public int Count => Instances.Count;
}