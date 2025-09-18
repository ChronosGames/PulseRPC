using Microsoft.Extensions.Logging;
using PulseRPC.Client.ServiceDiscovery;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace PulseRPC.Client.LoadBalancing;

/// <summary>
/// 随机负载均衡器
/// </summary>
public sealed class RandomLoadBalancer : LoadBalancerBase
{
    private readonly Random _random;
    private readonly bool _useSecureRandom;

    /// <summary>
    /// 负载均衡器名称
    /// </summary>
    public override string Name => "Random";

    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public override LoadBalancingStrategy Strategy => LoadBalancingStrategy.Random;

    /// <summary>
    /// 构造函数
    /// </summary>
    public RandomLoadBalancer(
        LoadBalancingConfiguration? configuration = null,
        ILogger<RandomLoadBalancer>? logger = null,
        bool useSecureRandom = false)
        : base(configuration, logger)
    {
        _useSecureRandom = useSecureRandom;

        if (useSecureRandom)
        {
            // 使用安全随机数生成器
            _random = new Random(GenerateSecureRandomSeed());
        }
        else
        {
            // 使用时间戳作为种子
            _random = new Random((int)DateTime.UtcNow.Ticks);
        }

        _logger.LogDebug("随机负载均衡器已创建 (安全随机: {UseSecureRandom})", useSecureRandom);
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

        // 生成随机索引
        int selectedIndex;
        lock (_random) // Random 不是线程安全的
        {
            selectedIndex = _random.Next(0, eligibleInstances.Count);
        }

        var selectedInstance = eligibleInstances[selectedIndex];

        var result = LoadBalancingResult.Success(
            selectedInstance,
            $"随机选择 (索引: {selectedIndex})",
            eligibleInstances.Count);

        // 添加扩展数据
        result.ExtendedData["RandomIndex"] = selectedIndex;
        result.ExtendedData["UseSecureRandom"] = _useSecureRandom;

        _logger.LogTrace("随机选择实例: {InstanceId} (索引: {Index}/{Total})",
            selectedInstance.Id, selectedIndex, eligibleInstances.Count);

        return Task.FromResult(result);
    }

    /// <summary>
    /// 生成安全随机种子
    /// </summary>
    private static int GenerateSecureRandomSeed()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[4];
        rng.GetBytes(bytes);
        return BitConverter.ToInt32(bytes, 0);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();
        // Random 类不需要显式释放资源
    }
}
