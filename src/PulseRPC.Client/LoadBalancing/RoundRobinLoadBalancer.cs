using Microsoft.Extensions.Logging;
using PulseRPC.Client.Core.ServiceDiscovery;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Core.LoadBalancing;

/// <summary>
/// 轮询负载均衡器
/// </summary>
public sealed class RoundRobinLoadBalancer : LoadBalancerBase
{
    private long _currentIndex = -1;

    /// <summary>
    /// 负载均衡器名称
    /// </summary>
    public override string Name => "RoundRobin";

    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public override LoadBalancingStrategy Strategy => LoadBalancingStrategy.RoundRobin;

    /// <summary>
    /// 构造函数
    /// </summary>
    public RoundRobinLoadBalancer(
        LoadBalancingConfiguration? configuration = null,
        ILogger<RoundRobinLoadBalancer>? logger = null)
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

        // 原子递增索引并获取下一个实例
        var nextIndex = Interlocked.Increment(ref _currentIndex);
        var selectedIndex = (int)(nextIndex % eligibleInstances.Count);
        var selectedInstance = eligibleInstances[selectedIndex];

        var result = LoadBalancingResult.Success(
            selectedInstance,
            $"轮询选择 (索引: {selectedIndex})",
            eligibleInstances.Count);

        // 添加扩展数据
        result.ExtendedData["RoundRobinIndex"] = selectedIndex;
        result.ExtendedData["CurrentSequence"] = nextIndex;

        _logger.LogTrace("轮询选择实例: {InstanceId} (索引: {Index}/{Total})",
            selectedInstance.Id, selectedIndex, eligibleInstances.Count);

        return Task.FromResult(result);
    }

    /// <summary>
    /// 重置统计信息时也重置索引
    /// </summary>
    public override async Task ResetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await base.ResetStatisticsAsync(cancellationToken);

        // 重置轮询索引
        Interlocked.Exchange(ref _currentIndex, -1);

        _logger.LogDebug("轮询索引已重置");
    }

    /// <summary>
    /// 更新实例列表时调整索引
    /// </summary>
    public override async Task UpdateInstancesAsync(IReadOnlyList<ServiceInstance> instances, CancellationToken cancellationToken = default)
    {
        await base.UpdateInstancesAsync(instances, cancellationToken);

        // 如果实例数量减少，可能需要调整当前索引
        if (instances.Count > 0)
        {
            var currentIndex = Interlocked.Read(ref _currentIndex);
            if (currentIndex >= instances.Count)
            {
                // 将索引重置为安全值
                Interlocked.Exchange(ref _currentIndex, -1);
                _logger.LogDebug("轮询索引已调整，因为实例列表大小改变");
            }
        }
        else
        {
            // 没有实例时重置索引
            Interlocked.Exchange(ref _currentIndex, -1);
        }
    }
}