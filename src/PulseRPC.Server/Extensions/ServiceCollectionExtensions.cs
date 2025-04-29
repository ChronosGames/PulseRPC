using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Internal;
using PulseRPC.Server.Auth;
using PulseRPC.Server.Monitoring;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// PulseRPC 服务器的依赖注入扩展
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 向服务容器添加 PulseRPC 服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configure">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServer(
        this IServiceCollection services,
        Action<PulseServerOptions> configure)
    {
        var options = new PulseServerOptions();
        configure(options);

        services.AddSingleton(options);

        // 注册度量收集器（如果启用）
        if (options.EnableMetrics)
        {
            services.AddSingleton<MetricsCollector>();
        }

        // 注册认证提供者（如果有）
        if (options.AuthenticationProvider != null)
        {
            services.AddSingleton(options.AuthenticationProvider);
        }

        // 注册授权提供者（如果有）
        if (options.AuthorizationProvider != null)
        {
            services.AddSingleton(options.AuthorizationProvider);
        }

        // 注册 PulseServer
        services.AddSingleton<PulseServer>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<PulseServer>>();
            PulseServer server;

            if (options.Certificate != null)
            {
                if (options.AuthenticationProvider != null)
                {
                    var authProvider = sp.GetRequiredService<IAuthenticationProvider>();
                    var authzProvider = sp.GetService<IAuthorizationProvider>();
                    server = new PulseServer(
                        options.EndPoint,
                        logger,
                        options.Certificate,
                        authProvider,
                        authzProvider,
                        options.ClientCertificateValidation);
                }
                else
                {
                    server = new PulseServer(
                        options.EndPoint,
                        logger,
                        options.Certificate,
                        options.ClientCertificateValidation);
                }
            }
            else
            {
                server = new PulseServer(options.EndPoint, logger);
            }

            // 注册服务和 Hub
            foreach (var serviceRegistration in options.ServiceRegistrations)
            {
                serviceRegistration(server, sp);
            }

            return server;
        });

        return services;
    }
}

/// <summary>
/// PulseRPC 服务器配置选项
/// </summary>
public class PulseServerOptions
{
    /// <summary>
    /// 服务器监听的 IP 端点
    /// </summary>
    public IPEndPoint EndPoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 8080);

    /// <summary>
    /// SSL 证书（如果使用 SSL/TLS）
    /// </summary>
    public X509Certificate2? Certificate { get; set; }

    /// <summary>
    /// 客户端证书验证回调
    /// </summary>
    public System.Net.Security.RemoteCertificateValidationCallback? ClientCertificateValidation { get; set; }

    /// <summary>
    /// 认证提供者
    /// </summary>
    public IAuthenticationProvider? AuthenticationProvider { get; set; }

    /// <summary>
    /// 授权提供者
    /// </summary>
    public IAuthorizationProvider? AuthorizationProvider { get; set; }

    /// <summary>
    /// 是否启用性能指标收集
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// 服务注册回调列表
    /// </summary>
    internal List<Action<PulseServer, IServiceProvider>> ServiceRegistrations { get; } = new();

    /// <summary>
    /// 添加服务注册
    /// </summary>
    /// <typeparam name="TService">服务接口</typeparam>
    /// <typeparam name="TImplementation">服务实现类</typeparam>
    public PulseServerOptions AddService<TService, TImplementation>()
        where TService : class, IPulseService<TService>
        where TImplementation : class, TService
    {
        ServiceRegistrations.Add((server, sp) =>
        {
            var implementation = sp.GetRequiredService<TImplementation>();
            server.RegisterService<TService, TImplementation>(implementation);
        });

        return this;
    }

    /// <summary>
    /// 添加 Hub 注册
    /// </summary>
    /// <typeparam name="THub">Hub 接口</typeparam>
    /// <typeparam name="TReceiver">接收器接口</typeparam>
    /// <typeparam name="TImplementation">Hub 实现类</typeparam>
    public PulseServerOptions AddHub<THub, TReceiver, TImplementation>()
        where THub : class, IPulseHub<THub, TReceiver>
        where TReceiver : class
        where TImplementation : class, THub
    {
        ServiceRegistrations.Add((server, _) =>
        {
            server.RegisterHub<THub, TReceiver>(typeof(TImplementation));
        });

        return this;
    }
}
