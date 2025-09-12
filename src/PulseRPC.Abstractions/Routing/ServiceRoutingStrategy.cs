using System;
using System.Collections.Generic;
using PulseRPC.Configuration;

namespace PulseRPC.Routing;

/// <summary>
/// 服务路由策略
/// </summary>
public enum ServiceRoutingStrategy
{
    /// <summary>
    /// 轮询
    /// </summary>
    RoundRobin,

    /// <summary>
    /// 一致性哈希
    /// </summary>
    ConsistentHashing,

    /// <summary>
    /// 最少连接
    /// </summary>
    LeastConnections,

    /// <summary>
    /// 地理位置最近
    /// </summary>
    Geolocation,

    /// <summary>
    /// 加权随机
    /// </summary>
    WeightedRandom,

    /// <summary>
    /// 亲和性优先
    /// </summary>
    AffinityFirst,

    /// <summary>
    /// 自定义策略
    /// </summary>
    Custom
}

/// <summary>
/// 服务路由配置
/// </summary>
public class ServiceRoutingConfiguration<T> where T : class, IPulseHub
{
    /// <summary>
    /// 默认路由策略
    /// </summary>
    public ServiceRoutingStrategy DefaultStrategy { get; set; } = ServiceRoutingStrategy.RoundRobin;

    /// <summary>
    /// 自定义路由选择器
    /// </summary>
    public Func<IReadOnlyList<ServiceInstanceInfo>, IRoutingContext, ServiceInstanceInfo>? CustomSelector { get; set; }

    /// <summary>
    /// 实例权重配置
    /// </summary>
    public Dictionary<string, int> InstanceWeights { get; set; } = new();

    /// <summary>
    /// 故障转移配置
    /// </summary>
    public FailoverConfiguration Failover { get; set; } = new();

    /// <summary>
    /// 健康检查配置
    /// </summary>
    public HealthCheckOptions HealthCheck { get; set; } = new();

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 重试延迟策略
    /// </summary>
    public RetryDelayStrategy RetryDelay { get; set; } = RetryDelayStrategy.Exponential;
}

/// <summary>
/// 故障转移配置
/// </summary>
public class FailoverConfiguration
{
    /// <summary>
    /// 是否启用故障转移
    /// </summary>
    public bool EnableFailover { get; set; } = true;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 重试延迟
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 快速故障检测阈值
    /// </summary>
    public TimeSpan FailureDetectionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 断路器配置
    /// </summary>
    public CircuitBreakerConfiguration CircuitBreaker { get; set; } = new();
}

/// <summary>
/// 断路器配置
/// </summary>
public class CircuitBreakerConfiguration
{
    /// <summary>
    /// 是否启用断路器
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 失败阈值（连续失败次数）
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// 成功阈值（恢复所需的成功次数）
    /// </summary>
    public int SuccessThreshold { get; set; } = 3;

    /// <summary>
    /// 超时时间
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 半开状态持续时间
    /// </summary>
    public TimeSpan HalfOpenDuration { get; set; } = TimeSpan.FromSeconds(30);
}

// 健康检查配置已统一到 PulseRPC.Configuration.HealthCheckOptions

/// <summary>
/// 重试延迟策略
/// </summary>
public enum RetryDelayStrategy
{
    /// <summary>
    /// 固定延迟
    /// </summary>
    Fixed,

    /// <summary>
    /// 线性延迟
    /// </summary>
    Linear,

    /// <summary>
    /// 指数退避
    /// </summary>
    Exponential,

    /// <summary>
    /// 随机抖动
    /// </summary>
    Jitter
}
