using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server;

/// <summary>
/// 服务注册中心实现
/// </summary>
public class NetCoreServiceRegistry : IServiceRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentServiceCollection> _serviceCollections = new();
    private readonly Timer _healthCheckTimer;
    private readonly TimeSpan _heartbeatTimeout;
    private readonly ILogger? _logger;

    public NetCoreServiceRegistry(TimeSpan? heartbeatTimeout = null, ILogger? logger = null)
    {
        _heartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromSeconds(30);
        _logger = logger;

        // 启动健康检查定时器
        _healthCheckTimer = new Timer(CheckServiceHealth, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public Task RegisterServiceAsync(ServiceRegistration registration)
    {
        if (registration == null)
            throw new ArgumentNullException(nameof(registration));

        var serviceKey = GetServiceKey(registration);
        var serviceType = registration.ServiceType;

        var collection = _serviceCollections.GetOrAdd(serviceType, _ => new ConcurrentServiceCollection());
        var success = collection.TryAdd(serviceKey, registration);

        if (success)
        {
            _logger?.LogInformation($"Service registered: {serviceKey}");
        }

        return Task.CompletedTask;
    }

    public Task UnregisterServiceAsync(string serviceType, string serviceId)
    {
        var serviceKey = $"{serviceType}:{serviceId}";

        if (_serviceCollections.TryGetValue(serviceType, out var collection))
        {
            if (collection.TryRemove(serviceKey, out _))
            {
                _logger?.LogInformation($"Service unregistered: {serviceKey}");
            }

            // 如果该类型的服务集合为空,则移除该类型
            if (collection.GetAll().Count == 0)
            {
                _serviceCollections.TryRemove(serviceType, out _);
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> UpdateHeartbeatAsync(string serviceType, string serviceId)
    {
        var serviceKey = $"{serviceType}:{serviceId}";

        if (_serviceCollections.TryGetValue(serviceType, out var collection))
        {
            return Task.FromResult(collection.UpdateHeartbeat(serviceKey));
        }

        return Task.FromResult(false);
    }

    public Task<List<ServiceRegistration>> GetServicesAsync(string serviceType)
    {
        if (_serviceCollections.TryGetValue(serviceType, out var collection))
        {
            return Task.FromResult(collection.GetAll());
        }

        return Task.FromResult(new List<ServiceRegistration>());
    }

    public Task<ServiceRegistration?> GetServiceAsync(string serviceType, string serviceId)
    {
        var serviceKey = $"{serviceType}:{serviceId}";

        if (_serviceCollections.TryGetValue(serviceType, out var collection) &&
            collection.TryGetValue(serviceKey, out var registration))
        {
            return Task.FromResult(registration);
        }

        return Task.FromResult<ServiceRegistration?>(null);
    }

    private async void CheckServiceHealth(object? state)
    {
        try
        {
            foreach (var kvp in _serviceCollections)
            {
                var serviceType = kvp.Key;
                var collection = kvp.Value;
                var expiredServices = collection.GetExpiredServices(_heartbeatTimeout);

                foreach (var expired in expiredServices)
                {
                    var serviceKey = expired.serviceKey;
                    var registration = expired.registration;
                    await UnregisterServiceAsync(serviceType, GetServiceId(registration));
                    _logger?.LogWarning($"Service removed due to heartbeat timeout: {serviceKey}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error checking service health");
        }
    }

    private string GetServiceKey(ServiceRegistration registration)
    {
        return $"{registration.ServiceType}:{GetServiceId(registration)}";
    }

    private string GetServiceId(ServiceRegistration registration)
    {
        // 根据不同服务类型构建服务ID
        if (!string.IsNullOrEmpty(registration.InstanceId))
        {
            // 使用实例ID (例如BattleServer)
            return $"{registration.ZoneId}:{registration.InstanceId}";
        }
        else if (!string.IsNullOrEmpty(registration.ServerId))
        {
            // 使用服务器ID (例如GameServer)
            return $"{registration.ZoneId}:{registration.ServerId}";
        }
        else
        {
            // 使用主机和端口 (通用服务)
            return $"{registration.ZoneId}:{registration.Host}:{registration.Port}";
        }
    }

    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
        foreach (var collection in _serviceCollections.Values)
        {
            collection.Dispose();
        }
    }
}
