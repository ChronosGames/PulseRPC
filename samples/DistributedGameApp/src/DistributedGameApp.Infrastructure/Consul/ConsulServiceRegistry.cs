using Consul;
using DistributedGameApp.Infrastructure.Health;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DistributedGameApp.Infrastructure.Consul;

/// <summary>
/// 服务注册信息（用于 HTTP 健康检查模式）
/// </summary>
public class HttpHealthCheckInfo
{
    /// <summary>
    /// HTTP 端口
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// 健康检查路径
    /// </summary>
    public string Path { get; set; } = "/health";
}

/// <summary>
/// Consul 服务注册
/// </summary>
public class ConsulServiceRegistry : IAsyncDisposable
{
    private readonly IConsulClient _client;
    private readonly ConsulOptions _options;
    private readonly HttpEndpointOptions? _httpEndpointOptions;
    private readonly ILogger<ConsulServiceRegistry> _logger;
    private readonly IHealthCheckProvider? _healthCheckProvider;
    private readonly List<string> _registeredServiceIds = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _healthReportTask;

    /// <summary>
    /// HTTP 健康检查信息（用于注册时指定 HTTP 端口）
    /// </summary>
    public HttpHealthCheckInfo? HttpHealthCheck { get; set; }

    public ConsulServiceRegistry(
        IOptions<ConsulOptions> options,
        ILogger<ConsulServiceRegistry> logger,
        IHealthCheckProvider? healthCheckProvider = null,
        IOptions<HttpEndpointOptions>? httpEndpointOptions = null)
    {
        _options = options.Value;
        _httpEndpointOptions = httpEndpointOptions?.Value;
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

        _logger.LogInformation("Consul 健康检查模式: {Mode}", _options.HealthCheckMode);
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

            // 根据健康检查模式构建健康检查配置
            AgentServiceCheck healthCheck;

            if (_options.HealthCheckMode == HealthCheckMode.HTTP)
            {
                // HTTP 模式：Consul 主动拉取健康状态
                var httpPort = HttpHealthCheck?.Port ?? _httpEndpointOptions?.Port ?? 9090;
                var httpPath = HttpHealthCheck?.Path ?? _httpEndpointOptions?.HealthPath ?? "/health";

                // 优先使用配置的 URL，否则自动构建
                var healthCheckUrl = _options.HttpHealthCheckUrl
                    ?? $"http://{registration.Host}:{httpPort}{httpPath}";

                healthCheck = new AgentServiceCheck
                {
                    HTTP = healthCheckUrl,
                    Interval = TimeSpan.FromSeconds(_options.HealthCheckInterval),
                    Timeout = TimeSpan.FromSeconds(_options.HealthCheckTimeout),
                    DeregisterCriticalServiceAfter = TimeSpan.FromSeconds(_options.DeregisterCriticalServiceAfter)
                };

                _logger.LogInformation(
                    "使用 HTTP 健康检查模式: {Url}, 间隔: {Interval}秒",
                    healthCheckUrl,
                    _options.HealthCheckInterval);
            }
            else
            {
                // TTL 模式：服务主动推送健康状态（保留现有逻辑）
                healthCheck = new AgentServiceCheck
                {
                    TTL = TimeSpan.FromSeconds(_options.HealthCheckInterval * 3), // TTL 设为 3 倍间隔
                    DeregisterCriticalServiceAfter = TimeSpan.FromSeconds(_options.DeregisterCriticalServiceAfter)
                };

                _logger.LogInformation(
                    "使用 TTL 健康检查模式: TTL={TTL}秒",
                    _options.HealthCheckInterval * 3);
            }

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
                "Service registered: {ServiceType}/{ServiceId} at {Host}:{Port} (HealthCheckMode: {Mode})",
                registration.ServiceType,
                registration.ServiceId,
                registration.Host,
                registration.TcpPort,
                _options.HealthCheckMode);

            // 仅在 TTL 模式下启动健康状态定期上报
            if (_options.HealthCheckMode == HealthCheckMode.TTL && _healthReportTask == null)
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

        // 添加外网端点信息（对外客户端使用）
        if (registration.ExternalEndpoint != null && registration.ExternalEndpoint.Enabled)
        {
            // 使用公网地址（如果有配置），否则使用容器内地址
            var publicHost = registration.ExternalEndpoint.GetPublicHost();
            var publicTcpPort = registration.ExternalEndpoint.GetPublicTcpPort();
            var publicKcpPort = registration.ExternalEndpoint.GetPublicKcpPort();

            meta["External_Host"] = publicHost;
            meta["External_TcpPort"] = publicTcpPort.ToString();

            _logger.LogInformation(
                "[Consul] Registering External Endpoint - Host: {Host}, PublicHost: {PublicHost}, TcpPort: {TcpPort}, PublicTcpPort: {PublicTcpPort}",
                registration.ExternalEndpoint.Host,
                publicHost,
                registration.ExternalEndpoint.TcpPort,
                publicTcpPort);

            if (publicKcpPort.HasValue)
            {
                meta["External_KcpPort"] = publicKcpPort.Value.ToString();
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
        await _cts.CancelAsync();

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
