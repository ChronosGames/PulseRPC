using DistributedGameApp.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.Consul;

/// <summary>
/// Consul服务注册后台服务（支持PulseRPC和ASP.NET Core）
/// </summary>
public sealed class ConsulServiceRegistrationService : BackgroundService
{
    private readonly ConsulServiceRegistry _registry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConsulServiceRegistrationService> _logger;
    private readonly ServerIdentityOptions _identity;
    private string? _serviceId;

    public ConsulServiceRegistrationService(
        ConsulServiceRegistry registry,
        IConfiguration configuration,
        ILogger<ConsulServiceRegistrationService> logger,
        ServerIdentityOptions identity)
    {
        _registry = registry;
        _configuration = configuration;
        _logger = logger;
        _identity = identity;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 延迟等待服务器完全启动
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        var registration = BuildServiceRegistration();
        _serviceId = registration.ServiceId;

        var success = await _registry.RegisterServiceAsync(registration, stoppingToken);

        if (success)
        {
            _logger.LogInformation(
                "[Consul] Service registered: {ServiceId} ({ServiceType}) at {Host}:{Port}",
                registration.ServiceId,
                registration.ServiceType,
                registration.Host,
                registration.TcpPort);
        }
        else
        {
            _logger.LogError(
                "[Consul] Failed to register service: {ServiceId}",
                registration.ServiceId);
        }

        // 保持运行（阻塞）
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_serviceId != null)
        {
            await _registry.UnregisterServiceAsync(_serviceId, cancellationToken);
            _logger.LogInformation("[Consul] Service unregistered: {ServiceId}", _serviceId);
        }

        await base.StopAsync(cancellationToken);
    }

    private ServiceRegistration BuildServiceRegistration()
    {
        // 尝试从新配置结构（Network）读取
        var networkConfig = _configuration.GetSection("Network");
        if (networkConfig.Exists())
        {
            return BuildFromNetworkConfig(networkConfig);
        }

        // 兼容旧配置结构（ServiceRegistration）
        var serviceConfig = _configuration.GetSection("ServiceRegistration");
        if (serviceConfig.Exists())
        {
            return BuildFromServiceConfig(serviceConfig);
        }

        throw new InvalidOperationException(
            "Neither 'Network' nor 'ServiceRegistration' configuration section found.");
    }

    private ServiceRegistration BuildFromNetworkConfig(IConfigurationSection networkConfig)
    {
        var internalConfig = networkConfig.GetSection("Internal");
        var externalConfig = networkConfig.GetSection("External");

        var tcpPort = 0;
        var host = "localhost";
        int? kcpPort = null;

        // 优先使用外网配置（如果有）
        if (externalConfig.Exists() && externalConfig.GetValue<bool>("Enabled"))
        {
            var tcpConfig = externalConfig.GetSection("Tcp");
            if (tcpConfig.Exists() && tcpConfig.GetValue<bool>("Enabled"))
            {
                tcpPort = tcpConfig.GetValue<int>("Port");
            }

            var kcpConfig = externalConfig.GetSection("Kcp");
            if (kcpConfig.Exists() && kcpConfig.GetValue<bool>("Enabled"))
            {
                kcpPort = kcpConfig.GetValue<int>("Port");
            }

            host = externalConfig.GetValue<string>("Host") ?? "localhost";
        }
        // 如果没有外网配置，使用内网配置
        else if (internalConfig.Exists() && internalConfig.GetValue<bool>("Enabled"))
        {
            tcpPort = internalConfig.GetValue<int>("Port");
            host = internalConfig.GetValue<string>("Host") ?? "localhost";
        }

        // Consul 不支持 0.0.0.0，转换为 localhost
        if (host == "0.0.0.0")
        {
            host = "localhost";
        }

        return new ServiceRegistration
        {
            ServiceId = $"{_identity.ServiceType.ToLower()}-{_identity.NodeId}",
            ServiceType = _identity.ServiceType,
            NodeId = _identity.NodeId,
            NodeName = _identity.NodeName,
            Host = host,
            TcpPort = tcpPort,
            KcpPort = kcpPort,
            CurrentLoad = 0,
            MaxCapacity = _identity.MaxCapacity,
            Status = "Online"
        };
    }

    private ServiceRegistration BuildFromServiceConfig(IConfigurationSection serviceConfig)
    {
        return new ServiceRegistration
        {
            ServiceId = serviceConfig.GetValue<string>("ServiceId")
                ?? $"{_identity.ServiceType.ToLower()}-{_identity.NodeId}",
            ServiceType = serviceConfig.GetValue<string>("ServiceType")
                ?? _identity.ServiceType,
            NodeId = serviceConfig.GetValue<int?>("NodeId")
                ?? _identity.NodeId,
            NodeName = serviceConfig.GetValue<string>("NodeName")
                ?? _identity.NodeName,
            Host = serviceConfig.GetValue<string>("Host") ?? "localhost",
            TcpPort = serviceConfig.GetValue<int>("TcpPort"),
            KcpPort = serviceConfig.GetValue<int?>("KcpPort"),
            CurrentLoad = 0,
            MaxCapacity = serviceConfig.GetValue<int?>("MaxCapacity")
                ?? _identity.MaxCapacity,
            Status = "Online"
        };
    }
}
