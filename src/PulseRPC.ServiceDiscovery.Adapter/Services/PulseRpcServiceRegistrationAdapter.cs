using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseServiceDiscovery.Abstractions;
using PulseServiceDiscovery.Abstractions.Models;
using ServiceDiscoveryEndpoint = PulseServiceDiscovery.Abstractions.Models.ServiceEndpoint;

namespace PulseRPC.ServiceDiscovery.Adapter.Services;

/// <summary>
/// PulseRPC服务注册适配器
/// </summary>
public class PulseRpcServiceRegistrationAdapter
{
    private readonly PulseServiceDiscovery.Abstractions.IServiceRegistry _serviceRegistry;
    private readonly ILogger<PulseRpcServiceRegistrationAdapter> _logger;

    public PulseRpcServiceRegistrationAdapter(
        PulseServiceDiscovery.Abstractions.IServiceRegistry serviceRegistry,
        ILogger<PulseRpcServiceRegistrationAdapter> logger)
    {
        _serviceRegistry = serviceRegistry ?? throw new ArgumentNullException(nameof(serviceRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 注册PulseRPC服务
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="host">主机地址</param>
    /// <param name="port">端口号</param>
    /// <param name="metadata">元数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务ID</returns>
    public async Task<string> RegisterServiceAsync(
        string serviceName,
        string host,
        int port,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var serviceId = GenerateServiceId(serviceName, host, port);

        var serviceMetadata = new ServiceMetadata();
        if (metadata != null)
        {
            foreach (var kvp in metadata)
            {
                serviceMetadata.SetValue(kvp.Key, kvp.Value?.ToString() ?? string.Empty);
            }
        }

        var registration = new ServiceRegistration
        {
            Id = serviceId,
            ServiceName = serviceName,
            Host = host,
            Port = port,
            Protocol = "tcp",
            Weight = 1,
            Metadata = serviceMetadata,
            RegisteredAt = DateTime.UtcNow,
            LastHeartbeat = DateTime.UtcNow
        };

        await _serviceRegistry.RegisterAsync(registration, cancellationToken);

        _logger.LogInformation("Registered PulseRPC service: {ServiceName} at {Host}:{Port} (ID: {ServiceId})",
            serviceName, host, port, serviceId);

        return serviceId;
    }

    /// <summary>
    /// 注销PulseRPC服务
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task UnregisterServiceAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        await _serviceRegistry.UnregisterAsync(serviceId, cancellationToken);

        _logger.LogInformation("Unregistered PulseRPC service: {ServiceId}", serviceId);
    }

    /// <summary>
    /// 更新服务心跳
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task UpdateHeartbeatAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        await _serviceRegistry.UpdateHealthAsync(serviceId, PulseServiceDiscovery.Abstractions.Models.HealthStatus.Healthy, cancellationToken);

        _logger.LogDebug("Updated heartbeat for PulseRPC service: {ServiceId}", serviceId);
    }

    /// <summary>
    /// 生成服务ID
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="host">主机地址</param>
    /// <param name="port">端口号</param>
    /// <returns>服务ID</returns>
    private static string GenerateServiceId(string serviceName, string host, int port)
    {
        return $"{serviceName}-{host}-{port}-{Environment.MachineName}-{Environment.ProcessId}";
    }
}

/// <summary>
/// PulseRPC自动注册服务配置选项
/// </summary>
public record PulseRpcAutoRegistrationOptions
{
    /// <summary>
    /// 是否启用自动注册
    /// </summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>
    /// 主机地址
    /// </summary>
    public string Host { get; init; } = "localhost";

    /// <summary>
    /// 端口号
    /// </summary>
    public int Port { get; init; } = 8080;

    /// <summary>
    /// 心跳间隔
    /// </summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 服务元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// 健康检查配置
    /// </summary>
    public HealthCheckConfig? HealthCheck { get; init; }
}

/// <summary>
/// PulseRPC自动注册服务
/// </summary>
public class PulseRpcAutoRegistrationService : BackgroundService
{
    private readonly PulseRpcServiceRegistrationAdapter _adapter;
    private readonly ILogger<PulseRpcAutoRegistrationService> _logger;
    private readonly PulseRpcAutoRegistrationOptions _options;
    private string? _serviceId;

    public PulseRpcAutoRegistrationService(
        PulseRpcServiceRegistrationAdapter adapter,
        ILogger<PulseRpcAutoRegistrationService> logger,
        IOptions<PulseRpcAutoRegistrationOptions> options)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("PulseRPC auto registration is disabled");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ServiceName))
        {
            _logger.LogError("Service name is required for auto registration");
            return;
        }

        try
        {
            // 注册服务
            _serviceId = await _adapter.RegisterServiceAsync(
                _options.ServiceName,
                _options.Host,
                _options.Port,
                _options.Metadata,
                stoppingToken);

            _logger.LogInformation("PulseRPC auto registration completed for service: {ServiceName} (ID: {ServiceId})",
                _options.ServiceName, _serviceId);

            // 定期发送心跳
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.HeartbeatInterval, stoppingToken);

                    if (!string.IsNullOrEmpty(_serviceId))
                    {
                        await _adapter.UpdateHeartbeatAsync(_serviceId, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send heartbeat for service: {ServiceId}", _serviceId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during PulseRPC auto registration");
        }
        finally
        {
            // 注销服务
            if (!string.IsNullOrEmpty(_serviceId))
            {
                try
                {
                    await _adapter.UnregisterServiceAsync(_serviceId, CancellationToken.None);
                    _logger.LogInformation("PulseRPC service unregistered: {ServiceId}", _serviceId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to unregister PulseRPC service: {ServiceId}", _serviceId);
                }
            }
        }
    }
}
