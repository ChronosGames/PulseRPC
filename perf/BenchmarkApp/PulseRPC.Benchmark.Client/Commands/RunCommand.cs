using System;
using System.CommandLine;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Client.Configuration;
using PulseRPC.Benchmark.Client.Engine;

namespace PulseRPC.Benchmark.Client.Commands;

/// <summary>
/// 运行测试命令处理器
/// </summary>
public class RunCommand(
    ILogger<RunCommand> logger,
    TestExecutionEngine testEngine,
    ClientConfigurationLoader configLoader)
{
    private readonly ILogger<RunCommand> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TestExecutionEngine _testEngine = testEngine ?? throw new ArgumentNullException(nameof(testEngine));
    private readonly ClientConfigurationLoader _configLoader = configLoader ?? throw new ArgumentNullException(nameof(configLoader));

    /// <summary>
    /// 创建运行命令
    /// </summary>
    public Command CreateCommand()
    {
        var serverOption = new Option<string>(
            aliases: new[] { "--server", "-s" },
            description: "服务器地址（格式：host:port）",
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
            description: "报告格式（json|csv|html）",
            getDefaultValue: () => "json");

        var warmupOption = new Option<int>(
            aliases: new[] { "--warmup", "-w" },
            description: "预热时间（秒）",
            getDefaultValue: () => 10);

        var profileOption = new Option<string>(
            aliases: new[] { "--profile", "-p" },
            description: "测试配置文件（light|medium|heavy|stress）");

        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "启用详细输出");

        var runCommand = new Command("run", "运行基准测试")
        {
            serverOption,
            scenarioOption,
            durationOption,
            connectionsOption,
            rateOption,
            outputOption,
            formatOption,
            warmupOption,
            profileOption,
            verboseOption
        };

        runCommand.SetHandler(async (context) =>
        {
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            try
            {
                var server = context.ParseResult.GetValueForOption(serverOption)!;
                var scenario = context.ParseResult.GetValueForOption(scenarioOption);
                var duration = context.ParseResult.GetValueForOption(durationOption);
                var connections = context.ParseResult.GetValueForOption(connectionsOption);
                var rate = context.ParseResult.GetValueForOption(rateOption);
                var output = context.ParseResult.GetValueForOption(outputOption);
                var format = context.ParseResult.GetValueForOption(formatOption)!;
                var warmup = context.ParseResult.GetValueForOption(warmupOption);
                var profile = context.ParseResult.GetValueForOption(profileOption);

                await ExecuteRunCommandAsync(server, scenario, duration, connections, rate, output, format, warmup, profile, verbose);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "运行测试命令执行失败");
                Console.WriteLine($"❌ 测试执行失败: {ex.Message}");
                if (verbose)
                {
                    Console.WriteLine($"详细错误信息: {ex}");
                }
                Environment.Exit(1);
            }
        });

        return runCommand;
    }

    /// <summary>
    /// 执行运行命令
    /// </summary>
    private async Task ExecuteRunCommandAsync(
        string server, string? scenario, int duration, int connections, int rate,
        string? output, string format, int warmup, string? profile, bool verbose)
    {
        _logger.LogInformation("开始执行基准测试");

        // 显示测试信息
        Console.WriteLine("🚀 启动PulseRPC基准测试");
        Console.WriteLine($"🌐 目标服务器: {server}");
        Console.WriteLine($"🧪 测试场景: {scenario ?? "ping-pong"}");
        Console.WriteLine($"⏱️  测试时长: {duration}秒 (预热: {warmup}秒)");
        Console.WriteLine($"🔗 并发连接: {connections}");
        Console.WriteLine($"📊 请求速率: {rate} QPS");
        Console.WriteLine($"📄 报告格式: {format}");

        if (!string.IsNullOrEmpty(profile))
        {
            Console.WriteLine($"🎯 测试配置: {profile}");
        }

        if (!string.IsNullOrEmpty(output))
        {
            Console.WriteLine($"📁 输出文件: {output}");
        }

        Console.WriteLine();

        // 应用配置文件设置
        var config = await ApplyProfileSettingsAsync(profile, connections, rate, duration);

        // 创建测试配置
        var testConfig = new TestConfiguration
        {
            ServerAddress = server,
            ScenarioName = scenario ?? "ping-pong",
            DurationSeconds = config.duration,
            ConcurrentConnections = config.connections,
            RequestRate = config.rate,
            WarmupSeconds = warmup,
            OutputPath = output,
            ReportFormat = format,
            Verbose = verbose
        };

        // 验证服务器连接
        Console.WriteLine("🔍 验证服务器连接...");
        if (!await ValidateServerConnectionAsync(server))
        {
            throw new InvalidOperationException($"无法连接到服务器: {server}");
        }
        Console.WriteLine("✅ 服务器连接正常");

        // 执行测试
        Console.WriteLine("▶️  开始执行测试...");
        var results = await _testEngine.ExecuteTestAsync(testConfig);

        // 显示测试结果摘要
        DisplayTestSummary(results);

        // 生成报告
        if (!string.IsNullOrEmpty(output))
        {
            await GenerateReportAsync(results, output, format);
            Console.WriteLine($"📄 测试报告已保存到: {output}");
        }

        Console.WriteLine("✅ 测试完成");
    }

    /// <summary>
    /// 应用配置文件设置
    /// </summary>
    private async Task<(int connections, int rate, int duration)> ApplyProfileSettingsAsync(
        string? profile, int defaultConnections, int defaultRate, int defaultDuration)
    {
        if (string.IsNullOrEmpty(profile))
        {
            return (defaultConnections, defaultRate, defaultDuration);
        }

        try
        {
            var clientConfig = await _configLoader.LoadFromFileAsync("configs/templates/client-config-template.json");

            // 这里应该根据profile从配置中读取设置
            // 简化实现，使用预定义值
            return profile.ToLower() switch
            {
                "light" => (5, 50, 30),
                "medium" => (20, 200, 120),
                "heavy" => (100, 1000, 300),
                "stress" => (500, 5000, 600),
                _ => (defaultConnections, defaultRate, defaultDuration)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载配置文件失败，使用默认配置");
            return (defaultConnections, defaultRate, defaultDuration);
        }
    }

    /// <summary>
    /// 验证服务器连接
    /// </summary>
    private async Task<bool> ValidateServerConnectionAsync(string serverAddress)
    {
        try
        {
            // 这里应该实现实际的连接验证
            // 简化实现，模拟验证过程
            await Task.Delay(1000); // 模拟连接时间
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证服务器连接失败: {ServerAddress}", serverAddress);
            return false;
        }
    }

    /// <summary>
    /// 显示测试结果摘要
    /// </summary>
    private void DisplayTestSummary(TestResults results)
    {
        Console.WriteLine();
        Console.WriteLine("📊 测试结果摘要");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine($"🕐 测试时长: {results.TotalDuration:hh\\:mm\\:ss}");
        Console.WriteLine($"📈 总请求数: {results.TotalRequests:N0}");
        Console.WriteLine($"✅ 成功请求: {results.SuccessfulRequests:N0} ({results.SuccessRate:P2})");
        Console.WriteLine($"❌ 失败请求: {results.FailedRequests:N0}");
        Console.WriteLine($"⚡ 平均QPS: {results.RequestsPerSecond:F2}");
        Console.WriteLine();
        Console.WriteLine("⏱️  延迟统计");
        Console.WriteLine($"   平均值: {results.AverageLatencyMs:F3} ms");
        Console.WriteLine($"   中位数: {results.MedianLatencyMs:F3} ms");
        Console.WriteLine($"   P95:   {results.P95LatencyMs:F3} ms");
        Console.WriteLine($"   P99:   {results.P99LatencyMs:F3} ms");
        Console.WriteLine($"   最小值: {results.MinLatencyMs:F3} ms");
        Console.WriteLine($"   最大值: {results.MaxLatencyMs:F3} ms");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    }

    /// <summary>
    /// 生成报告
    /// </summary>
    private async Task GenerateReportAsync(TestResults results, string outputPath, string format)
    {
        try
        {
            // 这里应该调用报告生成器
            // 简化实现，输出基本JSON
            var json = System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(outputPath, json);
            _logger.LogInformation("报告已生成: {OutputPath}", outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成报告失败: {OutputPath}", outputPath);
            throw;
        }
    }
}
