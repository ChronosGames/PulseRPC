using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseServiceDiscovery.Abstractions;
using PulseServiceDiscovery.Abstractions.Events;
using PulseServiceDiscovery.Abstractions.Models;
using PulseServiceDiscovery.Server.Options;
using PulseServiceDiscovery.Server.Storage;
using System.Collections.Concurrent;

namespace PulseServiceDiscovery.Server.Services;

/// <summary>
/// 服务注册器实现
/// </summary>
public class ServiceRegistry : IServiceRegistry
{
    private readonly IServiceStorage _storage;
    private readonly ILogger<ServiceRegistry> _logger;
    private readonly ServerOptions _options;
    private readonly ConcurrentDictionary<string, DateTime> _lastHeartbeat = new();

    // 事件
    public event Func<ServiceRegisteredEvent, Task>? ServiceRegistered;
    public event Func<ServiceUnregisteredEvent, Task>? ServiceUnregistered;
    public event Func<ServiceHealthChangedEvent, Task>? ServiceHealthChanged;

    public ServiceRegistry(
        IServiceStorage storage,
        ILogger<ServiceRegistry> logger,
        IOptions<ServerOptions> options)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task RegisterAsync(
        ServiceRegistration registration,
        CancellationToken cancellationToken = default)
    {
        if (registration == null)
            throw new ArgumentNullException(nameof(registration));

        ValidateRegistration(registration);

        if (string.IsNullOrEmpty(registration.Id))
        {
            registration.Id = GenerateServiceId(registration);
        }

        registration.RegisteredAt = DateTime.UtcNow;
        registration.LastHeartbeat = DateTime.UtcNow;

        await _storage.StoreServiceAsync(registration, cancellationToken);
        _lastHeartbeat[registration.Id] = DateTime.UtcNow;

        _logger.LogInformation("Service registered: {ServiceName} at {Host}:{Port} (ID: {ServiceId})",
            registration.ServiceName, registration.Host, registration.Port, registration.Id);

        await TriggerServiceRegisteredEvent(registration);
    }

    public async Task UnregisterAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        var registration = await _storage.GetServiceAsync(serviceId, cancellationToken);
        if (registration == null)
        {
            _logger.LogWarning("Attempted to unregister non-existent service: {ServiceId}", serviceId);
            return;
        }

        await _storage.RemoveServiceAsync(serviceId, cancellationToken);
        _lastHeartbeat.TryRemove(serviceId, out _);

        _logger.LogInformation("Service unregistered: {ServiceName} (ID: {ServiceId})",
            registration.ServiceName, serviceId);

        await TriggerServiceUnregisteredEvent(registration);
    }

    public async Task<ServiceRegistration?> UpdateAsync(
        string serviceId,
        ServiceRegistration registration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        if (registration == null)
            throw new ArgumentNullException(nameof(registration));

        var existingRegistration = await _storage.GetServiceAsync(serviceId, cancellationToken);
        if (existingRegistration == null)
        {
            _logger.LogWarning("Attempted to update non-existent service: {ServiceId}", serviceId);
            return null;
        }

        ValidateRegistration(registration);

        registration.Id = serviceId;
        registration.RegisteredAt = existingRegistration.RegisteredAt;
        registration.LastHeartbeat = DateTime.UtcNow;

        await _storage.StoreServiceAsync(registration, cancellationToken);
        _lastHeartbeat[serviceId] = DateTime.UtcNow;

        _logger.LogInformation("Service updated: {ServiceName} (ID: {ServiceId})",
            registration.ServiceName, serviceId);

        return registration;
    }

    public async Task HeartbeatAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        var registration = await _storage.GetServiceAsync(serviceId, cancellationToken);
        if (registration == null)
        {
            _logger.LogWarning("Heartbeat received for non-existent service: {ServiceId}", serviceId);
            return;
        }

        registration.LastHeartbeat = DateTime.UtcNow;
        await _storage.StoreServiceAsync(registration, cancellationToken);
        _lastHeartbeat[serviceId] = DateTime.UtcNow;

        _logger.LogDebug("Heartbeat received for service: {ServiceName} (ID: {ServiceId})",
            registration.ServiceName, serviceId);
    }

    public async Task UpdateHealthAsync(string serviceId, HealthStatus status, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        var registration = await _storage.GetServiceAsync(serviceId, cancellationToken);
        if (registration == null)
        {
            _logger.LogWarning("Attempted to update health for non-existent service: {ServiceId}", serviceId);
            return;
        }

        var endpoint = registration.ToEndpoint();
        var oldStatus = endpoint.Health;
        
        registration.LastHeartbeat = DateTime.UtcNow;
        await _storage.StoreServiceAsync(registration, cancellationToken);
        _lastHeartbeat[serviceId] = DateTime.UtcNow;

        _logger.LogInformation("Service health updated: {ServiceName} (ID: {ServiceId}) from {OldStatus} to {NewStatus}",
            registration.ServiceName, serviceId, oldStatus, status);

        // 如果健康状态发生变化，触发事件
        if (oldStatus != status)
        {
            await TriggerServiceHealthChangedEvent(registration, oldStatus, status);
        }
    }

    public async Task<ServiceRegistration?> GetServiceAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        return await _storage.GetServiceAsync(serviceId, cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceRegistration>> GetServicesAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        return await _storage.GetServicesByNameAsync(serviceName, cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceRegistration>> GetAllServicesAsync(CancellationToken cancellationToken = default)
    {
        return await _storage.GetAllServicesAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        var registration = await _storage.GetServiceAsync(serviceId, cancellationToken);
        return registration != null;
    }

    /// <summary>
    /// 清理过期的服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task CleanupExpiredServicesAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Cleanup.Enabled)
            return;

        try
        {
            var allServices = await _storage.GetAllServicesAsync(cancellationToken);
            var expiredServices = new List<ServiceRegistration>();
            var expiredThreshold = DateTime.UtcNow - _options.Cleanup.ServiceExpiration;

            foreach (var service in allServices)
            {
                if (service.LastHeartbeat < expiredThreshold)
                {
                    expiredServices.Add(service);
                }
            }

            if (expiredServices.Any())
            {
                _logger.LogInformation("Cleaning up {Count} expired services", expiredServices.Count);

                foreach (var service in expiredServices)
                {
                    await UnregisterAsync(service.Id, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired services");
        }
    }

    /// <summary>
    /// 获取注册统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    public async Task<Dictionary<string, object>> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var allServices = await _storage.GetAllServicesAsync(cancellationToken);
            var servicesByName = allServices.GroupBy(s => s.ServiceName)
                .ToDictionary(g => g.Key, g => g.Count());

            return new Dictionary<string, object>
            {
                ["TotalServices"] = allServices.Count,
                ["ServiceTypes"] = servicesByName.Count,
                ["ServicesByName"] = servicesByName,
                ["ActiveHeartbeats"] = _lastHeartbeat.Count,
                ["StorageType"] = _storage.GetType().Name
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get statistics");
            return new Dictionary<string, object>();
        }
    }

    private void ValidateRegistration(ServiceRegistration registration)
    {
        if (string.IsNullOrWhiteSpace(registration.ServiceName))
            throw new ArgumentException("Service name cannot be null or empty");

        if (string.IsNullOrWhiteSpace(registration.Host))
            throw new ArgumentException("Service host cannot be null or empty");

        if (registration.Port <= 0 || registration.Port > 65535)
            throw new ArgumentException("Service port must be between 1 and 65535");
    }

    private static string GenerateServiceId(ServiceRegistration registration)
    {
        var baseId = $"{registration.ServiceName}:{registration.Host}:{registration.Port}";
        return $"{baseId}:{Guid.NewGuid():N}";
    }

    private async Task TriggerServiceRegisteredEvent(ServiceRegistration registration)
    {
        if (ServiceRegistered == null) return;

        try
        {
            var eventArgs = ServiceRegisteredEvent.CreateSuccess(registration, "ServiceRegistry");
            await ServiceRegistered.Invoke(eventArgs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering ServiceRegistered event for service: {ServiceId}", registration.Id);
        }
    }

    private async Task TriggerServiceUnregisteredEvent(ServiceRegistration registration)
    {
        if (ServiceUnregistered == null) return;

        try
        {
            var eventArgs = new ServiceUnregisteredEvent
            {
                ServiceName = registration.ServiceName,
                ServiceId = registration.Id,
                Endpoint = registration.ToEndpoint(),
                Timestamp = DateTime.UtcNow
            };

            await ServiceUnregistered.Invoke(eventArgs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering ServiceUnregistered event for service: {ServiceId}", registration.Id);
        }
    }

    private async Task TriggerServiceHealthChangedEvent(ServiceRegistration registration, HealthStatus oldStatus, HealthStatus newStatus)
    {
        if (ServiceHealthChanged == null) return;

        try
        {
            var eventArgs = new ServiceHealthChangedEvent
            {
                ServiceId = registration.Id,
                ServiceName = registration.ServiceName,
                Endpoint = registration.ToEndpoint(newStatus),
                OldStatus = oldStatus,
                NewStatus = newStatus,
                CheckTime = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow
            };

            await ServiceHealthChanged.Invoke(eventArgs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering ServiceHealthChanged event for service: {ServiceId}", registration.Id);
        }
    }

    public async Task<IReadOnlyList<ServiceRegistration>> GetRegistrationsAsync(CancellationToken cancellationToken = default)
    {
        return await GetAllServicesAsync(cancellationToken);
    }
}
