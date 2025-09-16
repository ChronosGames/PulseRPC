using Microsoft.Extensions.Logging;
using PulseRPC.Client.Core.ServiceDiscovery;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Core.LoadBalancing;

/// <summary>
/// 负载均衡器基类
/// </summary>
public abstract class LoadBalancerBase : ILoadBalancer, IDisposable
{
    protected readonly ILogger _logger;
    protected readonly LoadBalancingConfiguration _configuration;
    protected readonly ConcurrentDictionary<string, InstanceHealthInfo> _instanceHealth = new();
    protected readonly ConcurrentDictionary<string, long> _instanceSelectionCounts = new();
    protected readonly LoadBalancingStatistics _statistics = new();
    protected readonly Timer? _healthCheckTimer;
    protected readonly Timer? _statisticsTimer;
    protected volatile bool _disposed;
    protected volatile bool _isStarted;
    protected IReadOnlyList<ServiceInstance> _instances = Array.Empty<ServiceInstance>();
    protected readonly object _instancesLock = new();

    /// <summary>
    /// 负载均衡器名称
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public abstract LoadBalancingStrategy Strategy { get; }

    /// <summary>
    /// 是否已启动
    /// </summary>
    public bool IsStarted => _isStarted && !_disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    protected LoadBalancerBase(
        LoadBalancingConfiguration? configuration = null,
        ILogger? logger = null)
    {
        _configuration = configuration ?? new LoadBalancingConfiguration();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        _statistics.LoadBalancerName = Name;
        _statistics.Strategy = Strategy;

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
            dueTime: _configuration.StatisticsUpdateInterval,
            period: _configuration.StatisticsUpdateInterval);

        _logger.LogDebug("负载均衡器已创建: {LoadBalancerName} ({Strategy})", Name, Strategy);
    }

    /// <summary>
    /// 启动负载均衡器
    /// </summary>
    public virtual async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LoadBalancerBase));
        }

        if (_isStarted)
        {
            return;
        }

        await Task.Yield(); // 异步操作

        _isStarted = true;
        _logger.LogInformation("负载均衡器已启动: {LoadBalancerName}", Name);
    }

    /// <summary>
    /// 停止负载均衡器
    /// </summary>
    public virtual async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isStarted)
        {
            return;
        }

        await Task.Yield(); // 异步操作

        _isStarted = false;
        _logger.LogInformation("负载均衡器已停止: {LoadBalancerName}", Name);
    }

    /// <summary>
    /// 选择服务实例
    /// </summary>
    public async Task<LoadBalancingResult> SelectInstanceAsync(LoadBalancingContext context)
    {
        if (_disposed)
        {
            return LoadBalancingResult.Failure("负载均衡器已释放", 0);
        }

        if (!_isStarted)
        {
            return LoadBalancingResult.Failure("负载均衡器未启动", 0);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 过滤可用实例
            var eligibleInstances = FilterEligibleInstances(context);

            if (!eligibleInstances.Any())
            {
                _statistics.FailedSelections++;
                return LoadBalancingResult.Failure("没有可用的服务实例", context.AvailableInstances.Count);
            }

            // 调用具体的选择策略
            var result = await SelectInstanceInternalAsync(context, eligibleInstances);

            stopwatch.Stop();

            // 更新统计信息
            _statistics.TotalSelections++;
            if (result.IsSuccess)
            {
                _statistics.SuccessfulSelections++;
                if (result.SelectedInstance != null)
                {
                    _instanceSelectionCounts.AddOrUpdate(result.SelectedInstance.Id, 1, (_, count) => count + 1);
                }
            }
            else
            {
                _statistics.FailedSelections++;
            }

            // 更新平均选择时间
            UpdateAverageSelectionTime(stopwatch.Elapsed);

            _logger.LogDebug("实例选择完成: {Success}, 耗时: {Duration}ms, 候选数量: {CandidateCount}",
                result.IsSuccess, stopwatch.Elapsed.TotalMilliseconds, eligibleInstances.Count);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _statistics.FailedSelections++;

            _logger.LogError(ex, "选择服务实例失败: {LoadBalancerName}", Name);
            return LoadBalancingResult.Failure($"选择失败: {ex.Message}", context.AvailableInstances.Count);
        }
    }

    /// <summary>
    /// 报告请求结果
    /// </summary>
    public virtual async Task ReportRequestResultAsync(string instanceId, bool success, TimeSpan responseTime, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        await Task.Yield(); // 异步操作

        try
        {
            var healthInfo = _instanceHealth.GetOrAdd(instanceId, _ => new InstanceHealthInfo { InstanceId = instanceId });

            lock (healthInfo)
            {
                if (success)
                {
                    healthInfo.SuccessfulRequests++;
                }
                else
                {
                    healthInfo.FailedRequests++;
                }

                // 更新平均响应时间（使用指数移动平均）
                const double alpha = 0.1; // 平滑因子
                healthInfo.AverageResponseTime = healthInfo.AverageResponseTime == 0
                    ? responseTime.TotalMilliseconds
                    : alpha * responseTime.TotalMilliseconds + (1 - alpha) * healthInfo.AverageResponseTime;

                // 检查健康状态
                var errorRate = healthInfo.ErrorRate;
                if (errorRate > _configuration.ErrorThreshold)
                {
                    healthInfo.HealthStatus = ServiceInstanceStatus.Unhealthy;
                }
                else if (errorRate > _configuration.ErrorThreshold / 2)
                {
                    healthInfo.HealthStatus = ServiceInstanceStatus.Unknown;
                }
                else
                {
                    healthInfo.HealthStatus = ServiceInstanceStatus.Healthy;
                }

                // 更新负载评分
                UpdateLoadScore(healthInfo);
            }

            _logger.LogTrace("报告请求结果: {InstanceId}, 成功: {Success}, 响应时间: {ResponseTime}ms",
                instanceId, success, responseTime.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "报告请求结果失败: {InstanceId}", instanceId);
        }
    }

    /// <summary>
    /// 报告实例健康状态
    /// </summary>
    public virtual async Task ReportInstanceHealthAsync(InstanceHealthInfo healthInfo, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        await Task.Yield(); // 异步操作

        try
        {
            _instanceHealth.AddOrUpdate(healthInfo.InstanceId, healthInfo, (_, existing) =>
            {
                lock (existing)
                {
                    existing.CurrentConnections = healthInfo.CurrentConnections;
                    existing.CpuUsage = healthInfo.CpuUsage;
                    existing.MemoryUsage = healthInfo.MemoryUsage;
                    existing.Weight = healthInfo.Weight;
                    existing.LastHealthCheck = DateTime.UtcNow;

                    // 更新负载评分
                    UpdateLoadScore(existing);

                    return existing;
                }
            });

            _logger.LogTrace("报告实例健康状态: {InstanceId}, 状态: {HealthStatus}",
                healthInfo.InstanceId, healthInfo.HealthStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "报告实例健康状态失败: {InstanceId}", healthInfo.InstanceId);
        }
    }

    /// <summary>
    /// 更新实例列表
    /// </summary>
    public virtual async Task UpdateInstancesAsync(IReadOnlyList<ServiceInstance> instances, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        await Task.Yield(); // 异步操作

        try
        {
            lock (_instancesLock)
            {
                _instances = instances;
                _statistics.ActiveInstanceCount = instances.Count;
            }

            // 初始化新实例的健康信息
            foreach (var instance in instances)
            {
                _instanceHealth.GetOrAdd(instance.Id, _ => new InstanceHealthInfo
                {
                    InstanceId = instance.Id,
                    Weight = instance.Weight,
                    HealthStatus = instance.Status,
                    LastHealthCheck = DateTime.UtcNow
                });
            }

            // 清理已移除实例的健康信息
            var instanceIds = new HashSet<string>(instances.Select(i => i.Id));
            var toRemove = _instanceHealth.Keys.Where(id => !instanceIds.Contains(id)).ToList();
            foreach (var id in toRemove)
            {
                _instanceHealth.TryRemove(id, out _);
                _instanceSelectionCounts.TryRemove(id, out _);
            }

            _logger.LogDebug("更新实例列表: {Count} 个实例", instances.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新实例列表失败");
        }
    }

    /// <summary>
    /// 获取负载均衡统计信息
    /// </summary>
    public virtual async Task<LoadBalancingStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield(); // 异步操作

        var statistics = new LoadBalancingStatistics
        {
            LoadBalancerName = _statistics.LoadBalancerName,
            Strategy = _statistics.Strategy,
            TotalSelections = _statistics.TotalSelections,
            SuccessfulSelections = _statistics.SuccessfulSelections,
            FailedSelections = _statistics.FailedSelections,
            AverageSelectionTime = _statistics.AverageSelectionTime,
            ActiveInstanceCount = _statistics.ActiveInstanceCount,
            LastUpdatedAt = DateTime.UtcNow,
            InstanceSelectionCounts = new Dictionary<string, long>(_instanceSelectionCounts),
            InstanceHealthStatus = new Dictionary<string, InstanceHealthInfo>()
        };

        // 复制健康状态信息
        foreach (var kvp in _instanceHealth)
        {
            statistics.InstanceHealthStatus[kvp.Key] = new InstanceHealthInfo
            {
                InstanceId = kvp.Value.InstanceId,
                CurrentConnections = kvp.Value.CurrentConnections,
                AverageResponseTime = kvp.Value.AverageResponseTime,
                SuccessfulRequests = kvp.Value.SuccessfulRequests,
                FailedRequests = kvp.Value.FailedRequests,
                LastHealthCheck = kvp.Value.LastHealthCheck,
                HealthStatus = kvp.Value.HealthStatus,
                CpuUsage = kvp.Value.CpuUsage,
                MemoryUsage = kvp.Value.MemoryUsage,
                Weight = kvp.Value.Weight,
                LoadScore = kvp.Value.LoadScore
            };
        }

        return statistics;
    }

    /// <summary>
    /// 重置统计信息
    /// </summary>
    public virtual async Task ResetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield(); // 异步操作

        _statistics.TotalSelections = 0;
        _statistics.SuccessfulSelections = 0;
        _statistics.FailedSelections = 0;
        _statistics.AverageSelectionTime = TimeSpan.Zero;

        _instanceSelectionCounts.Clear();

        foreach (var healthInfo in _instanceHealth.Values)
        {
            lock (healthInfo)
            {
                healthInfo.SuccessfulRequests = 0;
                healthInfo.FailedRequests = 0;
                healthInfo.AverageResponseTime = 0;
                healthInfo.LoadScore = 0;
            }
        }

        _logger.LogInformation("负载均衡统计信息已重置: {LoadBalancerName}", Name);
    }

    /// <summary>
    /// 内部选择实例实现（由具体策略实现）
    /// </summary>
    protected abstract Task<LoadBalancingResult> SelectInstanceInternalAsync(
        LoadBalancingContext context,
        IReadOnlyList<ServiceInstance> eligibleInstances);

    /// <summary>
    /// 过滤符合条件的实例
    /// </summary>
    protected virtual List<ServiceInstance> FilterEligibleInstances(LoadBalancingContext context)
    {
        var eligible = new List<ServiceInstance>();

        foreach (var instance in context.AvailableInstances)
        {
            // 检查排除列表
            if (context.ExcludedInstances.Contains(instance.Id))
            {
                continue;
            }

            // 检查实例状态
            if (instance.Status != ServiceInstanceStatus.Healthy)
            {
                continue;
            }

            // 检查健康信息
            if (_instanceHealth.TryGetValue(instance.Id, out var healthInfo))
            {
                if (healthInfo.HealthStatus == ServiceInstanceStatus.Unhealthy)
                {
                    continue;
                }

                // 检查连接数限制
                if (healthInfo.CurrentConnections >= _configuration.MaxConcurrentConnections)
                {
                    continue;
                }
            }

            // 检查区域偏好
            if (!string.IsNullOrEmpty(context.PreferredRegion) &&
                !string.Equals(instance.Region, context.PreferredRegion, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 检查可用区偏好
            if (!string.IsNullOrEmpty(context.PreferredZone) &&
                !string.Equals(instance.Zone, context.PreferredZone, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 检查版本偏好
            if (!string.IsNullOrEmpty(context.PreferredVersion) &&
                !string.Equals(instance.Version, context.PreferredVersion, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 检查标签过滤器
            bool tagMatches = true;
            foreach (var tagFilter in context.TagFilters)
            {
                if (!instance.Tags.TryGetValue(tagFilter.Key, out var tagValue) ||
                    !string.Equals(tagValue, tagFilter.Value, StringComparison.OrdinalIgnoreCase))
                {
                    tagMatches = false;
                    break;
                }
            }

            if (!tagMatches)
            {
                continue;
            }

            eligible.Add(instance);
        }

        return eligible;
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
            var cutoffTime = DateTime.UtcNow - TimeSpan.FromMinutes(5);

            foreach (var healthInfo in _instanceHealth.Values)
            {
                lock (healthInfo)
                {
                    // 检查最后健康检查时间
                    if (healthInfo.LastHealthCheck < cutoffTime)
                    {
                        healthInfo.HealthStatus = ServiceInstanceStatus.Unknown;
                    }

                    // 根据错误率调整健康状态
                    var errorRate = healthInfo.ErrorRate;
                    if (errorRate > _configuration.ErrorThreshold)
                    {
                        healthInfo.HealthStatus = ServiceInstanceStatus.Unhealthy;
                    }
                }
            }

            await Task.CompletedTask;
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
            _statistics.LastUpdatedAt = DateTime.UtcNow;

            lock (_instancesLock)
            {
                _statistics.ActiveInstanceCount = _instances.Count;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新统计信息失败");
        }
    }

    /// <summary>
    /// 更新平均选择时间
    /// </summary>
    protected void UpdateAverageSelectionTime(TimeSpan selectionTime)
    {
        const double alpha = 0.1; // 平滑因子
        var currentAverage = _statistics.AverageSelectionTime.TotalMilliseconds;
        var newAverage = currentAverage == 0
            ? selectionTime.TotalMilliseconds
            : alpha * selectionTime.TotalMilliseconds + (1 - alpha) * currentAverage;

        _statistics.AverageSelectionTime = TimeSpan.FromMilliseconds(newAverage);
    }

    /// <summary>
    /// 更新负载评分
    /// </summary>
    protected virtual void UpdateLoadScore(InstanceHealthInfo healthInfo)
    {
        // 基于多个因素计算负载评分（越低越好）
        var score = 0.0;

        // 响应时间因子
        if (healthInfo.AverageResponseTime > _configuration.ResponseTimeThreshold.TotalMilliseconds)
        {
            score += (healthInfo.AverageResponseTime / _configuration.ResponseTimeThreshold.TotalMilliseconds) * 30;
        }

        // 错误率因子
        score += healthInfo.ErrorRate * 0.5;

        // 连接数因子
        score += healthInfo.CurrentConnections * 0.1;

        // 资源使用率因子
        if (healthInfo.CpuUsage.HasValue)
        {
            score += healthInfo.CpuUsage.Value * 0.2;
        }

        if (healthInfo.MemoryUsage.HasValue)
        {
            score += healthInfo.MemoryUsage.Value * 0.1;
        }

        healthInfo.LoadScore = score;
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

        _logger.LogInformation("正在关闭负载均衡器: {LoadBalancerName}", Name);

        _healthCheckTimer?.Dispose();
        _statisticsTimer?.Dispose();

        _instanceHealth.Clear();
        _instanceSelectionCounts.Clear();

        _logger.LogInformation("负载均衡器已关闭: {LoadBalancerName}", Name);
    }
}