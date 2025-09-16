using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Core.ServiceDiscovery;

/// <summary>
/// 服务发现基类
/// </summary>
public abstract class ServiceDiscoveryBase : IServiceDiscovery, IDisposable
{
    protected readonly ILogger _logger;
    protected readonly ServiceDiscoveryConfiguration _configuration;
    protected readonly ConcurrentDictionary<string, ServiceInstance> _serviceInstances = new();
    protected readonly ConcurrentDictionary<string, HashSet<string>> _serviceWatchers = new();
    protected readonly ServiceDiscoveryStatistics _statistics = new();
    protected readonly Timer? _healthCheckTimer;
    protected readonly Timer? _statisticsTimer;
    protected volatile bool _disposed;
    protected volatile bool _isStarted;
    protected readonly object _watchersLock = new();

    /// <summary>
    /// 服务发现名称
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// 是否已启动
    /// </summary>
    public bool IsStarted => _isStarted && !_disposed;

    /// <summary>
    /// 服务实例变更事件
    /// </summary>
    public event EventHandler<ServiceDiscoveryEventArgs>? InstanceChanged;

    /// <summary>
    /// 构造函数
    /// </summary>
    protected ServiceDiscoveryBase(
        ServiceDiscoveryConfiguration? configuration = null,
        ILogger? logger = null)
    {
        _configuration = configuration ?? new ServiceDiscoveryConfiguration();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        _statistics.Name = Name;

        // 创建健康检查定时器
        _healthCheckTimer = new Timer(
            callback: async _ => await PerformHealthCheckAsync(),
            state: null,
            dueTime: _configuration.HealthCheckInterval,
            period: _configuration.HealthCheckInterval);

        // 创建统计信息更新定时器
        _statisticsTimer = new Timer(
            callback: _ => UpdateStatistics(),
            state: null,
            dueTime: TimeSpan.FromSeconds(30),
            period: TimeSpan.FromSeconds(30));

        _logger.LogDebug("服务发现已创建: {ServiceDiscoveryName}", Name);
    }

    /// <summary>
    /// 启动服务发现
    /// </summary>
    public virtual async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ServiceDiscoveryBase));
        }

        if (_isStarted)
        {
            return;
        }

        try
        {
            await StartInternalAsync(cancellationToken);
            _isStarted = true;

            _logger.LogInformation("服务发现已启动: {ServiceDiscoveryName}", Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动服务发现失败: {ServiceDiscoveryName}", Name);
            throw;
        }
    }

    /// <summary>
    /// 停止服务发现
    /// </summary>
    public virtual async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isStarted)
        {
            return;
        }

        try
        {
            await StopInternalAsync(cancellationToken);
            _isStarted = false;

            _logger.LogInformation("服务发现已停止: {ServiceDiscoveryName}", Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止服务发现失败: {ServiceDiscoveryName}", Name);
            throw;
        }
    }

    /// <summary>
    /// 注册服务实例
    /// </summary>
    public virtual async Task RegisterInstanceAsync(ServiceInstance instance, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ServiceDiscoveryBase));
        }

        if (!_isStarted)
        {
            throw new InvalidOperationException("服务发现未启动");
        }

        ArgumentNullException.ThrowIfNull(instance);

        try
        {
            // 设置注册时间
            instance.RegisteredAt = DateTime.UtcNow;
            instance.LastUpdatedAt = DateTime.UtcNow;

            await RegisterInstanceInternalAsync(instance, cancellationToken);

            // 添加到本地缓存
            _serviceInstances.AddOrUpdate(instance.Id, instance, (_, _) => instance);

            // 更新统计信息
            _statistics.RegisteredInstances++;
            _statistics.TotalInstances = _serviceInstances.Count;

            // 触发事件
            OnInstanceChanged(new ServiceDiscoveryEventArgs(ServiceDiscoveryEventType.InstanceRegistered, instance));

            _logger.LogInformation("服务实例已注册: {ServiceName}@{Endpoint} (ID: {InstanceId})",
                instance.ServiceName, instance.GetEndpoint(), instance.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注册服务实例失败: {ServiceName}@{Endpoint}",
                instance.ServiceName, instance.GetEndpoint());
            throw;
        }
    }

    /// <summary>
    /// 注销服务实例
    /// </summary>
    public virtual async Task DeregisterInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ServiceDiscoveryBase));
        }

        if (!_isStarted)
        {
            throw new InvalidOperationException("服务发现未启动");
        }

        ArgumentException.ThrowIfNullOrEmpty(instanceId);

        try
        {
            await DeregisterInstanceInternalAsync(instanceId, cancellationToken);

            // 从本地缓存移除
            if (_serviceInstances.TryRemove(instanceId, out var instance))
            {
                // 更新统计信息
                _statistics.DeregisteredInstances++;
                _statistics.TotalInstances = _serviceInstances.Count;

                // 触发事件
                OnInstanceChanged(new ServiceDiscoveryEventArgs(ServiceDiscoveryEventType.InstanceDeregistered, instance));

                _logger.LogInformation("服务实例已注销: {ServiceName}@{Endpoint} (ID: {InstanceId})",
                    instance.ServiceName, instance.GetEndpoint(), instance.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注销服务实例失败: {InstanceId}", instanceId);
            throw;
        }
    }

    /// <summary>
    /// 更新服务实例
    /// </summary>
    public virtual async Task UpdateInstanceAsync(ServiceInstance instance, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ServiceDiscoveryBase));
        }

        if (!_isStarted)
        {
            throw new InvalidOperationException("服务发现未启动");
        }

        ArgumentNullException.ThrowIfNull(instance);

        try
        {
            var oldInstance = _serviceInstances.TryGetValue(instance.Id, out var existing) ? existing.Clone() : null;

            // 设置更新时间
            instance.LastUpdatedAt = DateTime.UtcNow;

            await UpdateInstanceInternalAsync(instance, cancellationToken);

            // 更新本地缓存
            _serviceInstances.AddOrUpdate(instance.Id, instance, (_, _) => instance);

            // 触发事件
            OnInstanceChanged(new ServiceDiscoveryEventArgs(ServiceDiscoveryEventType.InstanceUpdated, instance, oldInstance));

            _logger.LogDebug("服务实例已更新: {ServiceName}@{Endpoint} (ID: {InstanceId})",
                instance.ServiceName, instance.GetEndpoint(), instance.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新服务实例失败: {ServiceName}@{Endpoint}",
                instance.ServiceName, instance.GetEndpoint());
            throw;
        }
    }

    /// <summary>
    /// 发现服务实例
    /// </summary>
    public virtual async Task<IReadOnlyList<ServiceInstance>> DiscoverInstancesAsync(ServiceDiscoveryQuery query, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ServiceDiscoveryBase));
        }

        if (!_isStarted)
        {
            throw new InvalidOperationException("服务发现未启动");
        }

        ArgumentNullException.ThrowIfNull(query);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _statistics.TotalDiscoveryRequests++;

            var instances = await DiscoverInstancesInternalAsync(query, cancellationToken);

            // 应用本地过滤
            var filteredInstances = ApplyLocalFilters(instances, query);

            stopwatch.Stop();

            // 更新统计信息
            _statistics.SuccessfulDiscoveryRequests++;
            UpdateAverageDiscoveryLatency(stopwatch.Elapsed);

            _logger.LogDebug("服务发现完成: {ServiceName}, 发现 {Count} 个实例, 耗时: {Duration}ms",
                query.ServiceName, filteredInstances.Count, stopwatch.Elapsed.TotalMilliseconds);

            return filteredInstances;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _statistics.FailedDiscoveryRequests++;

            _logger.LogError(ex, "服务发现失败: {ServiceName}", query.ServiceName);
            throw;
        }
    }

    /// <summary>
    /// 获取所有服务名称
    /// </summary>
    public virtual async Task<IReadOnlyList<string>> GetServiceNamesAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ServiceDiscoveryBase));
        }

        if (!_isStarted)
        {
            throw new InvalidOperationException("服务发现未启动");
        }

        try
        {
            var serviceNames = await GetServiceNamesInternalAsync(cancellationToken);

            _logger.LogDebug("获取服务名称列表: {Count} 个服务", serviceNames.Count);

            return serviceNames;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取服务名称列表失败");
            throw;
        }
    }

    /// <summary>
    /// 检查服务实例健康状态
    /// </summary>
    public virtual async Task<ServiceInstanceStatus> CheckInstanceHealthAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return ServiceInstanceStatus.Unknown;
        }

        if (!_isStarted)
        {
            return ServiceInstanceStatus.Unknown;
        }

        ArgumentException.ThrowIfNullOrEmpty(instanceId);

        try
        {
            var status = await CheckInstanceHealthInternalAsync(instanceId, cancellationToken);

            // 更新本地缓存中的健康状态
            if (_serviceInstances.TryGetValue(instanceId, out var instance) && instance.Status != status)
            {
                var oldInstance = instance.Clone();
                instance.Status = status;
                instance.LastUpdatedAt = DateTime.UtcNow;

                OnInstanceChanged(new ServiceDiscoveryEventArgs(ServiceDiscoveryEventType.InstanceStatusChanged, instance, oldInstance));
            }

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查实例健康状态失败: {InstanceId}", instanceId);
            return ServiceInstanceStatus.Unknown;
        }
    }

    /// <summary>
    /// 监听服务变更
    /// </summary>
    public virtual async Task StartWatchingAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ServiceDiscoveryBase));
        }

        if (!_isStarted)
        {
            throw new InvalidOperationException("服务发现未启动");
        }

        ArgumentException.ThrowIfNullOrEmpty(serviceName);

        try
        {
            await StartWatchingInternalAsync(serviceName, cancellationToken);

            lock (_watchersLock)
            {
                if (!_serviceWatchers.TryGetValue(serviceName, out var watchers))
                {
                    watchers = new HashSet<string>();
                    _serviceWatchers[serviceName] = watchers;
                }
                watchers.Add(serviceName);
            }

            _logger.LogInformation("开始监听服务变更: {ServiceName}", serviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "开始监听服务变更失败: {ServiceName}", serviceName);
            throw;
        }
    }

    /// <summary>
    /// 停止监听服务变更
    /// </summary>
    public virtual async Task StopWatchingAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrEmpty(serviceName);

        try
        {
            await StopWatchingInternalAsync(serviceName, cancellationToken);

            lock (_watchersLock)
            {
                if (_serviceWatchers.TryGetValue(serviceName, out var watchers))
                {
                    watchers.Remove(serviceName);
                    if (watchers.Count == 0)
                    {
                        _serviceWatchers.TryRemove(serviceName, out _);
                    }
                }
            }

            _logger.LogInformation("停止监听服务变更: {ServiceName}", serviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止监听服务变更失败: {ServiceName}", serviceName);
        }
    }

    /// <summary>
    /// 获取服务发现统计信息
    /// </summary>
    public ServiceDiscoveryStatistics GetStatistics()
    {
        UpdateStatistics();

        return new ServiceDiscoveryStatistics
        {
            Name = _statistics.Name,
            TotalServices = _statistics.TotalServices,
            TotalInstances = _statistics.TotalInstances,
            HealthyInstances = _statistics.HealthyInstances,
            UnhealthyInstances = _statistics.UnhealthyInstances,
            RegisteredInstances = _statistics.RegisteredInstances,
            DeregisteredInstances = _statistics.DeregisteredInstances,
            TotalDiscoveryRequests = _statistics.TotalDiscoveryRequests,
            SuccessfulDiscoveryRequests = _statistics.SuccessfulDiscoveryRequests,
            FailedDiscoveryRequests = _statistics.FailedDiscoveryRequests,
            AverageDiscoveryLatency = _statistics.AverageDiscoveryLatency,
            LastUpdatedAt = DateTime.UtcNow,
            ServiceInstanceCounts = new Dictionary<string, int>(_statistics.ServiceInstanceCounts),
            RegionDistribution = new Dictionary<string, int>(_statistics.RegionDistribution),
            StatusDistribution = new Dictionary<ServiceInstanceStatus, int>(_statistics.StatusDistribution)
        };
    }

    /// <summary>
    /// 内部启动实现（由具体实现类重写）
    /// </summary>
    protected abstract Task StartInternalAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 内部停止实现（由具体实现类重写）
    /// </summary>
    protected abstract Task StopInternalAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 内部注册实例实现（由具体实现类重写）
    /// </summary>
    protected abstract Task RegisterInstanceInternalAsync(ServiceInstance instance, CancellationToken cancellationToken);

    /// <summary>
    /// 内部注销实例实现（由具体实现类重写）
    /// </summary>
    protected abstract Task DeregisterInstanceInternalAsync(string instanceId, CancellationToken cancellationToken);

    /// <summary>
    /// 内部更新实例实现（由具体实现类重写）
    /// </summary>
    protected abstract Task UpdateInstanceInternalAsync(ServiceInstance instance, CancellationToken cancellationToken);

    /// <summary>
    /// 内部发现实例实现（由具体实现类重写）
    /// </summary>
    protected abstract Task<IReadOnlyList<ServiceInstance>> DiscoverInstancesInternalAsync(ServiceDiscoveryQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// 内部获取服务名称实现（由具体实现类重写）
    /// </summary>
    protected abstract Task<IReadOnlyList<string>> GetServiceNamesInternalAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 内部健康检查实现（由具体实现类重写）
    /// </summary>
    protected abstract Task<ServiceInstanceStatus> CheckInstanceHealthInternalAsync(string instanceId, CancellationToken cancellationToken);

    /// <summary>
    /// 内部开始监听实现（由具体实现类重写）
    /// </summary>
    protected abstract Task StartWatchingInternalAsync(string serviceName, CancellationToken cancellationToken);

    /// <summary>
    /// 内部停止监听实现（由具体实现类重写）
    /// </summary>
    protected abstract Task StopWatchingInternalAsync(string serviceName, CancellationToken cancellationToken);

    /// <summary>
    /// 触发实例变更事件
    /// </summary>
    protected virtual void OnInstanceChanged(ServiceDiscoveryEventArgs args)
    {
        try
        {
            InstanceChanged?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理实例变更事件失败: {EventType}, {InstanceId}", args.EventType, args.Instance.Id);
        }
    }

    /// <summary>
    /// 应用本地过滤器
    /// </summary>
    protected virtual List<ServiceInstance> ApplyLocalFilters(IReadOnlyList<ServiceInstance> instances, ServiceDiscoveryQuery query)
    {
        var filtered = instances.AsEnumerable();

        // 版本过滤
        if (!string.IsNullOrEmpty(query.Version))
        {
            filtered = filtered.Where(i => string.Equals(i.Version, query.Version, StringComparison.OrdinalIgnoreCase));
        }

        // 区域过滤
        if (!string.IsNullOrEmpty(query.Region))
        {
            filtered = filtered.Where(i => string.Equals(i.Region, query.Region, StringComparison.OrdinalIgnoreCase));
        }

        // 可用区过滤
        if (!string.IsNullOrEmpty(query.Zone))
        {
            filtered = filtered.Where(i => string.Equals(i.Zone, query.Zone, StringComparison.OrdinalIgnoreCase));
        }

        // 标签过滤
        if (query.TagFilters.Any())
        {
            filtered = filtered.Where(i =>
                query.TagFilters.All(filter =>
                    i.Tags.TryGetValue(filter.Key, out var value) &&
                    string.Equals(value, filter.Value, StringComparison.OrdinalIgnoreCase)));
        }

        // 状态过滤
        if (query.StatusFilters != null && query.StatusFilters.Length > 0)
        {
            filtered = filtered.Where(i => query.StatusFilters.Contains(i.Status));
        }
        else if (!query.IncludeUnhealthyInstances)
        {
            filtered = filtered.Where(i => i.Status == ServiceInstanceStatus.Healthy);
        }

        var result = filtered.ToList();

        // 限制返回数量
        if (query.MaxInstances.HasValue && result.Count > query.MaxInstances.Value)
        {
            result = result.Take(query.MaxInstances.Value).ToList();
        }

        return result;
    }

    /// <summary>
    /// 执行健康检查
    /// </summary>
    protected virtual async Task PerformHealthCheckAsync()
    {
        if (_disposed || !_isStarted)
        {
            return;
        }

        try
        {
            var instances = _serviceInstances.Values.ToList();

            foreach (var instance in instances)
            {
                var status = await CheckInstanceHealthAsync(instance.Id);
                // 健康检查结果已在CheckInstanceHealthAsync中处理
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行健康检查失败");
        }
    }

    /// <summary>
    /// 更新统计信息
    /// </summary>
    protected virtual void UpdateStatistics()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var instances = _serviceInstances.Values.ToList();

            _statistics.TotalInstances = instances.Count;
            _statistics.HealthyInstances = instances.Count(i => i.Status == ServiceInstanceStatus.Healthy);
            _statistics.UnhealthyInstances = instances.Count(i => i.Status == ServiceInstanceStatus.Unhealthy);

            // 更新服务分布
            _statistics.ServiceInstanceCounts.Clear();
            foreach (var group in instances.GroupBy(i => i.ServiceName))
            {
                _statistics.ServiceInstanceCounts[group.Key] = group.Count();
            }
            _statistics.TotalServices = _statistics.ServiceInstanceCounts.Count;

            // 更新区域分布
            _statistics.RegionDistribution.Clear();
            foreach (var group in instances.Where(i => !string.IsNullOrEmpty(i.Region)).GroupBy(i => i.Region!))
            {
                _statistics.RegionDistribution[group.Key] = group.Count();
            }

            // 更新状态分布
            _statistics.StatusDistribution.Clear();
            foreach (var group in instances.GroupBy(i => i.Status))
            {
                _statistics.StatusDistribution[group.Key] = group.Count();
            }

            _statistics.LastUpdatedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新统计信息失败");
        }
    }

    /// <summary>
    /// 更新平均发现延迟
    /// </summary>
    protected void UpdateAverageDiscoveryLatency(TimeSpan latency)
    {
        const double alpha = 0.1; // 平滑因子
        var currentAverage = _statistics.AverageDiscoveryLatency?.TotalMilliseconds ?? 0;
        var newAverage = currentAverage == 0
            ? latency.TotalMilliseconds
            : alpha * latency.TotalMilliseconds + (1 - alpha) * currentAverage;

        _statistics.AverageDiscoveryLatency = TimeSpan.FromMilliseconds(newAverage);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public virtual void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _logger.LogInformation("正在关闭服务发现: {ServiceDiscoveryName}", Name);

        try
        {
            if (_isStarted)
            {
                StopAsync().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止服务发现时发生错误");
        }

        _healthCheckTimer?.Dispose();
        _statisticsTimer?.Dispose();

        _serviceInstances.Clear();
        _serviceWatchers.Clear();

        _logger.LogInformation("服务发现已关闭: {ServiceDiscoveryName}", Name);
    }
}