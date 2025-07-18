using Consul;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using PulseRPC.HealthCheck;
using PulseRPC.ServiceRegistration;
using PulseRPC.Infrastructure.Consul;

namespace PulseRPC.Infrastructure.Consul;

public class ConsulServiceDiscovery : IServiceDiscovery, IDisposable
{
    private readonly IConsulClient _consulClient;
    private readonly ILogger<ConsulServiceDiscovery> _logger;
    private readonly ConsulOptions _options;
    private readonly Timer? _healthCheckTimer;
    private readonly string _instanceId;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public ConsulServiceDiscovery(
        IConsulClient consulClient,
        ILogger<ConsulServiceDiscovery> logger,
        IOptions<ConsulOptions> options)
    {
        _consulClient = consulClient ?? throw new ArgumentNullException(nameof(consulClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _instanceId = Environment.MachineName + "-" + Environment.ProcessId;

        if (_options.HealthCheck.Enabled)
        {
            _healthCheckTimer = new Timer(PerformHealthCheck, null,
                _options.HealthCheck.Interval, _options.HealthCheck.Interval);
        }

        _logger.LogInformation("Consul service discovery initialized with endpoint: {Endpoint}",
            _options.Endpoint);
    }

    public async Task<IReadOnlyList<ServiceEndpoint>> DiscoverServicesAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var healthyOnly = _options.DiscoveryOptions.HealthyOnly;
            var response = await _consulClient.Health.Service(serviceName, string.Empty, healthyOnly, cancellationToken);

            var endpoints = new List<ServiceEndpoint>();

            foreach (var service in response.Response)
            {
                try
                {
                    var endpoint = ConvertToServiceEndpoint(service);
                    if (endpoint != null)
                    {
                        endpoints.Add(endpoint);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to convert Consul service to endpoint: {ServiceId}",
                        service.Service?.ID);
                }
            }

            _logger.LogDebug("Discovered {Count} endpoints for service: {ServiceName}",
                endpoints.Count, serviceName);

            return endpoints.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover services for: {ServiceName}", serviceName);
            throw;
        }
    }

    public async Task RegisterAsync(ServiceRegistration.ServiceRegistration registration, CancellationToken cancellationToken = default)
    {
        if (registration == null)
            throw new ArgumentNullException(nameof(registration));

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            var consulRegistration = ConvertToConsulRegistration(registration);
            await _consulClient.Agent.ServiceRegister(consulRegistration, cancellationToken);

            _logger.LogInformation("Registered service: {ServiceName} (ID: {ServiceId})",
                registration.ServiceName, registration.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register service: {ServiceName} (ID: {ServiceId})",
                registration.ServiceName, registration.Id);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task UnregisterAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            await _consulClient.Agent.ServiceDeregister(serviceId, cancellationToken);

            _logger.LogInformation("Unregistered service: {ServiceId}", serviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister service: {ServiceId}", serviceId);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<ServiceRegistration.ServiceRegistration?> GetServiceAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _consulClient.Agent.Services(cancellationToken);

            if (response.Response.TryGetValue(serviceId, out var service))
            {
                return ConvertToServiceRegistration(service);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get service: {ServiceId}", serviceId);
            throw;
        }
    }

    public async Task<IReadOnlyList<ServiceRegistration.ServiceRegistration>> GetAllServicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _consulClient.Agent.Services(cancellationToken);
            var registrations = new List<ServiceRegistration.ServiceRegistration>();

            foreach (var service in response.Response.Values)
            {
                try
                {
                    var registration = ConvertToServiceRegistration(service);
                    if (registration != null)
                    {
                        registrations.Add(registration);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to convert service: {ServiceId}", service.ID);
                }
            }

            return registrations.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all services");
            throw;
        }
    }

    public async Task UpdateHeartbeatAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        try
        {
            await _consulClient.Agent.PassTTL($"service:{serviceId}", "Heartbeat", cancellationToken);
            _logger.LogDebug("Updated heartbeat for service: {ServiceId}", serviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update heartbeat for service: {ServiceId}", serviceId);
            throw;
        }
    }

    private ServiceEndpoint? ConvertToServiceEndpoint(ServiceEntry consulService)
    {
        var service = consulService.Service;
        if (service == null) return null;

        var metadata = new Dictionary<string, string>();

        if (service.Meta != null)
        {
            foreach (var meta in service.Meta)
            {
                metadata[meta.Key] = meta.Value;
            }
        }

        // 尝试从metadata中获取权重
        var weight = 1;
        if (metadata.TryGetValue("weight", out var weightStr) && int.TryParse(weightStr, out var parsedWeight))
        {
            weight = parsedWeight;
        }

        // 构建健康检查配置
        HealthCheckConfig? healthCheck = null;
        if (metadata.TryGetValue("healthcheck", out var healthCheckJson))
        {
            try
            {
                healthCheck = JsonSerializer.Deserialize<HealthCheckConfig>(healthCheckJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize health check config for service: {ServiceId}", service.ID);
            }
        }

        return new ServiceEndpoint
        {
            Id = service.ID,
            Host = service.Address,
            Port = service.Port,
            Weight = weight,
            Metadata = new ServiceMetadata(metadata),
            HealthCheck = healthCheck
        };
    }

    private ServiceRegistration.ServiceRegistration? ConvertToServiceRegistration(AgentService consulService)
    {
        var metadata = new Dictionary<string, string>();

        if (consulService.Meta != null)
        {
            foreach (var meta in consulService.Meta)
            {
                metadata[meta.Key] = meta.Value;
            }
        }

        var endpoint = new ServiceEndpoint
        {
            Id = consulService.ID,
            Host = consulService.Address,
            Port = consulService.Port,
            Weight = 1,
            Metadata = new ServiceMetadata(metadata)
        };

        return new ServiceRegistration.ServiceRegistration
        {
            Id = consulService.ID,
            ServiceName = consulService.Service,
            Endpoint = endpoint,
            RegisteredAt = DateTime.UtcNow,
            LastHeartbeat = DateTime.UtcNow
        };
    }

    private AgentServiceRegistration ConvertToConsulRegistration(ServiceRegistration.ServiceRegistration registration)
    {
        var metadata = new Dictionary<string, string>();

        if (registration.Endpoint.Metadata.Properties != null)
        {
            foreach (var prop in registration.Endpoint.Metadata.Properties)
            {
                metadata[prop.Key] = prop.Value?.ToString() ?? string.Empty;
            }
        }

        // 添加权重到metadata
        metadata["weight"] = registration.Endpoint.Weight.ToString();

        // 添加健康检查配置到metadata
        if (registration.Endpoint.HealthCheck != null)
        {
            metadata["healthcheck"] = JsonSerializer.Serialize(registration.Endpoint.HealthCheck);
        }

        var consulRegistration = new AgentServiceRegistration
        {
            ID = registration.Id,
            Name = registration.ServiceName,
            Address = registration.Endpoint.Host,
            Port = registration.Endpoint.Port,
            Meta = metadata,
            Tags = new[] { $"version:{metadata.GetValueOrDefault("version", "1.0.0")}" }
        };

        // 配置健康检查
        if (registration.Endpoint.HealthCheck != null)
        {
            var healthCheck = registration.Endpoint.HealthCheck;

            if (healthCheck.Type == "HTTP" && !string.IsNullOrWhiteSpace(healthCheck.Path))
            {
                consulRegistration.Check = new AgentServiceCheck
                {
                    HTTP = $"http://{registration.Endpoint.Host}:{registration.Endpoint.Port}{healthCheck.Path}",
                    Interval = healthCheck.Interval,
                    Timeout = healthCheck.Timeout,
                    DeregisterCriticalServiceAfter = _options.HealthCheck.DeregisterAfter
                };
            }
            else if (healthCheck.Type == "TCP")
            {
                consulRegistration.Check = new AgentServiceCheck
                {
                    TCP = $"{registration.Endpoint.Host}:{registration.Endpoint.Port}",
                    Interval = healthCheck.Interval,
                    Timeout = healthCheck.Timeout,
                    DeregisterCriticalServiceAfter = _options.HealthCheck.DeregisterAfter
                };
            }
            else if (healthCheck.Type == "TTL")
            {
                consulRegistration.Check = new AgentServiceCheck
                {
                    TTL = healthCheck.Interval,
                    DeregisterCriticalServiceAfter = _options.HealthCheck.DeregisterAfter
                };
            }
        }

        return consulRegistration;
    }

    private async void PerformHealthCheck(object? state)
    {
        try
        {
            // 这里可以实现自定义的健康检查逻辑
            // 例如检查数据库连接、外部服务等
            _logger.LogDebug("Performing health check");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
        }
    }

    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
        _semaphore?.Dispose();
        _consulClient?.Dispose();
    }
}
