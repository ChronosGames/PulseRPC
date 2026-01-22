using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Contracts;
using PulseRPC.Benchmark.Models;

namespace PulseRPC.Benchmark.Scenarios;

/// <summary>
/// 场景基类
/// </summary>
public abstract class ScenarioBase : IScenario
{
    protected readonly ILogger Logger;

    protected ScenarioBase(ILogger logger)
    {
        Logger = logger;
    }

    public abstract string Name { get; }
    public abstract string Description { get; }

    public abstract Task<BenchmarkResult> RunAsync(IBenchmarkHub service, BenchmarkConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// 生成测试字符串
    /// </summary>
    protected static string GenerateTestString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
    }

    /// <summary>
    /// 计算百分位数
    /// </summary>
    protected static double GetPercentile(double[] sortedValues, double percentile)
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

    /// <summary>
    /// 计算标准差
    /// </summary>
    protected static double CalculateStandardDeviation(double[] values)
    {
        if (values.Length == 0) return 0;

        var mean = values.Average();
        var sumOfSquaredDifferences = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumOfSquaredDifferences / values.Length);
    }

    /// <summary>
    /// 填充延迟统计信息
    /// </summary>
    protected static void PopulateLatencyMetrics(LatencyMetrics metrics, double[] latencies)
    {
        if (latencies.Length == 0) return;

        var sorted = latencies.OrderBy(x => x).ToArray();
        metrics.SampleCount = sorted.Length;
        metrics.AverageMs = sorted.Average();
        metrics.MinMs = sorted[0];
        metrics.MaxMs = sorted[^1];
        metrics.P50Ms = GetPercentile(sorted, 50);
        metrics.P90Ms = GetPercentile(sorted, 90);
        metrics.P95Ms = GetPercentile(sorted, 95);
        metrics.P99Ms = GetPercentile(sorted, 99);
        metrics.StandardDeviationMs = CalculateStandardDeviation(sorted);
    }
}
