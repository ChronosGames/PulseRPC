using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Abstractions;
using PulseRPC.Benchmark.Metrics.Collectors;
using PulseRPC.Benchmark.Metrics.Core;
using PulseRPC.Benchmark.Server.Configuration;
using PulseRPC.Benchmark.Server.Services;
using PulseRPC.Benchmark.Shared;
using PulseRPC.Generated;
using PulseRPC.Serialization;
using PulseRPC.Server;
using PulseRPC.Server.Extensions;
using PulseRPC.Transport;
using CollectorConfiguration = PulseRPC.Benchmark.Metrics.Abstractions.CollectorConfiguration;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace PulseRPC.Benchmark.Server;

/// <summary>
/// BenchmarkApp服务端主程序入口
/// </summary>
internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("PulseRPC Benchmark Server - 高性能RPC基准测试服务端")
        {
            CreateStartCommand(),
            CreateValidateCommand(),
            CreateVersionCommand()
        };

        return await rootCommand.InvokeAsync(args);
    }

    /// <summary>
    /// 创建启动服务命令
    /// </summary>
    private static Command CreateStartCommand()
    {
        var configOption = new Option<string>(
            aliases: ["--config", "-c"],
            description: "配置文件路径",
            getDefaultValue: () => "configs/server-config.json");

        var portOption = new Option<int>(
            aliases: ["--port", "-p"],
            description: "服务监听端口",
            getDefaultValue: () => 8080);

        var logLevelOption = new Option<string>(
            aliases: ["--log-level", "-l"],
            description: "日志级别 (Trace|Debug|Information|Warning|Error|Critical)",
            getDefaultValue: () => "Information");

        var metricsPortOption = new Option<int>(
            aliases: ["--metrics-port", "-m"],
            description: "指标监控端口",
            getDefaultValue: () => 9090);

        var startCommand = new Command("start", "启动基准测试服务端")
        {
            configOption,
            portOption,
            logLevelOption,
            metricsPortOption
        };

        startCommand.SetHandler(async (configPath, port, logLevel, metricsPort) =>
        {
            try
            {
                await StartServerAsync(configPath, port, logLevel, metricsPort);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"服务端启动失败: {ex.Message}");
                Environment.Exit(1);
            }
        }, configOption, portOption, logLevelOption, metricsPortOption);

        return startCommand;
    }

    /// <summary>
    /// 创建配置验证命令
    /// </summary>
    private static Command CreateValidateCommand()
    {
        var configOption = new Option<string>(
            aliases: ["--config", "-c"],
            description: "要验证的配置文件路径",
            getDefaultValue: () => "configs/server-config.json");

        var validateCommand = new Command("validate", "验证服务端配置文件")
        {
            configOption
        };

        validateCommand.SetHandler(async (configPath) =>
        {
            try
            {
                await ValidateConfigurationAsync(configPath);
                Console.WriteLine("✅ 配置文件验证通过");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 配置文件验证失败: {ex.Message}");
                Environment.Exit(1);
            }
        }, configOption);

        return validateCommand;
    }

    /// <summary>
    /// 创建版本信息命令
    /// </summary>
    private static Command CreateVersionCommand()
    {
        var versionCommand = new Command("version", "显示版本信息");

        versionCommand.SetHandler(() =>
        {
            var version = typeof(Program).Assembly.GetName().Version;
            Console.WriteLine($"PulseRPC Benchmark Server v{version}");
            Console.WriteLine("Build: Release");
            Console.WriteLine($"Runtime: {Environment.Version}");
            Console.WriteLine($"Platform: {Environment.OSVersion}");
        });

        return versionCommand;
    }

    /// <summary>
    /// 启动服务端
    /// </summary>
    private static async Task StartServerAsync(string configPath, int port, string logLevel, int metricsPort)
    {
        Console.WriteLine("🚀 启动PulseRPC基准测试服务端...");
        Console.WriteLine($"📁 配置文件: {configPath}");
        Console.WriteLine($"🌐 服务端口: {port}");
        Console.WriteLine($"📊 指标端口: {metricsPort}");
        Console.WriteLine($"📝 日志级别: {logLevel}");

        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // 1. 基础配置
                var config = LoadServerConfiguration(configPath, port, metricsPort);
                services.AddSingleton(config);

                // 2. 指标系统配置
                services.AddSingleton<CollectorConfiguration>(provider =>
                {
                    var serverConfig = provider.GetRequiredService<ServerConfiguration>();
                    return new CollectorConfiguration
                    {
                        SamplingIntervalMs = 1000,
                        MaxHistorySnapshots = 100,
                        EnableAutoSnapshot = true,
                        CollectSystemMetrics = serverConfig.EnablePerformanceCounters,
                        SnapshotIntervalMs = Math.Max(5000, serverConfig.HealthCheckIntervalSeconds * 500)
                    };
                });

                services.AddSingleton(GeneratedResponseSerializers.Registry);

                // 3. 配置 PulseRPC 服务端 - 使用手动构建模式避免服务注册被覆盖
                var builder = services.AddUnifiedPulseServerBuilder();  // 使用无参版本，不自动启用高级特性

                // 配置 TCP 传输通道（必需）
                builder.AddTcpTransport("TcpChannel", config.Port, true, tcpOptions =>
                {
                    tcpOptions.NoDelay = true;
                    tcpOptions.RecvBufferSize = config.BufferSize;
                    tcpOptions.SendBufferSize = config.BufferSize;
                });

                // 如果启用 KCP，也添加 KCP 传输
                if (config.EnableKcp)
                {
                    builder.AddKcpTransport("KcpChannel", config.Port + 1, false, kcpOptions =>
                    {
                        kcpOptions.NoDelay = true;
                        // kcpOptions.Interval = 10;
                        // kcpOptions.Resend = 2;
                        // kcpOptions.SendWindow = 256;
                        // kcpOptions.RecvWindow = 256;
                    });
                }

                // ⭐ 关键：注册 RPC 服务实现
                // builder.AddService<IBenchmarkHub, BenchmarkHubImpl>(ServiceLifetime.Scoped);

                // 配置服务器选项
                builder.ConfigureOptions(options =>
                {
                    options.MaxConcurrentOperations = config.MaxConnections;
                    options.Transports = new List<TransportChannelConfiguration>()
                    {
                        new TransportChannelConfiguration()
                        {
                            Name = "TcpChannel",
                            Type = TransportType.Tcp,
                            Port = config.Port,
                            IsDefault = true
                        },
                    };

                    if (config.EnableKcp)
                    {
                        options.Transports.Add(new TransportChannelConfiguration()
                        {
                            Name = "KcpChannel",
                            Type = TransportType.Kcp,
                            Port = config.Port + 1,
                            IsDefault = false,
                        });
                    }
                });

                // === 逐步启用高性能特性 ===

                // 阶段1: 启用高性能消息引擎（已验证稳定）
                // builder.UseHighPerformanceEngine(options =>
                // {
                //     options.Enabled = true;
                //     options.L1BufferSize = 4096;         // L1 循环缓冲区
                //     options.L2QueueCapacity = 256;       // L2 批处理队列
                //     options.L3QueueCapacity = 128;       // L3 响应队列
                // });

                // 阶段2: 启用优先级调度器（已验证稳定）
                // builder.UsePriorityScheduler(options =>
                // {
                //     options.Enabled = true;
                //     options.CriticalWeight = 50;         // 关键消息权重
                //     options.NormalWeight = 30;           // 普通消息权重
                //     options.BulkWeight = 20;             // 批量消息权重
                // });

                // 阶段3: 启用分层消息处理器（⚠️ 谨慎启用，建议先测试）
                // 注意：根据之前的重构，现在的元数据传递和内存管理已修复
                // 如果测试稳定，可以启用以获得更高性能
                // builder.UseTieredMessageProcessor(options =>
                // {
                //     options.Enabled = true;
                //
                //     // 快速路径配置（小消息）
                //     options.FastPath.MessageSizeThreshold = 1024;  // < 1KB
                //     options.FastPath.DedicatedThreads = 2;
                //
                //     // 批处理路径配置（中等消息）
                //     options.BatchPath.MinMessageSize = 1024;       // >= 1KB
                //     options.BatchPath.MaxMessageSize = 65536;      // <= 64KB
                //     options.BatchPath.BatchSize = 32;
                // });

                // 手动构建，确保服务注册生效
                builder.Build();

                // 使用高吞吐量处理器（依赖配置）
                // services.AddHighThroughputProcessor(options =>
                // {
                //     options.Enabled = true;
                //     options.L1BufferSize = 8192;        // 8K消息缓冲
                //     options.L2QueueCapacity = 200;      // 200个批次队列
                //     options.L3QueueCapacity = 100;      // 100个响应批次队列
                //     options.BatchIntervalMs = 1;        // 1ms批处理间隔
                //     options.MaxBatchSize = 128;         // 每批最多128条消息
                //     options.CriticalMessageTimeoutUs = 50;  // 关键消息50微秒超时
                //     options.NormalMessageDropRate = 0.9;     // 90%丢弃率（高负载时）
                //     options.BatchSoftTimeoutMs = 20;    // 20ms软超时
                //     options.PerformanceCheckFrequency = 20; // 每20条消息检查性能
                //     options.EnableDetailedLogging = false;  // 生产环境禁用详细日志
                // });

                // 4. 指标收集器（依赖配置）
                MetricsConfigurationBuilderExtensions.CreateProductionConfiguration();

                //
                services.AddSingleton<IMetricsCollector, RealTimeMetricsCollector>();

                // 6. 健康检查
                // services.AddSingleton<HealthCheckService>();
                // services.AddHealthChecks()
                //     .AddCheck<MetricsHealthCheck>("metrics")
                //     .AddCheck<BenchmarkServiceHealthCheck>("benchmark");

                // 7. 宿主服务（最后注册）
                services.AddHostedService<BenchmarkServerHost>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                if (Enum.TryParse<LogLevel>(logLevel, out var level))
                {
                    logging.SetMinimumLevel(level);
                }
            });

        var host = hostBuilder.Build();

        // 注册优雅关闭
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n🛑 接收到关闭信号，正在优雅关闭服务端...");
            host.StopAsync().Wait(TimeSpan.FromSeconds(30));
        };

        Console.WriteLine("✅ 服务端启动完成，按 Ctrl+C 停止服务");
        await host.RunAsync();
    }

    /// <summary>
    /// 加载服务端配置
    /// </summary>
    private static ServerConfiguration LoadServerConfiguration(string configPath, int port, int metricsPort)
    {
        try
        {
            if (File.Exists(configPath))
            {
                var configJson = File.ReadAllText(configPath);
                var config = System.Text.Json.JsonSerializer.Deserialize<ServerConfiguration>(configJson)
                    ?? new ServerConfiguration();

                // 命令行参数覆盖配置文件
                config.Port = port;
                config.MetricsPort = metricsPort;

                return config;
            }
            else
            {
                Console.WriteLine($"⚠️  配置文件 {configPath} 不存在，使用默认配置");
                return new ServerConfiguration
                {
                    Port = port,
                    MetricsPort = metricsPort
                };
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"加载配置文件失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 验证配置文件
    /// </summary>
    private static async Task ValidateConfigurationAsync(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"配置文件不存在: {configPath}");
        }

        var configJson = await File.ReadAllTextAsync(configPath);

        try
        {
            var config = System.Text.Json.JsonSerializer.Deserialize<ServerConfiguration>(configJson);

            if (config == null)
            {
                throw new InvalidOperationException("配置文件内容为空");
            }

            // 验证端口范围
            if (config.Port is <= 0 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(config.Port), "端口号必须在1-65535范围内");
            }

            if (config.MetricsPort is <= 0 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(config.MetricsPort), "指标端口号必须在1-65535范围内");
            }

            // 验证其他配置项
            if (config.MaxConnections <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(config.MaxConnections), "最大连接数必须大于0");
            }

            Console.WriteLine($"✅ 端口配置: {config.Port}");
            Console.WriteLine($"✅ 指标端口: {config.MetricsPort}");
            Console.WriteLine($"✅ 最大连接数: {config.MaxConnections}");
            Console.WriteLine($"✅ 压缩启用: {config.EnableCompression}");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException($"配置文件JSON格式错误: {ex.Message}", ex);
        }
    }
}
