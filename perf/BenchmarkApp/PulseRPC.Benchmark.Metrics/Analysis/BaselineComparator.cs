using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Analysis;

/// <summary>
/// Compares current benchmark results against baseline to detect performance regressions
/// </summary>
public class BaselineComparator
{
    private const double RegressionThresholdPercentage = 10.0; // 10% degradation is considered regression
    private const double NoiseTolerancePercentage = 5.0; // Changes under 5% are considered noise

    /// <summary>
    /// Compares current metrics against baseline and generates comparison analysis
    /// </summary>
    /// <param name="baseline">Baseline metrics to compare against</param>
    /// <param name="current">Current benchmark metrics</param>
    /// <returns>Comparison analysis with regression detection</returns>
    public BaselineComparison Compare(BaselineData baseline, PerformanceMetrics current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var comparison = new BaselineComparison
        {
            Baseline = baseline,
            CurrentMetrics = current
        };

        // Calculate latency deltas
        comparison.LatencyDelta = CompareLatencyMetrics(baseline.Metrics.Latency, current.Latency);

        // Calculate throughput deltas
        comparison.ThroughputDelta = CompareThroughputMetrics(baseline.Metrics.Throughput, current.Throughput);

        // Calculate success rate delta
        comparison.SuccessRateDelta = CalculateSuccessRateDelta(baseline.Metrics.Throughput, current.Throughput);

        // Detect regressions
        DetectRegressions(comparison);

        return comparison;
    }

    private LatencyDelta CompareLatencyMetrics(
        Models.LatencyMetrics baseline,
        Models.LatencyMetrics current)
    {
        return new LatencyDelta
        {
            P50PercentageChange = CalculatePercentageChange(baseline.P50Ms, current.P50Ms),
            P95PercentageChange = CalculatePercentageChange(baseline.P95Ms, current.P95Ms),
            P99PercentageChange = CalculatePercentageChange(baseline.P99Ms, current.P99Ms),
            MeanPercentageChange = CalculatePercentageChange(baseline.AverageMs, current.AverageMs)
        };
    }

    private ThroughputDelta CompareThroughputMetrics(
        Models.ThroughputMetrics baseline,
        Models.ThroughputMetrics current)
    {
        var baselineSuccessRate = baseline.TotalRequests > 0 
            ? (double)baseline.SuccessfulRequests / baseline.TotalRequests * 100 
            : 0;
        var currentSuccessRate = current.TotalRequests > 0 
            ? (double)current.SuccessfulRequests / current.TotalRequests * 100 
            : 0;

        return new ThroughputDelta
        {
            OpsPerSecondPercentageChange = CalculatePercentageChange(
                baseline.AverageRps,
                current.AverageRps),
            BandwidthPercentageChange = 0  // TODO: Add bandwidth to ThroughputMetrics if needed
        };
    }

    private double CalculateSuccessRateDelta(
        Models.ThroughputMetrics baseline,
        Models.ThroughputMetrics current)
    {
        var baselineSuccessRate = baseline.TotalRequests > 0 
            ? (double)baseline.SuccessfulRequests / baseline.TotalRequests * 100 
            : 0;
        var currentSuccessRate = current.TotalRequests > 0 
            ? (double)current.SuccessfulRequests / current.TotalRequests * 100 
            : 0;

        // Return absolute difference in percentage points
        return currentSuccessRate - baselineSuccessRate;
    }

    private double CalculatePercentageChange(double baseline, double current)
    {
        if (Math.Abs(baseline) < 0.0001) // Avoid division by zero
        {
            return current > 0 ? 100.0 : 0.0; // If baseline is zero but current isn't, that's infinite improvement
        }

        return ((current - baseline) / baseline) * 100.0;
    }

    private void DetectRegressions(BaselineComparison comparison)
    {
        var regressions = new List<string>();

        // Check latency regressions (increase is bad)
        CheckLatencyRegression(comparison.LatencyDelta.P95PercentageChange, "P95 latency", regressions);
        CheckLatencyRegression(comparison.LatencyDelta.P99PercentageChange, "P99 latency", regressions);
        CheckLatencyRegression(comparison.LatencyDelta.MeanPercentageChange, "Mean latency", regressions);

        // Check throughput regressions (decrease is bad)
        CheckThroughputRegression(comparison.ThroughputDelta.OpsPerSecondPercentageChange, "Operations per second", regressions);

        // Check success rate regressions (decrease is bad)
        if (comparison.SuccessRateDelta < -1.0) // More than 1 percentage point decrease
        {
            regressions.Add($"Success rate decreased by {Math.Abs(comparison.SuccessRateDelta):F2} percentage points");
        }

        // Check for improvements
        if (comparison.LatencyDelta.P95PercentageChange < -NoiseTolerancePercentage)
        {
            regressions.Add($"✓ P95 latency improved by {Math.Abs(comparison.LatencyDelta.P95PercentageChange):F1}%");
        }

        if (comparison.ThroughputDelta.OpsPerSecondPercentageChange > NoiseTolerancePercentage)
        {
            regressions.Add($"✓ Throughput improved by {comparison.ThroughputDelta.OpsPerSecondPercentageChange:F1}%");
        }

        comparison.RegressionDetails = regressions;
        comparison.OverallRegression = regressions.Any(r => !r.StartsWith("✓")); // Regression if any non-improvement entry exists
    }

    private void CheckLatencyRegression(double percentageChange, string metricName, List<string> regressions)
    {
        if (Math.Abs(percentageChange) < NoiseTolerancePercentage)
        {
            // Within noise tolerance, ignore
            return;
        }

        if (percentageChange > RegressionThresholdPercentage)
        {
            regressions.Add($"❌ {metricName} regression: increased by {percentageChange:F1}%");
        }
        else if (percentageChange > NoiseTolerancePercentage)
        {
            regressions.Add($"⚠️ {metricName}: increased by {percentageChange:F1}% (minor degradation)");
        }
    }

    private void CheckThroughputRegression(double percentageChange, string metricName, List<string> regressions)
    {
        if (Math.Abs(percentageChange) < NoiseTolerancePercentage)
        {
            // Within noise tolerance, ignore
            return;
        }

        if (percentageChange < -RegressionThresholdPercentage)
        {
            regressions.Add($"❌ {metricName} regression: decreased by {Math.Abs(percentageChange):F1}%");
        }
        else if (percentageChange < -NoiseTolerancePercentage)
        {
            regressions.Add($"⚠️ {metricName}: decreased by {Math.Abs(percentageChange):F1}% (minor degradation)");
        }
    }
}

