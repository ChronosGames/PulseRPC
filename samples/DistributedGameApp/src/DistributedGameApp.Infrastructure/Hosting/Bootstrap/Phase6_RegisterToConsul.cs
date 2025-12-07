using DistributedGameApp.Infrastructure.Consul;
using DistributedGameApp.Infrastructure.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistributedGameApp.Infrastructure.Hosting.Bootstrap;

/// <summary>
/// 阶段6: 注册自己的节点信息到 Consul
/// </summary>
public class Phase6_RegisterToConsul : IBootstrapPhase
{
    private readonly ILogger<Phase6_RegisterToConsul> _logger;

    public string PhaseName => "Phase 6: Register to Consul";

    public Phase6_RegisterToConsul(ILogger<Phase6_RegisterToConsul> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("========== {PhaseName} ==========", PhaseName);

        try
        {
            // 获取 Consul 服务注册
            var consulRegistry = context.ServiceProvider.GetService<ConsulServiceRegistry>();
            if (consulRegistry == null)
            {
                _logger.LogWarning("ConsulServiceRegistry 未配置，跳过服务注册");
                return true;
            }

            // 获取配置和身份信息
            var configuration = context.ServiceProvider.GetRequiredService<IConfiguration>();
            var identity = context.ServiceProvider.GetRequiredService<ServerIdentityOptions>();

            // 获取 HTTP 端点配置（用于健康检查）
            var httpEndpointOptions = context.ServiceProvider.GetService<IOptions<HttpEndpointOptions>>()?.Value;
            if (httpEndpointOptions?.Enabled == true)
            {
                consulRegistry.HttpHealthCheck = new HttpHealthCheckInfo
                {
                    Port = httpEndpointOptions.Port,
                    Path = httpEndpointOptions.HealthPath
                };
                _logger.LogInformation(
                    "HTTP 健康检查端点: http://:{Port}{Path}",
                    httpEndpointOptions.Port,
                    httpEndpointOptions.HealthPath);
            }

            _logger.LogInformation("正在注册服务到 Consul...");

            // 构建服务注册信息
            var registration = BuildServiceRegistration(configuration, identity, context, httpEndpointOptions);

            // 注册到 Consul
            var success = await consulRegistry.RegisterServiceAsync(registration, cancellationToken);

            if (success)
            {
                context.ServiceId = registration.ServiceId;

                _logger.LogInformation(
                    "✓ 服务注册成功: {ServiceId} ({ServiceType}) @ Node {NodeId}",
                    registration.ServiceId,
                    registration.ServiceType,
                    registration.NodeId);

                // 记录端点信息
                if (registration.ExternalEndpoint?.Enabled == true)
                {
                    _logger.LogInformation(
                        "  - External: {Host}:{TcpPort}{KcpInfo}",
                        registration.ExternalEndpoint.Host,
                        registration.ExternalEndpoint.TcpPort,
                        registration.ExternalEndpoint.KcpPort.HasValue
                            ? $" (KCP: {registration.ExternalEndpoint.KcpPort})"
                            : "");
                }

                if (registration.InternalEndpoint?.Enabled == true)
                {
                    _logger.LogInformation(
                        "  - Internal: {Host}:{TcpPort}{KcpInfo}",
                        registration.InternalEndpoint.Host,
                        registration.InternalEndpoint.TcpPort,
                        registration.InternalEndpoint.KcpPort.HasValue
                            ? $" (KCP: {registration.InternalEndpoint.KcpPort})"
                            : "");
                }

                return true;
            }
            else
            {
                _logger.LogError("✗ 服务注册失败: {ServiceId}", registration.ServiceId);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "服务注册阶段失败");
            return false;
        }
    }

    private ServiceRegistration BuildServiceRegistration(
        IConfiguration configuration,
        ServerIdentityOptions identity,
        BootstrapContext context,
        HttpEndpointOptions? httpEndpointOptions)
    {
        var networkConfig = configuration.GetSection("Network");
        if (!networkConfig.Exists())
        {
            throw new InvalidOperationException("Network configuration section not found");
        }

        var registration = new ServiceRegistration
        {
            ServiceId = $"{identity.ServiceType.ToLower()}-{identity.NodeId}",
            ServiceType = identity.ServiceType,
            NodeId = identity.NodeId,
            NodeName = identity.NodeName,
            CurrentLoad = 0,
            MaxCapacity = identity.MaxCapacity,
            Status = "Online"
        };

        // 解析内网端点（从上下文中获取实际监听端口）
        var internalConfig = networkConfig.GetSection("Internal");
        if (internalConfig.Exists() && internalConfig.GetValue<bool>("Enabled") && context.InternalServer != null)
        {
            var defaultTransport = context.InternalServer.GetDefaultTransport();
            if (defaultTransport != null)
            {
                var host = internalConfig.GetValue<string>("Host") ?? "localhost";
                registration.InternalEndpoint = new ServiceClient.NetworkEndpoint
                {
                    Host = host == "0.0.0.0" ? "localhost" : host,
                    TcpPort = defaultTransport.Port,
                    Enabled = true
                };
            }
        }

        // 解析外网端点（从上下文中获取实际监听端口）
        var externalConfig = networkConfig.GetSection("External");
        if (externalConfig.Exists() && externalConfig.GetValue<bool>("Enabled") && context.ExternalServer != null)
        {
            var defaultTransport = context.ExternalServer.GetDefaultTransport();
            if (defaultTransport != null)
            {
                var host = externalConfig.GetValue<string>("Host") ?? "localhost";

                // 读取 TCP 和 KCP 的公网端口配置
                var tcpConfig = externalConfig.GetSection("Tcp");
                var kcpConfig = externalConfig.GetSection("Kcp");

                var publicHost = externalConfig.GetValue<string>("PublicHost");
                var publicTcpPort = tcpConfig.GetValue<int?>("PublicPort");
                var publicKcpPort = kcpConfig.GetValue<int?>("PublicPort");

                _logger.LogInformation(
                    "[Phase6] External endpoint - ListenPort: {ListenPort}, PublicHost: {PublicHost}, PublicTcpPort: {PublicTcpPort}, PublicKcpPort: {PublicKcpPort}",
                    defaultTransport.Port, publicHost, publicTcpPort, publicKcpPort);

                registration.ExternalEndpoint = new ServiceClient.NetworkEndpoint
                {
                    Host = host == "0.0.0.0" ? "localhost" : host,
                    TcpPort = defaultTransport.Port,
                    PublicHost = publicHost,
                    PublicTcpPort = publicTcpPort,
                    PublicKcpPort = publicKcpPort,
                    Enabled = true
                };
            }
        }

        // 向后兼容：设置旧字段（优先使用内网）
        var preferredEndpoint = registration.GetPreferredEndpoint(preferInternal: true);
        if (preferredEndpoint != null)
        {
            registration.Host = preferredEndpoint.Host;
            registration.TcpPort = preferredEndpoint.TcpPort;
            registration.KcpPort = preferredEndpoint.KcpPort;
        }

        // 添加 HTTP 端点元数据（用于健康检查和 Prometheus metrics）
        if (httpEndpointOptions?.Enabled == true)
        {
            var httpHost = preferredEndpoint?.Host ?? "localhost";
            registration.Metadata["HttpHealthPort"] = httpEndpointOptions.Port.ToString();
            registration.Metadata["HttpHealthUrl"] = $"http://{httpHost}:{httpEndpointOptions.Port}{httpEndpointOptions.HealthPath}";

            if (httpEndpointOptions.EnableMetrics)
            {
                registration.Metadata["HttpMetricsUrl"] = $"http://{httpHost}:{httpEndpointOptions.Port}{httpEndpointOptions.MetricsPath}";
            }
        }

        return registration;
    }
}
