using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.ServiceDiscovery;

/// <summary>
/// 内存服务发现器
/// </summary>
public sealed class InMemoryServiceDiscovery : ServiceDiscoveryBase
{
    /// <summary>
    /// 服务发现名称
    /// </summary>
    public override string Name => "InMemory";

    /// <summary>
    /// 构造函数
    /// </summary>
    public InMemoryServiceDiscovery(
        ServiceDiscoveryConfiguration? configuration = null,
        ILogger<InMemoryServiceDiscovery>? logger = null)
        : base(configuration, logger)
    {
    }

    /// <summary>
    /// 内部启动实现
    /// </summary>
    protected override async Task StartInternalAsync(CancellationToken cancellationToken)
    {
        // 内存服务发现不需要特殊的启动逻辑
        await Task.Yield();
        _logger.LogDebug("内存服务发现启动完成");
    }

    /// <summary>
    /// 内部停止实现
    /// </summary>
    protected override async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        // 内存服务发现不需要特殊的停止逻辑
        await Task.Yield();
        _logger.LogDebug("内存服务发现停止完成");
    }

    /// <summary>
    /// 内部注册实例实现
    /// </summary>
    protected override async Task RegisterInstanceInternalAsync(ServiceInstance instance, CancellationToken cancellationToken)
    {
        await Task.Yield();

        // 对于内存服务发现，实例已经在基类中添加到_serviceInstances
        // 这里可以添加额外的验证逻辑

        ValidateInstance(instance);

        _logger.LogDebug("内存服务发现注册实例: {ServiceName}@{Endpoint}",
            instance.ServiceName, instance.GetEndpoint());
    }

    /// <summary>
    /// 内部注销实例实现
    /// </summary>
    protected override async Task DeregisterInstanceInternalAsync(string instanceId, CancellationToken cancellationToken)
    {
        await Task.Yield();

        // 对于内存服务发现，实例已经在基类中从_serviceInstances移除
        _logger.LogDebug("内存服务发现注销实例: {InstanceId}", instanceId);
    }

    /// <summary>
    /// 内部更新实例实现
    /// </summary>
    protected override async Task UpdateInstanceInternalAsync(ServiceInstance instance, CancellationToken cancellationToken)
    {
        await Task.Yield();

        ValidateInstance(instance);

        _logger.LogDebug("内存服务发现更新实例: {ServiceName}@{Endpoint}",
            instance.ServiceName, instance.GetEndpoint());
    }

    /// <summary>
    /// 内部发现实例实现
    /// </summary>
    protected override async Task<IReadOnlyList<ServiceInstance>> DiscoverInstancesInternalAsync(ServiceDiscoveryQuery query, CancellationToken cancellationToken)
    {
        await Task.Yield();

        // 从内存中查找匹配的服务实例
        var instances = _serviceInstances.Values
            .Where(i => string.Equals(i.ServiceName, query.ServiceName, StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Clone()) // 返回副本以避免外部修改
            .ToList();

        _logger.LogTrace("内存服务发现查询: {ServiceName}, 找到 {Count} 个实例",
            query.ServiceName, instances.Count);

        return instances;
    }

    /// <summary>
    /// 内部获取服务名称实现
    /// </summary>
    protected override async Task<IReadOnlyList<string>> GetServiceNamesInternalAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();

        var serviceNames = _serviceInstances.Values
            .Select(i => i.ServiceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();

        _logger.LogTrace("内存服务发现获取服务名称: {Count} 个服务", serviceNames.Count);

        return serviceNames;
    }

    /// <summary>
    /// 内部健康检查实现
    /// </summary>
    protected override async Task<ServiceInstanceStatus> CheckInstanceHealthInternalAsync(string instanceId, CancellationToken cancellationToken)
    {
        await Task.Yield();

        if (_serviceInstances.TryGetValue(instanceId, out var instance))
        {
            // 对于内存服务发现，我们可以模拟健康检查
            var status = SimulateHealthCheck(instance);

            _logger.LogTrace("内存服务发现健康检查: {InstanceId}, 状态: {Status}", instanceId, status);

            return status;
        }

        return ServiceInstanceStatus.Unknown;
    }

    /// <summary>
    /// 内部开始监听实现
    /// </summary>
    protected override async Task StartWatchingInternalAsync(string serviceName, CancellationToken cancellationToken)
    {
        await Task.Yield();

        // 对于内存服务发现，监听功能通过事件机制实现
        // 这里不需要额外的逻辑
        _logger.LogDebug("内存服务发现开始监听: {ServiceName}", serviceName);
    }

    /// <summary>
    /// 内部停止监听实现
    /// </summary>
    protected override async Task StopWatchingInternalAsync(string serviceName, CancellationToken cancellationToken)
    {
        await Task.Yield();

        // 对于内存服务发现，监听功能通过事件机制实现
        // 这里不需要额外的逻辑
        _logger.LogDebug("内存服务发现停止监听: {ServiceName}", serviceName);
    }

    /// <summary>
    /// 批量注册服务实例
    /// </summary>
    public async Task RegisterInstancesAsync(IEnumerable<ServiceInstance> instances, CancellationToken cancellationToken = default)
    {
        if (instances == null) throw new ArgumentNullException(nameof(instances));

        foreach (var instance in instances)
        {
            await RegisterInstanceAsync(instance, cancellationToken);
        }
    }

    /// <summary>
    /// 清空所有服务实例
    /// </summary>
    public async Task ClearAllInstancesAsync()
    {
        var instanceIds = _serviceInstances.Keys.ToList();

        foreach (var instanceId in instanceIds)
        {
            await DeregisterInstanceAsync(instanceId);
        }

        _logger.LogInformation("已清空所有服务实例: {Count} 个", instanceIds.Count);
    }

    /// <summary>
    /// 获取指定服务的所有实例
    /// </summary>
    public Task<IReadOnlyList<ServiceInstance>> GetServiceInstancesAsync(string serviceName)
    {
        var query = new ServiceDiscoveryQuery
        {
            ServiceName = serviceName,
            IncludeUnhealthyInstances = true
        };

        return DiscoverInstancesAsync(query);
    }

    /// <summary>
    /// 验证实例
    /// </summary>
    private static void ValidateInstance(ServiceInstance instance)
    {
        if (string.IsNullOrEmpty(instance.Id))
        {
            throw new ArgumentException("实例ID不能为空", nameof(instance));
        }

        if (string.IsNullOrEmpty(instance.ServiceName))
        {
            throw new ArgumentException("服务名称不能为空", nameof(instance));
        }

        if (string.IsNullOrEmpty(instance.Host))
        {
            throw new ArgumentException("主机地址不能为空", nameof(instance));
        }

        if (instance.Port <= 0 || instance.Port > 65535)
        {
            throw new ArgumentException($"端口号无效: {instance.Port}", nameof(instance));
        }

        if (instance.Weight < 0)
        {
            throw new ArgumentException($"权重不能为负数: {instance.Weight}", nameof(instance));
        }
    }

    /// <summary>
    /// 模拟健康检查
    /// </summary>
    private ServiceInstanceStatus SimulateHealthCheck(ServiceInstance instance)
    {
        // 简单的模拟逻辑：
        // 1. 检查实例是否过期
        var instanceAge = DateTime.UtcNow - instance.LastUpdatedAt;
        if (instanceAge > _configuration.InstanceTtl)
        {
            return ServiceInstanceStatus.Unknown;
        }

        // 2. 根据当前状态和一些随机因素决定健康状态
        // 在实际实现中，这里应该是真正的网络健康检查
        switch (instance.Status)
        {
            case ServiceInstanceStatus.Healthy:
                // 健康实例有很小概率变为不健康
                return new Random().NextDouble() < 0.05 ? ServiceInstanceStatus.Unknown : ServiceInstanceStatus.Healthy;

            case ServiceInstanceStatus.Unhealthy:
                // 不健康实例有一定概率恢复
                return new Random().NextDouble() < 0.3 ? ServiceInstanceStatus.Healthy : ServiceInstanceStatus.Unhealthy;

            case ServiceInstanceStatus.Maintenance:
                // 维护状态保持不变
                return ServiceInstanceStatus.Maintenance;

            default:
                return ServiceInstanceStatus.Unknown;
        }
    }

    /// <summary>
    /// 创建测试服务实例
    /// </summary>
    public static ServiceInstance CreateTestInstance(string serviceName, string host, int port, int weight = 100)
    {
        return new ServiceInstance
        {
            Id = Guid.NewGuid().ToString(),
            ServiceName = serviceName,
            Host = host,
            Port = port,
            Weight = weight,
            Status = ServiceInstanceStatus.Healthy,
            Protocol = "tcp",
            RegisteredAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 创建测试服务实例列表
    /// </summary>
    public static List<ServiceInstance> CreateTestInstances(string serviceName, int count, int basePort = 8080)
    {
        var instances = new List<ServiceInstance>();

        for (int i = 0; i < count; i++)
        {
            instances.Add(CreateTestInstance(serviceName, "localhost", basePort + i, 100 + i * 10));
        }

        return instances;
    }
}
