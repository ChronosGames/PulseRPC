namespace PulseRPC.Benchmark.Metrics.Models;

/// <summary>
/// Historical benchmark results for comparison
/// Stores baseline metrics used for performance regression detection
/// </summary>
public class BaselineData
{
    /// <summary>
    /// Unique baseline identifier
    /// </summary>
    public Guid BaselineId { get; set; }

    /// <summary>
    /// Baseline name/version (e.g., "v1.0-production", "main-branch-2024-01")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Baseline description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Baseline creation time (UTC)
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Original test scenario that produced this baseline
    /// </summary>
    public string ScenarioName { get; set; } = string.Empty;

    /// <summary>
    /// Baseline metrics (uses existing PerformanceMetrics from BenchmarkReportData)
    /// </summary>
    public PerformanceMetrics Metrics { get; set; } = new();

    /// <summary>
    /// Baseline execution environment (uses existing EnvironmentInfo from BenchmarkReportData)
    /// </summary>
    public EnvironmentInfo Environment { get; set; } = new();
}

/// <summary>
/// Performance regression analysis comparing current run to baseline
/// </summary>
public class BaselineComparison
{
    /// <summary>
    /// Reference baseline
    /// </summary>
    public BaselineData Baseline { get; set; } = new();

    /// <summary>
    /// Current run metrics
    /// </summary>
    public PerformanceMetrics CurrentMetrics { get; set; } = new();

    /// <summary>
    /// Latency differences
    /// </summary>
    public LatencyDelta LatencyDelta { get; set; } = new();

    /// <summary>
    /// Throughput differences
    /// </summary>
    public ThroughputDelta ThroughputDelta { get; set; } = new();

    /// <summary>
    /// Success rate difference (percentage points)
    /// </summary>
    public double SuccessRateDelta { get; set; }

    /// <summary>
    /// True if performance degraded compared to baseline
    /// </summary>
    public bool OverallRegression { get; set; }

    /// <summary>
    /// Specific regression descriptions
    /// </summary>
    public List<string> RegressionDetails { get; set; } = new();
}

/// <summary>
/// Latency metric deltas between baseline and current
/// </summary>
public class LatencyDelta
{
    /// <summary>
    /// P50 percentage change
    /// </summary>
    public double P50PercentageChange { get; set; }

    /// <summary>
    /// P95 percentage change
    /// </summary>
    public double P95PercentageChange { get; set; }

    /// <summary>
    /// P99 percentage change
    /// </summary>
    public double P99PercentageChange { get; set; }

    /// <summary>
    /// Mean percentage change
    /// </summary>
    public double MeanPercentageChange { get; set; }
}

/// <summary>
/// Throughput metric deltas between baseline and current
/// </summary>
public class ThroughputDelta
{
    /// <summary>
    /// Operations per second percentage change
    /// </summary>
    public double OpsPerSecondPercentageChange { get; set; }

    /// <summary>
    /// Bandwidth percentage change
    /// </summary>
    public double BandwidthPercentageChange { get; set; }
}

