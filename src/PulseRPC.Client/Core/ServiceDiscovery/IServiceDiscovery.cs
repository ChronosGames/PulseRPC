using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Core.ServiceDiscovery;

/// <summary>
/// 服务实例状态
/// </summary>
public enum ServiceInstanceStatus
{
    /// <summary>
    /// 健康
    /// </summary>
    Healthy,

    /// <summary>
    /// 不健康
    /// </summary>
    Unhealthy,

    /// <summary>
    /// 未知
    /// </summary>
    Unknown,

    /// <summary>
    /// 维护中
    /// </summary>
    Maintenance
}

/// <summary>
/// 服务实例
/// </summary>
public sealed class ServiceInstance
{
    /// <summary>
    /// 实例ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// 主机地址
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 端口号
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// 协议类型
    /// </summary>
    public string Protocol { get; set; } = "tcp";

    /// <summary>
    /// 权重（用于负载均衡）
    /// </summary>
    public int Weight { get; set; } = 100;

    /// <summary>
    /// 状态
    /// </summary>
    public ServiceInstanceStatus Status { get; set; } = ServiceInstanceStatus.Healthy;

    /// <summary>
    /// 版本号
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// 区域
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// 可用区
    /// </summary>
    public string? Zone { get; set; }

    /// <summary>
    /// 标签
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 健康检查URL
    /// </summary>
    public string? HealthCheckUrl { get; set; }

    /// <summary>
    /// 获取端点地址
    /// </summary>
    public string GetEndpoint() => $"{Host}:{Port}";

    /// <summary>
    /// 获取完整地址
    /// </summary>
    public string GetFullAddress() => $"{Protocol}://{Host}:{Port}";

    /// <summary>
    /// 克隆实例
    /// </summary>
    public ServiceInstance Clone()
    {
        return new ServiceInstance
        {
            Id = Id,
            ServiceName = ServiceName,
            Host = Host,
            Port = Port,
            Protocol = Protocol,
            Weight = Weight,
            Status = Status,
            Version = Version,
            Region = Region,
            Zone = Zone,
            Tags = new Dictionary<string, string>(Tags),
            Metadata = new Dictionary<string, object>(Metadata),
            RegisteredAt = RegisteredAt,
            LastUpdatedAt = LastUpdatedAt,
            HealthCheckUrl = HealthCheckUrl
        };
    }

    /// <summary>
    /// 转换为字符串
    /// </summary>
    public override string ToString()
    {
        return $"{ServiceName}@{GetEndpoint()} (Status: {Status}, Weight: {Weight})";
    }

    /// <summary>
    /// 重写相等比较
    /// </summary>
    public override bool Equals(object? obj)
    {
        return obj is ServiceInstance instance && Id == instance.Id;
    }

    /// <summary>
    /// 重写哈希码
    /// </summary>
    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }
}

/// <summary>
/// 服务发现查询条件
/// </summary>
public sealed class ServiceDiscoveryQuery
{
    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// 版本过滤器
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// 区域过滤器
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// 可用区过滤器
    /// </summary>
    public string? Zone { get; set; }

    /// <summary>
    /// 标签过滤器
    /// </summary>
    public Dictionary<string, string> TagFilters { get; set; } = new();

    /// <summary>
    /// 状态过滤器
    /// </summary>
    public ServiceInstanceStatus[]? StatusFilters { get; set; }

    /// <summary>
    /// 是否包含不健康的实例
    /// </summary>
    public bool IncludeUnhealthyInstances { get; set; } = false;

    /// <summary>
    /// 最大实例数量
    /// </summary>
    public int? MaxInstances { get; set; }
}

/// <summary>
/// 服务发现事件类型
/// </summary>
public enum ServiceDiscoveryEventType
{
    /// <summary>
    /// 服务实例注册
    /// </summary>
    InstanceRegistered,

    /// <summary>
    /// 服务实例注销
    /// </summary>
    InstanceDeregistered,

    /// <summary>
    /// 服务实例更新
    /// </summary>
    InstanceUpdated,

    /// <summary>
    /// 服务实例状态变更
    /// </summary>
    InstanceStatusChanged
}

/// <summary>
/// 服务发现事件参数
/// </summary>
public sealed class ServiceDiscoveryEventArgs : EventArgs
{
    /// <summary>
    /// 事件类型
    /// </summary>
    public ServiceDiscoveryEventType EventType { get; }

    /// <summary>
    /// 服务实例
    /// </summary>
    public ServiceInstance Instance { get; }

    /// <summary>
    /// 旧实例信息（用于更新事件）
    /// </summary>
    public ServiceInstance? OldInstance { get; }

    /// <summary>
    /// 事件时间
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public ServiceDiscoveryEventArgs(ServiceDiscoveryEventType eventType, ServiceInstance instance, ServiceInstance? oldInstance = null)
    {
        EventType = eventType;
        Instance = instance;
        OldInstance = oldInstance;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// 服务发现接口
/// </summary>
public interface IServiceDiscovery
{
    /// <summary>
    /// 服务发现名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 是否已启动
    /// </summary>
    bool IsStarted { get; }

    /// <summary>
    /// 服务实例变更事件
    /// </summary>
    event EventHandler<ServiceDiscoveryEventArgs>? InstanceChanged;

    /// <summary>
    /// 启动服务发现
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止服务发现
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册服务实例
    /// </summary>
    Task RegisterInstanceAsync(ServiceInstance instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注销服务实例
    /// </summary>
    Task DeregisterInstanceAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新服务实例
    /// </summary>
    Task UpdateInstanceAsync(ServiceInstance instance, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发现服务实例
    /// </summary>
    Task<IReadOnlyList<ServiceInstance>> DiscoverInstancesAsync(ServiceDiscoveryQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有服务名称
    /// </summary>
    Task<IReadOnlyList<string>> GetServiceNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查服务实例健康状态
    /// </summary>
    Task<ServiceInstanceStatus> CheckInstanceHealthAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 监听服务变更
    /// </summary>
    Task StartWatchingAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止监听服务变更
    /// </summary>
    Task StopWatchingAsync(string serviceName, CancellationToken cancellationToken = default);
}

/// <summary>
/// 服务发现统计信息
/// </summary>
public sealed class ServiceDiscoveryStatistics
{
    /// <summary>
    /// 服务发现名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 总服务数量
    /// </summary>
    public int TotalServices { get; set; }

    /// <summary>
    /// 总实例数量
    /// </summary>
    public int TotalInstances { get; set; }

    /// <summary>
    /// 健康实例数量
    /// </summary>
    public int HealthyInstances { get; set; }

    /// <summary>
    /// 不健康实例数量
    /// </summary>
    public int UnhealthyInstances { get; set; }

    /// <summary>
    /// 注册的实例数量
    /// </summary>
    public int RegisteredInstances { get; set; }

    /// <summary>
    /// 注销的实例数量
    /// </summary>
    public int DeregisteredInstances { get; set; }

    /// <summary>
    /// 发现请求总数
    /// </summary>
    public long TotalDiscoveryRequests { get; set; }

    /// <summary>
    /// 成功的发现请求数
    /// </summary>
    public long SuccessfulDiscoveryRequests { get; set; }

    /// <summary>
    /// 失败的发现请求数
    /// </summary>
    public long FailedDiscoveryRequests { get; set; }

    /// <summary>
    /// 平均发现延迟
    /// </summary>
    public TimeSpan? AverageDiscoveryLatency { get; set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 服务实例分布
    /// </summary>
    public Dictionary<string, int> ServiceInstanceCounts { get; set; } = new();

    /// <summary>
    /// 区域分布
    /// </summary>
    public Dictionary<string, int> RegionDistribution { get; set; } = new();

    /// <summary>
    /// 状态分布
    /// </summary>
    public Dictionary<ServiceInstanceStatus, int> StatusDistribution { get; set; } = new();
}

/// <summary>
/// 服务发现配置
/// </summary>
public sealed class ServiceDiscoveryConfiguration
{
    /// <summary>
    /// 服务发现类型
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 连接字符串
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 实例过期时间
    /// </summary>
    public TimeSpan InstanceTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 刷新间隔
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 重试配置
    /// </summary>
    public RetryConfiguration RetryConfiguration { get; set; } = new();

    /// <summary>
    /// 扩展配置
    /// </summary>
    public Dictionary<string, object> ExtendedConfiguration { get; set; } = new();
}

/// <summary>
/// 重试配置
/// </summary>
public sealed class RetryConfiguration
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// 基础延迟时间
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 最大延迟时间
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 延迟倍数
    /// </summary>
    public double DelayMultiplier { get; set; } = 2.0;

    /// <summary>
    /// 是否使用抖动
    /// </summary>
    public bool UseJitter { get; set; } = true;
}