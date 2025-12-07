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

        var registration = new ServiceRegistration
        {
            ServiceId = $"{_identity.ServiceType.ToLower()}-{_identity.NodeId}",
            ServiceType = _identity.ServiceType,
            NodeId = _identity.NodeId,
            NodeName = _identity.NodeName,
            CurrentLoad = 0,
            MaxCapacity = _identity.MaxCapacity,
            Status = "Online"
        };

        // 解析内网端点
        if (internalConfig.Exists() && internalConfig.GetValue<bool>("Enabled"))
        {
            var internalHost = internalConfig.GetValue<string>("Host") ?? "localhost";
            var internalTcpPort = internalConfig.GetValue<int>("Port");
            int? internalKcpPort = null;

            var kcpConfig = internalConfig.GetSection("Kcp");
            if (kcpConfig.Exists() && kcpConfig.GetValue<bool>("Enabled"))
            {
                internalKcpPort = kcpConfig.GetValue<int>("Port");
            }

            registration.InternalEndpoint = new Infrastructure.ServiceClient.NetworkEndpoint
            {
                Host = internalHost == "0.0.0.0" ? "localhost" : internalHost,
                TcpPort = internalTcpPort,
                KcpPort = internalKcpPort,
                Enabled = true
            };
        }

        // 解析外网端点
        if (externalConfig.Exists() && externalConfig.GetValue<bool>("Enabled"))
        {
            var externalHost = externalConfig.GetValue<string>("Host") ?? "localhost";
            var tcpConfig = externalConfig.GetSection("Tcp");
            var kcpConfig = externalConfig.GetSection("Kcp");

            _logger.LogInformation(
                "[Config] External config exists. Host: {Host}, tcpConfig.Exists: {TcpExists}, kcpConfig.Exists: {KcpExists}",
                externalHost, tcpConfig.Exists(), kcpConfig.Exists());

            int externalTcpPort = 0;
            int? externalKcpPort = null;
            string? publicHost = externalConfig.GetValue<string>("PublicHost");
            int? publicTcpPort = null;
            int? publicKcpPort = null;

            // 尝试读取 TCP 配置（支持新格式）
            var tcpEnabled = tcpConfig.GetValue<bool?>("Enabled");
            var tcpPort = tcpConfig.GetValue<int?>("Port");
            var tcpPublicPort = tcpConfig.GetValue<int?>("PublicPort");

            _logger.LogInformation(
                "[Config] TCP Config - Enabled: {Enabled}, Port: {Port}, PublicPort: {PublicPort}",
                tcpEnabled, tcpPort, tcpPublicPort);

            if (tcpEnabled == true && tcpPort.HasValue)
            {
                externalTcpPort = tcpPort.Value;
                publicTcpPort = tcpPublicPort;

                _logger.LogInformation(
                    "[Config] Using TCP - Port: {Port}, PublicPort: {PublicPort}",
                    externalTcpPort, publicTcpPort);
            }

            // 尝试读取 KCP 配置（支持新格式）
            var kcpEnabled = kcpConfig.GetValue<bool?>("Enabled");
            var kcpPort = kcpConfig.GetValue<int?>("Port");
            var kcpPublicPortVal = kcpConfig.GetValue<int?>("PublicPort");

            _logger.LogInformation(
                "[Config] KCP Config - Enabled: {Enabled}, Port: {Port}, PublicPort: {PublicPort}",
                kcpEnabled, kcpPort, kcpPublicPortVal);

            if (kcpEnabled == true && kcpPort.HasValue)
            {
                externalKcpPort = kcpPort.Value;
                publicKcpPort = kcpPublicPortVal;

                _logger.LogInformation(
                    "[Config] Using KCP - Port: {Port}, PublicPort: {PublicPort}",
                    externalKcpPort, publicKcpPort);
            }

            registration.ExternalEndpoint = new Infrastructure.ServiceClient.NetworkEndpoint
            {
                Host = externalHost == "0.0.0.0" ? "localhost" : externalHost,
                TcpPort = externalTcpPort,
                KcpPort = externalKcpPort,
                PublicHost = publicHost,
                PublicTcpPort = publicTcpPort,
                PublicKcpPort = publicKcpPort,
                Enabled = externalTcpPort > 0
            };
        }

        // 向后兼容：设置旧字段（优先使用内网）
        var preferredEndpoint = registration.GetPreferredEndpoint(preferInternal: true);
        if (preferredEndpoint != null)
        {
            registration.Host = preferredEndpoint.Host;
            registration.TcpPort = preferredEndpoint.TcpPort;
            registration.KcpPort = preferredEndpoint.KcpPort;
        }

        return registration;
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
