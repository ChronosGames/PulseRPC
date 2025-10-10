using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Core.Models;
using PulseRPC.Benchmark.Shared.Models;

namespace PulseRPC.Benchmark.Scenarios.Advanced;

/// <summary>
/// 协议比较场景
/// 在TCP和KCP协议上运行相同的测试并比较性能
/// </summary>
public class ProtocolComparisonScenario(ILoggerFactory loggerFactory) : BenchmarkClientBase(loggerFactory)
{
    private readonly Dictionary<string, List<double>> _latencySamplesByProtocol = new();
    private readonly Dictionary<string, long> _successCountByProtocol = new();
    private readonly Dictionary<string, long> _errorCountByProtocol = new();

    public override string ScenarioName => "Protocol Comparison Test";
    public override string Description => "比较TCP和KCP协议的性能差异";
    public override string Category => ScenarioCategories.ProtocolComparison;

    public override async Task<BenchmarkResult> ExecuteScenarioAsync(
        BenchmarkConfiguration config,
        CancellationToken cancellationToken = default)
    {
        var result = new BenchmarkResult
        {
            ScenarioName = ScenarioName,
            StartTime = DateTime.UtcNow,
            Configuration = config
        };

        try
        {
            Logger.LogInformation("开始协议比较测试");

            // 清空数据
            _latencySamplesByProtocol.Clear();
            _successCountByProtocol.Clear();
            _errorCountByProtocol.Clear();

            // 在TCP协议上运行测试
            Logger.LogInformation("在TCP协议上运行测试...");
            await RunTestOnProtocol("TCP", config, cancellationToken);

            // 如果启用了KCP，在KCP协议上运行相同的测试
            if (config.EnableKcp)
            {
                Logger.LogInformation("在KCP协议上运行测试...");
                await RunTestOnProtocol("KCP", config, cancellationToken);
            }
            else
            {
                Logger.LogWarning("KCP未启用，跳过KCP测试");
            }

            // 填充结果
            PopulateComparisonResults(result);

            result.IsSuccessful = true;
            result.EndTime = DateTime.UtcNow;

            Logger.LogInformation("协议比较测试完成");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "协议比较测试失败");
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.Exception = ex;
            result.EndTime = DateTime.UtcNow;
        }

        return result;
    }

    private async Task RunTestOnProtocol(
        string protocol,
        BenchmarkConfiguration config,
        CancellationToken cancellationToken)
    {
        _latencySamplesByProtocol[protocol] = new List<double>();
        _successCountByProtocol[protocol] = 0;
        _errorCountByProtocol[protocol] = 0;

        var iterations = config.Iterations;
        var messageSize = config.MessageSizeBytes > 0 ? config.MessageSizeBytes : 1024;

        // 预热
        if (config.WarmupIterations > 0)
        {
            Logger.LogInformation("预热 {Protocol}: {Warmup} 次迭代", protocol, config.WarmupIterations);
            for (int i = 0; i < config.WarmupIterations && !cancellationToken.IsCancellationRequested; i++)
            {
                await ExecuteSingleRequest(messageSize, cancellationToken);
            }
        }

        // 主测试
        Logger.LogInformation("开始 {Protocol} 测试: {Iterations} 次迭代", protocol, iterations);
        
        for (int i = 0; i < iterations; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogWarning("{Protocol} 测试被取消，已完成 {Current}/{Total}", protocol, i, iterations);
                break;
            }

            var latency = await ExecuteSingleRequest(messageSize, cancellationToken);

            if (latency >= 0)
            {
                _latencySamplesByProtocol[protocol].Add(latency);
                _successCountByProtocol[protocol]++;
            }
            else
            {
                _errorCountByProtocol[protocol]++;
            }

            // 进度报告
            if ((i + 1) % 100 == 0)
            {
                Logger.LogDebug("{Protocol}: 已完成 {Current}/{Total}, 成功: {Success}, 失败: {Failed}",
                    protocol, i + 1, iterations,
                    _successCountByProtocol[protocol],
                    _errorCountByProtocol[protocol]);
            }

            // 测试间隔
            if (config.TestIntervalMs > 0)
            {
                await Task.Delay(config.TestIntervalMs, cancellationToken);
            }
        }

        Logger.LogInformation("{Protocol} 测试完成。成功: {Success}, 失败: {Failed}",
            protocol,
            _successCountByProtocol[protocol],
            _errorCountByProtocol[protocol]);
    }

    private async Task<double> ExecuteSingleRequest(int messageSize, CancellationToken cancellationToken)
    {
        if (BenchmarkService == null)
        {
            throw new InvalidOperationException("BenchmarkService 未初始化，请先调用 InitializeAsync");
        }

        try
        {
            var payload = GenerateTestString(messageSize);
            var request = new EchoRequest
            {
                Message = payload,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var stopwatch = Stopwatch.StartNew();
            var response = await BenchmarkService.EchoAsync(request, cancellationToken);
            stopwatch.Stop();

            if (response != null && response.Success)
            {
                return stopwatch.Elapsed.TotalMilliseconds;
            }

            return -1;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "请求失败");
            return -1;
        }
    }

    private void PopulateComparisonResults(BenchmarkResult result)
    {
        // 使用 TCP 的结果作为主要结果
        if (_latencySamplesByProtocol.TryGetValue("TCP", out var tcpLatencies) && tcpLatencies.Count > 0)
        {
            var sortedLatencies = tcpLatencies.OrderBy(x => x).ToArray();
            result.Latency.SampleCount = sortedLatencies.Length;
            result.Latency.AverageMs = sortedLatencies.Average();
            result.Latency.MinMs = sortedLatencies[0];
            result.Latency.MaxMs = sortedLatencies[^1];
            result.Latency.MedianMs = GetPercentile(sortedLatencies, 50);
            result.Latency.P50Ms = result.Latency.MedianMs;
            result.Latency.P90Ms = GetPercentile(sortedLatencies, 90);
            result.Latency.P95Ms = GetPercentile(sortedLatencies, 95);
            result.Latency.P99Ms = GetPercentile(sortedLatencies, 99);
            result.Latency.P999Ms = GetPercentile(sortedLatencies, 99.9);
            result.Latency.StandardDeviationMs = CalculateStandardDeviation(sortedLatencies);
        }

        // 吞吐量指标（总和）
        var totalOperations = _successCountByProtocol.Values.Sum() + _errorCountByProtocol.Values.Sum();
        var totalSuccess = _successCountByProtocol.Values.Sum();
        var totalErrors = _errorCountByProtocol.Values.Sum();

        result.Throughput.TotalOperations = totalOperations;
        result.Throughput.SuccessfulOperations = totalSuccess;
        result.Throughput.FailedOperations = totalErrors;

        var duration = result.Duration.TotalSeconds;
        if (duration > 0)
        {
            result.Throughput.OperationsPerSecond = totalOperations / duration;
        }

        // 保存每个协议的详细指标到自定义指标
        foreach (var protocol in _latencySamplesByProtocol.Keys)
        {
            if (_latencySamplesByProtocol[protocol].Count > 0)
            {
                var latencies = _latencySamplesByProtocol[protocol].OrderBy(x => x).ToArray();
                result.CustomMetrics[$"{protocol}_AverageLatencyMs"] = latencies.Average();
                result.CustomMetrics[$"{protocol}_P50Ms"] = GetPercentile(latencies, 50);
                result.CustomMetrics[$"{protocol}_P95Ms"] = GetPercentile(latencies, 95);
                result.CustomMetrics[$"{protocol}_P99Ms"] = GetPercentile(latencies, 99);
                result.CustomMetrics[$"{protocol}_MinMs"] = latencies[0];
                result.CustomMetrics[$"{protocol}_MaxMs"] = latencies[^1];
                result.CustomMetrics[$"{protocol}_SuccessCount"] = _successCountByProtocol[protocol];
                result.CustomMetrics[$"{protocol}_ErrorCount"] = _errorCountByProtocol[protocol];
            }
        }

        // 生成协议推荐
        GenerateProtocolRecommendation(result);
    }

    private void GenerateProtocolRecommendation(BenchmarkResult result)
    {
        if (!_latencySamplesByProtocol.ContainsKey("TCP") || 
            !_latencySamplesByProtocol.ContainsKey("KCP") ||
            _latencySamplesByProtocol["TCP"].Count == 0 ||
            _latencySamplesByProtocol["KCP"].Count == 0)
        {
            result.CustomMetrics["Recommendation"] = "无法生成推荐：缺少协议数据";
            return;
        }

        var tcpAvg = _latencySamplesByProtocol["TCP"].Average();
        var kcpAvg = _latencySamplesByProtocol["KCP"].Average();
        var tcpP95 = GetPercentile(_latencySamplesByProtocol["TCP"].OrderBy(x => x).ToArray(), 95);
        var kcpP95 = GetPercentile(_latencySamplesByProtocol["KCP"].OrderBy(x => x).ToArray(), 95);

        var recommendation = new System.Text.StringBuilder();
        recommendation.AppendLine("协议性能比较结果：");
        recommendation.AppendLine($"TCP 平均延迟: {tcpAvg:F2}ms, P95: {tcpP95:F2}ms");
        recommendation.AppendLine($"KCP 平均延迟: {kcpAvg:F2}ms, P95: {kcpP95:F2}ms");

        if (tcpAvg < kcpAvg * 0.9) // TCP significantly better
        {
            recommendation.AppendLine("推荐: TCP - TCP 在延迟和稳定性方面表现更好");
        }
        else if (kcpAvg < tcpAvg * 0.9) // KCP significantly better
        {
            recommendation.AppendLine("推荐: KCP - KCP 在延迟方面表现更好，适合实时应用");
        }
        else
        {
            recommendation.AppendLine("推荐: 两种协议性能相近，可根据具体需求选择");
            recommendation.AppendLine("- TCP: 更稳定，适合可靠传输");
            recommendation.AppendLine("- KCP: 更快速，适合实时游戏和流媒体");
        }

        result.CustomMetrics["Recommendation"] = recommendation.ToString();
        Logger.LogInformation("协议推荐: {Recommendation}", recommendation.ToString());
    }

    private static double GetPercentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0) return 0;

        var index = (percentile / 100.0) * (sortedValues.Length - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);

        if (lower == upper)
            return sortedValues[lower];

        var weight = index - lower;
        return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
    }

    private static double CalculateStandardDeviation(double[] values)
    {
        if (values.Length == 0) return 0;

        var mean = values.Average();
        var sumOfSquaredDifferences = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumOfSquaredDifferences / values.Length);
    }
}

