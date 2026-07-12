using System.Text.Json;
using FluentAssertions;
using PulseRPC.Server.Extensions;
using PulseRPC.Diagnostics;
using Xunit;

namespace PulseRPC.Server.Tests.Extensions;

public sealed class DiagnosticEndpointsTests
{
    [Fact]
    public void QueueStats_MustUseRegisteredRuntimeQueueCapacityAndDepth()
    {
        var depth = 4;
        using var registration = RuntimeQueueMetrics.Register(
            "diagnostic-test", "instance", 8, () => depth);
        registration.Observe();
        var endpoints = new DiagnosticEndpoints(new PipelineMetricsCollector());

        using var document = JsonDocument.Parse(endpoints.GetQueueStats());
        var queue = document.RootElement.GetProperty("queues")
            .EnumerateArray()
            .Single(item => item.GetProperty("QueueName").GetString() == "diagnostic-test");

        queue.GetProperty("Capacity").GetInt32().Should().Be(8);
        queue.GetProperty("Depth").GetInt32().Should().Be(4);
        queue.GetProperty("Saturation").GetDouble().Should().Be(0.5);
    }
}
