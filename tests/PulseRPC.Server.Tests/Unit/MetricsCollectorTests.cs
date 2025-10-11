using FluentAssertions;
using PulseRPC.Server.Observability;
using System.Linq;
using System.Threading;
using Xunit;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// Unit tests for PipelineMetricsCollector (T063).
/// Tests percentile calculation, counter accuracy, and rate tracking.
/// </summary>
public class MetricsCollectorTests
{
    [Fact]
    public void MetricsCollector_ShouldInitialize_WithZeroMetrics()
    {
        // Arrange & Act
        var collector = new PipelineMetricsCollector();

        // Assert
        var snapshot = collector.GetSnapshot();
        snapshot.TotalRequests.Should().Be(0);
        snapshot.TotalErrors.Should().Be(0);
        snapshot.ActiveRequests.Should().Be(0);
        snapshot.ActiveConnections.Should().Be(0);
    }

    [Fact]
    public void RecordRequestStart_ShouldIncrementCounters()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();

        // Act
        collector.RecordRequestStart();
        collector.RecordRequestStart();
        collector.RecordRequestStart();

        // Assert
        var snapshot = collector.GetSnapshot();
        snapshot.TotalRequests.Should().Be(3);
        snapshot.ActiveRequests.Should().Be(3);
    }

    [Fact]
    public void RecordRequestComplete_ShouldDecrementActiveRequests()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();
        collector.RecordRequestStart();
        collector.RecordRequestStart();

        // Act
        collector.RecordRequestComplete(5.0, isError: false);

        // Assert
        var snapshot = collector.GetSnapshot();
        snapshot.TotalRequests.Should().Be(2);
        snapshot.ActiveRequests.Should().Be(1);
    }

    [Fact]
    public void RecordRequestComplete_ShouldTrackErrors()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();

        // Act: Record 10 requests, 3 errors
        for (int i = 0; i < 10; i++)
        {
            collector.RecordRequestStart();
            bool isError = i < 3;
            collector.RecordRequestComplete(5.0, isError: isError);
        }

        // Assert
        var snapshot = collector.GetSnapshot();
        snapshot.TotalRequests.Should().Be(10);
        snapshot.TotalErrors.Should().Be(3);
        snapshot.ErrorRate.Should().BeApproximately(0.3, 0.01); // 30% error rate
    }

    [Fact]
    public void RecordRequestComplete_ShouldTrackTimeouts()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();

        // Act
        collector.RecordRequestStart();
        collector.RecordRequestComplete(1000.0, isError: true, isTimeout: true);

        // Assert
        var snapshot = collector.GetSnapshot();
        snapshot.TotalTimeouts.Should().Be(1);
        snapshot.TotalErrors.Should().Be(1);
    }

    [Fact]
    public void RecordConnectionChange_ShouldUpdateActiveConnections()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();

        // Act: Add 5 connections
        collector.RecordConnectionChange(5);

        // Assert
        collector.GetSnapshot().ActiveConnections.Should().Be(5);

        // Act: Remove 2 connections
        collector.RecordConnectionChange(-2);

        // Assert
        collector.GetSnapshot().ActiveConnections.Should().Be(3);
    }

    [Fact]
    public void UpdateQueueDepths_ShouldUpdateAllQueues()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();

        // Act
        collector.UpdateQueueDepths(100, 200, 300);

        // Assert
        var snapshot = collector.GetSnapshot();
        snapshot.L1QueueDepth.Should().Be(100);
        snapshot.L2QueueDepth.Should().Be(200);
        snapshot.L3QueueDepth.Should().Be(300);
    }

    [Fact]
    public void CalculatePercentile_ShouldReturnCorrectP50()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();

        // Act: Record latencies: 1, 2, 3, 4, 5 ms
        for (int i = 1; i <= 5; i++)
        {
            collector.RecordRequestStart();
            collector.RecordRequestComplete(i, isError: false);
        }

        // Assert: P50 should be ~3ms (median of 1,2,3,4,5)
        var snapshot = collector.GetSnapshot();
        snapshot.LatencyP50.Should().BeInRange(2, 5); // Bucket-based approximation
    }

    [Fact]
    public void CalculatePercentile_ShouldReturnCorrectP95()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();

        // Act: Record 100 requests with latencies 1-100ms
        for (int i = 1; i <= 100; i++)
        {
            collector.RecordRequestStart();
            collector.RecordRequestComplete(i, isError: false);
        }

        // Assert: P95 should be ~95ms
        var snapshot = collector.GetSnapshot();
        snapshot.LatencyP95.Should().BeGreaterThan(50); // Should be in high bucket
    }

    [Fact]
    public void CalculatePercentile_ShouldReturnCorrectP99()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();

        // Act: Record 100 requests with varying latencies
        for (int i = 1; i <= 100; i++)
        {
            collector.RecordRequestStart();
            collector.RecordRequestComplete(i, isError: false);
        }

        // Assert: P99 should be ~99ms
        var snapshot = collector.GetSnapshot();
        snapshot.LatencyP99.Should().BeGreaterThan(90); // Should be in highest bucket
    }

    [Fact]
    public void GetSnapshot_ShouldCalculateRequestsPerSecond()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();

        // Act: Record requests
        for (int i = 0; i < 10; i++)
        {
            collector.RecordRequestStart();
            collector.RecordRequestComplete(5.0, isError: false);
        }

        // Wait a bit to allow rate calculation
        Thread.Sleep(1100);

        // Record more requests
        for (int i = 0; i < 5; i++)
        {
            collector.RecordRequestStart();
            collector.RecordRequestComplete(5.0, isError: false);
        }

        // Assert: Should calculate req/s
        var snapshot = collector.GetSnapshot();
        snapshot.RequestsPerSecond.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Reset_ShouldClearAllMetrics()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();

        // Populate with data
        for (int i = 0; i < 10; i++)
        {
            collector.RecordRequestStart();
            collector.RecordRequestComplete(5.0, isError: i < 3);
        }
        collector.RecordConnectionChange(5);
        collector.UpdateQueueDepths(100, 200, 300);

        // Act
        collector.Reset();

        // Assert
        var snapshot = collector.GetSnapshot();
        snapshot.TotalRequests.Should().Be(0);
        snapshot.TotalErrors.Should().Be(0);
        snapshot.ActiveRequests.Should().Be(0);
        snapshot.LatencyP50.Should().Be(0);
        snapshot.LatencyP95.Should().Be(0);
    }

    [Fact]
    public void ExportPrometheus_ShouldGenerateValidFormat()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();

        // Populate with data
        for (int i = 0; i < 5; i++)
        {
            collector.RecordRequestStart();
            collector.RecordRequestComplete(10.0, isError: false);
        }

        // Act
        var prometheus = collector.ExportPrometheus();

        // Assert
        prometheus.Should().NotBeNullOrEmpty();
        prometheus.Should().Contain("pulserpc_requests_total");
        prometheus.Should().Contain("pulserpc_errors_total");
        prometheus.Should().Contain("pulserpc_requests_per_second");
        prometheus.Should().Contain("pulserpc_latency_p95_ms");
        prometheus.Should().Contain("# HELP");
        prometheus.Should().Contain("# TYPE");
    }

    [Fact]
    public void MetricsCollector_ShouldBeThreadSafe()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();
        var threads = 10;
        var requestsPerThread = 100;

        // Act: Concurrent requests from multiple threads
        var tasks = Enumerable.Range(0, threads).Select(_ => System.Threading.Tasks.Task.Run(() =>
        {
            for (int i = 0; i < requestsPerThread; i++)
            {
                collector.RecordRequestStart();
                collector.RecordRequestComplete(5.0, isError: false);
            }
        })).ToArray();

        System.Threading.Tasks.Task.WaitAll(tasks);

        // Assert: Should have exactly threads * requestsPerThread
        var snapshot = collector.GetSnapshot();
        snapshot.TotalRequests.Should().Be(threads * requestsPerThread);
    }

    [Fact]
    public void GetSnapshot_ShouldIncludeCpuAndMemoryUsage()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();

        // Act
        var snapshot = collector.GetSnapshot();

        // Assert: CPU and memory should be reported (may be 0 in test environment)
        snapshot.CpuUsagePercent.Should().BeGreaterThanOrEqualTo(0);
        snapshot.MemoryUsageMB.Should().BeGreaterThan(0);
    }

    [Fact]
    public void LatencyBuckets_ShouldCategorizeCorrectly()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();

        // Act: Record latencies in different ranges
        collector.RecordRequestStart();
        collector.RecordRequestComplete(0.5, isError: false); // <1ms

        collector.RecordRequestStart();
        collector.RecordRequestComplete(1.5, isError: false); // 1-2ms

        collector.RecordRequestStart();
        collector.RecordRequestComplete(7.0, isError: false); // 5-10ms

        collector.RecordRequestStart();
        collector.RecordRequestComplete(150.0, isError: false); // 100+ms

        // Assert: Percentiles should reflect the distribution
        var snapshot = collector.GetSnapshot();
        snapshot.LatencyP50.Should().BeGreaterThan(0);
        snapshot.LatencyP95.Should().BeGreaterThan(snapshot.LatencyP50);
    }

    [Fact]
    public void ErrorRate_ShouldBeZero_WhenNoErrors()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();

        // Act: Record successful requests only
        for (int i = 0; i < 10; i++)
        {
            collector.RecordRequestStart();
            collector.RecordRequestComplete(5.0, isError: false);
        }

        // Assert
        var snapshot = collector.GetSnapshot();
        snapshot.ErrorRate.Should().Be(0);
    }

    [Fact]
    public void ErrorRate_ShouldBeOne_WhenAllErrors()
    {
        // Arrange
        var collector = new PipelineMetricsCollector();

        // Act: Record all errors
        for (int i = 0; i < 10; i++)
        {
            collector.RecordRequestStart();
            collector.RecordRequestComplete(5.0, isError: true);
        }

        // Assert
        var snapshot = collector.GetSnapshot();
        snapshot.ErrorRate.Should().Be(1.0);
    }
}
