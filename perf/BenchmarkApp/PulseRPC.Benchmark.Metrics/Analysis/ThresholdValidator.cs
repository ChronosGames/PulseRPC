using System.Reflection;
using PulseRPC.Benchmark.Configuration.Models;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Analysis;

/// <summary>
/// Validates performance metrics against configured thresholds
/// Supports multiple comparison operators and generates pass/fail results
/// </summary>
public class ThresholdValidator
{
    private const double EqualityTolerance = 0.001; // Tolerance for floating-point equality comparisons

    /// <summary>
    /// Validates a single threshold against performance metrics
    /// </summary>
    /// <param name="threshold">Threshold configuration to validate</param>
    /// <param name="metrics">Performance metrics to validate against</param>
    /// <returns>Validation result with pass/fail status and message</returns>
    public ThresholdResult Validate(PerformanceThreshold threshold, PerformanceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(threshold);
        ArgumentNullException.ThrowIfNull(metrics);

        var actualValue = GetMetricValue(threshold.MetricName, metrics);

        var passed = EvaluateThreshold(threshold, actualValue);

        var message = GenerateMessage(threshold, actualValue, passed);

        return new ThresholdResult
        {
            Threshold = threshold,
            ActualValue = actualValue,
            Passed = passed,
            Message = message
        };
    }

    /// <summary>
    /// Validates all thresholds against performance metrics
    /// </summary>
    /// <param name="thresholds">Collection of thresholds to validate</param>
    /// <param name="metrics">Performance metrics to validate against</param>
    /// <returns>List of validation results</returns>
    public List<ThresholdResult> ValidateAll(IEnumerable<PerformanceThreshold> thresholds, PerformanceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(metrics);

        return thresholds.Select(threshold => Validate(threshold, metrics)).ToList();
    }

    private double GetMetricValue(string metricName, PerformanceMetrics metrics)
    {
        try
        {
            // Split metric name by dot notation (e.g., "Latency.P95Ms")
            var parts = metricName.Split('.');

            if (parts.Length != 2)
            {
                throw new ArgumentException($"Invalid metric name format: {metricName}. Expected format: Category.PropertyName");
            }

            var category = parts[0];
            var propertyName = parts[1];

            object? categoryObject = category switch
            {
                "Latency" => metrics.Latency,
                "Throughput" => metrics.Throughput,
                "Resources" => metrics.Resources,
                _ => throw new ArgumentException($"Unknown metric category: {category}")
            };

            var property = categoryObject.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

            if (property == null)
            {
                throw new ArgumentException($"Property '{propertyName}' not found in {category} metrics");
            }

            var value = property.GetValue(categoryObject);

            return value switch
            {
                double d => d,
                int i => i,
                long l => l,
                _ => throw new ArgumentException($"Metric '{metricName}' has unsupported type: {value?.GetType().Name}")
            };
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed to retrieve metric value for '{metricName}': {ex.Message}", ex);
        }
    }

    private bool EvaluateThreshold(PerformanceThreshold threshold, double actualValue)
    {
        return threshold.Operator switch
        {
            ThresholdOperator.LessThan => actualValue < threshold.TargetValue,
            ThresholdOperator.LessThanOrEqual => actualValue <= threshold.TargetValue || 
                                                  Math.Abs(actualValue - threshold.TargetValue) < EqualityTolerance,
            ThresholdOperator.GreaterThan => actualValue > threshold.TargetValue,
            ThresholdOperator.GreaterThanOrEqual => actualValue >= threshold.TargetValue || 
                                                     Math.Abs(actualValue - threshold.TargetValue) < EqualityTolerance,
            ThresholdOperator.Between => actualValue >= threshold.TargetValue && actualValue <= (threshold.MaxValue ?? double.MaxValue),
            ThresholdOperator.Equals => Math.Abs(actualValue - threshold.TargetValue) < EqualityTolerance,
            ThresholdOperator.NotEquals => Math.Abs(actualValue - threshold.TargetValue) >= EqualityTolerance,
            _ => throw new NotSupportedException($"Threshold operator '{threshold.Operator}' is not supported")
        };
    }

    private string GenerateMessage(PerformanceThreshold threshold, double actualValue, bool passed)
    {
        var severityIcon = threshold.Severity switch
        {
            ThresholdSeverity.Error => "❌",
            ThresholdSeverity.Warning => "⚠️",
            ThresholdSeverity.Info => "ℹ️",
            _ => ""
        };

        var operatorText = threshold.Operator switch
        {
            ThresholdOperator.LessThan => "less than",
            ThresholdOperator.LessThanOrEqual => "less than or equal to",
            ThresholdOperator.GreaterThan => "greater than",
            ThresholdOperator.GreaterThanOrEqual => "greater than or equal to",
            ThresholdOperator.Between => $"between {threshold.TargetValue:F2} and {threshold.MaxValue:F2}",
            ThresholdOperator.Equals => "equal to",
            ThresholdOperator.NotEquals => "not equal to",
            _ => threshold.Operator.ToString()
        };

        if (passed)
        {
            return threshold.Operator == ThresholdOperator.Between
                ? $"✓ {threshold.MetricName}: {actualValue:F2} is {operatorText} (PASS)"
                : $"✓ {threshold.MetricName}: {actualValue:F2} is {operatorText} {threshold.TargetValue:F2} (PASS)";
        }

        return threshold.Operator == ThresholdOperator.Between
            ? $"{severityIcon} {threshold.MetricName}: {actualValue:F2} is NOT {operatorText} (FAIL)"
            : $"{severityIcon} {threshold.MetricName}: {actualValue:F2} is NOT {operatorText} {threshold.TargetValue:F2} (FAIL)";
    }
}

