using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Abstractions;
using PulseRPC.Benchmark.Metrics.Aggregators;
using PulseRPC.Benchmark.Metrics.Analyzers;
using PulseRPC.Benchmark.Metrics.Collectors;
using PulseRPC.Benchmark.Metrics.Core;
using PulseRPC.Benchmark.Metrics.Exporters;
using PulseRPC.Benchmark.Metrics.Models;
using PulseRPC.Benchmark.Metrics.Serialization;

namespace PulseRPC.Benchmark.Metrics.Tests;

/// <summary>
/// 指标系统集成测试
/// </summary>
public class MetricsIntegrationTest
{
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<MetricsIntegrationTest>? _logger;
    private readonly List<string> _testResults;

    public MetricsIntegrationTest(ILoggerFactory? loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<MetricsIntegrationTest>();
        _testResults = new List<string>();
    }

    /// <summary>
    /// 运行完整的集成测试
    /// </summary>
    public async Task<TestResults> RunCompleteIntegrationTestAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new TestResults
        {
            TestName = "完整指标系统集成测试",
            StartTime = DateTime.UtcNow
        };

        try
        {
            _logger?.LogInformation("开始运行完整指标系统集成测试");

            // 测试1：配置系统测试
            await TestConfigurationSystem(results);

            // 测试2：组件集成测试
            await TestComponentIntegration(results);

            // 测试3：数据流测试
            await TestDataFlow(results);

            // 测试4：性能测试
            await TestPerformance(results);

            // 测试5：异常处理测试
            await TestErrorHandling(results);

            // 测试6：并发测试
            await TestConcurrency(results);

            stopwatch.Stop();
            results.Duration = stopwatch.Elapsed;
            results.EndTime = DateTime.UtcNow;
            results.Success = results.FailedTests.Count == 0;

            _logger?.LogInformation("集成测试完成，总用时: {Duration}ms, 成功: {Success}",
                results.Duration.TotalMilliseconds, results.Success);

            return results;
        }
        catch (Exception ex)
        {
            results.Success = false;
            results.FailedTests.Add($"集成测试异常: {ex.Message}");
            _logger?.LogError(ex, "集成测试发生异常");
            return results;
        }
    }

    /// <summary>
    /// 测试配置系统
    /// </summary>
    private async Task TestConfigurationSystem(TestResults results)
    {
        try
        {
            _logger?.LogInformation("测试配置系统");

            // 测试默认配置创建
            var defaultConfig = MetricsConfiguration.CreateDefault();
            var validation = defaultConfig.Validate();
            if (!validation.IsValid)
            {
                results.FailedTests.Add($"默认配置验证失败: {string.Join(", ", validation.Errors)}");
                return;
            }

            // 测试配置构建器
            var builder = MetricsConfigurationBuilderExtensions.CreateDevelopmentConfiguration();
            var builtConfig = builder.Build();
            var builtValidation = builtConfig.Validate();
            if (!builtValidation.IsValid)
            {
                results.FailedTests.Add($"构建配置验证失败: {string.Join(", ", builtValidation.Errors)}");
                return;
            }

            // 测试配置文件保存和加载
            var tempFile = Path.GetTempFileName();
            try
            {
                await MetricsConfiguration.SaveToFileAsync(builtConfig, tempFile);
                var loadedConfig = await MetricsConfiguration.LoadFromFileAsync(tempFile);
                var loadedValidation = loadedConfig.Validate();
                if (!loadedValidation.IsValid)
                {
                    results.FailedTests.Add($"加载配置验证失败: {string.Join(", ", loadedValidation.Errors)}");
                    return;
                }
            }
            finally
            {
                File.Delete(tempFile);
            }

            results.PassedTests.Add("配置系统测试");
            _logger?.LogInformation("配置系统测试通过");
        }
        catch (Exception ex)
        {
            results.FailedTests.Add($"配置系统测试失败: {ex.Message}");
            _logger?.LogError(ex, "配置系统测试失败");
        }
    }

    /// <summary>
    /// 测试组件集成
    /// </summary>
    private async Task TestComponentIntegration(TestResults results)
    {
        try
        {
            _logger?.LogInformation("测试组件集成");

            var configuration = MetricsConfigurationBuilderExtensions.CreateDevelopmentConfiguration().Build();
            using var integrator = new MetricsSystemIntegrator(configuration, _loggerFactory?.CreateLogger<MetricsSystemIntegrator>());

            // 注册组件
            var collector = new RealTimeMetricsCollector();
            var aggregator = new TimeWindowAggregator();
            var analyzer = new TrendAnalyzer();
            var jsonProvider = new SystemTextJsonProvider();
            var jsonExporter = new SystemTextJsonMetricsExporter(jsonProvider);

            var collectorRegistered = await integrator.RegisterCollectorAsync("RealTime", collector);
            var aggregatorRegistered = await integrator.RegisterAggregatorAsync("TimeWindow", aggregator);
            var analyzerRegistered = await integrator.RegisterAnalyzerAsync("Trend", analyzer);
            var exporterRegistered = await integrator.RegisterExporterAsync("Json", jsonExporter);

            if (!collectorRegistered || !aggregatorRegistered || !analyzerRegistered || !exporterRegistered)
            {
                results.FailedTests.Add("组件注册失败");
                return;
            }

            // 启动系统
            await integrator.StartAsync();

            if (!integrator.IsRunning)
            {
                results.FailedTests.Add("系统启动失败");
                return;
            }

            // 检查健康状态
            var health = await integrator.GetSystemHealthAsync();
            if (!health.IsHealthy)
            {
                results.FailedTests.Add($"系统健康检查失败: {string.Join(", ", health.Issues)}");
                return;
            }

            // 停止系统
            await integrator.StopAsync();

            if (integrator.IsRunning)
            {
                results.FailedTests.Add("系统停止失败");
                return;
            }

            results.PassedTests.Add("组件集成测试");
            _logger?.LogInformation("组件集成测试通过");
        }
        catch (Exception ex)
        {
            results.FailedTests.Add($"组件集成测试失败: {ex.Message}");
            _logger?.LogError(ex, "组件集成测试失败");
        }
    }

    /// <summary>
    /// 测试数据流
    /// </summary>
    private async Task TestDataFlow(TestResults results)
    {
        try
        {
            _logger?.LogInformation("测试数据流");

            var configuration = MetricsConfigurationBuilderExtensions.CreateDevelopmentConfiguration().Build();
            using var integrator = new MetricsSystemIntegrator(configuration, _loggerFactory?.CreateLogger<MetricsSystemIntegrator>());

            // 注册组件
            await integrator.RegisterCollectorAsync("RealTime", new RealTimeMetricsCollector());
            await integrator.RegisterAggregatorAsync("TimeWindow", new TimeWindowAggregator());
            await integrator.RegisterAnalyzerAsync("Trend", new TrendAnalyzer());
            var jsonProvider3 = new SystemTextJsonProvider();
            await integrator.RegisterExporterAsync("Json", new SystemTextJsonMetricsExporter(jsonProvider3));

            await integrator.StartAsync();

            // 生成测试数据
            var testMetrics = GenerateTestMetrics(100);

            // 测试聚合
            var aggregationResults = await integrator.PerformAggregationAsync(testMetrics);
            if (aggregationResults.Count == 0)
            {
                results.FailedTests.Add("聚合结果为空");
                return;
            }

            // 测试分析
            var analysisResults = await integrator.PerformAnalysisAsync(testMetrics);
            if (analysisResults.Count == 0)
            {
                results.FailedTests.Add("分析结果为空");
                return;
            }

            // 测试导出
            await integrator.PerformExportAsync(testMetrics);

            await integrator.StopAsync();

            results.PassedTests.Add("数据流测试");
            _logger?.LogInformation("数据流测试通过");
        }
        catch (Exception ex)
        {
            results.FailedTests.Add($"数据流测试失败: {ex.Message}");
            _logger?.LogError(ex, "数据流测试失败");
        }
    }

    /// <summary>
    /// 测试性能
    /// </summary>
    private async Task TestPerformance(TestResults results)
    {
        try
        {
            _logger?.LogInformation("测试性能");

            var configuration = MetricsConfigurationBuilderExtensions.CreateProductionConfiguration().Build();
            using var integrator = new MetricsSystemIntegrator(configuration, _loggerFactory?.CreateLogger<MetricsSystemIntegrator>());

            // 注册组件
            await integrator.RegisterAggregatorAsync("TimeWindow", new TimeWindowAggregator());
            await integrator.RegisterAnalyzerAsync("Trend", new TrendAnalyzer());

            await integrator.StartAsync();

            // 大量数据性能测试
            var stopwatch = Stopwatch.StartNew();
            var largeDataSet = GenerateTestMetrics(10000);

            var aggregationResults = await integrator.PerformAggregationAsync(largeDataSet);
            var analysisResults = await integrator.PerformAnalysisAsync(largeDataSet);

            stopwatch.Stop();

            // 性能检查
            if (stopwatch.ElapsedMilliseconds > 10000) // 10秒阈值
            {
                results.FailedTests.Add($"性能测试超时: {stopwatch.ElapsedMilliseconds}ms");
                return;
            }

            if (aggregationResults.Count == 0 || analysisResults.Count == 0)
            {
                results.FailedTests.Add("大数据量处理结果为空");
                return;
            }

            await integrator.StopAsync();

            results.PassedTests.Add($"性能测试 (处理10000条数据用时: {stopwatch.ElapsedMilliseconds}ms)");
            _logger?.LogInformation("性能测试通过，用时: {ElapsedMilliseconds}ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            results.FailedTests.Add($"性能测试失败: {ex.Message}");
            _logger?.LogError(ex, "性能测试失败");
        }
    }

    /// <summary>
    /// 测试异常处理
    /// </summary>
    private async Task TestErrorHandling(TestResults results)
    {
        try
        {
            _logger?.LogInformation("测试异常处理");

            var configuration = MetricsConfigurationBuilderExtensions.CreateDevelopmentConfiguration().Build();
            using var integrator = new MetricsSystemIntegrator(configuration, _loggerFactory?.CreateLogger<MetricsSystemIntegrator>());

            // 测试无效配置
            var invalidAggregator = new TimeWindowAggregator();
            var invalidConfig = new AggregatorConfiguration
            {
                DefaultWindowConfig = new TimeWindowConfiguration
                {
                    WindowSize = TimeSpan.FromSeconds(-1) // 无效配置
                }
            };

            var isValid = await invalidAggregator.ValidateConfigurationAsync(invalidConfig);
            if (isValid)
            {
                results.FailedTests.Add("无效配置验证应该失败但返回成功");
                return;
            }

            // 测试空数据处理
            await integrator.RegisterAggregatorAsync("TimeWindow", new TimeWindowAggregator());
            await integrator.StartAsync();

            var emptyResults = await integrator.PerformAggregationAsync(new List<JsonOptimizedMetricsEvent>());
            if (emptyResults == null)
            {
                results.FailedTests.Add("空数据处理返回null");
                return;
            }

            await integrator.StopAsync();

            results.PassedTests.Add("异常处理测试");
            _logger?.LogInformation("异常处理测试通过");
        }
        catch (Exception ex)
        {
            results.FailedTests.Add($"异常处理测试失败: {ex.Message}");
            _logger?.LogError(ex, "异常处理测试失败");
        }
    }

    /// <summary>
    /// 测试并发处理
    /// </summary>
    private async Task TestConcurrency(TestResults results)
    {
        try
        {
            _logger?.LogInformation("测试并发处理");

            var configuration = MetricsConfigurationBuilderExtensions.CreateProductionConfiguration().Build();
            using var integrator = new MetricsSystemIntegrator(configuration, _loggerFactory?.CreateLogger<MetricsSystemIntegrator>());

            await integrator.RegisterAggregatorAsync("TimeWindow", new TimeWindowAggregator());
            await integrator.RegisterAnalyzerAsync("Trend", new TrendAnalyzer());

            await integrator.StartAsync();

            // 并发测试
            var concurrentTasks = new List<Task>();
            var testMetrics = GenerateTestMetrics(1000);

            for (int i = 0; i < 10; i++)
            {
                concurrentTasks.Add(Task.Run(async () =>
                {
                    await integrator.PerformAggregationAsync(testMetrics);
                    await integrator.PerformAnalysisAsync(testMetrics);
                }));
            }

            var stopwatch = Stopwatch.StartNew();
            await Task.WhenAll(concurrentTasks);
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > 15000) // 15秒阈值
            {
                results.FailedTests.Add($"并发测试超时: {stopwatch.ElapsedMilliseconds}ms");
                return;
            }

            await integrator.StopAsync();

            results.PassedTests.Add($"并发处理测试 (10个并发任务用时: {stopwatch.ElapsedMilliseconds}ms)");
            _logger?.LogInformation("并发处理测试通过，用时: {ElapsedMilliseconds}ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            results.FailedTests.Add($"并发处理测试失败: {ex.Message}");
            _logger?.LogError(ex, "并发处理测试失败");
        }
    }

    /// <summary>
    /// 生成测试指标数据
    /// </summary>
    private List<JsonOptimizedMetricsEvent> GenerateTestMetrics(int count)
    {
        var random = new Random();
        var metrics = new List<JsonOptimizedMetricsEvent>();
        var baseTime = DateTime.UtcNow.AddMinutes(-count);

        for (int i = 0; i < count; i++)
        {
            metrics.Add(new JsonOptimizedMetricsEvent
            {
                MetricName = $"test_metric_{i % 10}",
                Timestamp = baseTime.AddSeconds(i),
                Value = JsonSerializer.SerializeToElement(random.NextDouble() * 100),
                Unit = "ms",
                Tags = new Dictionary<string, string>
                {
                    ["test"] = "true",
                    ["batch"] = (i / 100).ToString()
                }
            });
        }

        return metrics;
    }

    /// <summary>
    /// 运行性能基准测试
    /// </summary>
    public async Task<BenchmarkResults> RunPerformanceBenchmarkAsync()
    {
        var results = new BenchmarkResults
        {
            TestName = "指标系统性能基准测试",
            StartTime = DateTime.UtcNow
        };

        try
        {
            _logger?.LogInformation("开始性能基准测试");

            var configuration = MetricsConfigurationBuilderExtensions.CreateProductionConfiguration().Build();
            using var integrator = new MetricsSystemIntegrator(configuration, _loggerFactory?.CreateLogger<MetricsSystemIntegrator>());

            await integrator.RegisterAggregatorAsync("TimeWindow", new TimeWindowAggregator());
            await integrator.RegisterAnalyzerAsync("Trend", new TrendAnalyzer());
            await integrator.StartAsync();

            // 不同数据量的基准测试
            var dataSizes = new[] { 100, 1000, 10000, 50000 };

            foreach (var size in dataSizes)
            {
                var testData = GenerateTestMetrics(size);
                var stopwatch = Stopwatch.StartNew();

                await integrator.PerformAggregationAsync(testData);
                await integrator.PerformAnalysisAsync(testData);

                stopwatch.Stop();

                results.Benchmarks.Add(new BenchmarkResult
                {
                    DataSize = size,
                    Duration = stopwatch.Elapsed,
                    ThroughputPerSecond = size / stopwatch.Elapsed.TotalSeconds
                });

                _logger?.LogInformation("数据量 {Size}: {Duration}ms, 吞吐量: {Throughput:F2} events/s",
                    size, stopwatch.ElapsedMilliseconds, size / stopwatch.Elapsed.TotalSeconds);
            }

            await integrator.StopAsync();

            results.EndTime = DateTime.UtcNow;
            results.Success = true;

            return results;
        }
        catch (Exception ex)
        {
            results.Success = false;
            results.ErrorMessage = ex.Message;
            _logger?.LogError(ex, "性能基准测试失败");
            return results;
        }
    }
}

/// <summary>
/// 测试结果
/// </summary>
public class TestResults
{
    public string TestName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public List<string> PassedTests { get; set; } = new();
    public List<string> FailedTests { get; set; } = new();
    public Dictionary<string, object> Metrics { get; set; } = new();

    public int TotalTests => PassedTests.Count + FailedTests.Count;
    public double SuccessRate => TotalTests > 0 ? (double)PassedTests.Count / TotalTests * 100 : 0;
}

/// <summary>
/// 基准测试结果
/// </summary>
public class BenchmarkResults
{
    public string TestName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public List<BenchmarkResult> Benchmarks { get; set; } = new();
}

/// <summary>
/// 单个基准测试结果
/// </summary>
public class BenchmarkResult
{
    public int DataSize { get; set; }
    public TimeSpan Duration { get; set; }
    public double ThroughputPerSecond { get; set; }
    public long MemoryUsage { get; set; }
}
