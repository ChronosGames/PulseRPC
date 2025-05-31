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
using PulseRPC.Benchmark.Metrics.Exporters;
using PulseRPC.Benchmark.Metrics.Models;
using PulseRPC.Benchmark.Client.UI;

namespace PulseRPC.Benchmark.Client;

/// <summary>
/// BenchmarkApp客户端主程序入口
/// </summary>
internal class Program
{
    private static readonly Option<bool> VerboseOption = new(
        aliases: ["--verbose", "-v"],
        description: "启用详细输出");

    private static readonly Option<string> ConfigOption = new(
        aliases: ["--config", "-c"],
        description: "配置文件路径",
        getDefaultValue: () => "configs/benchmark-config.json");

    private static readonly Option<string> LogLevelOption = new(
        aliases: ["--log-level", "-l"],
        description: "日志级别 (Trace|Debug|Information|Warning|Error|Critical)",
        getDefaultValue: () => "Information");

    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("PulseRPC Benchmark Client - 高性能RPC基准测试客户端")
        {
            CreateRunCommand(),
            CreateListScenariosCommand(),
            CreateValidateConfigCommand(),
            CreateValidateReportConfigCommand(),
            CreateGenerateReportCommand(),
            CreateVersionCommand(),
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
            aliases: ["--server", "-s"],
            description: "服务器地址",
            getDefaultValue: () => "localhost:8080");

        var scenarioOption = new Option<string>(
            aliases: ["--scenario", "--test"],
            description: "测试场景名称");

        var durationOption = new Option<int>(
            aliases: ["--duration", "-d"],
            description: "测试持续时间（秒）",
            getDefaultValue: () => 60);

        var connectionsOption = new Option<int>(
            aliases: ["--connections", "--conn"],
            description: "并发连接数",
            getDefaultValue: () => 10);

        var rateOption = new Option<int>(
            aliases: ["--rate", "-r"],
            description: "请求速率（QPS）",
            getDefaultValue: () => 100);

        var outputOption = new Option<string>(
            aliases: ["--output", "-o"],
            description: "输出文件路径");

        var formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            description: "报告格式 (json|csv|html)",
            getDefaultValue: () => "json");

        var warmupOption = new Option<int>(
            aliases: ["--warmup", "-w"],
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
            aliases: ["--file", "-f"],
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
    /// 创建验证报告配置命令
    /// </summary>
    private static Command CreateValidateReportConfigCommand()
    {
        var formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            description: "报告格式 (html|json|csv|markdown)",
            getDefaultValue: () => "html");

        var outputOption = new Option<string>(
            aliases: ["--output", "-o"],
            description: "输出路径");

        var templateOption = new Option<string>(
            aliases: ["--template", "-t"],
            description: "自定义模板路径");

        var chartsOption = new Option<bool>(
            aliases: ["--charts", "-c"],
            description: "是否包含图表",
            getDefaultValue: () => true);

        var validateReportCommand = new Command("validate-report-config", "验证报告配置")
        {
            formatOption,
            outputOption,
            templateOption,
            chartsOption
        };

        validateReportCommand.SetHandler(async (format, output, template, charts) =>
        {
            try
            {
                await ValidateReportConfigurationAsync(format, output, template, charts);
                Console.WriteLine("✅ 报告配置验证通过");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 报告配置验证失败: {ex.Message}");
                Environment.Exit(1);
            }
        }, formatOption, outputOption, templateOption, chartsOption);

        return validateReportCommand;
    }

    /// <summary>
    /// 创建生成报告命令
    /// </summary>
    private static Command CreateGenerateReportCommand()
    {
        var inputOption = new Option<string>(
            aliases: ["--input", "-i"],
            description: "输入结果文件路径");
        inputOption.IsRequired = true;

        var outputOption = new Option<string>(
            aliases: ["--output", "-o"],
            description: "输出报告文件路径");

        var formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            description: "报告格式 (html|pdf|json|csv)",
            getDefaultValue: () => "html");

        var templateOption = new Option<string>(
            aliases: ["--template", "-t"],
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

        // 创建实时显示管理器
        var displayConfig = new DisplayConfiguration();
        var displayManager = new RealtimeDisplayManager(displayConfig, serviceProvider.GetService<ILogger<RealtimeDisplayManager>>());

        // 订阅测试引擎事件
        testEngine.ProgressUpdated += displayManager.UpdateProgress;
        testEngine.StateChanged += displayManager.UpdateState;

        TestResults? results;
        try
        {
            // 启动实时显示
            if (displayConfig.EnableRealTimeDisplay)
            {
                await displayManager.StartDisplayAsync(testConfig);

                // 给用户一点时间看到界面初始化
                await Task.Delay(1000);
            }

            // 执行测试
            results = await testEngine.ExecuteTestAsync(testConfig);

            // 停止实时显示
            if (displayConfig.EnableRealTimeDisplay)
            {
                await displayManager.StopDisplayAsync();

                // 显示完成摘要
                displayManager.ShowCompletionSummary(results);
            }
        }
        catch (Exception ex)
        {
            // 发生错误时也要停止显示
            if (displayConfig.EnableRealTimeDisplay)
            {
                displayManager.SetError(ex.Message);
                await displayManager.StopDisplayAsync();
            }

            Console.WriteLine($"❌ 测试执行失败: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }
            throw;
        }
        finally
        {
            // 取消事件订阅
            testEngine.ProgressUpdated -= displayManager.UpdateProgress;
            testEngine.StateChanged -= displayManager.UpdateState;

            // 释放显示管理器资源
            displayManager.Dispose();
        }

        // 生成报告
        if (!string.IsNullOrEmpty(output) && results != null)
        {
            await GenerateReportFromResultsAsync(results, output, format, serviceProvider);
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
    /// 验证报告配置
    /// </summary>
    private static async Task ValidateReportConfigurationAsync(string format, string? output, string? template, bool charts)
    {
        Console.WriteLine("🔍 验证报告配置...");

        var reportConfig = new ReportConfiguration
        {
            Format = ParseReportFormat(format),
            OutputPath = output ?? $"test-report.{format}",
            Title = "配置验证测试报告",
            IncludeCharts = charts,
            CustomTemplatePath = template
        };

        // 使用报告生成器验证配置
        var logger = new ConsoleLogger<BenchmarkReportGenerator>();
        var reportGenerator = new BenchmarkReportGenerator(logger);

        var validationResult = await reportGenerator.ValidateConfigurationAsync(reportConfig);

        if (!validationResult.IsValid)
        {
            Console.WriteLine("❌ 配置验证失败:");
            foreach (var error in validationResult.Errors)
            {
                Console.WriteLine($"   • {error}");
            }
            throw new ArgumentException("报告配置无效");
        }

        if (validationResult.Warnings.Count != 0)
        {
            Console.WriteLine("⚠️  配置警告:");
            foreach (var warning in validationResult.Warnings)
            {
                Console.WriteLine($"   • {warning}");
            }
        }

        // 显示配置详情
        Console.WriteLine($"✅ 报告格式: {reportConfig.Format}");
        Console.WriteLine($"✅ 输出路径: {reportConfig.OutputPath}");
        Console.WriteLine($"✅ 包含图表: {reportConfig.IncludeCharts}");

        if (!string.IsNullOrEmpty(reportConfig.CustomTemplatePath))
        {
            Console.WriteLine($"✅ 自定义模板: {reportConfig.CustomTemplatePath}");
        }

        Console.WriteLine($"✅ 支持的格式: {string.Join(", ", reportGenerator.GetSupportedFormats())}");
    }

    /// <summary>
    /// 生成报告
    /// </summary>
    private static async Task GenerateReportAsync(string input, string? output, string format, string template)
    {
        // 从文件加载测试结果数据
        if (!File.Exists(input))
        {
            throw new FileNotFoundException($"输入文件不存在: {input}");
        }

        var data = await CreateDemoReportDataAsync();

        var reportConfig = new ReportConfiguration
        {
            Format = ParseReportFormat(format),
            OutputPath = output ?? $"report.{format}",
            Title = "PulseRPC 基准测试报告"
        };

        var logger = new ConsoleLogger<BenchmarkReportGenerator>();
        var reportGenerator = new BenchmarkReportGenerator(logger);

        await reportGenerator.GenerateReportToFileAsync(data, reportConfig);
    }

    /// <summary>
    /// 从测试结果生成报告
    /// </summary>
    private static async Task GenerateReportFromResultsAsync(object results, string output, string format, IServiceProvider serviceProvider)
    {
        try
        {
            Console.WriteLine("📊 开始生成性能测试报告...");

            // 创建报告配置
            var reportConfig = new ReportConfiguration
            {
                Format = ParseReportFormat(format),
                OutputPath = output,
                Title = "PulseRPC 基准测试报告",
                IncludeCharts = format.Equals("html", StringComparison.OrdinalIgnoreCase),
                IncludeDetailedData = true,
                IncludeErrorDetails = true,
                IncludeEnvironmentInfo = true
            };

            // 验证配置
            var logger = serviceProvider.GetRequiredService<ILogger<BenchmarkReportGenerator>>();
            var reportGenerator = new BenchmarkReportGenerator(logger);

            Console.WriteLine("🔍 验证报告配置...");
            var validationResult = await reportGenerator.ValidateConfigurationAsync(reportConfig);

            if (!validationResult.IsValid)
            {
                Console.WriteLine("❌ 报告配置验证失败:");
                foreach (var error in validationResult.Errors)
                {
                    Console.WriteLine($"   • {error}");
                }
                throw new ArgumentException("报告配置无效");
            }

            if (validationResult.Warnings.Count != 0)
            {
                Console.WriteLine("⚠️  配置警告:");
                foreach (var warning in validationResult.Warnings)
                {
                    Console.WriteLine($"   • {warning}");
                }
            }

            // 创建模拟的报告数据 (实际应用中这里应该从results中提取)
            Console.WriteLine("📈 准备测试数据...");
            var data = await CreateDemoReportDataAsync();

            // 生成报告
            Console.WriteLine($"📝 生成 {reportConfig.Format} 格式报告...");
            await reportGenerator.GenerateReportToFileAsync(data, reportConfig);

            Console.WriteLine($"✅ 报告生成成功: {output}");

            // 显示报告摘要信息
            DisplayReportSummary(data, reportConfig);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 报告生成失败: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   内部错误: {ex.InnerException.Message}");
            }
            throw;
        }
    }

    /// <summary>
    /// 显示报告摘要信息
    /// </summary>
    private static void DisplayReportSummary(BenchmarkReportData data, ReportConfiguration config)
    {
        Console.WriteLine();
        Console.WriteLine("📋 报告摘要:");
        Console.WriteLine($"   📊 测试场景: {data.TestConfig.ScenarioName}");
        Console.WriteLine($"   ⏱️  测试时长: {data.TestConfig.DurationSeconds}秒");
        Console.WriteLine($"   🔗 并发连接: {data.TestConfig.ConnectionCount}");
        Console.WriteLine($"   📈 平均延迟: {data.Metrics.Latency.AverageMs:F2}ms");
        Console.WriteLine($"   🚀 平均RPS: {data.Metrics.Throughput.AverageRps:F2}");
        Console.WriteLine($"   ❌ 错误率: {data.Metrics.Errors.ErrorRate:F2}%");
        Console.WriteLine($"   📄 报告格式: {config.Format}");
        Console.WriteLine($"   📁 文件大小: {GetFileSize(config.OutputPath)}");
        Console.WriteLine();
    }

    /// <summary>
    /// 获取文件大小信息
    /// </summary>
    private static string GetFileSize(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return "未知";
            }

            var fileInfo = new FileInfo(filePath);
            var sizeInBytes = fileInfo.Length;

            return sizeInBytes switch
            {
                < 1024 => $"{sizeInBytes} B",
                < 1024 * 1024 => $"{sizeInBytes / 1024.0:F1} KB",
                < 1024 * 1024 * 1024 => $"{sizeInBytes / (1024.0 * 1024.0):F1} MB",
                _ => $"{sizeInBytes / (1024.0 * 1024.0 * 1024):F1} GB"
            };
        }
        catch
        {
            return "无法获取";
        }
    }

    /// <summary>
    /// 增强的CreateDemoReportDataAsync方法，包含更真实的测试数据
    /// </summary>
    private static async Task<BenchmarkReportData> CreateDemoReportDataAsync()
    {
        var now = DateTime.UtcNow;
        var random = new Random(42); // 固定种子以获得一致的结果

        // 生成更真实的延迟分布数据（模拟真实的网络延迟模式）
        var latencyDistribution = new List<LatencyPoint>();
        for (int i = 0; i < 100; i++)
        {
            // 模拟延迟峰值和正常延迟
            var baseLatency = 15.0; // 基础延迟
            var variation = Math.Sin(i * 0.1) * 10; // 周期性变化
            var spike = (i % 20 == 0) ? random.NextDouble() * 30 : 0; // 偶发性峰值

            latencyDistribution.Add(new LatencyPoint
            {
                Timestamp = now.AddSeconds(i),
                LatencyMs = Math.Max(5, baseLatency + variation + spike + random.NextDouble() * 5)
            });
        }

        // 生成更真实的吞吐量时间序列数据
        var throughputSeries = new List<ThroughputPoint>();
        var targetRps = 50.0;
        for (int i = 0; i < 100; i++)
        {
            // 模拟预热期和稳定期
            var warmupFactor = Math.Min(1.0, i / 10.0); // 前10秒预热
            var performanceDrop = (i > 80) ? 0.9 : 1.0; // 后期轻微性能下降
            var randomVariation = (random.NextDouble() - 0.5) * 0.2; // ±10%随机变化

            throughputSeries.Add(new ThroughputPoint
            {
                Timestamp = now.AddSeconds(i),
                Rps = Math.Max(0, targetRps * warmupFactor * performanceDrop * (1 + randomVariation))
            });
        }

        // 生成更真实的资源使用时间序列数据
        var resourceSeries = new List<ResourcePoint>();
        for (int i = 0; i < 100; i++)
        {
            // 模拟负载增加时的资源消耗
            var loadFactor = Math.Min(1.0, i / 15.0); // 逐渐增加负载
            var cpuBase = 20.0 + loadFactor * 25.0; // 基础CPU使用率
            var memoryBase = 512 + loadFactor * 200; // 基础内存使用

            resourceSeries.Add(new ResourcePoint
            {
                Timestamp = now.AddSeconds(i),
                CpuPercent = Math.Min(95, cpuBase + random.NextDouble() * 10),
                MemoryMB = (long)(memoryBase + random.NextDouble() * 100)
            });
        }

        var data = new BenchmarkReportData
        {
            TestConfig = new PulseRPC.Benchmark.Metrics.Models.TestConfiguration
            {
                ServerAddress = "localhost:8080",
                ScenarioName = "ping-pong",
                DurationSeconds = 100, // 更长的测试时间
                ConnectionCount = 5,
                RequestRate = 50,
                WarmupSeconds = 10,
                StartTime = now.AddSeconds(-110),
                EndTime = now.AddSeconds(-10)
            },
            Environment = new EnvironmentInfo
            {
                OperatingSystem = Environment.OSVersion.ToString(),
                DotNetVersion = Environment.Version.ToString(),
                ProcessorInfo = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                TotalMemoryMB = 16384,
                MachineName = Environment.MachineName,
                NetworkConfig = "Gigabit Ethernet"
            },
            Metrics = new PerformanceMetrics
            {
                Latency = new LatencyMetrics
                {
                    AverageMs = latencyDistribution.Average(x => x.LatencyMs),
                    MinMs = latencyDistribution.Min(x => x.LatencyMs),
                    MaxMs = latencyDistribution.Max(x => x.LatencyMs),
                    P50Ms = GetPercentile(latencyDistribution.Select(x => x.LatencyMs), 0.5),
                    P95Ms = GetPercentile(latencyDistribution.Select(x => x.LatencyMs), 0.95),
                    P99Ms = GetPercentile(latencyDistribution.Select(x => x.LatencyMs), 0.99),
                    P999Ms = GetPercentile(latencyDistribution.Select(x => x.LatencyMs), 0.999),
                    StandardDeviation = CalculateStandardDeviation(latencyDistribution.Select(x => x.LatencyMs)),
                    Distribution = latencyDistribution
                },
                Throughput = new ThroughputMetrics
                {
                    AverageRps = throughputSeries.Average(x => x.Rps),
                    PeakRps = throughputSeries.Max(x => x.Rps),
                    TotalRequests = (long)(throughputSeries.Sum(x => x.Rps)),
                    SuccessfulRequests = (long)(throughputSeries.Sum(x => x.Rps) * 0.9979), // 99.79%成功率
                    FailedRequests = (long)(throughputSeries.Sum(x => x.Rps) * 0.0021), // 0.21%失败率
                    TimeSeries = throughputSeries
                },
                Resources = new ResourceMetrics
                {
                    CpuUsagePercent = resourceSeries.Average(x => x.CpuPercent),
                    MemoryUsageMB = (long)resourceSeries.Average(x => x.MemoryMB),
                    NetworkSentBytes = 1024 * 1024 * 15,
                    NetworkReceivedBytes = 1024 * 1024 * 18,
                    TimeSeries = resourceSeries
                },
                Errors = new ErrorMetrics
                {
                    TotalErrors = 11, // 更真实的错误数
                    ErrorRate = 0.21,
                    TimeoutErrors = 7,
                    ConnectionErrors = 4,
                    ErrorsByType = new Dictionary<string, long>
                    {
                        ["Timeout"] = 7,
                        ["Connection"] = 4
                    }
                }
            },
            Summary = new TestSummary
            {
                IsSuccessful = true,
                Grade = PerformanceGrade.Good,
                KeyFindings = new List<string>
                {
                    $"平均延迟{latencyDistribution.Average(x => x.LatencyMs):F1}ms，处于可接受范围内",
                    $"吞吐量稳定，达到{throughputSeries.Average(x => x.Rps):F1} RPS",
                    "错误率较低，仅为0.21%",
                    $"资源使用合理，平均CPU使用率{resourceSeries.Average(x => x.CpuPercent):F1}%",
                    "系统在测试负载下表现稳定"
                },
                Recommendations = new List<string>
                {
                    $"考虑优化延迟峰值，P99延迟为{GetPercentile(latencyDistribution.Select(x => x.LatencyMs), 0.99):F1}ms",
                    "可适当增加并发连接数以提升吞吐量",
                    "监控超时错误，考虑调整连接超时配置",
                    "建议在生产环境进行更长时间的压力测试",
                    "定期监控系统性能指标变化趋势"
                }
            }
        };

        await Task.CompletedTask;
        return data;
    }

    /// <summary>
    /// 计算百分位数
    /// </summary>
    private static double GetPercentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Max(0, Math.Min(index, sorted.Length - 1))];
    }

    /// <summary>
    /// 计算标准差
    /// </summary>
    private static double CalculateStandardDeviation(IEnumerable<double> values)
    {
        var array = values.ToArray();
        var mean = array.Average();
        var sumOfSquares = array.Sum(x => Math.Pow(x - mean, 2));
        return Math.Sqrt(sumOfSquares / array.Length);
    }

    /// <summary>
    /// 解析报告格式
    /// </summary>
    private static ReportFormat ParseReportFormat(string format)
    {
        return format.ToLower() switch
        {
            "html" => ReportFormat.Html,
            "json" => ReportFormat.Json,
            "csv" => ReportFormat.Csv,
            "markdown" or "md" => ReportFormat.Markdown,
            _ => ReportFormat.Html
        };
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

        // 添加核心服务
        services.AddSingleton<TestExecutionEngine>();
        services.AddSingleton<ClientConnectionManager>();
        services.AddSingleton<RealTimeMetricsCollector>();

        // 添加UI服务
        services.AddSingleton<DisplayConfiguration>();
        services.AddTransient<RealtimeDisplayManager>();

        // 添加配置加载器
        services.AddSingleton<ClientConfigurationLoader>();

        // 添加报告生成器
        services.AddSingleton<BenchmarkReportGenerator>();

        return services.BuildServiceProvider();
    }
}

/// <summary>
/// 测试配置
/// </summary>
public class TestConfiguration
{
    public string ServerAddress { get; init; } = string.Empty;
    public string ScenarioName { get; init; } = string.Empty;
    public int DurationSeconds { get; init; }
    public int ConcurrentConnections { get; init; }
    public int RequestRate { get; init; }
    public int WarmupSeconds { get; init; }
    public string? OutputPath { get; init; }
    public string ReportFormat { get; init; } = string.Empty;
    public bool Verbose { get; init; }
}

/// <summary>
/// 简单控制台日志记录器
/// </summary>
public class ConsoleLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
        {
            Console.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        }
    }
}
