using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using PulseRPC.Cluster;
using PulseRPC.HealthCheck;

namespace PulseRPC.ServiceDiscovery;

/// <summary>
/// 服务发现实现
/// </summary>
public class ServiceDiscovery : IServiceDiscovery
{
    private readonly ILogger<ServiceDiscovery> _logger;
    private readonly ServiceDiscoveryOptions _options;
    private readonly ConcurrentDictionary<string, ServiceEndpoint> _endpoints = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastHeartbeat = new();

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

    /// <summary>
    /// 创建服务发现实例
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="options">配置选项</param>
    public ServiceDiscovery(
        ILogger<ServiceDiscovery> logger,
        IOptions<ServiceDiscoveryOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 注册服务
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task RegisterAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        ValidateEndpoint(endpoint);

        _endpoints[endpoint.ServiceId] = endpoint;
        _lastHeartbeat[endpoint.ServiceId] = DateTime.UtcNow;

        _logger.LogInformation("Service registered: {ServiceName} at {Host}:{Port} (ID: {ServiceId})",
            endpoint.ServiceType, endpoint.Channel.Address.Host, endpoint.Channel.Address.Port, endpoint.ServiceId);

        await TriggerServiceRegisteredEvent(endpoint);
    }

    /// <summary>
    /// 注销服务
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task UnregisterAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        if (_endpoints.TryRemove(serviceId, out var endpoint))
        {
            _lastHeartbeat.TryRemove(serviceId, out _);

            _logger.LogInformation("Service unregistered: {ServiceName} (ID: {ServiceId})",
                endpoint.ServiceType, serviceId);

            await TriggerServiceUnregisteredEvent(endpoint);
        }
        else
        {
            _logger.LogWarning("Attempted to unregister non-existent service: {ServiceId}", serviceId);
        }
    }

    /// <summary>
    /// 更新服务健康状态
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="status">健康状态</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task UpdateHealthAsync(string serviceId, HealthStatus status, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        if (_endpoints.TryGetValue(serviceId, out var endpoint))
        {
            var oldStatus = endpoint.Health;
            endpoint.Health = status;
            _lastHeartbeat[serviceId] = DateTime.UtcNow;

            _logger.LogInformation("Service health updated: {ServiceName} (ID: {ServiceId}) from {OldStatus} to {NewStatus}",
                endpoint.ServiceType, serviceId, oldStatus, status);

            if (oldStatus != status)
            {
                await TriggerServiceHealthChangedEvent(endpoint, oldStatus, status);
            }
        }
        else
        {
            _logger.LogWarning("Attempted to update health for non-existent service: {ServiceId}", serviceId);
        }
    }

    /// <summary>
    /// 获取已注册的服务列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<IReadOnlyList<ServiceEndpoint>> GetRegisteredServicesAsync(CancellationToken cancellationToken = default)
    {
        return _endpoints.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// 获取指定服务名称的所有服务
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<IReadOnlyList<ServiceEndpoint>> GetServicesAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        return _endpoints.Values
            .Where(e => e.ServiceType == serviceName)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// 获取指定服务ID的服务
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<ServiceEndpoint?> GetServiceAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        _endpoints.TryGetValue(serviceId, out var endpoint);
        return endpoint;
    }

    /// <summary>
    /// 检查服务是否存在
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<bool> ExistsAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        return _endpoints.ContainsKey(serviceId);
    }

    /// <summary>
    /// 发送心跳
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task HeartbeatAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        if (_endpoints.TryGetValue(serviceId, out var endpoint))
        {
            _lastHeartbeat[serviceId] = DateTime.UtcNow;

            _logger.LogDebug("Heartbeat received for service: {ServiceName} (ID: {ServiceId})",
                endpoint.ServiceType, serviceId);
        }
        else
        {
            _logger.LogWarning("Heartbeat received for non-existent service: {ServiceId}", serviceId);
        }
    }

    /// <summary>
    /// 清理过期服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task CleanupExpiredServicesAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Cleanup.Enabled)
            return;

        try
        {
            var expiredThreshold = DateTime.UtcNow - _options.Cleanup.ServiceExpiration;
            var expiredServices = _endpoints.Values
                .Where(e => _lastHeartbeat.TryGetValue(e.ServiceId, out var lastHeartbeat) && lastHeartbeat < expiredThreshold)
                .ToList();

            if (expiredServices.Any())
            {
                _logger.LogInformation("Cleaning up {Count} expired services", expiredServices.Count);

                foreach (var service in expiredServices)
                {
                    await UnregisterAsync(service.ServiceId, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired services");
        }
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<Dictionary<string, object>> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var servicesByName = _endpoints.Values
                .GroupBy(e => e.ServiceType)
                .ToDictionary(g => g.Key, g => g.Count());

            return new Dictionary<string, object>
            {
                ["TotalServices"] = _endpoints.Count,
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

    private void ValidateEndpoint(ServiceEndpoint endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.ServiceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(endpoint.ServiceId));

        if (string.IsNullOrWhiteSpace(endpoint.ServiceType))
            throw new ArgumentException("Service name cannot be null or empty", nameof(endpoint.ServiceType));

        if (string.IsNullOrWhiteSpace(endpoint.Channel.Address.Host))
            throw new ArgumentException("Host cannot be null or empty", nameof(endpoint.Channel.Address.Host));

        if (endpoint.Channel.Address.Port <= 0 || endpoint.Channel.Address.Port > 65535)
            throw new ArgumentException("Port must be between 1 and 65535", nameof(endpoint.Channel.Address.Port));
    }

    private static string GenerateServiceId(ServiceEndpoint endpoint)
    {
        return $"{endpoint.ServiceType}-{endpoint.Channel.Address.Host}-{endpoint.Channel.Address.Port}-{Guid.NewGuid():N}";
    }

    private async Task TriggerServiceRegisteredEvent(ServiceEndpoint endpoint)
    {
        if (ServiceRegistered != null)
        {
            var evt = ServiceRegisteredEvent.CreateSuccess(endpoint);
            await ServiceRegistered(evt);
        }
    }

    private async Task TriggerServiceUnregisteredEvent(ServiceEndpoint endpoint)
    {
        if (ServiceUnregistered != null)
        {
            var evt = ServiceUnregisteredEvent.CreateSuccess(endpoint);
            await ServiceUnregistered(evt);
        }
    }

    private async Task TriggerServiceHealthChangedEvent(ServiceEndpoint endpoint, HealthStatus oldStatus, HealthStatus newStatus)
    {
        if (ServiceHealthChanged != null)
        {
            var evt = ServiceHealthChangedEvent.Create(endpoint, oldStatus, newStatus);
            await ServiceHealthChanged(evt);
        }
    }
}
