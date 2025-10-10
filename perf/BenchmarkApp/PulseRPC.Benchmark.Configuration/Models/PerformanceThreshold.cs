namespace PulseRPC.Benchmark.Configuration.Models;

/// <summary>
/// Acceptable performance limit for a specific metric
/// Used for pass/fail validation of benchmark results
/// </summary>
public class PerformanceThreshold
{
    /// <summary>
    /// Metric identifier using dot notation (e.g., "Latency.P95Ms", "Throughput.OperationsPerSecond")
    /// </summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>
    /// Comparison operator to apply
    /// </summary>
    public ThresholdOperator Operator { get; set; }

    /// <summary>
    /// Target/expected value (or minimum value for Between operator)
    /// </summary>
    public double TargetValue { get; set; }

    /// <summary>
    /// Maximum value (used only with Between operator)
    /// </summary>
    public double? MaxValue { get; set; }

    /// <summary>
    /// Severity level of threshold violation
    /// </summary>
    public ThresholdSeverity Severity { get; set; }

    /// <summary>
    /// Optional description of what this threshold validates
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Threshold comparison operators
/// </summary>
public enum ThresholdOperator
{
    /// <summary>
    /// Actual value must be less than target value
    /// </summary>
    LessThan,

    /// <summary>
    /// Actual value must be less than or equal to target value
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// Actual value must be greater than target value
    /// </summary>
    GreaterThan,

    /// <summary>
    /// Actual value must be greater than or equal to target value
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// Actual value must be between target value and max value (inclusive)
    /// </summary>
    Between,

    /// <summary>
    /// Actual value must equal target value (within tolerance)
    /// </summary>
    Equals,

    /// <summary>
    /// Actual value must not equal target value
    /// </summary>
    NotEquals
}

/// <summary>
/// Threshold violation severity levels
/// </summary>
public enum ThresholdSeverity
{
    /// <summary>
    /// Informational - for tracking purposes only
    /// </summary>
    Info,

    /// <summary>
    /// Warning - performance concern but not critical
    /// </summary>
    Warning,

    /// <summary>
    /// Error - critical performance regression
    /// </summary>
    Error
}

/// <summary>
/// Threshold validation outcome
/// </summary>
public class ThresholdResult
{
    /// <summary>
    /// Evaluated threshold configuration
    /// </summary>
    public PerformanceThreshold Threshold { get; set; } = new();

    /// <summary>
    /// Actual measured value from benchmark
    /// </summary>
    public double ActualValue { get; set; }

    /// <summary>
    /// True if threshold requirement was met
    /// </summary>
    public bool Passed { get; set; }

    /// <summary>
    /// Descriptive message explaining the result
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Configuration for threshold-based validation
/// </summary>
public class ThresholdConfiguration
{
    /// <summary>
    /// List of performance thresholds to validate
    /// </summary>
    public List<PerformanceThreshold> Thresholds { get; set; } = new();

    /// <summary>
    /// Whether to fail the entire benchmark if any threshold fails
    /// </summary>
    public bool FailOnAnyThresholdViolation { get; set; } = true;

    /// <summary>
    /// Whether to fail only on Error severity violations (ignore Warning/Info)
    /// </summary>
    public bool FailOnErrorOnly { get; set; } = false;
}

