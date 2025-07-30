using Consul;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;
using PulseRPC.HealthCheck;
using PulseRPC.Configuration;
using PulseRPC.ServiceDiscovery;
using PulseRPC.Infrastructure;
using PulseRPC.Transport;
using PulseRPC.ServiceRegistration;

namespace PulseRPC.Infrastructure.Consul;

/// <summary>
/// 基于Consul的服务发现实现
/// </summary>
public class ConsulServiceDiscovery : IServiceDiscovery, PulseRPC.ServiceRegistration.IServiceRegistry, IDisposable
{
    private readonly IConsulClient _consulClient;
    private readonly ILogger<ConsulServiceDiscovery> _logger;
    private readonly ConsulOptions _options;
    private readonly Timer? _healthCheckTimer;
    private readonly Timer? _watchTimer;
    private readonly string _instanceId;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, PulseRPC.ServiceDiscovery.ServiceEndpoint> _serviceCache = new();
    private readonly ConcurrentDictionary<string, ulong> _watchIndices = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _watchTokens = new();
    private volatile bool _disposed = false;

    /// <summary>
    /// 服务注册事件
    /// </summary>
    public event Func<ServiceRegisteredEvent, Task>? ServiceRegistered;

    /// <summary>
    /// 服务注销事件
    /// </summary>
    public event Func<ServiceUnregisteredEvent, Task>? ServiceUnregistered;

    /// <summary>
    /// 服务健康状态变更事件
    /// </summary>
    public event Func<ServiceHealthChangedEvent, Task>? ServiceHealthChanged;

    public ConsulServiceDiscovery(
        IConsulClient consulClient,
        ILogger<ConsulServiceDiscovery> logger,
        IOptions<ConsulOptions> options)
    {
        _consulClient = consulClient ?? throw new ArgumentNullException(nameof(consulClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _instanceId = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";

        if (_options.HealthCheck.Enabled)
        {
            _healthCheckTimer = new Timer(PerformHealthCheck, null,
                _options.HealthCheck.Interval, _options.HealthCheck.Interval);
        }

        // 启动服务监听
        if (_options.DiscoveryOptions.EnableWatching)
        {
            _watchTimer = new Timer(StartWatching, null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
        }

        _logger.LogInformation("Consul service discovery initialized with endpoint: {Endpoint}", _options.Endpoint);
    }

    #region IServiceDiscovery Implementation

    /// <summary>
    /// 注册服务
    /// </summary>
    public async Task RegisterAsync(PulseRPC.ServiceDiscovery.ServiceEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        if (endpoint == null)
            throw new ArgumentNullException(nameof(endpoint));

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            var registration = ConvertToConsulRegistration(endpoint);
            await _consulClient.Agent.ServiceRegister(registration, cancellationToken);

            // 更新缓存
            _serviceCache[endpoint.ServiceId] = endpoint;

            _logger.LogInformation("Registered service: {ServiceName} (ID: {ServiceId})", 
                endpoint.ServiceType, endpoint.ServiceId);

            // 触发事件
            if (ServiceRegistered != null)
            {
                var @event = ServiceRegisteredEvent.CreateSuccess(endpoint, "ConsulServiceDiscovery");
                await ServiceRegistered(@event);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register service: {ServiceName} (ID: {ServiceId})", 
                endpoint.ServiceType, endpoint.ServiceId);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 注销服务
    /// </summary>
    public async Task UnregisterAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        try
        {
            await _semaphore.WaitAsync(cancellationToken);

            // 获取服务信息用于事件
            var serviceInfo = _serviceCache.TryGetValue(serviceId, out var endpoint) ? endpoint : null;

            await _consulClient.Agent.ServiceDeregister(serviceId, cancellationToken);

            // 从缓存中移除
            _serviceCache.TryRemove(serviceId, out _);

            _logger.LogInformation("Unregistered service: {ServiceId}", serviceId);

            // 触发事件
            if (ServiceUnregistered != null && serviceInfo != null)
            {
                var @event = ServiceUnregisteredEvent.CreateSuccess(serviceInfo, UnregistrationReason.Manual, "ConsulServiceDiscovery");
                await ServiceUnregistered(@event);
            }
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

    /// <summary>
    /// 更新服务健康状态
    /// </summary>
    public async Task UpdateHealthAsync(string serviceId, PulseRPC.HealthCheck.HealthStatus status, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        try
        {
            var checkId = $"service:{serviceId}";
            var note = $"Health status updated to {status}";

            switch (status)
            {
                case PulseRPC.HealthCheck.HealthStatus.Healthy:
                    await _consulClient.Agent.PassTTL(checkId, note, cancellationToken);
                    break;
                case PulseRPC.HealthCheck.HealthStatus.Unhealthy:
                    await _consulClient.Agent.FailTTL(checkId, note, cancellationToken);
                    break;
                case PulseRPC.HealthCheck.HealthStatus.Degraded:
                    await _consulClient.Agent.WarnTTL(checkId, note, cancellationToken);
                    break;
            }

            _logger.LogDebug("Updated health status for service: {ServiceId} to {Status}", serviceId, status);

            // 触发健康状态变更事件
            if (ServiceHealthChanged != null && _serviceCache.TryGetValue(serviceId, out var endpoint))
            {
                var previousHealth = endpoint.IsHealthy ? PulseRPC.HealthCheck.HealthStatus.Healthy : PulseRPC.HealthCheck.HealthStatus.Unhealthy;
                endpoint.IsHealthy = status == PulseRPC.HealthCheck.HealthStatus.Healthy;
                
                var @event = ServiceHealthChangedEvent.Create(
                    endpoint,
                    previousHealth,
                    status,
                    note,
                    "ConsulServiceDiscovery");
                await ServiceHealthChanged(@event);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update health status for service: {ServiceId}", serviceId);
            throw;
        }
    }

    /// <summary>
    /// 获取已注册的服务列表
    /// </summary>
    public async Task<IReadOnlyList<ServiceEndpoint>> GetRegisteredServicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _consulClient.Agent.Services(cancellationToken);
            var endpoints = new List<ServiceEndpoint>();

            foreach (var service in response.Response.Values)
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
                    _logger.LogWarning(ex, "Failed to convert service: {ServiceId}", service.ID);
                }
            }

            return endpoints.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get registered services");
            throw;
        }
    }

    /// <summary>
    /// 获取指定服务名称的所有服务
    /// </summary>
    public async Task<IReadOnlyList<PulseRPC.ServiceDiscovery.ServiceEndpoint>> GetServicesAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        try
        {
            var healthyOnly = _options.DiscoveryOptions.HealthyOnly;
            var tags = _options.DiscoveryOptions.Tags;
            var tag = tags?.FirstOrDefault() ?? string.Empty;

            var response = await _consulClient.Health.Service(serviceName, tag, healthyOnly, cancellationToken);
            var endpoints = new List<PulseRPC.ServiceDiscovery.ServiceEndpoint>();

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

    /// <summary>
    /// 获取指定服务ID的服务
    /// </summary>
    public async Task<PulseRPC.ServiceDiscovery.ServiceEndpoint?> GetServiceAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        try
        {
            var response = await _consulClient.Agent.Services(cancellationToken);

            if (response.Response.TryGetValue(serviceId, out var service))
            {
                return ConvertToServiceEndpoint(service);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get service: {ServiceId}", serviceId);
            throw;
        }
    }

    /// <summary>
    /// 检查服务是否存在
    /// </summary>
    public async Task<bool> ExistsAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        try
        {
            var response = await _consulClient.Agent.Services(cancellationToken);
            return response.Response.ContainsKey(serviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check service existence: {ServiceId}", serviceId);
            throw;
        }
    }

    /// <summary>
    /// 发送心跳
    /// </summary>
    public async Task HeartbeatAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        try
        {
            await _consulClient.Agent.PassTTL($"service:{serviceId}", "Heartbeat", cancellationToken);
            _logger.LogDebug("Heartbeat sent for service: {ServiceId}", serviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send heartbeat for service: {ServiceId}", serviceId);
            throw;
        }
    }

    /// <summary>
    /// 清理过期服务
    /// </summary>
    public async Task CleanupExpiredServicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _consulClient.Agent.Services(cancellationToken);
            var expiredServices = new List<string>();

            foreach (var service in response.Response.Values)
            {
                // 检查服务是否过期（这里可以根据实际需要实现过期逻辑）
                if (IsServiceExpired(service))
                {
                    expiredServices.Add(service.ID);
                }
            }

            foreach (var serviceId in expiredServices)
            {
                try
                {
                    await UnregisterAsync(serviceId, cancellationToken);
                    _logger.LogInformation("Cleaned up expired service: {ServiceId}", serviceId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup expired service: {ServiceId}", serviceId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired services");
            throw;
        }
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public async Task<Dictionary<string, object>> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = new Dictionary<string, object>();
            var response = await _consulClient.Agent.Services(cancellationToken);

            stats["TotalServices"] = response.Response.Count;
            stats["CachedServices"] = _serviceCache.Count;
            stats["WatchedServices"] = _watchIndices.Count;
            stats["InstanceId"] = _instanceId;
            stats["Endpoint"] = _options.Endpoint;
            stats["HealthCheckEnabled"] = _options.HealthCheck.Enabled;
            stats["LastUpdate"] = DateTime.UtcNow;

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get statistics");
            throw;
        }
    }

    #endregion

    #region IServiceRegistry Implementation

    /// <summary>
    /// 注册服务
    /// </summary>
    public async Task RegisterAsync(PulseRPC.ServiceRegistration.ServiceRegistration registration, CancellationToken cancellationToken = default)
    {
        if (registration == null)
            throw new ArgumentNullException(nameof(registration));

        await RegisterAsync(registration.ToEndpoint(), cancellationToken);
    }

    /// <summary>
    /// 获取所有注册的服务
    /// </summary>
    public async Task<IReadOnlyList<PulseRPC.ServiceRegistration.ServiceRegistration>> GetRegistrationsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _consulClient.Agent.Services(cancellationToken);
            var registrations = new List<PulseRPC.ServiceRegistration.ServiceRegistration>();

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
            _logger.LogError(ex, "Failed to get all registrations");
            throw;
        }
    }

    #endregion

    #region Private Methods

    private PulseRPC.ServiceDiscovery.ServiceEndpoint? ConvertToServiceEndpoint(AgentService consulService)
    {
        try
        {
            var metadata = new Dictionary<string, string>();

            if (consulService.Meta != null)
            {
                foreach (var meta in consulService.Meta)
                {
                    metadata[meta.Key] = meta.Value ?? "";
                }
            }

            var endpoint = new PulseRPC.ServiceDiscovery.ServiceEndpoint
            {
                ServiceId = consulService.ID,
                ServiceType = consulService.Service,
                Host = consulService.Address,
                Port = consulService.Port,
                Protocol = "Tcp",
                Weight = 100, // 默认权重
                IsHealthy = true, // Consul返回的服务默认健康
                RegisteredAt = DateTime.UtcNow,
                LastHealthCheck = DateTime.UtcNow,
                Metadata = metadata
            };

            return endpoint;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert Consul service to ServiceEndpoint: {ServiceId}", 
                consulService.ID);
            return null;
        }
    }

    private PulseRPC.ServiceDiscovery.ServiceEndpoint? ConvertToServiceEndpoint(ServiceEntry consulService)
    {
        try
        {
            var service = consulService.Service;
            if (service == null) return null;

            var metadata = new Dictionary<string, string>();

            if (service.Meta != null)
            {
                foreach (var meta in service.Meta)
                {
                    metadata[meta.Key] = meta.Value ?? "";
                }
            }

            // 从健康检查结果确定健康状态
            var isHealthy = consulService.Checks?.All(check => check.Status == global::Consul.HealthStatus.Passing) ?? true;

            var endpoint = new PulseRPC.ServiceDiscovery.ServiceEndpoint
            {
                ServiceId = service.ID,
                ServiceType = service.Service,
                Host = service.Address,
                Port = service.Port,
                Protocol = "Tcp",
                Weight = 100, // 默认权重
                IsHealthy = isHealthy,
                RegisteredAt = DateTime.UtcNow,
                LastHealthCheck = DateTime.UtcNow,
                Metadata = metadata
            };

            return endpoint;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert Consul service entry to ServiceEndpoint: {ServiceId}", 
                consulService.Service?.ID);
            return null;
        }
    }

    private PulseRPC.ServiceRegistration.ServiceRegistration? ConvertToServiceRegistration(AgentService consulService)
    {
        try
        {
            var metadata = new Dictionary<string, string>();
            if (consulService.Meta != null)
            {
                foreach (var meta in consulService.Meta)
                {
                    metadata[meta.Key] = meta.Value;
                }
            }

            return new PulseRPC.ServiceRegistration.ServiceRegistration
            {
                Id = consulService.ID,
                ServiceType = consulService.Service,
                Host = consulService.Address,
                Port = consulService.Port,
                Tags = consulService.Tags?.ToList() ?? new List<string>(),
                Metadata = new ServiceMetadata(metadata),
                RegisteredAt = DateTime.UtcNow,
                LastHeartbeat = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert to ServiceRegistration: {ServiceId}", consulService.ID);
            return null;
        }
    }

    private AgentServiceRegistration ConvertToConsulRegistration(PulseRPC.ServiceDiscovery.ServiceEndpoint endpoint)
    {
        var metadata = new Dictionary<string, string>();

        if (endpoint.Metadata != null)
        {
            foreach (var meta in endpoint.Metadata)
            {
                metadata[meta.Key] = meta.Value ?? string.Empty;
            }
        }

        var consulRegistration = new AgentServiceRegistration
        {
            ID = endpoint.ServiceId,
            Name = endpoint.ServiceType,
            Address = endpoint.Host,
            Port = endpoint.Port,
            Meta = metadata,
            Tags = Array.Empty<string>() // 简化实现，可以后续从metadata中提取
        };

        // 配置健康检查
        if (_options.HealthCheck.Enabled)
        {
            consulRegistration.Check = new AgentServiceCheck
            {
                TTL = _options.HealthCheck.Interval,
                DeregisterCriticalServiceAfter = _options.HealthCheck.DeregisterAfter,
                Status = global::Consul.HealthStatus.Passing
            };
        }

        return consulRegistration;
    }

    private bool IsServiceExpired(AgentService service)
    {
        // 实现服务过期检查逻辑
        // 这里可以根据实际需要实现，比如检查最后心跳时间等
        return false;
    }

    private async void PerformHealthCheck(object? state)
    {
        if (_disposed) return;

        try
        {
            // 执行健康检查逻辑
            _logger.LogDebug("Performing health check for instance: {InstanceId}", _instanceId);
            
            // 这里可以添加自定义的健康检查逻辑
            // 比如检查数据库连接、外部服务等
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for instance: {InstanceId}", _instanceId);
        }
    }

    private async void StartWatching(object? state)
    {
        if (_disposed) return;

        try
        {
            // 实现服务监听逻辑
            _logger.LogDebug("Starting service watching");
            
            // 这里可以实现Consul的blocking queries来监听服务变化
            // 由于复杂性，这里提供一个简化的实现框架
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start service watching");
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _healthCheckTimer?.Dispose();
        _watchTimer?.Dispose();
        _semaphore?.Dispose();

        // 取消所有监听
        foreach (var token in _watchTokens.Values)
        {
            token.Cancel();
            token.Dispose();
        }
        _watchTokens.Clear();

        _consulClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}