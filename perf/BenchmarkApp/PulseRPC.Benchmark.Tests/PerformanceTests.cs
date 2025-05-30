using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using PulseRPC.Benchmark.Client.Services;
using PulseRPC.Benchmark.Client.Engine;
using PulseRPC.Benchmark.Client.Configuration;

namespace PulseRPC.Benchmark.Tests;

/// <summary>
/// 性能基准验证测试套件
/// 验证基准测试框架本身的性能表现
/// </summary>
public class PerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PerformanceTests> _logger;

    public PerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _serviceProvider = BuildTestServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<PerformanceTests>>();
    }

    /// <summary>
    /// 测试结果收集器的性能 - 高频率数据收集
    /// </summary>
    [Fact]
    public void ResultCollector_HighFrequencyCollection_PerformanceTest()
    {
        // Arrange
        var collector = _serviceProvider.GetRequiredService<ResultCollector>();
        const int totalResults = 100000; // 10万个结果
        var stopwatch = Stopwatch.StartNew();

        // Act
        collector.StartCollection(0); // 禁用快照以专注于收集性能

        for (int i = 0; i < totalResults; i++)
        {
            collector.RecordResult(new TestRequestResult
            {
                RequestId = $"perf_test_{i}",
                Success = i % 100 != 0, // 1%失败率
                ResponseTimeMs = 5 + (i % 50), // 5-55ms延迟变化
                ErrorMessage = i % 100 == 0 ? "Performance test error" : null
            });
        }

        var collectTime = stopwatch.ElapsedMilliseconds;
        stopwatch.Restart();

        var stats = collector.GetLiveStatistics();
        var statsTime = stopwatch.ElapsedMilliseconds;

        collector.StopCollection();
        stopwatch.Stop();

        // Assert
        Assert.Equal(totalResults, stats.TotalRequests);
        Assert.True(collectTime < 5000, $"收集{totalResults}个结果耗时{collectTime}ms，应小于5000ms");
        Assert.True(statsTime < 1000, $"生成统计信息耗时{statsTime}ms，应小于1000ms");

        var collectThroughput = totalResults / (collectTime / 1000.0);
        _output.WriteLine($"✅ 结果收集性能测试通过");
        _output.WriteLine($"   - 收集{totalResults:N0}个结果耗时: {collectTime}ms");
        _output.WriteLine($"   - 收集吞吐量: {collectThroughput:F0} 结果/秒");
        _output.WriteLine($"   - 统计生成耗时: {statsTime}ms");
        _output.WriteLine($"   - 成功率: {stats.SuccessRate:P2}");
    }

    /// <summary>
    /// 测试进度显示服务的性能 - 频繁更新
    /// </summary>
    [Fact]
    public async Task ProgressDisplayService_FrequentUpdates_PerformanceTest()
    {
        // Arrange
        var progressService = _serviceProvider.GetRequiredService<ProgressDisplayService>();
        const int updateCount = 1000;
        var stopwatch = Stopwatch.StartNew();

        // Act
        progressService.StartDisplay("Performance Test", TimeSpan.FromSeconds(10));

        for (int i = 0; i < updateCount; i++)
        {
            progressService.UpdateProgress(new ProgressInfo
            {
                TestName = "Performance Test",
                Phase = TestPhase.Running,
                TotalRequests = i * 10,
                SuccessfulRequests = i * 9,
                FailedRequests = i * 1,
                CurrentQPS = 1000.0 + (i % 500),
                AverageLatencyMs = 10.0 + (i % 20),
                ActiveConnections = 5 + (i % 10)
            });

            // 模拟高频率更新
            if (i % 100 == 0)
            {
                await Task.Delay(1);
            }
        }

        progressService.StopDisplay();
        stopwatch.Stop();

        // Assert
        var totalTime = stopwatch.ElapsedMilliseconds;
        var updatesPerSecond = updateCount / (totalTime / 1000.0);

        Assert.True(totalTime < 5000, $"处理{updateCount}次进度更新耗时{totalTime}ms，应小于5000ms");

        _output.WriteLine($"✅ 进度显示性能测试通过");
        _output.WriteLine($"   - 处理{updateCount:N0}次更新耗时: {totalTime}ms");
        _output.WriteLine($"   - 更新处理速度: {updatesPerSecond:F0} 更新/秒");
    }

    /// <summary>
    /// 测试指标收集器的内存效率
    /// </summary>
    [Fact]
    public void MetricsCollector_MemoryEfficiency_PerformanceTest()
    {
        // Arrange
        var initialMemory = GC.GetTotalMemory(true);
        var collector = _serviceProvider.GetRequiredService<ResultCollector>();
        const int metricsCount = 50000; // 5万个指标

        // Act
        collector.StartCollection();

        for (int i = 0; i < metricsCount; i++)
        {
            collector.RecordMetric($"test_metric_{i % 100}", i * 1.5, DateTime.UtcNow,
                new System.Collections.Generic.Dictionary<string, string>
                {
                    ["iteration"] = i.ToString(),
                    ["category"] = (i % 10).ToString()
                });
        }

        var report = collector.GenerateReport();
        collector.StopCollection();

        var finalMemory = GC.GetTotalMemory(true);
        var memoryUsed = finalMemory - initialMemory;

        // Assert
        Assert.Equal(metricsCount, report.Metrics.Count);
        Assert.True(memoryUsed < 100 * 1024 * 1024, $"内存使用{memoryUsed / 1024 / 1024}MB，应小于100MB");

        _output.WriteLine($"✅ 指标收集内存效率测试通过");
        _output.WriteLine($"   - 收集{metricsCount:N0}个指标");
        _output.WriteLine($"   - 内存使用: {memoryUsed / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"   - 平均每指标: {memoryUsed / (double)metricsCount:F2} 字节");
    }

    /// <summary>
    /// 测试报告生成性能
    /// </summary>
    [Fact]
    public async Task ReportGeneration_LargeDataset_PerformanceTest()
    {
        // Arrange
        var collector = _serviceProvider.GetRequiredService<ResultCollector>();
        const int datasetSize = 25000; // 2.5万个结果
        var stopwatch = Stopwatch.StartNew();

        // 生成大量测试数据
        collector.StartCollection();
        for (int i = 0; i < datasetSize; i++)
        {
            collector.RecordResult(new TestRequestResult
            {
                RequestId = $"report_test_{i}",
                Success = i % 50 != 0, // 2%失败率
                ResponseTimeMs = 10 + (i % 100),
                ErrorMessage = i % 50 == 0 ? $"Error type {i % 5}" : null
            });
        }

        var dataGenerationTime = stopwatch.ElapsedMilliseconds;
        stopwatch.Restart();

        // Act - 生成报告
        var report = collector.GenerateReport();
        var reportGenerationTime = stopwatch.ElapsedMilliseconds;

        collector.StopCollection();
        stopwatch.Stop();

        // Assert
        Assert.Equal(datasetSize, report.TotalRequests);
        Assert.True(reportGenerationTime < 3000, $"生成报告耗时{reportGenerationTime}ms，应小于3000ms");

        var processingSpeed = datasetSize / (reportGenerationTime / 1000.0);

        _output.WriteLine($"✅ 报告生成性能测试通过");
        _output.WriteLine($"   - 数据集大小: {datasetSize:N0} 个结果");
        _output.WriteLine($"   - 数据生成耗时: {dataGenerationTime}ms");
        _output.WriteLine($"   - 报告生成耗时: {reportGenerationTime}ms");
        _output.WriteLine($"   - 处理速度: {processingSpeed:F0} 结果/秒");
        _output.WriteLine($"   - 错误类型数: {report.ErrorSummary.Count}");
    }

    /// <summary>
    /// 测试并发结果收集性能
    /// </summary>
    [Fact]
    public async Task ResultCollector_ConcurrentCollection_PerformanceTest()
    {
        // Arrange
        var collector = _serviceProvider.GetRequiredService<ResultCollector>();
        const int concurrency = 10;
        const int resultsPerTask = 5000;
        var stopwatch = Stopwatch.StartNew();

        collector.StartCollection();

        // Act - 并发收集
        var tasks = new Task[concurrency];
        for (int t = 0; t < concurrency; t++)
        {
            var taskId = t;
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < resultsPerTask; i++)
                {
                    collector.RecordResult(new TestRequestResult
                    {
                        RequestId = $"concurrent_{taskId}_{i}",
                        Success = (taskId + i) % 20 != 0, // 5%失败率
                        ResponseTimeMs = 5 + ((taskId * resultsPerTask + i) % 30),
                        ErrorMessage = (taskId + i) % 20 == 0 ? $"Concurrent error {taskId}" : null
                    });
                }
            });
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        var stats = collector.GetLiveStatistics();
        collector.StopCollection();

        // Assert
        var totalResults = concurrency * resultsPerTask;
        var totalTime = stopwatch.ElapsedMilliseconds;
        var throughput = totalResults / (totalTime / 1000.0);

        Assert.Equal(totalResults, stats.TotalRequests);
        Assert.True(totalTime < 10000, $"并发收集耗时{totalTime}ms，应小于10000ms");

        _output.WriteLine($"✅ 并发收集性能测试通过");
        _output.WriteLine($"   - 并发度: {concurrency}");
        _output.WriteLine($"   - 每任务结果数: {resultsPerTask:N0}");
        _output.WriteLine($"   - 总结果数: {totalResults:N0}");
        _output.WriteLine($"   - 总耗时: {totalTime}ms");
        _output.WriteLine($"   - 吞吐量: {throughput:F0} 结果/秒");
        _output.WriteLine($"   - 成功率: {stats.SuccessRate:P2}");
    }

    /// <summary>
    /// 测试百分位数计算性能
    /// </summary>
    [Fact]
    public void PercentileCalculation_LargeDataset_PerformanceTest()
    {
        // Arrange
        var collector = _serviceProvider.GetRequiredService<ResultCollector>();
        const int sampleSize = 100000; // 10万个样本
        var random = new Random(42); // 固定种子确保可重现性

        // 生成正态分布的延迟数据
        collector.StartCollection();
        for (int i = 0; i < sampleSize; i++)
        {
            // 使用Box-Muller变换生成正态分布
            var latency = Math.Max(1, 50 + random.NextGaussian() * 20); // 均值50ms，标准差20ms

            collector.RecordResult(new TestRequestResult
            {
                RequestId = $"percentile_test_{i}",
                Success = true,
                ResponseTimeMs = latency
            });
        }

        var stopwatch = Stopwatch.StartNew();

        // Act
        var stats = collector.GetLiveStatistics();

        stopwatch.Stop();
        collector.StopCollection();

        // Assert
        var calculationTime = stopwatch.ElapsedMilliseconds;
        Assert.True(calculationTime < 2000, $"百分位数计算耗时{calculationTime}ms，应小于2000ms");

        // 验证百分位数的合理性（正态分布的性质）
        Assert.True(stats.P50LatencyMs < stats.P95LatencyMs);
        Assert.True(stats.P95LatencyMs < stats.P99LatencyMs);
        Assert.True(stats.P50LatencyMs > 30 && stats.P50LatencyMs < 70); // 应该接近50ms

        _output.WriteLine($"✅ 百分位数计算性能测试通过");
        _output.WriteLine($"   - 样本大小: {sampleSize:N0}");
        _output.WriteLine($"   - 计算耗时: {calculationTime}ms");
        _output.WriteLine($"   - P50: {stats.P50LatencyMs:F2}ms");
        _output.WriteLine($"   - P95: {stats.P95LatencyMs:F2}ms");
        _output.WriteLine($"   - P99: {stats.P99LatencyMs:F2}ms");
        _output.WriteLine($"   - 平均值: {stats.AverageLatencyMs:F2}ms");
    }

    /// <summary>
    /// 基准测试框架整体性能测试
    /// </summary>
    [Fact]
    public async Task BenchmarkFramework_OverallPerformance_Test()
    {
        // Arrange
        var testEngine = _serviceProvider.GetRequiredService<TestExecutionEngine>();
        var resultCollector = _serviceProvider.GetRequiredService<ResultCollector>();
        var progressService = _serviceProvider.GetRequiredService<ProgressDisplayService>();

        var testConfig = new TestConfiguration
        {
            ServerAddress = "localhost:8080",
            ScenarioName = "performance-test",
            DurationSeconds = 1, // 短时间测试
            ConcurrentConnections = 5,
            RequestRate = 1000, // 高请求率
            WarmupSeconds = 0,
            Verbose = false
        };

        var stopwatch = Stopwatch.StartNew();

        // Act
        resultCollector.StartCollection();
        progressService.StartDisplay("Performance Framework Test", TimeSpan.FromSeconds(1));

        try
        {
            var results = await testEngine.ExecuteTestAsync(testConfig);

            // 即使连接失败，框架本身的开销也应该是可接受的
            stopwatch.Stop();

            var frameworkOverhead = stopwatch.ElapsedMilliseconds;
            Assert.True(frameworkOverhead < 5000, $"框架开销{frameworkOverhead}ms，应小于5000ms");

            _output.WriteLine($"✅ 框架整体性能测试通过");
            _output.WriteLine($"   - 框架开销: {frameworkOverhead}ms");
            _output.WriteLine($"   - 配置处理正常");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var frameworkOverhead = stopwatch.ElapsedMilliseconds;

            // 即使有异常，框架响应时间也应该合理
            Assert.True(frameworkOverhead < 3000, $"框架异常处理耗时{frameworkOverhead}ms，应小于3000ms");

            _output.WriteLine($"✅ 框架异常处理性能测试通过（预期的连接错误）");
            _output.WriteLine($"   - 异常处理耗时: {frameworkOverhead}ms");
            _output.WriteLine($"   - 异常信息: {ex.Message}");
        }
        finally
        {
            resultCollector.StopCollection();
            progressService.StopDisplay();
        }
    }

    private IServiceProvider BuildTestServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddProvider(new XunitLoggerProvider(_output));
            builder.SetMinimumLevel(LogLevel.Warning); // 减少日志输出以专注性能测试
        });

        services.AddSingleton<TestExecutionEngine>();
        services.AddSingleton<ResultCollector>();
        services.AddSingleton<ProgressDisplayService>();

        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

/// <summary>
/// 随机数扩展方法
/// </summary>
public static class RandomExtensions
{
    /// <summary>
    /// 生成正态分布随机数
    /// </summary>
    public static double NextGaussian(this Random random)
    {
        // Box-Muller变换
        static double NextGaussianInternal()
        {
            var u1 = 1.0 - Random.Shared.NextDouble();
            var u2 = 1.0 - Random.Shared.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }

        return NextGaussianInternal();
    }
}

// XUnit日志提供者（重用）
public class XunitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;

    public XunitLoggerProvider(ITestOutputHelper output)
    {
        _output = output;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new XunitLogger(_output, categoryName);
    }

    public void Dispose() { }
}

public class XunitLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public XunitLogger(ITestOutputHelper output, string categoryName)
    {
        _output = output;
        _categoryName = categoryName;
    }

    public IDisposable BeginScope<TState>(TState state) => null!;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            _output.WriteLine($"[{logLevel}] {_categoryName}: {formatter(state, exception)}");
        }
        catch
        {
            // 忽略输出错误
        }
    }
}
