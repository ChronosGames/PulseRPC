namespace PulseServiceDiscovery.Abstractions.Enums;

/// <summary>
/// 负载均衡策略枚举
/// </summary>
public enum LoadBalancingStrategy
{
    /// <summary>
    /// 轮询策略
    /// </summary>
    RoundRobin = 0,

    /// <summary>
    /// 随机策略
    /// </summary>
    Random = 1,

    /// <summary>
    /// 加权轮询策略
    /// </summary>
    WeightedRoundRobin = 2,

    /// <summary>
    /// 最少连接策略
    /// </summary>
    LeastConnections = 3,

    /// <summary>
    /// 一致性哈希策略
    /// </summary>
    ConsistentHash = 4,

    /// <summary>
    /// 最快响应策略
    /// </summary>
    FastestResponse = 5,

    /// <summary>
    /// 故障转移策略
    /// </summary>
    Failover = 6,

    /// <summary>
    /// 粘性会话策略
    /// </summary>
    StickySession = 7
}
