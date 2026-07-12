using FluentAssertions;
using PulseRPC.Diagnostics;
using Xunit;

namespace PulseRPC.Server.Tests.Extensions;

public sealed class LatencyHistogramTests
{
    [Fact]
    public void Merge_MustPreserveCountSumBoundsAndPercentiles()
    {
        var left = new LatencyHistogram();
        var right = new LatencyHistogram();
        for (var index = 1; index <= 1_000; index++)
            left.Record(TimeSpan.FromMilliseconds(index));
        for (var index = 1_001; index <= 2_000; index++)
            right.Record(TimeSpan.FromMilliseconds(index));

        left.Merge(right.GetSnapshot());
        var snapshot = left.GetSnapshot();

        snapshot.Count.Should().Be(2_000);
        snapshot.MinTicks.Should().Be(TimeSpan.FromMilliseconds(1).Ticks);
        snapshot.MaxTicks.Should().Be(TimeSpan.FromMilliseconds(2_000).Ticks);
        snapshot.GetPercentileMilliseconds(0.50).Should().BeInRange(950, 1_050);
        snapshot.GetPercentileMilliseconds(0.99).Should().BeInRange(1_950, 2_050);
    }

    [Fact]
    public void RuntimeQueueRegistration_MustTrackHighWaterSaturationWaitAndReject()
    {
        var depth = 0;
        using var registration = RuntimeQueueMetrics.Register("queue-test", "one", 2, () => depth);
        depth = 2;
        registration.Observe();
        registration.RecordEnqueueWait(TimeSpan.FromMilliseconds(3));
        registration.RecordRejectedEnqueue();
        depth = 1;
        registration.Observe();

        var snapshot = registration.GetSnapshot();
        snapshot.Capacity.Should().Be(2);
        snapshot.Depth.Should().Be(1);
        snapshot.HighWatermark.Should().Be(2);
        snapshot.SaturationEvents.Should().Be(1);
        snapshot.EnqueueWaitCount.Should().Be(1);
        snapshot.EnqueueWaitDuration.Should().Be(TimeSpan.FromMilliseconds(3));
        snapshot.RejectedEnqueues.Should().Be(1);
    }

    [Fact]
    public void OverflowBucket_MustNotClampReportedPercentileToTenMinutes()
    {
        var histogram = new LatencyHistogram();
        histogram.Record(TimeSpan.FromMinutes(20));

        histogram.GetSnapshot().GetPercentileMilliseconds(0.99)
            .Should().Be(TimeSpan.FromMinutes(20).TotalMilliseconds);
    }
}
