using Consul;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DistributedGameApp.Infrastructure.Consul;

/// <summary>
/// Consul 服务注册
/// </summary>
public class ConsulServiceRegistry : IAsyncDisposable
{
    private readonly IConsulClient _client;
    private readonly ConsulOptions _options;
    private readonly ILogger<ConsulServiceRegistry> _logger;
    private readonly List<string> _registeredServiceIds = new();
    private readonly CancellationTokenSource _cts = new();

    public ConsulServiceRegistry(
        IOptions<ConsulOptions> options,
        ILogger<ConsulServiceRegistry> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new ConsulClient(config =>
        {
            config.Address = new Uri(_options.Address);
        });
    }

    /// <summary>
    /// 注册服务
    /// </summary>
    public async Task<bool> RegisterServiceAsync(
        ServiceRegistration registration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 构建服务名称
            var serviceName = $"{_options.ServiceBasePath}-{registration.ServiceType.ToLower()}";

            // 构建健康检查端点 (使用 TCP 检查)
            var healthCheck = new AgentServiceCheck
            {
                TCP = $"{registration.Host}:{registration.TcpPort}",
                Interval = TimeSpan.FromSeconds(_options.HealthCheckInterval),
                Timeout = TimeSpan.FromSeconds(_options.HealthCheckTimeout),
                DeregisterCriticalServiceAfter = TimeSpan.FromSeconds(_options.DeregisterCriticalServiceAfter)
            };

            // 构建服务注册
            var agentRegistration = new AgentServiceRegistration
            {
                ID = registration.ServiceId,
                Name = serviceName,
                Address = registration.Host,
                Port = registration.TcpPort,
                Check = healthCheck,
                Tags = BuildTags(registration),
                Meta = BuildMetadata(registration)
            };

            // 注册服务
            await _client.Agent.ServiceRegister(agentRegistration, cancellationToken);

            // 保存注册ID
            _registeredServiceIds.Add(registration.ServiceId);

            _logger.LogInformation(
                "Service registered: {ServiceType}/{ServiceId} at {Host}:{Port}",
                registration.ServiceType,
                registration.ServiceId,
                registration.Host,
                registration.TcpPort);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register service: {ServiceId}", registration.ServiceId);
            return false;
        }
    }

    /// <summary>
    /// 注销服务
    /// </summary>
    public async Task<bool> UnregisterServiceAsync(
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Agent.ServiceDeregister(serviceId, cancellationToken);

            _registeredServiceIds.Remove(serviceId);

            _logger.LogInformation("Service unregistered: {ServiceId}", serviceId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister service: {ServiceId}", serviceId);
            return false;
        }
    }

    /// <summary>
    /// 更新服务状态
    /// </summary>
    public async Task<bool> UpdateServiceStatusAsync(
        ServiceRegistration registration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Consul 不直接支持更新服务信息，需要重新注册
            // 使用元数据更新来实现状态更新
            var serviceName = $"{_options.ServiceBasePath}-{registration.ServiceType.ToLower()}";

            var agentRegistration = new AgentServiceRegistration
            {
                ID = registration.ServiceId,
                Name = serviceName,
                Address = registration.Host,
                Port = registration.TcpPort,
                Tags = BuildTags(registration),
                Meta = BuildMetadata(registration)
            };

            await _client.Agent.ServiceRegister(agentRegistration, cancellationToken);

            _logger.LogDebug("Service status updated: {ServiceId}", registration.ServiceId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update service status: {ServiceId}", registration.ServiceId);
            return false;
        }
    }

    /// <summary>
    /// 构建服务标签
    /// </summary>
    private string[] BuildTags(ServiceRegistration registration)
    {
        var tags = new List<string>
        {
            $"type:{registration.ServiceType}",
            $"node:{registration.NodeId}",
            $"status:{registration.Status}",
            $"load:{registration.CurrentLoad}/{registration.MaxCapacity}"
        };

        if (registration.KcpPort.HasValue)
        {
            tags.Add($"kcp:{registration.KcpPort.Value}");
        }

        if (!string.IsNullOrEmpty(registration.NodeName))
        {
            tags.Add($"node-name:{registration.NodeName}");
        }

        return tags.ToArray();
    }

    /// <summary>
    /// 构建服务元数据
    /// </summary>
    private Dictionary<string, string> BuildMetadata(ServiceRegistration registration)
    {
        var meta = new Dictionary<string, string>
        {
            ["ServiceType"] = registration.ServiceType,
            ["NodeId"] = registration.NodeId.ToString(),
            ["CurrentLoad"] = registration.CurrentLoad.ToString(),
            ["MaxCapacity"] = registration.MaxCapacity.ToString(),
            ["Status"] = registration.Status,
            ["RegisteredAt"] = registration.RegisteredAt.ToString("O"),
            ["LastHeartbeat"] = registration.LastHeartbeat.ToString("O")
        };

        if (registration.KcpPort.HasValue)
        {
            meta["KcpPort"] = registration.KcpPort.Value.ToString();
        }

        if (!string.IsNullOrEmpty(registration.NodeName))
        {
            meta["NodeName"] = registration.NodeName;
        }

        // 合并自定义元数据
        foreach (var kvp in registration.Metadata)
        {
            meta[$"Custom_{kvp.Key}"] = kvp.Value;
        }

        return meta;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        // 注销所有已注册的服务
        foreach (var serviceId in _registeredServiceIds.ToList())
        {
            try
            {
                await _client.Agent.ServiceDeregister(serviceId);
                _logger.LogInformation("Service deregistered on disposal: {ServiceId}", serviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deregister service on disposal: {ServiceId}", serviceId);
            }
        }

        _client?.Dispose();
        _cts?.Dispose();
    }
}
