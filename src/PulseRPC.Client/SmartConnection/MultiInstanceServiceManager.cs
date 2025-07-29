using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Routing;
using PulseRPC.SmartConnection;

namespace PulseRPC.Client.SmartConnection;

/// <summary>
/// 多实例服务管理器实现
/// </summary>
public class MultiInstanceServiceManager<T> : IMultiInstanceServiceManager<T> where T : class, IPulseService
{
    private readonly string _serviceName;
    private readonly MultiInstanceConnectionManager _connectionManager;
    private readonly SmartConnectionOptions _defaultOptions;
    private readonly ILogger<MultiInstanceServiceManager<T>> _logger;
    private bool _disposed;

    public MultiInstanceServiceManager(
        string serviceName,
        MultiInstanceConnectionManager connectionManager, 
        SmartConnectionOptions defaultOptions,
        ILoggerFactory? loggerFactory = null)
    {
        _serviceName = serviceName;
        _connectionManager = connectionManager;
        _defaultOptions = defaultOptions;
        _logger = loggerFactory?.CreateLogger<MultiInstanceServiceManager<T>>() ?? 
                   Microsoft.Extensions.Logging.Abstractions.NullLogger<MultiInstanceServiceManager<T>>.Instance;
    }

    public async Task<IReadOnlyList<ServiceInstanceInfo>> GetAvailableInstancesAsync()
    {
        // 简化实现 - 返回模拟的服务实例
        return new List<ServiceInstanceInfo>
        {
            new ServiceInstanceInfo
            {
                InstanceId = $"{_serviceName}-1",
                ServiceName = _serviceName,
                Endpoint = new ServiceEndpoint { Host = "localhost", Port = 8000 },
                Weight = 100,
                IsHealthy = true,
                Region = "default",
                Zone = "zone-a"
            },
            new ServiceInstanceInfo
            {
                InstanceId = $"{_serviceName}-2",
                ServiceName = _serviceName,
                Endpoint = new ServiceEndpoint { Host = "localhost", Port = 8001 },
                Weight = 100,
                IsHealthy = true,
                Region = "default",
                Zone = "zone-b"
            }
        };
    }

    public async Task<T> GetServiceAsync(IRoutingContext routingContext)
    {
        return await _connectionManager.GetServiceAsync<T>(_serviceName, routingContext, _defaultOptions);
    }

    public async Task<T> GetServiceAsync(string instanceId)
    {
        return await _connectionManager.GetServiceAsync<T>(_serviceName, instanceId, _defaultOptions);
    }

    public async Task<BroadcastResult<TResult>> BroadcastAsync<TResult>(Func<T, Task<TResult>> operation)
    {
        var instances = await GetAvailableInstancesAsync();
        var tasks = new List<Task<BroadcastResultItem<TResult>>>();

        foreach (var instance in instances.Where(i => i.IsHealthy))
        {
            tasks.Add(ExecuteOnInstanceAsync(instance.InstanceId, operation));
        }

        var results = await Task.WhenAll(tasks);
        
        return new BroadcastResult<TResult>
        {
            Results = results.ToList(),
            SuccessCount = results.Count(r => r.IsSuccess),
            FailureCount = results.Count(r => !r.IsSuccess)
        };
    }

    private async Task<BroadcastResultItem<TResult>> ExecuteOnInstanceAsync<TResult>(
        string instanceId, 
        Func<T, Task<TResult>> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var service = await GetServiceAsync(instanceId);
            var result = await operation(service);
            
            return new BroadcastResultItem<TResult>
            {
                InstanceId = instanceId,
                IsSuccess = true,
                Result = result,
                Duration = stopwatch.Elapsed,
                CompletedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "广播操作在实例 {InstanceId} 上失败", instanceId);
            
            return new BroadcastResultItem<TResult>
            {
                InstanceId = instanceId,
                IsSuccess = false,
                Error = ex,
                Duration = stopwatch.Elapsed,
                CompletedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<TAggregated> AggregateAsync<TResult, TAggregated>(
        Func<T, Task<TResult>> operation, 
        Func<IEnumerable<TResult>, TAggregated> aggregator)
    {
        var broadcastResult = await BroadcastAsync(operation);
        var successResults = broadcastResult.Results
            .Where(r => r.IsSuccess && r.Result != null)
            .Select(r => r.Result!);
        
        return aggregator(successResults);
    }

    public async Task<ParallelResult<TResult>> ParallelAsync<TResult>(
        Func<T, Task<TResult>> operation,
        IEnumerable<string> instanceIds)
    {
        var targetInstanceIds = instanceIds.ToList();
        var availableInstances = await GetAvailableInstancesAsync();
        var availableInstanceIds = availableInstances.Select(i => i.InstanceId).ToHashSet();
        
        var notFoundInstanceIds = targetInstanceIds.Where(id => !availableInstanceIds.Contains(id)).ToList();
        var validInstanceIds = targetInstanceIds.Where(id => availableInstanceIds.Contains(id)).ToList();

        var tasks = validInstanceIds.Select(instanceId => ExecuteOnInstanceAsync(instanceId, operation));
        var results = await Task.WhenAll(tasks);

        return new ParallelResult<TResult>
        {
            Results = results.ToList(),
            SuccessCount = results.Count(r => r.IsSuccess),
            FailureCount = results.Count(r => !r.IsSuccess),
            TargetInstanceIds = targetInstanceIds,
            NotFoundInstanceIds = notFoundInstanceIds
        };
    }

    public async Task<Dictionary<string, bool>> GetInstanceHealthAsync()
    {
        var instances = await GetAvailableInstancesAsync();
        var healthStatus = new Dictionary<string, bool>();

        foreach (var instance in instances)
        {
            healthStatus[instance.InstanceId] = instance.IsHealthy;
        }

        return healthStatus;
    }

    public event EventHandler<ServiceInstanceEventArgs>? InstanceStateChanged;
    public event EventHandler<LoadBalancingEventArgs>? LoadBalancingChanged;

    protected virtual void OnInstanceStateChanged(ServiceInstanceEventArgs e)
    {
        InstanceStateChanged?.Invoke(this, e);
    }

    protected virtual void OnLoadBalancingChanged(LoadBalancingEventArgs e)
    {
        LoadBalancingChanged?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 清理事件订阅
        InstanceStateChanged = null;
        LoadBalancingChanged = null;
    }
} 