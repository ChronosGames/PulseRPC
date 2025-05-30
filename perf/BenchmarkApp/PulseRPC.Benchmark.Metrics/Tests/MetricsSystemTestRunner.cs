using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Core;
using Microsoft.Extensions.DependencyInjection;

namespace PulseRPC.Benchmark.Metrics.Tests;

/// <summary>
/// 指标系统测试运行器
/// </summary>
public class MetricsSystemTestRunner
{
    private readonly ILogger<MetricsSystemTestRunner>? _logger;
    private readonly ILoggerFactory? _loggerFactory;

    public MetricsSystemTestRunner(ILogger<MetricsSystemTestRunner>? logger = null, ILoggerFactory? loggerFactory = null)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// 创建命令行接口
    /// </summary>
    public static RootCommand CreateCommandLine()
    {
        var rootCommand = new RootCommand("PulseRPC 指标系统测试运行器");

        // 集成测试命令
        var integrationTestCommand = new Command("integration", "运行集成测试");
        var outputOption = new Option<string?>("--output", "输出文件路径");
        var verboseOption = new Option<bool>("--verbose", "详细输出");

        integrationTestCommand.AddOption(outputOption);
        integrationTestCommand.AddOption(verboseOption);

        integrationTestCommand.SetHandler(async (output, verbose) =>
        {
            var logger = CreateLogger(verbose);
            var runner = new MetricsSystemTestRunner(logger);
            await runner.RunIntegrationTestsAsync(output, verbose);
        }, outputOption, verboseOption);

        // 性能基准测试命令
        var benchmarkCommand = new Command("benchmark", "运行性能基准测试");
        var benchmarkOutputOption = new Option<string?>("--output", "输出文件路径");
        var benchmarkVerboseOption = new Option<bool>("--verbose", "详细输出");
        var iterationsOption = new Option<int>("--iterations", () => 1, "运行次数");

        benchmarkCommand.AddOption(benchmarkOutputOption);
        benchmarkCommand.AddOption(benchmarkVerboseOption);
        benchmarkCommand.AddOption(iterationsOption);

        benchmarkCommand.SetHandler(async (output, verbose, iterations) =>
        {
            var logger = CreateLogger(verbose);
            var runner = new MetricsSystemTestRunner(logger);
            await runner.RunBenchmarkTestsAsync(output, verbose, iterations);
        }, benchmarkOutputOption, benchmarkVerboseOption, iterationsOption);

        // 配置验证命令
        var configTestCommand = new Command("config", "验证配置系统");
        var configFileOption = new Option<string?>("--file", "配置文件路径");

        configTestCommand.AddOption(configFileOption);

        configTestCommand.SetHandler(async (configFile) =>
        {
            var logger = CreateLogger(false);
            var runner = new MetricsSystemTestRunner(logger);
            await runner.RunConfigurationTestsAsync(configFile);
        }, configFileOption);

        // 完整测试套件命令
        var fullSuiteCommand = new Command("all", "运行所有测试");
        var suiteOutputOption = new Option<string?>("--output", "输出目录");
        var suiteVerboseOption = new Option<bool>("--verbose", "详细输出");

        fullSuiteCommand.AddOption(suiteOutputOption);
        fullSuiteCommand.AddOption(suiteVerboseOption);

        fullSuiteCommand.SetHandler(async (output, verbose) =>
        {
            var logger = CreateLogger(verbose);
            var runner = new MetricsSystemTestRunner(logger);
            await runner.RunFullTestSuiteAsync(output, verbose);
        }, suiteOutputOption, suiteVerboseOption);

        rootCommand.AddCommand(integrationTestCommand);
        rootCommand.AddCommand(benchmarkCommand);
        rootCommand.AddCommand(configTestCommand);
        rootCommand.AddCommand(fullSuiteCommand);

        return rootCommand;
    }

    /// <summary>
    /// 运行集成测试
    /// </summary>
    public async Task<int> RunIntegrationTestsAsync(string? outputFile = null, bool verbose = false)
    {
        try
        {
            _logger?.LogInformation("开始运行指标系统集成测试");

            var integrationTest = new MetricsIntegrationTest(_loggerFactory);
            var results = await integrationTest.RunCompleteIntegrationTestAsync();

            // 输出结果
            await OutputTestResultsAsync(results, outputFile, "integration_test_results.json");

            // 控制台输出摘要
            OutputTestSummaryToConsole(results, verbose);

            _logger?.LogInformation("集成测试完成");
            return results.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "运行集成测试时发生错误");
            Console.WriteLine($"错误: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 运行性能基准测试
    /// </summary>
    public async Task<int> RunBenchmarkTestsAsync(string? outputFile = null, bool verbose = false, int iterations = 1)
    {
        try
        {
            _logger?.LogInformation("开始运行性能基准测试，迭代次数: {Iterations}", iterations);

            var integrationTest = new MetricsIntegrationTest(_loggerFactory);
            var allResults = new List<BenchmarkResults>();

            for (int i = 0; i < iterations; i++)
            {
                _logger?.LogInformation("运行第 {Iteration}/{Total} 次基准测试", i + 1, iterations);
                var results = await integrationTest.RunPerformanceBenchmarkAsync();
                allResults.Add(results);

                if (verbose)
                {
                    OutputBenchmarkResultsToConsole(results, i + 1);
                }
            }

            // 计算平均结果
            var averageResults = CalculateAverageBenchmarkResults(allResults);

            // 输出结果
            await OutputBenchmarkResultsAsync(averageResults, outputFile, "benchmark_results.json");

            // 控制台输出摘要
            OutputBenchmarkSummaryToConsole(averageResults, iterations);

            _logger?.LogInformation("性能基准测试完成");
            return averageResults.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "运行性能基准测试时发生错误");
            Console.WriteLine($"错误: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 运行配置测试
    /// </summary>
    public async Task<int> RunConfigurationTestsAsync(string? configFile = null)
    {
        try
        {
            _logger?.LogInformation("开始运行配置验证测试");

            var success = true;

            // 测试默认配置
            Console.WriteLine("测试默认配置...");
            var defaultConfig = MetricsConfiguration.CreateDefault();
            var defaultValidation = defaultConfig.Validate();
            if (!defaultValidation.IsValid)
            {
                Console.WriteLine($"❌ 默认配置验证失败: {string.Join(", ", defaultValidation.Errors)}");
                success = false;
            }
            else
            {
                Console.WriteLine("✅ 默认配置验证通过");
            }

            // 测试开发配置
            Console.WriteLine("测试开发配置...");
            var devConfig = MetricsConfigurationBuilderExtensions.CreateDevelopmentConfiguration().Build();
            var devValidation = devConfig.Validate();
            if (!devValidation.IsValid)
            {
                Console.WriteLine($"❌ 开发配置验证失败: {string.Join(", ", devValidation.Errors)}");
                success = false;
            }
            else
            {
                Console.WriteLine("✅ 开发配置验证通过");
            }

            // 测试生产配置
            Console.WriteLine("测试生产配置...");
            var prodConfig = MetricsConfigurationBuilderExtensions.CreateProductionConfiguration().Build();
            var prodValidation = prodConfig.Validate();
            if (!prodValidation.IsValid)
            {
                Console.WriteLine($"❌ 生产配置验证失败: {string.Join(", ", prodValidation.Errors)}");
                success = false;
            }
            else
            {
                Console.WriteLine("✅ 生产配置验证通过");
            }

            // 测试自定义配置文件
            if (!string.IsNullOrEmpty(configFile))
            {
                Console.WriteLine($"测试自定义配置文件: {configFile}...");
                if (File.Exists(configFile))
                {
                    var customConfig = await MetricsConfiguration.LoadFromFileAsync(configFile);
                    var customValidation = customConfig.Validate();
                    if (!customValidation.IsValid)
                    {
                        Console.WriteLine($"❌ 自定义配置验证失败: {string.Join(", ", customValidation.Errors)}");
                        success = false;
                    }
                    else
                    {
                        Console.WriteLine("✅ 自定义配置验证通过");
                    }
                }
                else
                {
                    Console.WriteLine($"❌ 配置文件不存在: {configFile}");
                    success = false;
                }
            }

            _logger?.LogInformation("配置验证测试完成");
            return success ? 0 : 1;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "运行配置测试时发生错误");
            Console.WriteLine($"错误: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 运行完整测试套件
    /// </summary>
    public async Task<int> RunFullTestSuiteAsync(string? outputDir = null, bool verbose = false)
    {
        try
        {
            _logger?.LogInformation("开始运行完整测试套件");

            var stopwatch = Stopwatch.StartNew();
            var overallSuccess = true;

            // 创建输出目录
            var outputDirectory = outputDir ?? "test_results";
            Directory.CreateDirectory(outputDirectory);

            Console.WriteLine("🚀 开始运行 PulseRPC 指标系统完整测试套件");
            Console.WriteLine(new string('=', 60));

            // 1. 配置测试
            Console.WriteLine("1️⃣ 运行配置验证测试...");
            var configResult = await RunConfigurationTestsAsync();
            overallSuccess &= configResult == 0;

            Console.WriteLine();

            // 2. 集成测试
            Console.WriteLine("2️⃣ 运行集成测试...");
            var integrationOutputFile = Path.Combine(outputDirectory, "integration_results.json");
            var integrationResult = await RunIntegrationTestsAsync(integrationOutputFile, verbose);
            overallSuccess &= integrationResult == 0;

            Console.WriteLine();

            // 3. 性能基准测试
            Console.WriteLine("3️⃣ 运行性能基准测试...");
            var benchmarkOutputFile = Path.Combine(outputDirectory, "benchmark_results.json");
            var benchmarkResult = await RunBenchmarkTestsAsync(benchmarkOutputFile, verbose, 3);
            overallSuccess &= benchmarkResult == 0;

            stopwatch.Stop();

            Console.WriteLine();
            Console.WriteLine(new string('=', 60));
            Console.WriteLine($"🏁 测试套件完成 - 总用时: {stopwatch.Elapsed.TotalSeconds:F2}秒");
            Console.WriteLine($"📊 总体结果: {(overallSuccess ? "✅ 成功" : "❌ 失败")}");
            Console.WriteLine($"📁 结果输出到: {Path.GetFullPath(outputDirectory)}");

            // 生成汇总报告
            await GenerateSummaryReportAsync(outputDirectory);

            _logger?.LogInformation("完整测试套件完成，总体成功: {Success}", overallSuccess);
            return overallSuccess ? 0 : 1;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "运行完整测试套件时发生错误");
            Console.WriteLine($"错误: {ex.Message}");
            return 1;
        }
    }

    #region Private Methods

    private static ILogger<MetricsSystemTestRunner> CreateLogger(bool verbose)
    {
        var services = new ServiceCollection();

        if (verbose)
        {
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug));
        }
        else
        {
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information));
        }

        var loggerFactory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
        return loggerFactory.CreateLogger<MetricsSystemTestRunner>();
    }

    private async Task OutputTestResultsAsync(TestResults results, string? outputFile, string defaultFileName)
    {
        var fileName = outputFile ?? defaultFileName;
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(fileName, json);
    }

    private async Task OutputBenchmarkResultsAsync(BenchmarkResults results, string? outputFile, string defaultFileName)
    {
        var fileName = outputFile ?? defaultFileName;
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(fileName, json);
    }

    private void OutputTestSummaryToConsole(TestResults results, bool verbose)
    {
        Console.WriteLine();
        Console.WriteLine("📋 集成测试结果摘要");
        Console.WriteLine(new string('-', 40));
        Console.WriteLine($"测试名称: {results.TestName}");
        Console.WriteLine($"开始时间: {results.StartTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"结束时间: {results.EndTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"总用时: {results.Duration.TotalSeconds:F2}秒");
        Console.WriteLine($"总测试数: {results.TotalTests}");
        Console.WriteLine($"通过测试: {results.PassedTests.Count}");
        Console.WriteLine($"失败测试: {results.FailedTests.Count}");
        Console.WriteLine($"成功率: {results.SuccessRate:F1}%");
        Console.WriteLine($"整体结果: {(results.Success ? "✅ 成功" : "❌ 失败")}");

        if (verbose && results.PassedTests.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("✅ 通过的测试:");
            foreach (var test in results.PassedTests)
            {
                Console.WriteLine($"  • {test}");
            }
        }

        if (results.FailedTests.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("❌ 失败的测试:");
            foreach (var test in results.FailedTests)
            {
                Console.WriteLine($"  • {test}");
            }
        }
    }

    private void OutputBenchmarkResultsToConsole(BenchmarkResults results, int iteration = 0)
    {
        var title = iteration > 0 ? $"第 {iteration} 次基准测试结果" : "基准测试结果";

        Console.WriteLine();
        Console.WriteLine($"📊 {title}");
        Console.WriteLine(new string('-', 40));
        Console.WriteLine($"测试名称: {results.TestName}");
        Console.WriteLine($"开始时间: {results.StartTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"结束时间: {results.EndTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"结果: {(results.Success ? "✅ 成功" : "❌ 失败")}");

        if (!string.IsNullOrEmpty(results.ErrorMessage))
        {
            Console.WriteLine($"错误: {results.ErrorMessage}");
        }

        if (results.Benchmarks.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("性能指标:");
            Console.WriteLine($"{"数据量",-10} {"用时(ms)",-12} {"吞吐量(events/s)",-20}");
            Console.WriteLine(new string('-', 45));

            foreach (var benchmark in results.Benchmarks)
            {
                Console.WriteLine($"{benchmark.DataSize,-10} {benchmark.Duration.TotalMilliseconds,-12:F2} {benchmark.ThroughputPerSecond,-20:F2}");
            }
        }
    }

    private void OutputBenchmarkSummaryToConsole(BenchmarkResults results, int iterations)
    {
        Console.WriteLine();
        Console.WriteLine($"📊 平均基准测试结果 (基于 {iterations} 次运行)");
        Console.WriteLine(new string('-', 50));

        OutputBenchmarkResultsToConsole(results);
    }

    private BenchmarkResults CalculateAverageBenchmarkResults(List<BenchmarkResults> allResults)
    {
        if (allResults.Count == 0) return new BenchmarkResults();
        if (allResults.Count == 1) return allResults[0];

        var averageResults = new BenchmarkResults
        {
            TestName = allResults[0].TestName + $" (平均值, 基于 {allResults.Count} 次运行)",
            StartTime = allResults.Min(r => r.StartTime),
            EndTime = allResults.Max(r => r.EndTime),
            Success = allResults.All(r => r.Success)
        };

        // 计算平均基准测试结果
        var dataSizes = allResults[0].Benchmarks.Select(b => b.DataSize).ToList();

        foreach (var dataSize in dataSizes)
        {
            var benchmarksForSize = allResults
                .SelectMany(r => r.Benchmarks)
                .Where(b => b.DataSize == dataSize)
                .ToList();

            if (benchmarksForSize.Count > 0)
            {
                averageResults.Benchmarks.Add(new BenchmarkResult
                {
                    DataSize = dataSize,
                    Duration = TimeSpan.FromMilliseconds(benchmarksForSize.Average(b => b.Duration.TotalMilliseconds)),
                    ThroughputPerSecond = benchmarksForSize.Average(b => b.ThroughputPerSecond),
                    MemoryUsage = (long)benchmarksForSize.Average(b => b.MemoryUsage)
                });
            }
        }

        return averageResults;
    }

    private async Task GenerateSummaryReportAsync(string outputDirectory)
    {
        try
        {
            var summaryFile = Path.Combine(outputDirectory, "summary_report.md");
            var content = new List<string>
            {
                "# PulseRPC 指标系统测试报告",
                "",
                $"**生成时间**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                "",
                "## 测试概览",
                "",
                "本报告包含以下测试结果:",
                "",
                "- 🔧 配置验证测试",
                "- 🔗 集成测试",
                "- ⚡ 性能基准测试",
                "",
                "## 详细结果",
                "",
                "详细的测试结果请查看相应的JSON文件:",
                "",
                "- `integration_results.json` - 集成测试详细结果",
                "- `benchmark_results.json` - 性能基准测试详细结果",
                "",
                "## 系统信息",
                "",
                $"- **操作系统**: {Environment.OSVersion}",
                $"- **.NET版本**: {Environment.Version}",
                $"- **处理器数量**: {Environment.ProcessorCount}",
                $"- **工作集内存**: {Environment.WorkingSet / 1024 / 1024} MB",
                "",
                "---",
                "",
                $"*报告生成于 {DateTime.Now:yyyy-MM-dd HH:mm:ss} by PulseRPC 指标系统测试运行器*"
            };

            await File.WriteAllLinesAsync(summaryFile, content);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "生成汇总报告时发生错误");
        }
    }

    #endregion

    /// <summary>
    /// 程序入口点
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = CreateCommandLine();
        return await rootCommand.InvokeAsync(args);
    }
}
