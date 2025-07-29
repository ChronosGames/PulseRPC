using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Routing;
using PulseRPC.SmartConnection;

namespace PulseRPC.Client.SmartConnection;

/// <summary>
/// 多实例连接管理器
/// </summary>
public class MultiInstanceConnectionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ServiceInstanceConnections> _serviceInstances = new();
    private readonly ConcurrentDictionary<string, ServiceRoutingConfiguration> _routingConfigs = new();
    private readonly SmartConnectionManager _connectionManager;
    private readonly ILogger<MultiInstanceConnectionManager> _logger;
    private readonly Timer _healthCheckTimer;
    private bool _disposed;

    public MultiInstanceConnectionManager(
        SmartConnectionManager connectionManager,
        ILoggerFactory? loggerFactory = null)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = loggerFactory?.CreateLogger<MultiInstanceConnectionManager>() ?? 
                   Microsoft.Extensions.Logging.Abstractions.NullLogger<MultiInstanceConnectionManager>.Instance;

        // 每30秒检查一次实例健康状态
        _healthCheckTimer = new Timer(CheckInstanceHealth, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// 获取服务代理 - 自动路由
    /// </summary>
    public async Task<T> GetServiceAsync<T>(
        string serviceName, 
        IRoutingContext? routingContext = null,
        SmartConnectionOptions? options = null) where T : class, IPulseService
    {
        // 发现服务实例
        var instances = await DiscoverServiceInstancesAsync(serviceName);
        
        // 选择最佳实例
        var selectedInstance = SelectBestInstance<T>(instances, routingContext ?? RoutingContext.ByKey(""));
        
        // 获取或创建连接
        var connection = await _connectionManager.GetOrCreateConnectionAsync<T>(
            $"{serviceName}-{selectedInstance.InstanceId}", options);
        
        // 返回服务代理
        return connection.ChannelManager.GetService<T>();
    }

    /// <summary>
    /// 获取特定实例的服务代理
    /// </summary>
    public async Task<T> GetServiceAsync<T>(
        string serviceName, 
        string instanceId,
        SmartConnectionOptions? options = null) where T : class, IPulseService
    {
        var instances = await DiscoverServiceInstancesAsync(serviceName);
        var targetInstance = instances.FirstOrDefault(i => i.InstanceId == instanceId);
        
        if (targetInstance == null)
        {
            throw new ServiceInstanceNotFoundException($"服务实例未找到: {serviceName}#{instanceId}");
        }

        var connection = await _connectionManager.GetOrCreateConnectionAsync<T>(
            $"{serviceName}-{instanceId}", options);
        return connection.ChannelManager.GetService<T>();
    }

    /// <summary>
    /// 配置服务路由策略
    /// </summary>
    public void ConfigureServiceRouting<T>(Action<ServiceRoutingConfiguration<T>> configure) where T : class, IPulseService
    {
        var config = new ServiceRoutingConfiguration<T>();
        configure(config);
        _routingConfigs[typeof(T).Name] = new ServiceRoutingConfiguration
        {
            DefaultStrategy = config.DefaultStrategy,
            MaxRetries = config.MaxRetries,
            RetryDelay = config.RetryDelay
        };
    }

    /// <summary>
    /// 服务实例选择算法
    /// </summary>
    private ServiceInstanceInfo SelectBestInstance<T>(
        IReadOnlyList<ServiceInstanceInfo> instances, 
        IRoutingContext routingContext) where T : class, IPulseService
    {
        if (instances.Count == 0)
            throw new ServiceNotFoundException($"没有可用的服务实例: {typeof(T).Name}");

        if (instances.Count == 1)
            return instances[0];

        // 获取路由配置
        var serviceName = typeof(T).Name;
        var routingConfig = _routingConfigs.GetValueOrDefault(serviceName);

        var strategy = routingConfig?.DefaultStrategy ?? ServiceRoutingStrategy.RoundRobin;

        return strategy switch
        {
            ServiceRoutingStrategy.RoundRobin => SelectRoundRobin(instances),
            ServiceRoutingStrategy.ConsistentHashing => SelectConsistentHash(instances, routingContext.RoutingKey),
            ServiceRoutingStrategy.LeastConnections => SelectLeastConnections(instances),
            ServiceRoutingStrategy.AffinityFirst => SelectAffinityFirst(instances, routingContext),
            ServiceRoutingStrategy.WeightedRandom => SelectWeightedRandom(instances),
            _ => instances[0]
        };
    }

    private ServiceInstanceInfo SelectRoundRobin(IReadOnlyList<ServiceInstanceInfo> instances)
    {
        var healthyInstances = instances.Where(i => i.IsHealthy).ToList();
        if (healthyInstances.Count == 0) return instances[0];

        var index = (int)(DateTime.UtcNow.Ticks % healthyInstances.Count);
        return healthyInstances[index];
    }

    private ServiceInstanceInfo SelectConsistentHash(IReadOnlyList<ServiceInstanceInfo> instances, string routingKey)
    {
        var healthyInstances = instances.Where(i => i.IsHealthy).ToList();
        if (healthyInstances.Count == 0) return instances[0];

        // 简单哈希实现
        var hash = (uint)routingKey.GetHashCode();
        var index = (int)(hash % (uint)healthyInstances.Count);
        return healthyInstances[index];
    }

    private ServiceInstanceInfo SelectLeastConnections(IReadOnlyList<ServiceInstanceInfo> instances)
    {
        return instances
            .Where(i => i.IsHealthy)
            .OrderBy(i => i.ActiveConnections)
            .ThenByDescending(i => i.Weight)
            .FirstOrDefault() ?? instances[0];
    }

    private ServiceInstanceInfo SelectAffinityFirst(IReadOnlyList<ServiceInstanceInfo> instances, IRoutingContext context)
    {
        if (!string.IsNullOrEmpty(context.AffinityId))
        {
            var affinityInstance = instances.FirstOrDefault(i => 
                i.IsHealthy && 
                i.Metadata.TryGetValue("affinity", out var affinity) && 
                affinity.Contains(context.AffinityId));
            
            if (affinityInstance != null)
                return affinityInstance;
        }

        return SelectRoundRobin(instances);
    }

    private ServiceInstanceInfo SelectWeightedRandom(IReadOnlyList<ServiceInstanceInfo> instances)
    {
        var healthyInstances = instances.Where(i => i.IsHealthy).ToList();
        if (healthyInstances.Count == 0) return instances[0];

        var totalWeight = healthyInstances.Sum(i => i.Weight);
        var random = new Random().Next(totalWeight);
        
        var currentWeight = 0;
        foreach (var instance in healthyInstances)
        {
            currentWeight += instance.Weight;
            if (random < currentWeight)
                return instance;
        }

        return healthyInstances[0];
    }

    /// <summary>
    /// 发现服务实例
    /// </summary>
    private Task<IReadOnlyList<ServiceInstanceInfo>> DiscoverServiceInstancesAsync(string serviceName)
    {
        // 简化实现 - 返回模拟的服务实例
        var instances = new List<ServiceInstanceInfo>
        {
            new ServiceInstanceInfo
            {
                InstanceId = $"{serviceName}-1",
                ServiceName = serviceName,
                Endpoint = new ServiceEndpoint { Host = "localhost", Port = 8000 },
                Weight = 100,
                IsHealthy = true,
                Region = "default",
                Zone = "zone-a"
            },
            new ServiceInstanceInfo
            {
                InstanceId = $"{serviceName}-2",
                ServiceName = serviceName,
                Endpoint = new ServiceEndpoint { Host = "localhost", Port = 8001 },
                Weight = 100,
                IsHealthy = true,
                Region = "default",
                Zone = "zone-b"
            }
        };
        
        return Task.FromResult<IReadOnlyList<ServiceInstanceInfo>>(instances);
    }

    /// <summary>
    /// 检查实例健康状态
    /// </summary>
    private void CheckInstanceHealth(object? state)
    {
        if (_disposed) return;

        // 简化实现 - 实际应该执行健康检查
        _logger.LogDebug("执行实例健康检查");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _healthCheckTimer?.Dispose();

        foreach (var serviceInstance in _serviceInstances.Values)
        {
            serviceInstance.Dispose();
        }
        _serviceInstances.Clear();
    }
}

/// <summary>
/// 服务实例连接集合
/// </summary>
public class ServiceInstanceConnections : IDisposable
{
    public string ServiceName { get; set; } = "";
    public ConcurrentDictionary<string, ServiceConnectionInfo> Connections { get; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    public void Dispose()
    {
        foreach (var connection in Connections.Values)
        {
            connection.Dispose();
        }
        Connections.Clear();
    }
}

/// <summary>
/// 简化的服务路由配置（非泛型版本）
/// </summary>
public class ServiceRoutingConfiguration
{
    public ServiceRoutingStrategy DefaultStrategy { get; set; } = ServiceRoutingStrategy.RoundRobin;
    public int MaxRetries { get; set; } = 3;
    public RetryDelayStrategy RetryDelay { get; set; } = RetryDelayStrategy.Exponential;
}

/// <summary>
/// 服务未找到异常
/// </summary>
public class ServiceNotFoundException : Exception
{
    public ServiceNotFoundException(string message) : base(message) { }
    public ServiceNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// 服务实例未找到异常
/// </summary>
public class ServiceInstanceNotFoundException : Exception
{
    public ServiceInstanceNotFoundException(string message) : base(message) { }
    public ServiceInstanceNotFoundException(string message, Exception innerException) : base(message, innerException) { }
} 