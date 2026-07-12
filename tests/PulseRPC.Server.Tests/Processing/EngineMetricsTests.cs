using FluentAssertions;
using PulseRPC.Server.Processing.Engine;
using Xunit;

namespace PulseRPC.Server.Tests.Processing;

public sealed class EngineMetricsTests
{
    [Fact]
    public void RecordEnqueueLatency_MustNotCorruptMessageCount()
    {
        var metrics = new EngineMetrics();
        metrics.L1MessagesEnqueued.Add(1);

        metrics.RecordEnqueueLatency(123_456);

        metrics.L1MessagesEnqueued.Value.Should().Be(1);
    }

    [Fact]
    public void BatchProcessingSamples_ProduceRealAverageAndP99()
    {
        var metrics = new EngineMetrics();

        metrics.RecordBatchProcessingTime(TimeSpan.FromMilliseconds(1));
        metrics.RecordBatchProcessingTime(TimeSpan.FromMilliseconds(3));
        metrics.RecordBatchProcessingTime(TimeSpan.FromMilliseconds(10));

        metrics.GetAverageLatencyMs().Should().BeApproximately(14d / 3d, 0.001);
        metrics.GetP99LatencyMs().Should().Be(10);
    }

    [Fact]
    public void EmptyMetrics_AreZeroInsteadOfFabricatedValues()
    {
        var metrics = new EngineMetrics();

        metrics.GetCurrentL1Utilization().Should().Be(0);
        metrics.GetAverageLatencyMs().Should().Be(0);
        metrics.GetP99LatencyMs().Should().Be(0);
    }
}
