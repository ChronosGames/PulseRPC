using Microsoft.Extensions.Logging;
using PulseRPC.Client.Core.ServiceDiscovery;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Core.LoadBalancing;

/// <summary>
/// 一致性哈希负载均衡器
/// </summary>
public sealed class ConsistentHashLoadBalancer : LoadBalancerBase
{
    private readonly ConsistentHashRing _hashRing = new();
    private readonly object _ringLock = new();

    /// <summary>
    /// 负载均衡器名称
    /// </summary>
    public override string Name => "ConsistentHash";

    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public override LoadBalancingStrategy Strategy => LoadBalancingStrategy.ConsistentHash;

    /// <summary>
    /// 构造函数
    /// </summary>
    public ConsistentHashLoadBalancer(
        LoadBalancingConfiguration? configuration = null,
        ILogger<ConsistentHashLoadBalancer>? logger = null)
        : base(configuration, logger)
    {
        // 应用一致性哈希配置
        if (configuration?.ConsistentHashConfiguration != null)
        {
            _hashRing.VirtualNodeCount = configuration.ConsistentHashConfiguration.VirtualNodeCount;
            _hashRing.HashFunction = configuration.ConsistentHashConfiguration.HashFunction;
            _hashRing.EnableNodeWeight = configuration.ConsistentHashConfiguration.EnableNodeWeight;
        }
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

        // 确定哈希键
        var hashKey = DetermineHashKey(context);
        if (string.IsNullOrEmpty(hashKey))
        {
            return Task.FromResult(LoadBalancingResult.Failure("无法确定哈希键", eligibleInstances.Count));
        }

        ServiceInstance? selectedInstance;
        string reason;

        lock (_ringLock)
        {
            // 确保哈希环是最新的
            UpdateHashRingIfNeeded(eligibleInstances);

            // 在哈希环中查找实例
            selectedInstance = _hashRing.GetNode(hashKey);

            if (selectedInstance == null)
            {
                return Task.FromResult(LoadBalancingResult.Failure("哈希环中未找到匹配的节点", eligibleInstances.Count));
            }

            // 验证选中的实例是否在符合条件的实例列表中
            if (!eligibleInstances.Any(i => i.Id == selectedInstance.Id))
            {
                // 如果选中的实例不在符合条件的列表中，需要重新选择
                var fallbackInstance = SelectFallbackInstance(eligibleInstances, hashKey);
                if (fallbackInstance != null)
                {
                    selectedInstance = fallbackInstance;
                    reason = $"一致性哈希回退选择 (原选择不可用, 哈希键: {hashKey})";
                }
                else
                {
                    return Task.FromResult(LoadBalancingResult.Failure("一致性哈希选择失败，无回退选项", eligibleInstances.Count));
                }
            }
            else
            {
                reason = $"一致性哈希选择 (哈希键: {hashKey})";
            }
        }

        var result = LoadBalancingResult.Success(selectedInstance, reason, eligibleInstances.Count);

        // 添加扩展数据
        result.ExtendedData["HashKey"] = hashKey;
        result.ExtendedData["HashValue"] = _hashRing.Hash(hashKey);
        result.ExtendedData["VirtualNodeCount"] = _hashRing.VirtualNodeCount;

        _logger.LogTrace("一致性哈希选择实例: {InstanceId} (哈希键: {HashKey}, 哈希值: {HashValue})",
            selectedInstance.Id, hashKey, result.ExtendedData["HashValue"]);

        return Task.FromResult(result);
    }

    /// <summary>
    /// 更新实例列表时重建哈希环
    /// </summary>
    public override async Task UpdateInstancesAsync(IReadOnlyList<ServiceInstance> instances, CancellationToken cancellationToken = default)
    {
        await base.UpdateInstancesAsync(instances, cancellationToken);

        lock (_ringLock)
        {
            _hashRing.Clear();
            foreach (var instance in instances)
            {
                _hashRing.AddNode(instance);
            }
        }

        _logger.LogDebug("一致性哈希环已更新: {Count} 个实例, {VirtualNodes} 个虚拟节点",
            instances.Count, _hashRing.TotalVirtualNodes);
    }

    /// <summary>
    /// 获取一致性哈希统计信息
    /// </summary>
    public async Task<ConsistentHashStatistics> GetConsistentHashStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var baseStats = await GetStatisticsAsync(cancellationToken);

        Dictionary<string, int> virtualNodeDistribution;
        Dictionary<uint, string> hashRingSnapshot;

        lock (_ringLock)
        {
            virtualNodeDistribution = _hashRing.GetVirtualNodeDistribution();
            hashRingSnapshot = _hashRing.GetRingSnapshot();
        }

        return new ConsistentHashStatistics
        {
            BaseStatistics = baseStats,
            VirtualNodeDistribution = virtualNodeDistribution,
            TotalVirtualNodes = _hashRing.TotalVirtualNodes,
            HashRingSize = hashRingSnapshot.Count,
            LoadDistributionVariance = CalculateLoadDistributionVariance(virtualNodeDistribution)
        };
    }

    /// <summary>
    /// 确定哈希键
    /// </summary>
    private string DetermineHashKey(LoadBalancingContext context)
    {
        // 优先使用显式指定的哈希键
        if (!string.IsNullOrEmpty(context.HashKey))
        {
            return context.HashKey;
        }

        // 使用会话ID作为哈希键
        if (!string.IsNullOrEmpty(context.SessionId))
        {
            return context.SessionId;
        }

        // 使用客户端ID作为哈希键
        if (!string.IsNullOrEmpty(context.ClientId))
        {
            return context.ClientId;
        }

        // 使用请求ID作为哈希键
        if (!string.IsNullOrEmpty(context.RequestId))
        {
            return context.RequestId;
        }

        // 如果都没有，尝试从自定义数据中获取
        if (context.CustomData.TryGetValue("HashKey", out var customHashKey) &&
            customHashKey is string hashKeyStr && !string.IsNullOrEmpty(hashKeyStr))
        {
            return hashKeyStr;
        }

        // 最后尝试使用服务名称（这会导致所有相同服务的请求都路由到同一个实例）
        return context.ServiceName;
    }

    /// <summary>
    /// 选择回退实例
    /// </summary>
    private ServiceInstance? SelectFallbackInstance(IReadOnlyList<ServiceInstance> eligibleInstances, string hashKey)
    {
        // 使用简单哈希算法选择回退实例
        var hash = _hashRing.Hash(hashKey);
        var index = (int)(hash % (uint)eligibleInstances.Count);
        return eligibleInstances[index];
    }

    /// <summary>
    /// 更新哈希环（如果需要）
    /// </summary>
    private void UpdateHashRingIfNeeded(IReadOnlyList<ServiceInstance> currentInstances)
    {
        // 检查哈希环中的实例是否与当前实例列表一致
        var ringInstances = _hashRing.GetAllNodes();
        var currentInstanceIds = new HashSet<string>(currentInstances.Select(i => i.Id));
        var ringInstanceIds = new HashSet<string>(ringInstances.Select(i => i.Id));

        if (!currentInstanceIds.SetEquals(ringInstanceIds))
        {
            // 重建哈希环
            _hashRing.Clear();
            foreach (var instance in currentInstances)
            {
                _hashRing.AddNode(instance);
            }
        }
    }

    /// <summary>
    /// 计算负载分布方差
    /// </summary>
    private double CalculateLoadDistributionVariance(Dictionary<string, int> virtualNodeDistribution)
    {
        if (!virtualNodeDistribution.Any()) return 0;

        var values = virtualNodeDistribution.Values.Select(v => (double)v).ToArray();
        var mean = values.Average();
        var variance = values.Sum(x => Math.Pow(x - mean, 2)) / values.Length;
        return variance;
    }
}

/// <summary>
/// 一致性哈希环
/// </summary>
internal sealed class ConsistentHashRing
{
    private readonly SortedDictionary<uint, ServiceInstance> _ring = new();
    private readonly Dictionary<string, ServiceInstance> _nodes = new();

    /// <summary>
    /// 虚拟节点数量
    /// </summary>
    public int VirtualNodeCount { get; set; } = 150;

    /// <summary>
    /// 哈希函数类型
    /// </summary>
    public string HashFunction { get; set; } = "MD5";

    /// <summary>
    /// 是否启用节点权重
    /// </summary>
    public bool EnableNodeWeight { get; set; } = true;

    /// <summary>
    /// 总虚拟节点数
    /// </summary>
    public int TotalVirtualNodes => _ring.Count;

    /// <summary>
    /// 添加节点
    /// </summary>
    public void AddNode(ServiceInstance instance)
    {
        _nodes[instance.Id] = instance;

        var virtualNodeCount = EnableNodeWeight ? VirtualNodeCount * instance.Weight / 100 : VirtualNodeCount;
        virtualNodeCount = Math.Max(1, virtualNodeCount); // 至少有一个虚拟节点

        for (int i = 0; i < virtualNodeCount; i++)
        {
            var virtualNodeKey = $"{instance.Id}:{i}";
            var hash = Hash(virtualNodeKey);
            _ring[hash] = instance;
        }
    }

    /// <summary>
    /// 移除节点
    /// </summary>
    public void RemoveNode(string instanceId)
    {
        if (!_nodes.ContainsKey(instanceId)) return;

        _nodes.Remove(instanceId);

        // 移除所有相关的虚拟节点
        var keysToRemove = _ring.Where(kvp => kvp.Value.Id == instanceId).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
        {
            _ring.Remove(key);
        }
    }

    /// <summary>
    /// 获取节点
    /// </summary>
    public ServiceInstance? GetNode(string key)
    {
        if (_ring.Count == 0) return null;

        var hash = Hash(key);

        // 找到第一个大于等于hash值的节点
        var node = _ring.FirstOrDefault(kvp => kvp.Key >= hash);

        // 如果没找到，说明应该选择环上的第一个节点（环形结构）
        return node.Key == 0 ? _ring.Values.First() : node.Value;
    }

    /// <summary>
    /// 清空哈希环
    /// </summary>
    public void Clear()
    {
        _ring.Clear();
        _nodes.Clear();
    }

    /// <summary>
    /// 获取所有节点
    /// </summary>
    public IReadOnlyList<ServiceInstance> GetAllNodes()
    {
        return _nodes.Values.ToList();
    }

    /// <summary>
    /// 获取虚拟节点分布
    /// </summary>
    public Dictionary<string, int> GetVirtualNodeDistribution()
    {
        var distribution = new Dictionary<string, int>();

        foreach (var instance in _nodes.Values)
        {
            distribution[instance.Id] = _ring.Count(kvp => kvp.Value.Id == instance.Id);
        }

        return distribution;
    }

    /// <summary>
    /// 获取哈希环快照
    /// </summary>
    public Dictionary<uint, string> GetRingSnapshot()
    {
        return _ring.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Id);
    }

    /// <summary>
    /// 计算哈希值
    /// </summary>
    public uint Hash(string input)
    {
        return HashFunction.ToUpperInvariant() switch
        {
            "MD5" => HashMD5(input),
            "SHA1" => HashSHA1(input),
            "MURMUR3" => HashMurMur3(input),
            _ => HashMD5(input) // 默认使用MD5
        };
    }

    /// <summary>
    /// MD5哈希
    /// </summary>
    private static uint HashMD5(string input)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToUInt32(hash, 0);
    }

    /// <summary>
    /// SHA1哈希
    /// </summary>
    private static uint HashSHA1(string input)
    {
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToUInt32(hash, 0);
    }

    /// <summary>
    /// MurMur3哈希（简化版）
    /// </summary>
    private static uint HashMurMur3(string input)
    {
        const uint seed = 0x12345678;
        const uint c1 = 0xcc9e2d51;
        const uint c2 = 0x1b873593;

        var bytes = Encoding.UTF8.GetBytes(input);
        var h1 = seed;

        for (int i = 0; i < bytes.Length; i += 4)
        {
            uint k1 = 0;
            var remainingBytes = Math.Min(4, bytes.Length - i);

            for (int j = 0; j < remainingBytes; j++)
            {
                k1 |= (uint)(bytes[i + j] << (j * 8));
            }

            k1 *= c1;
            k1 = RotateLeft(k1, 15);
            k1 *= c2;

            h1 ^= k1;
            h1 = RotateLeft(h1, 13);
            h1 = h1 * 5 + 0xe6546b64;
        }

        // Finalize
        h1 ^= (uint)bytes.Length;
        h1 = FMix(h1);

        return h1;
    }

    private static uint RotateLeft(uint value, int count)
    {
        return (value << count) | (value >> (32 - count));
    }

    private static uint FMix(uint h)
    {
        h ^= h >> 16;
        h *= 0x85ebca6b;
        h ^= h >> 13;
        h *= 0xc2b2ae35;
        h ^= h >> 16;
        return h;
    }
}

/// <summary>
/// 一致性哈希统计信息
/// </summary>
public sealed class ConsistentHashStatistics
{
    /// <summary>
    /// 基础统计信息
    /// </summary>
    public LoadBalancingStatistics BaseStatistics { get; set; } = new();

    /// <summary>
    /// 虚拟节点分布
    /// </summary>
    public Dictionary<string, int> VirtualNodeDistribution { get; set; } = new();

    /// <summary>
    /// 总虚拟节点数
    /// </summary>
    public int TotalVirtualNodes { get; set; }

    /// <summary>
    /// 哈希环大小
    /// </summary>
    public int HashRingSize { get; set; }

    /// <summary>
    /// 负载分布方差
    /// </summary>
    public double LoadDistributionVariance { get; set; }

    /// <summary>
    /// 平均虚拟节点数
    /// </summary>
    public double AverageVirtualNodesPerInstance => VirtualNodeDistribution.Count > 0
        ? (double)TotalVirtualNodes / VirtualNodeDistribution.Count
        : 0;

    /// <summary>
    /// 负载均衡质量评分（0-1，1为最佳）
    /// </summary>
    public double LoadBalanceQuality => VirtualNodeDistribution.Count > 1
        ? Math.Max(0, 1.0 - LoadDistributionVariance / (AverageVirtualNodesPerInstance * AverageVirtualNodesPerInstance))
        : 1.0;
}