using System;
using System.CommandLine;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Client.Commands;
using PulseRPC.Benchmark.Client.Configuration;
using PulseRPC.Benchmark.Client.Engine;
using PulseRPC.Benchmark.Client.Transport;
using PulseRPC.Benchmark.Core.Extensions;
using PulseRPC.Benchmark.Metrics.Collectors;

namespace PulseRPC.Benchmark.Client;

/// <summary>
/// BenchmarkApp客户端主程序入口
/// </summary>
internal class Program
{
    private static readonly Option<bool> VerboseOption = new(
        aliases: new[] { "--verbose", "-v" },
        description: "启用详细输出");

    private static readonly Option<string> ConfigOption = new(
        aliases: new[] { "--config", "-c" },
        description: "配置文件路径",
        getDefaultValue: () => "configs/benchmark-config.json");

    private static readonly Option<string> LogLevelOption = new(
        aliases: new[] { "--log-level", "-l" },
        description: "日志级别 (Trace|Debug|Information|Warning|Error|Critical)",
        getDefaultValue: () => "Information");

    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("PulseRPC Benchmark Client - 高性能RPC基准测试客户端")
        {
            CreateRunCommand(),
            CreateListScenariosCommand(),
            CreateValidateConfigCommand(),
            CreateGenerateReportCommand(),
            CreateVersionCommand()
        };

        // 全局选项
        rootCommand.AddGlobalOption(VerboseOption);
        rootCommand.AddGlobalOption(ConfigOption);
        rootCommand.AddGlobalOption(LogLevelOption);

        return await rootCommand.InvokeAsync(args);
    }

    /// <summary>
    /// 创建运行测试命令
    /// </summary>
    private static Command CreateRunCommand()
    {
        var serverOption = new Option<string>(
            aliases: new[] { "--server", "-s" },
            description: "服务器地址",
            getDefaultValue: () => "localhost:8080");

        var scenarioOption = new Option<string>(
            aliases: new[] { "--scenario", "--test" },
            description: "测试场景名称");

        var durationOption = new Option<int>(
            aliases: new[] { "--duration", "-d" },
            description: "测试持续时间（秒）",
            getDefaultValue: () => 60);

        var connectionsOption = new Option<int>(
            aliases: new[] { "--connections", "--conn" },
            description: "并发连接数",
            getDefaultValue: () => 10);

        var rateOption = new Option<int>(
            aliases: new[] { "--rate", "-r" },
            description: "请求速率（QPS）",
            getDefaultValue: () => 100);

        var outputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            description: "输出文件路径");

        var formatOption = new Option<string>(
            aliases: new[] { "--format", "-f" },
            description: "报告格式 (json|csv|html)",
            getDefaultValue: () => "json");

        var warmupOption = new Option<int>(
            aliases: new[] { "--warmup", "-w" },
            description: "预热时间（秒）",
            getDefaultValue: () => 10);

        var runCommand = new Command("run", "运行基准测试")
        {
            serverOption,
            scenarioOption,
            durationOption,
            connectionsOption,
            rateOption,
            outputOption,
            formatOption,
            warmupOption
        };

        runCommand.SetHandler(async (server, scenario, duration, connections, rate, output, format, warmup) =>
        {
            try
            {
                // 默认值处理
                var config = "configs/benchmark-config.json";
                var verbose = false;
                var logLevel = "Information";

                await RunBenchmarkAsync(server, scenario, duration, connections, rate, output, format, warmup, config, verbose, logLevel);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 测试执行失败: {ex.Message}");
                if (false) // verbose默认false
                {
                    Console.WriteLine(ex.StackTrace);
                }
                Environment.Exit(1);
            }
        }, serverOption, scenarioOption, durationOption, connectionsOption, rateOption, outputOption, formatOption, warmupOption);

        return runCommand;
    }

    /// <summary>
    /// 创建列出场景命令
    /// </summary>
    private static Command CreateListScenariosCommand()
    {
        var listCommand = new Command("list-scenarios", "列出所有可用的测试场景");

        listCommand.SetHandler(() =>
        {
            Console.WriteLine("📋 可用的测试场景:");
            Console.WriteLine("  • ping-pong        - Ping-Pong延迟测试");
            Console.WriteLine("  • echo-latency     - Echo回显延迟测试");
            Console.WriteLine("  • throughput       - 吞吐量测试");
            Console.WriteLine("  • latency-analysis - 高级延迟分析测试");
            Console.WriteLine("  • stress-test      - 压力测试（高负载）");
            Console.WriteLine("  • burst-test       - 突发流量测试");
            Console.WriteLine("  • mixed-workload   - 混合负载测试");
            Console.WriteLine();
            Console.WriteLine("使用 'run --scenario <scenario-name>' 运行特定场景");
        });

        return listCommand;
    }

    /// <summary>
    /// 创建验证配置命令
    /// </summary>
    private static Command CreateValidateConfigCommand()
    {
        var configPathOption = new Option<string>(
            aliases: new[] { "--file", "-f" },
            description: "要验证的配置文件路径");

        var validateCommand = new Command("validate-config", "验证配置文件")
        {
            configPathOption
        };

        validateCommand.SetHandler(async (configPath, globalConfig) =>
        {
            var targetConfig = configPath ?? globalConfig;
            try
            {
                await ValidateConfigurationAsync(targetConfig);
                Console.WriteLine("✅ 配置文件验证通过");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 配置文件验证失败: {ex.Message}");
                Environment.Exit(1);
            }
        }, configPathOption, ConfigOption);

        return validateCommand;
    }

    /// <summary>
    /// 创建生成报告命令
    /// </summary>
    private static Command CreateGenerateReportCommand()
    {
        var inputOption = new Option<string>(
            aliases: new[] { "--input", "-i" },
            description: "输入结果文件路径");
        inputOption.IsRequired = true;

        var outputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            description: "输出报告文件路径");

        var formatOption = new Option<string>(
            aliases: new[] { "--format", "-f" },
            description: "报告格式 (html|pdf|json|csv)",
            getDefaultValue: () => "html");

        var templateOption = new Option<string>(
            aliases: new[] { "--template", "-t" },
            description: "报告模板名称",
            getDefaultValue: () => "default");

        var reportCommand = new Command("generate-report", "从测试结果生成报告")
        {
            inputOption,
            outputOption,
            formatOption,
            templateOption
        };

        reportCommand.SetHandler(async (input, output, format, template) =>
        {
            try
            {
                await GenerateReportAsync(input, output, format, template);
                Console.WriteLine($"✅ 报告已生成: {output ?? $"report.{format}"}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 报告生成失败: {ex.Message}");
                Environment.Exit(1);
            }
        }, inputOption, outputOption, formatOption, templateOption);

        return reportCommand;
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
            Console.WriteLine($"PulseRPC Benchmark Client v{version}");
            Console.WriteLine("Build: Release");
            Console.WriteLine($"Runtime: {Environment.Version}");
            Console.WriteLine($"Platform: {Environment.OSVersion}");
            Console.WriteLine("Features: HTTP/JSON报告, CSV导出, 多场景支持");
        });

        return versionCommand;
    }

    /// <summary>
    /// 运行基准测试
    /// </summary>
    private static async Task RunBenchmarkAsync(
        string server, string? scenario, int duration, int connections, int rate,
        string? output, string format, int warmup, string config, bool verbose, string logLevel)
    {
        Console.WriteLine("🚀 启动PulseRPC基准测试客户端...");
        Console.WriteLine($"🌐 目标服务器: {server}");
        Console.WriteLine($"🧪 测试场景: {scenario ?? "默认"}");
        Console.WriteLine($"⏱️  测试时长: {duration}秒 (预热: {warmup}秒)");
        Console.WriteLine($"🔗 并发连接: {connections}");
        Console.WriteLine($"📊 请求速率: {rate} QPS");
        Console.WriteLine($"📁 配置文件: {config}");

        // 构建测试引擎
        var serviceProvider = BuildServiceProvider(config, logLevel, verbose);
        var testEngine = serviceProvider.GetRequiredService<TestExecutionEngine>();

        // 创建测试配置
        var testConfig = new TestConfiguration
        {
            ServerAddress = server,
            ScenarioName = scenario ?? "ping-pong",
            DurationSeconds = duration,
            ConcurrentConnections = connections,
            RequestRate = rate,
            WarmupSeconds = warmup,
            OutputPath = output,
            ReportFormat = format,
            Verbose = verbose
        };

        // 执行测试
        var results = await testEngine.ExecuteTestAsync(testConfig);

        // 生成报告
        if (!string.IsNullOrEmpty(output))
        {
            await GenerateReportFromResultsAsync(results, output, format);
            Console.WriteLine($"📄 测试报告已保存到: {output}");
        }

        Console.WriteLine("✅ 测试完成");
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

        var loader = new ClientConfigurationLoader();
        var config = await loader.LoadFromFileAsync(configPath);

        config.Validate();

        Console.WriteLine($"✅ 服务器地址: {config.ServerAddress}");
        Console.WriteLine($"✅ 默认场景: {config.DefaultScenario}");
        Console.WriteLine($"✅ 连接超时: {config.ConnectionTimeoutMs}ms");
        Console.WriteLine($"✅ 请求超时: {config.RequestTimeoutMs}ms");
    }

    /// <summary>
    /// 生成报告
    /// </summary>
    private static async Task GenerateReportAsync(string input, string? output, string format, string template)
    {
        // 这里将调用报告生成器
        await Task.CompletedTask;
        throw new NotImplementedException("报告生成功能将在后续项目中实现");
    }

    /// <summary>
    /// 从测试结果生成报告
    /// </summary>
    private static async Task GenerateReportFromResultsAsync(object results, string output, string format)
    {
        // 这里将调用报告生成器
        await Task.CompletedTask;
        throw new NotImplementedException("报告生成功能将在后续项目中实现");
    }

    /// <summary>
    /// 构建服务提供者
    /// </summary>
    private static IServiceProvider BuildServiceProvider(string configPath, string logLevel, bool verbose)
    {
        var services = new ServiceCollection();

        // 配置日志
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConsole();
            if (Enum.TryParse<LogLevel>(logLevel, out var level))
            {
                builder.SetMinimumLevel(level);
            }
        });

        // 注册指标收集器
        services.AddSingleton<RealTimeMetricsCollector>();

        // 注册客户端服务
        services.AddSingleton<TestExecutionEngine>();
        services.AddSingleton<ClientConnectionManager>();
        services.AddSingleton<ClientConfigurationLoader>();

        // 注册基准测试服务（注释掉避免编译错误）
        services.AddBenchmarkServices();

        return services.BuildServiceProvider();
    }
}

/// <summary>
/// 测试配置
/// </summary>
public class TestConfiguration
{
    public string ServerAddress { get; set; } = string.Empty;
    public string ScenarioName { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public int ConcurrentConnections { get; set; }
    public int RequestRate { get; set; }
    public int WarmupSeconds { get; set; }
    public string? OutputPath { get; set; }
    public string ReportFormat { get; set; } = string.Empty;
    public bool Verbose { get; set; }
}
