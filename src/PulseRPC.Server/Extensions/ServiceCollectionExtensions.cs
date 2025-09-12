using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Builder;
using PulseRPC.Transport;

namespace PulseRPC.Server;

/// <summary>
/// PulseRPC 服务器依赖注入扩展 - 高性能企业级实现
/// </summary>
public static class ServiceCollectionExtensions
{
    // === 基础服务器添加 ===

    /// <summary>
    /// 添加 PulseRPC 服务器 - 最简配置
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务器构建器</returns>
    public static IPulseRPCServerBuilder AddPulseRpcServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new PulseRPCServerBuilder(services);
    }

    /// <summary>
    /// 添加 PulseRPC 服务器 - 使用配置构建器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configure">配置回调</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServer(this IServiceCollection services,
        Action<IPulseRPCServerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = services.AddPulseRpcServer();
        configure(builder);

        // 自动完成构建并注册服务器实例
        var server = builder.Build();
        services.AddSingleton(server);

        return services;
    }

    /// <summary>
    /// 添加 PulseRPC 服务器 - 使用配置文件
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置对象</param>
    /// <param name="sectionName">配置节名称</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServer(this IServiceCollection services,
        IConfiguration configuration, string sectionName = "PulseRPC:Server")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        return services.AddPulseRpcServer(builder =>
        {
            builder.ConfigureFromConfiguration(configuration, sectionName);
        });
    }

    // === 快速配置方法 ===

    /// <summary>
    /// 添加简单 TCP 服务器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="port">监听端口</param>
    /// <param name="configure">额外配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcTcpServer(this IServiceCollection services,
        int port, Action<IPulseRPCServerBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "端口必须在1-65535范围内");
        }

        return services.AddPulseRpcServer(builder =>
        {
            builder.AddTcp("Default", port, isDefault: true)
                   .UseHighPerformanceEngine() // 默认启用性能优化
                   .UseTieredMessageProcessor()
                   .UsePriorityScheduler();

            configure?.Invoke(builder);
        });
    }

    /// <summary>
    /// 添加简单 KCP 服务器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="port">监听端口</param>
    /// <param name="configure">额外配置</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcKcpServer(this IServiceCollection services,
        int port, Action<IPulseRPCServerBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "端口必须在1-65535范围内");
        }

        return services.AddPulseRpcServer(builder =>
        {
            builder.AddKcp("Default", port, isDefault: true)
                   .UseHighPerformanceEngine() // 默认启用性能优化
                   .UseTieredMessageProcessor()
                   .UsePriorityScheduler();

            configure?.Invoke(builder);
        });
    }

    // === 高性能配置快捷方法 ===

    /// <summary>
    /// 添加高吞吐量优化的服务器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="port">监听端口</param>
    /// <param name="protocol">传输协议</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddHighThroughputPulseRpcServer(this IServiceCollection services,
        int port, TransportProtocol protocol = TransportProtocol.Tcp)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidatePortRange(port, nameof(port));

        return services.AddPulseRpcServer(builder =>
        {
            // 配置传输
            if (protocol == TransportProtocol.Tcp)
            {
                builder.AddTcp("Default", port, tcp =>
                {
                    tcp.ReadBufferSize = 16384;
                    tcp.WriteBufferSize = 16384;
                    tcp.NoDelay = true;
                }, isDefault: true);
            }
            else
            {
                builder.AddKcp("Default", port, kcp =>
                {
                    kcp.Kcp = new KcpOptions
                    {
                        NoDelay = true,
                        FastReliable = true,
                        SendWindow = 256,
                        ReceiveWindow = 256,
                        Interval = 20
                    };
                }, isDefault: true);
            }

            // 高吞吐量优化配置
            builder.UseHighPerformanceEngine(options =>
                   {
                       options.L1BufferSize = 8192;
                       options.L2QueueCapacity = 512;
                       options.L3QueueCapacity = 256;
                   })
                   .UseTieredMessageProcessor(options =>
                   {
                       options.FastPath.DedicatedThreads = 4;
                       options.BatchPath.BatchSize = 32;
                   })
                   .UsePriorityScheduler(options =>
                   {
                       options.CriticalWeight = 60;
                       options.NormalWeight = 30;
                       options.BulkWeight = 10;
                   });
        });
    }

    /// <summary>
    /// 添加低延迟优化的服务器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="port">监听端口</param>
    /// <param name="protocol">传输协议</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddLowLatencyPulseRpcServer(this IServiceCollection services,
        int port, TransportProtocol protocol = TransportProtocol.Tcp)
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidatePortRange(port, nameof(port));

        return services.AddPulseRpcServer(builder =>
        {
            // 配置传输
            if (protocol == TransportProtocol.Tcp)
            {
                builder.AddTcp("Default", port, tcp =>
                {
                    tcp.NoDelay = true;
                    tcp.ReadBufferSize = 4096; // 较小的缓冲区
                    tcp.WriteBufferSize = 4096;
                }, isDefault: true);
            }
            else
            {
                builder.AddKcp("Default", port, kcp =>
                {
                    kcp.Kcp = new KcpOptions
                    {
                        NoDelay = true,
                        Interval = 10, // 更频繁的更新
                        Resend = 0, // 快速重传
                        SendWindow = 64,
                        ReceiveWindow = 64
                    };
                }, isDefault: true);
            }

            // 低延迟优化配置
            builder.UseHighPerformanceEngine(options =>
                   {
                       options.L1BufferSize = 2048; // 较小缓冲区
                       options.L2QueueCapacity = 128;
                       options.L3QueueCapacity = 64;
                   })
                   .UseTieredMessageProcessor(options =>
                   {
                       options.FastPath.DedicatedThreads = 2;
                       options.BatchPath.BatchSize = 8; // 小批量处理
                   })
                   .UsePriorityScheduler(options =>
                   {
                       options.CriticalWeight = 70; // 提高关键消息权重
                   });
        });
    }

    // === IHostBuilder 扩展支持 ===

    /// <summary>
    /// 使用 PulseRPC 服务器作为后台服务
    /// </summary>
    /// <param name="hostBuilder">主机构建器</param>
    /// <param name="configure">配置回调</param>
    /// <returns>主机构建器</returns>
    public static IHostBuilder UsePulseRpcServer(this IHostBuilder hostBuilder,
        Action<IPulseRPCServerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(hostBuilder);
        ArgumentNullException.ThrowIfNull(configure);

        return hostBuilder.ConfigureServices((context, services) =>
        {
            services.AddPulseRpcServer(configure);
            services.AddHostedService<PulseRpcServerHostedService>();
        });
    }

    /// <summary>
    /// 使用配置文件配置 PulseRPC 服务器
    /// </summary>
    /// <param name="hostBuilder">主机构建器</param>
    /// <param name="sectionName">配置节名称</param>
    /// <returns>主机构建器</returns>
    public static IHostBuilder UsePulseRpcServer(this IHostBuilder hostBuilder,
        string sectionName = "PulseRPC:Server")
    {
        ArgumentNullException.ThrowIfNull(hostBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        return hostBuilder.ConfigureServices((context, services) =>
        {
            services.AddPulseRpcServer(context.Configuration, sectionName);
            services.AddHostedService<PulseRpcServerHostedService>();
        });
    }

    // === 私有辅助方法 ===

    private static void ValidatePortRange(int port, string paramName)
    {
        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(paramName, port, "端口必须在1-65535范围内");
        }
    }
}

/// <summary>
/// 传输协议枚举
/// </summary>
public enum TransportProtocol
{
    Tcp,
    Kcp
}

/// <summary>
/// PulseRPC 服务器托管服务 - 高性能后台服务
/// </summary>
internal sealed class PulseRpcServerHostedService : IHostedService
{
    private readonly IPulseRPCServer _server;
    private readonly ILogger<PulseRpcServerHostedService> _logger;

    public PulseRpcServerHostedService(IPulseRPCServer server,
        ILogger<PulseRpcServerHostedService> logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("正在启动 PulseRPC 服务器...");

        try
        {
            await _server.StartAsync(cancellationToken);
            _logger.LogInformation("PulseRPC 服务器已启动，状态: {State}", _server.State);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动 PulseRPC 服务器失败");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("正在停止 PulseRPC 服务器...");

        try
        {
            await _server.StopAsync(cancellationToken);
            _logger.LogInformation("PulseRPC 服务器已停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止 PulseRPC 服务器时发生异常");
            throw;
        }
    }
}

/// <summary>
/// 配置扩展方法
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// 从配置文件配置服务器构建器
    /// </summary>
    /// <param name="builder">服务器构建器</param>
    /// <param name="configuration">配置对象</param>
    /// <param name="sectionName">配置节名称</param>
    /// <returns>服务器构建器</returns>
    public static IPulseRPCServerBuilder ConfigureFromConfiguration(
        this IPulseRPCServerBuilder builder,
        IConfiguration configuration,
        string sectionName = "PulseRPC:Server")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var section = configuration.GetSection(sectionName);
        if (!section.Exists())
        {
            throw new InvalidOperationException($"配置节不存在: {sectionName}");
        }

        // 基础服务器配置
        builder.ConfigureServer(options =>
        {
            section.Bind(options);
        });

        // 传输配置
        var transportsSection = section.GetSection("Transports");
        if (transportsSection.Exists())
        {
            var transportConfigs = transportsSection.Get<TransportConfiguration[]>();
            if (transportConfigs != null)
            {
                foreach (var config in transportConfigs)
                {
                    ConfigureTransport(builder, config);
                }
            }
        }
        else
        {
            // 如果没有传输配置，使用默认TCP配置
            var defaultPort = section.GetValue<int>("Port", 5000);
            builder.AddTcp("Default", defaultPort, isDefault: true);
        }

        // 性能配置
        ConfigurePerformanceOptions(builder, section);

        return builder;
    }

    private static void ConfigureTransport(IPulseRPCServerBuilder builder, TransportConfiguration config)
    {
        if (config.Type.Equals("Tcp", StringComparison.OrdinalIgnoreCase))
        {
            builder.AddTcp(config.Name, config.Port, tcp =>
            {
                if (config.Options != null)
                {
                    tcp.ReadBufferSize = config.Options.GetValue("ReadBufferSize", tcp.ReadBufferSize);
                    tcp.WriteBufferSize = config.Options.GetValue("WriteBufferSize", tcp.WriteBufferSize);
                    tcp.NoDelay = config.Options.GetValue("NoDelay", tcp.NoDelay);
                }
            }, config.IsDefault);
        }
        else if (config.Type.Equals("Kcp", StringComparison.OrdinalIgnoreCase))
        {
            builder.AddKcp(config.Name, config.Port, kcp =>
            {
                if (config.Options != null)
                {
                    var kcpOptions = config.Options.GetSection("Kcp");
                    if (kcpOptions.Exists())
                    {
                        kcp.Kcp = new KcpOptions
                        {
                            NoDelay = kcpOptions.GetValue("NoDelay", true),
                            Interval = kcpOptions.GetValue("Interval", 40),
                            Resend = kcpOptions.GetValue("Resend", 2),
                            SendWindow = kcpOptions.GetValue("SendWindow", 128),
                            ReceiveWindow = kcpOptions.GetValue("ReceiveWindow", 128),
                            FastReliable = kcpOptions.GetValue("FastReliable", false)
                        };
                    }
                }
            }, config.IsDefault);
        }
    }

    private static void ConfigurePerformanceOptions(IPulseRPCServerBuilder builder, IConfigurationSection section)
    {
        var performanceSection = section.GetSection("Performance");
        if (performanceSection.Exists())
        {
            var enableHighPerformance = performanceSection.GetValue("EnableHighPerformance", true);
            if (enableHighPerformance)
            {
                builder.UseHighPerformanceEngine(options =>
                {
                    var engineSection = performanceSection.GetSection("MessageEngine");
                    if (engineSection.Exists())
                    {
                        engineSection.Bind(options);
                    }
                });

                builder.UseTieredMessageProcessor();
                builder.UsePriorityScheduler();
            }
        }
        else
        {
            // 默认启用性能优化
            builder.UseHighPerformanceEngine()
                   .UseTieredMessageProcessor()
                   .UsePriorityScheduler();
        }
    }
}

/// <summary>
/// 配置文件中的传输配置
/// </summary>
public class TransportConfiguration
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool IsDefault { get; set; }
    public IConfigurationSection? Options { get; set; }
}
