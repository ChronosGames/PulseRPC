using PulseRPC.Client.ServiceDiscovery;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.LoadBalancing;

/// <summary>
/// 负载均衡策略类型
/// </summary>
public enum LoadBalancingStrategy
{
    /// <summary>
    /// 随机选择
    /// </summary>
    Random,

    /// <summary>
    /// 轮询
    /// </summary>
    RoundRobin,

    /// <summary>
    /// 加权轮询
    /// </summary>
    WeightedRoundRobin,

    /// <summary>
    /// 最少连接
    /// </summary>
    LeastConnections,

    /// <summary>
    /// 加权最少连接
    /// </summary>
    WeightedLeastConnections,

    /// <summary>
    /// 响应时间
    /// </summary>
    ResponseTime,

    /// <summary>
    /// 哈希
    /// </summary>
    Hash,

    /// <summary>
    /// 一致性哈希
    /// </summary>
    ConsistentHash,

    /// <summary>
    /// 本地优先
    /// </summary>
    LocalityAware,

    /// <summary>
    /// 自适应
    /// </summary>
    Adaptive
}

/// <summary>
/// 负载均衡选择结果
/// </summary>
public sealed class LoadBalancingResult
{
    /// <summary>
    /// 选择的服务实例
    /// </summary>
    public ServiceInstance? SelectedInstance { get; }

    /// <summary>
    /// 是否选择成功
    /// </summary>
    public bool IsSuccess => SelectedInstance != null;

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// 选择原因
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// 选择时间
    /// </summary>
    public DateTime SelectionTime { get; }

    /// <summary>
    /// 候选实例数量
    /// </summary>
    public int CandidateCount { get; }

    /// <summary>
    /// 扩展数据
    /// </summary>
    public Dictionary<string, object> ExtendedData { get; }

    /// <summary>
    /// 构造函数 - 成功
    /// </summary>
    public LoadBalancingResult(ServiceInstance selectedInstance, string? reason = null, int candidateCount = 0)
    {
        SelectedInstance = selectedInstance;
        Reason = reason;
        SelectionTime = DateTime.UtcNow;
        CandidateCount = candidateCount;
        ExtendedData = new Dictionary<string, object>();
    }

    /// <summary>
    /// 构造函数 - 失败
    /// </summary>
    public LoadBalancingResult(string errorMessage, int candidateCount = 0)
    {
        ErrorMessage = errorMessage;
        SelectionTime = DateTime.UtcNow;
        CandidateCount = candidateCount;
        ExtendedData = new Dictionary<string, object>();
    }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static LoadBalancingResult Success(ServiceInstance instance, string? reason = null, int candidateCount = 0)
        => new(instance, reason, candidateCount);

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static LoadBalancingResult Failure(string errorMessage, int candidateCount = 0)
        => new(errorMessage, candidateCount);
}

/// <summary>
/// 负载均衡上下文
/// </summary>
public sealed class LoadBalancingContext
{
    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; }

    /// <summary>
    /// 可用实例列表
    /// </summary>
    public IReadOnlyList<ServiceInstance> AvailableInstances { get; }

    /// <summary>
    /// 请求标识
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// 客户端标识
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// 会话标识
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// 哈希键
    /// </summary>
    public string? HashKey { get; set; }

    /// <summary>
    /// 区域偏好
    /// </summary>
    public string? PreferredRegion { get; set; }

    /// <summary>
    /// 可用区偏好
    /// </summary>
    public string? PreferredZone { get; set; }

    /// <summary>
    /// 版本偏好
    /// </summary>
    public string? PreferredVersion { get; set; }

    /// <summary>
    /// 标签过滤器
    /// </summary>
    public Dictionary<string, string> TagFilters { get; set; } = new();

    /// <summary>
    /// 排除的实例
    /// </summary>
    public HashSet<string> ExcludedInstances { get; set; } = new();

    /// <summary>
    /// 自定义数据
    /// </summary>
    public Dictionary<string, object> CustomData { get; set; } = new();

    /// <summary>
    /// 取消令牌
    /// </summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public LoadBalancingContext(string serviceName, IReadOnlyList<ServiceInstance> availableInstances)
    {
        ServiceName = serviceName;
        AvailableInstances = availableInstances;
    }
}

/// <summary>
/// 实例健康状态信息
/// </summary>
public sealed class InstanceHealthInfo
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
    /// 平均响应时间 (ms)
    /// </summary>
    public double AverageResponseTime { get; set; }

    /// <summary>
    /// 成功请求数
    /// </summary>
    public long SuccessfulRequests { get; set; }

    /// <summary>
    /// 失败请求数
    /// </summary>
    public long FailedRequests { get; set; }

    /// <summary>
    /// 最后健康检查时间
    /// </summary>
    public DateTime LastHealthCheck { get; set; }

    /// <summary>
    /// 健康状态
    /// </summary>
    public ServiceInstanceStatus HealthStatus { get; set; }

    /// <summary>
    /// CPU 使用率
    /// </summary>
    public double? CpuUsage { get; set; }

    /// <summary>
    /// 内存使用率
    /// </summary>
    public double? MemoryUsage { get; set; }

    /// <summary>
    /// 权重
    /// </summary>
    public int Weight { get; set; } = 100;

    /// <summary>
    /// 错误率
    /// </summary>
    public double ErrorRate => (SuccessfulRequests + FailedRequests) > 0
        ? (double)FailedRequests / (SuccessfulRequests + FailedRequests) * 100
        : 0;

    /// <summary>
    /// 负载评分（越低越好）
    /// </summary>
    public double LoadScore { get; set; }
}

/// <summary>
/// 负载均衡器接口
/// </summary>
public interface ILoadBalancer
{
    /// <summary>
    /// 负载均衡器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 负载均衡策略
    /// </summary>
    LoadBalancingStrategy Strategy { get; }

    /// <summary>
    /// 是否已启动
    /// </summary>
    bool IsStarted { get; }

    /// <summary>
    /// 启动负载均衡器
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止负载均衡器
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 选择服务实例
    /// </summary>
    Task<LoadBalancingResult> SelectInstanceAsync(LoadBalancingContext context);

    /// <summary>
    /// 报告请求结果
    /// </summary>
    Task ReportRequestResultAsync(string instanceId, bool success, TimeSpan responseTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// 报告实例健康状态
    /// </summary>
    Task ReportInstanceHealthAsync(InstanceHealthInfo healthInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新实例列表
    /// </summary>
    Task UpdateInstancesAsync(IReadOnlyList<ServiceInstance> instances, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取负载均衡统计信息
    /// </summary>
    Task<LoadBalancingStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 重置统计信息
    /// </summary>
    Task ResetStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 负载均衡统计信息
/// </summary>
public sealed class LoadBalancingStatistics
{
    /// <summary>
    /// 负载均衡器名称
    /// </summary>
    public string LoadBalancerName { get; set; } = string.Empty;

    /// <summary>
    /// 策略类型
    /// </summary>
    public LoadBalancingStrategy Strategy { get; set; }

    /// <summary>
    /// 总选择次数
    /// </summary>
    public long TotalSelections { get; set; }

    /// <summary>
    /// 成功选择次数
    /// </summary>
    public long SuccessfulSelections { get; set; }

    /// <summary>
    /// 失败选择次数
    /// </summary>
    public long FailedSelections { get; set; }

    /// <summary>
    /// 平均选择时间
    /// </summary>
    public TimeSpan AverageSelectionTime { get; set; }

    /// <summary>
    /// 实例选择分布
    /// </summary>
    public Dictionary<string, long> InstanceSelectionCounts { get; set; } = new();

    /// <summary>
    /// 实例健康状态
    /// </summary>
    public Dictionary<string, InstanceHealthInfo> InstanceHealthStatus { get; set; } = new();

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 当前活跃实例数
    /// </summary>
    public int ActiveInstanceCount { get; set; }

    /// <summary>
    /// 选择成功率
    /// </summary>
    public double SelectionSuccessRate => TotalSelections > 0
        ? (double)SuccessfulSelections / TotalSelections * 100
        : 100;
}

/// <summary>
/// 负载均衡配置
/// </summary>
public sealed class LoadBalancingConfiguration
{
    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public LoadBalancingStrategy Strategy { get; set; } = LoadBalancingStrategy.RoundRobin;

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 统计信息更新间隔
    /// </summary>
    public TimeSpan StatisticsUpdateInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 实例选择超时时间
    /// </summary>
    public TimeSpan SelectionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 最大并发连接数
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 1000;

    /// <summary>
    /// 启用黏性会话
    /// </summary>
    public bool EnableStickySession { get; set; } = false;

    /// <summary>
    /// 会话超时时间
    /// </summary>
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 错误阈值（超过此阈值标记为不健康）
    /// </summary>
    public double ErrorThreshold { get; set; } = 50.0; // 50%

    /// <summary>
    /// 响应时间阈值（超过此阈值降低权重）
    /// </summary>
    public TimeSpan ResponseTimeThreshold { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 自适应策略配置
    /// </summary>
    public AdaptiveLoadBalancingConfiguration AdaptiveConfiguration { get; set; } = new();

    /// <summary>
    /// 一致性哈希配置
    /// </summary>
    public ConsistentHashConfiguration ConsistentHashConfiguration { get; set; } = new();

    /// <summary>
    /// 扩展配置
    /// </summary>
    public Dictionary<string, object> ExtendedConfiguration { get; set; } = new();
}

/// <summary>
/// 自适应负载均衡配置
/// </summary>
public sealed class AdaptiveLoadBalancingConfiguration
{
    /// <summary>
    /// 响应时间权重
    /// </summary>
    public double ResponseTimeWeight { get; set; } = 0.4;

    /// <summary>
    /// 错误率权重
    /// </summary>
    public double ErrorRateWeight { get; set; } = 0.3;

    /// <summary>
    /// 连接数权重
    /// </summary>
    public double ConnectionCountWeight { get; set; } = 0.2;

    /// <summary>
    /// 资源使用率权重
    /// </summary>
    public double ResourceUsageWeight { get; set; } = 0.1;

    /// <summary>
    /// 评分更新间隔
    /// </summary>
    public TimeSpan ScoreUpdateInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 历史数据窗口大小
    /// </summary>
    public int HistoryWindowSize { get; set; } = 100;
}

/// <summary>
/// 一致性哈希配置
/// </summary>
public sealed class ConsistentHashConfiguration
{
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
    /// 复制因子
    /// </summary>
    public int ReplicationFactor { get; set; } = 3;
}
