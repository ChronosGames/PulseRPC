using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using PulseRPC.Infrastructure;
using PulseRPC.HealthCheck;
using PulseRPC.Infrastructure.Extensions;
using PulseRPC.ServiceDiscovery;

namespace PulseRPC.ServiceRegistration;

/// <summary>
/// 服务注册器实现
/// </summary>
public class ServiceRegistry(
    ILogger<ServiceRegistry> logger,
    IOptions<ServiceRegistrationOptions> options)
    : IServiceRegistry
{
    private readonly ILogger<ServiceRegistry> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ServiceRegistrationOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ConcurrentDictionary<string, DateTime> _lastHeartbeat = new();
    private readonly ConcurrentDictionary<string, ServiceRegistration> _registrations = new();

    // 事件
    public event Func<ServiceRegisteredEvent, Task>? ServiceRegistered;
    public event Func<ServiceUnregisteredEvent, Task>? ServiceUnregistered;
    public event Func<ServiceHealthChangedEvent, Task>? ServiceHealthChanged;

    public async Task RegisterAsync(ServiceRegistration registration, CancellationToken cancellationToken = default)
    {
        ValidateRegistration(registration);

        if (string.IsNullOrEmpty(registration.Id))
        {
            registration.Id = GenerateServiceId(registration);
        }

        _registrations[registration.Id] = registration;
        _lastHeartbeat[registration.Id] = DateTime.UtcNow;

        _logger.LogInformation("Service registered: {ServiceType} at {Host}:{Port} (ID: {ServiceId})",
            registration.ServiceType, registration.Host, registration.Port, registration.Id);

        await TriggerServiceRegisteredEvent(registration);
    }

    public async Task RegisterAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var registration = new ServiceRegistration
        {
            Id = endpoint.ServiceId,
            ServiceType = endpoint.ServiceType,
            Host = endpoint.Channel.Address.Host,
            Port = endpoint.Channel.Address.Port,
            Metadata = endpoint.Metadata,
            Status = endpoint.Health,
            RegisteredAt = DateTime.UtcNow,
            LastHeartbeat = DateTime.UtcNow
        };

        await RegisterAsync(registration, cancellationToken);
    }

    public async Task<IReadOnlyList<PulseRPC.ServiceDiscovery.ServiceEndpoint>> GetRegisteredServicesAsync(CancellationToken cancellationToken = default)
    {
        return _registrations.Values
            .Select(r => new PulseRPC.ServiceDiscovery.ServiceEndpoint
            {
                ServiceId = r.Id,
                ServiceType = r.ServiceType,
                Host = r.Host,
                Port = r.Port,
                Protocol = "Tcp",
                Weight = 100, // 默认权重
                IsHealthy = r.Status == HealthStatus.Healthy,
                RegisteredAt = r.RegisteredAt,
                LastHealthCheck = r.LastHeartbeat,
                Metadata = r.Metadata.Properties.ToDictionary(
                    kvp => kvp.Key, 
                    kvp => kvp.Value?.ToString() ?? "")
            })
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<ServiceRegistration>> GetRegistrationsAsync(CancellationToken cancellationToken = default)
    {
        return _registrations.Values.ToList().AsReadOnly();
    }

    public async Task<ServiceRegistration?> GetServiceAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        _registrations.TryGetValue(serviceId, out var registration);
        return registration;
    }

    public async Task<IReadOnlyList<ServiceRegistration>> GetServicesAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        return _registrations.Values
            .Where(r => r.ServiceType == serviceName)
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<ServiceRegistration>> GetAllServicesAsync(CancellationToken cancellationToken = default)
    {
        return _registrations.Values.ToList().AsReadOnly();
    }

    public async Task<bool> ExistsAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        return _registrations.ContainsKey(serviceId);
    }

    public async Task UnregisterAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        if (_registrations.TryRemove(serviceId, out var registration))
        {
            _lastHeartbeat.TryRemove(serviceId, out _);

            _logger.LogInformation("Service unregistered: {ServiceType} (ID: {ServiceId})",
                registration.ServiceType, serviceId);

            await TriggerServiceUnregisteredEvent(registration);
        }
        else
        {
            _logger.LogWarning("Attempted to unregister non-existent service: {ServiceId}", serviceId);
        }
    }

    public async Task UpdateHealthAsync(string serviceId, HealthStatus status, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        if (_registrations.TryGetValue(serviceId, out var registration))
        {
            var oldStatus = registration.Status;
            registration.Status = status;
            registration.LastHeartbeat = DateTime.UtcNow;
            _lastHeartbeat[serviceId] = DateTime.UtcNow;

            _logger.LogInformation("Service health updated: {ServiceType} (ID: {ServiceId}) from {OldStatus} to {NewStatus}",
                registration.ServiceType, serviceId, oldStatus, status);

            if (oldStatus != status)
            {
                await TriggerServiceHealthChangedEvent(registration, oldStatus, status);
            }
        }
        else
        {
            _logger.LogWarning("Attempted to update health for non-existent service: {ServiceId}", serviceId);
        }
    }

    public async Task HeartbeatAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        if (_registrations.TryGetValue(serviceId, out var registration))
        {
            registration.LastHeartbeat = DateTime.UtcNow;
            _lastHeartbeat[serviceId] = DateTime.UtcNow;

            _logger.LogDebug("Heartbeat received for service: {ServiceType} (ID: {ServiceId})",
                registration.ServiceType, serviceId);
        }
        else
        {
            _logger.LogWarning("Heartbeat received for non-existent service: {ServiceId}", serviceId);
        }
    }

    public async Task CleanupExpiredServicesAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.CleanupOptions.Enabled)
            return;

        try
        {
            var expiredThreshold = DateTime.UtcNow - _options.ServiceExpiration;
            var expiredServices = _registrations.Values
                .Where(s => s.LastHeartbeat < expiredThreshold)
                .ToList();

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

    public async Task<Dictionary<string, object>> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var servicesByName = _registrations.Values
                .GroupBy(s => s.ServiceType)
                .ToDictionary(g => g.Key, g => g.Count());

            return new Dictionary<string, object>
            {
                ["TotalServices"] = _registrations.Count,
                ["ServiceTypes"] = servicesByName.Count,
                ["ServicesByName"] = servicesByName,
                ["ActiveHeartbeats"] = _lastHeartbeat.Count
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
        if (string.IsNullOrWhiteSpace(registration.ServiceType))
            throw new ArgumentException("Service name cannot be null or empty", nameof(registration.ServiceType));

        if (string.IsNullOrWhiteSpace(registration.Host))
            throw new ArgumentException("Host cannot be null or empty", nameof(registration.Host));

        if (registration.Port <= 0 || registration.Port > 65535)
            throw new ArgumentException("Port must be between 1 and 65535", nameof(registration.Port));
    }

    private static string GenerateServiceId(ServiceRegistration registration)
    {
        return $"{registration.ServiceType}-{registration.Host}-{registration.Port}-{Guid.NewGuid():N}";
    }

    private async Task TriggerServiceRegisteredEvent(ServiceRegistration registration)
    {
        if (ServiceRegistered != null)
        {
            var evt = ServiceRegisteredEvent.CreateSuccess(registration.ToEndpoint(registration.Status));
            await ServiceRegistered(evt);
        }
    }

    private async Task TriggerServiceUnregisteredEvent(ServiceRegistration registration)
    {
        if (ServiceUnregistered != null)
        {
            var evt = ServiceUnregisteredEvent.CreateSuccess(registration.ToEndpoint(registration.Status));
            await ServiceUnregistered(evt);
        }
    }

    private async Task TriggerServiceHealthChangedEvent(ServiceRegistration registration, HealthStatus oldStatus, HealthStatus newStatus)
    {
        if (ServiceHealthChanged != null)
        {
            var evt = ServiceHealthChangedEvent.Create(registration.ToEndpoint(registration.Status), oldStatus, newStatus);
            await ServiceHealthChanged(evt);
        }
    }
}
