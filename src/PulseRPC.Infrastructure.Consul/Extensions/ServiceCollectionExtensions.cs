using Consul;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.ServiceRegistration;
using PulseRPC.ServiceDiscovery;
using PulseRPC.Infrastructure.Consul;
using System.Security.Cryptography.X509Certificates;

namespace PulseRPC.Infrastructure.Consul;

/// <summary>
/// Consul服务发现依赖注入扩展
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加Consul服务发现
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置选项</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddConsulServiceDiscovery(
        this IServiceCollection services,
        Action<ConsulOptions>? configureOptions = null)
    {
        // 配置选项
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // 注册Consul客户端
        services.TryAddSingleton<IConsulClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ConsulOptions>>().Value;
            var logger = provider.GetRequiredService<ILogger<ConsulClient>>();
            
            return CreateConsulClient(options, logger);
        });

        // 注册服务发现和注册器
        services.TryAddSingleton<ConsulServiceDiscovery>();
        services.TryAddSingleton<IServiceDiscovery>(provider => 
            provider.GetRequiredService<ConsulServiceDiscovery>());
        services.TryAddSingleton<IServiceRegistry>(provider => 
            provider.GetRequiredService<ConsulServiceDiscovery>());

        // 注册健康检查服务
        services.TryAddSingleton<ConsulHealthCheckService>();
        services.TryAddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<ConsulHealthCheckService>());

        return services;
    }

    /// <summary>
    /// 添加Consul服务发现（从配置文件）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <param name="sectionName">配置节名称</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddConsulServiceDiscovery(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "Consul")
    {
        services.Configure<ConsulOptions>(configuration.GetSection(sectionName));
        return services.AddConsulServiceDiscovery();
    }

    /// <summary>
    /// 配置Consul端点
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="endpoint">Consul端点</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseConsulEndpoint(
        this IServiceCollection services,
        string endpoint)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with { Endpoint = endpoint };
        });

        return services;
    }

    /// <summary>
    /// 配置Consul数据中心
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="datacenter">数据中心</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseConsulDatacenter(
        this IServiceCollection services,
        string datacenter)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with { Datacenter = datacenter };
        });

        return services;
    }

    /// <summary>
    /// 配置Consul认证令牌
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="token">认证令牌</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseConsulToken(
        this IServiceCollection services,
        string token)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with { Token = token };
        });

        return services;
    }

    /// <summary>
    /// 配置Consul健康检查
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureHealthCheck">健康检查配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureConsulHealthCheck(
        this IServiceCollection services,
        Action<ConsulHealthCheckOptions> configureHealthCheck)
    {
        services.Configure<ConsulOptions>(options =>
        {
            var healthCheck = options.HealthCheck;
            configureHealthCheck(healthCheck);
            options = options with { HealthCheck = healthCheck };
        });

        return services;
    }

    /// <summary>
    /// 配置Consul服务发现选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureDiscovery">发现选项配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureConsulDiscovery(
        this IServiceCollection services,
        Action<ConsulDiscoveryOptions> configureDiscovery)
    {
        services.Configure<ConsulOptions>(options =>
        {
            var discoveryOptions = options.DiscoveryOptions;
            configureDiscovery(discoveryOptions);
            options = options with { DiscoveryOptions = discoveryOptions };
        });

        return services;
    }

    /// <summary>
    /// 配置Consul连接选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureConnection">连接选项配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureConsulConnection(
        this IServiceCollection services,
        Action<ConsulConnectionOptions> configureConnection)
    {
        services.Configure<ConsulOptions>(options =>
        {
            var connectionOptions = options.Connection;
            configureConnection(connectionOptions);
            options = options with { Connection = connectionOptions };
        });

        return services;
    }

    /// <summary>
    /// 配置Consul安全选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureSecurity">安全选项配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureConsulSecurity(
        this IServiceCollection services,
        Action<ConsulSecurityOptions> configureSecurity)
    {
        services.Configure<ConsulOptions>(options =>
        {
            var securityOptions = options.Security;
            configureSecurity(securityOptions);
            options = options with { Security = securityOptions };
        });

        return services;
    }

    /// <summary>
    /// 启用Consul TLS
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="certificatePath">证书路径</param>
    /// <param name="privateKeyPath">私钥路径</param>
    /// <param name="caCertificatePath">CA证书路径</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection EnableConsulTls(
        this IServiceCollection services,
        string? certificatePath = null,
        string? privateKeyPath = null,
        string? caCertificatePath = null)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with
            {
                Security = options.Security with
                {
                    EnableTls = true,
                    CertificatePath = certificatePath,
                    PrivateKeyPath = privateKeyPath,
                    CaCertificatePath = caCertificatePath
                }
            };
        });

        return services;
    }

    /// <summary>
    /// 禁用Consul健康检查
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection DisableConsulHealthCheck(this IServiceCollection services)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with
            {
                HealthCheck = options.HealthCheck with { Enabled = false }
            };
        });

        return services;
    }

    /// <summary>
    /// 启用包含不健康服务的发现
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection IncludeUnhealthyServices(this IServiceCollection services)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with
            {
                DiscoveryOptions = options.DiscoveryOptions with { HealthyOnly = false }
            };
        });

        return services;
    }

    /// <summary>
    /// 配置服务标签过滤
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="tags">标签列表</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseConsulTags(
        this IServiceCollection services,
        params string[] tags)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with
            {
                DiscoveryOptions = options.DiscoveryOptions with { Tags = tags }
            };
        });

        return services;
    }

    /// <summary>
    /// 启用服务监听
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="watchTimeout">监听超时时间</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection EnableConsulWatching(
        this IServiceCollection services,
        TimeSpan? watchTimeout = null)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with
            {
                DiscoveryOptions = options.DiscoveryOptions with 
                { 
                    EnableWatching = true,
                    WatchTimeout = watchTimeout ?? options.DiscoveryOptions.WatchTimeout
                }
            };
        });

        return services;
    }

    /// <summary>
    /// 禁用服务监听
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection DisableConsulWatching(this IServiceCollection services)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with
            {
                DiscoveryOptions = options.DiscoveryOptions with { EnableWatching = false }
            };
        });

        return services;
    }

    /// <summary>
    /// 配置缓存选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="useCache">是否使用缓存</param>
    /// <param name="cacheTtl">缓存生存时间</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureConsulCache(
        this IServiceCollection services,
        bool useCache = true,
        TimeSpan? cacheTtl = null)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with
            {
                DiscoveryOptions = options.DiscoveryOptions with 
                { 
                    UseCache = useCache,
                    CacheTtl = cacheTtl ?? options.DiscoveryOptions.CacheTtl
                }
            };
        });

        return services;
    }

    /// <summary>
    /// 配置重试选项
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="maxAttempts">最大重试次数</param>
    /// <param name="initialDelay">初始延迟</param>
    /// <param name="maxDelay">最大延迟</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection ConfigureConsulRetry(
        this IServiceCollection services,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with
            {
                Retry = options.Retry with 
                { 
                    MaxAttempts = maxAttempts,
                    InitialDelay = initialDelay ?? options.Retry.InitialDelay,
                    MaxDelay = maxDelay ?? options.Retry.MaxDelay
                }
            };
        });

        return services;
    }

    /// <summary>
    /// 使用开发环境配置
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseConsulDevelopmentSettings(this IServiceCollection services)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with
            {
                Endpoint = "http://localhost:8500",
                Security = options.Security with { InsecureSkipTlsVerify = true },
                HealthCheck = options.HealthCheck with { TlsSkipVerify = true },
                DiscoveryOptions = options.DiscoveryOptions with { HealthyOnly = false }
            };
        });

        return services;
    }

    /// <summary>
    /// 使用生产环境配置
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection UseConsulProductionSettings(this IServiceCollection services)
    {
        services.Configure<ConsulOptions>(options =>
        {
            options = options with
            {
                Security = options.Security with { InsecureSkipTlsVerify = false },
                HealthCheck = options.HealthCheck with { TlsSkipVerify = false },
                DiscoveryOptions = options.DiscoveryOptions with { HealthyOnly = true },
                Retry = options.Retry with { Enabled = true }
            };
        });

        return services;
    }

    #region Private Methods

    private static IConsulClient CreateConsulClient(ConsulOptions options, ILogger<ConsulClient> logger)
    {
        var config = new ConsulClientConfiguration
        {
            Address = new Uri(options.Endpoint)
        };

        // 配置数据中心
        if (!string.IsNullOrWhiteSpace(options.Datacenter))
        {
            config.Datacenter = options.Datacenter;
        }

        // 配置认证令牌
        if (!string.IsNullOrWhiteSpace(options.Token))
        {
            config.Token = options.Token;
        }

        // 配置HTTP客户端
        var handler = new HttpClientHandler();
        
        // 配置TLS
        if (options.Security.EnableTls)
        {
            ConfigureTls(handler, options.Security, logger);
        }

        // 配置超时
        var httpClient = new HttpClient(handler)
        {
            Timeout = options.Connection.Timeout
        };

        // 配置HTTP/2
        if (options.Connection.UseHttp2)
        {
            httpClient.DefaultRequestVersion = new Version(2, 0);
        }

        // 配置Keep-Alive
        if (options.Connection.KeepAlive > TimeSpan.Zero)
        {
            httpClient.DefaultRequestHeaders.Connection.Add("keep-alive");
        }

        return new ConsulClient(config, httpClient);
    }

    private static void ConfigureTls(HttpClientHandler handler, ConsulSecurityOptions security, ILogger logger)
    {
        try
        {
            // 跳过TLS验证
            if (security.InsecureSkipTlsVerify)
            {
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                logger.LogWarning("TLS verification is disabled. This should not be used in production.");
            }

            // 配置客户端证书
            if (!string.IsNullOrWhiteSpace(security.CertificatePath) && 
                !string.IsNullOrWhiteSpace(security.PrivateKeyPath))
            {
                var clientCert = LoadClientCertificate(security.CertificatePath, security.PrivateKeyPath);
                handler.ClientCertificates.Add(clientCert);
                logger.LogInformation("Client certificate configured from: {CertPath}", security.CertificatePath);
            }

            // 配置CA证书
            if (!string.IsNullOrWhiteSpace(security.CaCertificatePath))
            {
                var caCert = new X509Certificate2(security.CaCertificatePath);
                handler.ClientCertificates.Add(caCert);
                logger.LogInformation("CA certificate configured from: {CaPath}", security.CaCertificatePath);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to configure TLS settings");
            throw;
        }
    }

    private static X509Certificate2 LoadClientCertificate(string certificatePath, string privateKeyPath)
    {
        // 这里可以实现从文件加载证书和私钥的逻辑
        // 简化实现，假设证书文件已包含私钥
        return new X509Certificate2(certificatePath);
    }

    #endregion
}
