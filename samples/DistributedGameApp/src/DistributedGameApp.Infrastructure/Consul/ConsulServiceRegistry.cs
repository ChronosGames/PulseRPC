using Consul;
using DistributedGameApp.Infrastructure.Health;
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
    private readonly IHealthCheckProvider? _healthCheckProvider;
    private readonly List<string> _registeredServiceIds = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _healthReportTask;

    public ConsulServiceRegistry(
        IOptions<ConsulOptions> options,
        ILogger<ConsulServiceRegistry> logger,
        IHealthCheckProvider? healthCheckProvider = null)
    {
        _options = options.Value;
        _logger = logger;
        _healthCheckProvider = healthCheckProvider;
        _client = new ConsulClient(config =>
        {
            config.Address = new Uri(_options.Address);
        });

        if (_healthCheckProvider == null)
        {
            _logger.LogWarning("未提供 IHealthCheckProvider，将使用默认健康检查（总是返回健康）");
        }
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

            // 构建健康检查端点 (使用 TTL 检查 - 服务器主动报告健康状态)
            var healthCheck = new AgentServiceCheck
            {
                TTL = TimeSpan.FromSeconds(_options.HealthCheckInterval * 3), // TTL 设为 3 倍间隔
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

            // 启动健康状态定期上报（TTL 模式）
            if (_healthReportTask == null)
            {
                _healthReportTask = StartHealthReportLoopAsync(_cts.Token);
            }

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

        // 添加内网端点信息
        if (registration.InternalEndpoint != null && registration.InternalEndpoint.Enabled)
        {
            meta["Internal_Host"] = registration.InternalEndpoint.Host;
            meta["Internal_TcpPort"] = registration.InternalEndpoint.TcpPort.ToString();

            if (registration.InternalEndpoint.KcpPort.HasValue)
            {
                meta["Internal_KcpPort"] = registration.InternalEndpoint.KcpPort.Value.ToString();
            }
        }

        // 添加外网端点信息
        if (registration.ExternalEndpoint != null && registration.ExternalEndpoint.Enabled)
        {
            meta["External_Host"] = registration.ExternalEndpoint.Host;

            // 使用公网端口（如果有配置），否则使用监听端口
            var tcpPort = registration.ExternalEndpoint.PublicTcpPort ?? registration.ExternalEndpoint.TcpPort;
            meta["External_TcpPort"] = tcpPort.ToString();

            var kcpPort = registration.ExternalEndpoint.PublicKcpPort ?? registration.ExternalEndpoint.KcpPort;
            if (kcpPort.HasValue)
            {
                meta["External_KcpPort"] = kcpPort.Value.ToString();
            }
        }

        // 合并自定义元数据
        foreach (var kvp in registration.Metadata)
        {
            meta[$"Custom_{kvp.Key}"] = kvp.Value;
        }

        return meta;
    }

    /// <summary>
    /// 启动健康状态定期上报循环
    /// </summary>
    private async Task StartHealthReportLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("启动 TTL 健康状态上报循环，间隔: {Interval}秒", _options.HealthCheckInterval);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 等待一段时间
                await Task.Delay(TimeSpan.FromSeconds(_options.HealthCheckInterval), cancellationToken);

                // 执行健康检查（如果提供了健康检查提供者）
                HealthCheckResult? healthCheckResult = null;
                if (_healthCheckProvider != null)
                {
                    try
                    {
                        healthCheckResult = await _healthCheckProvider.CheckHealthAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "健康检查失败，将上报不健康状态");
                        healthCheckResult = HealthCheckResult.Unhealthy("HealthCheckException", new Dictionary<string, object>
                        {
                            ["Exception"] = ex.Message
                        });
                    }
                }

                // 为所有已注册的服务发送健康状态
                foreach (var serviceId in _registeredServiceIds.ToList())
                {
                    try
                    {
                        if (healthCheckResult == null || healthCheckResult.IsHealthy)
                        {
                            // 发送健康状态（pass）
                            var message = healthCheckResult?.Status ?? "Healthy";
                            await _client.Agent.PassTTL($"service:{serviceId}", message, cancellationToken);

                            _logger.LogDebug("已上报健康状态: {ServiceId} - {Status}", serviceId, message);
                        }
                        else
                        {
                            // 发送不健康状态（fail）
                            var message = $"Unhealthy: {healthCheckResult.Status}";
                            if (healthCheckResult.Details.Count > 0)
                            {
                                var detailsJson = JsonSerializer.Serialize(healthCheckResult.Details);
                                message += $" | Details: {detailsJson}";
                            }

                            await _client.Agent.FailTTL($"service:{serviceId}", message, cancellationToken);

                            _logger.LogWarning("已上报不健康状态: {ServiceId} - {Status}", serviceId, healthCheckResult.Status);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "上报健康状态失败: {ServiceId}", serviceId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "健康状态上报循环异常");
            }
        }

        _logger.LogInformation("健康状态上报循环已停止");
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        // 等待健康上报任务结束
        if (_healthReportTask != null)
        {
            try
            {
                await _healthReportTask;
            }
            catch
            {
                // Ignore
            }
        }

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
