using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Options;
using PulseRPC.Routing;
using PulseRPC.Server.Clustering;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// Phase E 回归：高级 placement 策略与诊断扩展点。
/// </summary>
public class PhaseEAdvancedPlacementDiagnosticsTests
{
    [Fact]
    public void LocalAffinityPlacementStrategy_MustPreferLocalNodeWhenLive()
    {
        var strategy = new LocalAffinityPlacementStrategy("node-local", new NodeConsistentHashRing(new[] { "node-local", "node-b" }));

        strategy.SelectOwner("RoomHub", "room-1").Should().Be("node-local");
    }

    [Fact]
    public void LocalAffinityPlacementStrategy_MustFallbackToHashWhenLocalNodeIsNotLive()
    {
        var ring = new NodeConsistentHashRing(new[] { "node-a", "node-b" });
        var strategy = new LocalAffinityPlacementStrategy("node-local", ring);

        strategy.SelectOwner("RoomHub", "room-1").Should().Be(ring.GetOwner(HashPlacementStrategy.BuildIdentity("RoomHub", "room-1")));
    }

    [Fact]
    public void LeastLoadedPlacementStrategy_MustChooseLowestLoadNode()
    {
        var metrics = new FixedLoadMetrics(new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["node-a"] = 3,
            ["node-b"] = 1,
            ["node-c"] = 2,
        });
        var strategy = new LeastLoadedPlacementStrategy(new NodeConsistentHashRing(new[] { "node-a", "node-b", "node-c" }), metrics);

        strategy.SelectOwner("RoomHub", "room-1").Should().Be("node-b");
    }

    [Fact]
    public void PinnedPlacementStrategy_MustPreferExactPinThenHubPinThenHashFallback()
    {
        var ring = new NodeConsistentHashRing(new[] { "node-a", "node-b", "node-c" });
        var options = new PinnedPlacementOptions();
        options.ActorPins[HashPlacementStrategy.BuildIdentity("RoomHub", "room-1")] = "node-c";
        options.HubPins["RoomHub"] = "node-b";
        var strategy = new PinnedPlacementStrategy(ring, Options.Create(options));

        strategy.SelectOwner("RoomHub", "room-1").Should().Be("node-c");
        strategy.SelectOwner("RoomHub", "room-2").Should().Be("node-b");
        strategy.SelectOwner("OtherHub", "room-1").Should().Be(ring.GetOwner(HashPlacementStrategy.BuildIdentity("OtherHub", "room-1")));
    }

    [Fact]
    public void NoopClusterDiagnostics_MustAcceptTracingMetricsAndDeadLetterSignals()
    {
        var diagnostics = new NoopClusterDiagnostics();

        diagnostics.RecordPlacementDecision("RoomHub", "room-1", "node-a", nameof(HashPlacementStrategy));
        diagnostics.RecordDeadLetter(PulseAddress.Actor("RoomHub", "room-1"), 0x1234, "no owner");
        diagnostics.RecordSlowMailbox("RoomHub", "room-1", TimeSpan.FromSeconds(2));
    }

    private sealed class FixedLoadMetrics : IClusterLoadMetrics
    {
        private readonly IReadOnlyDictionary<string, double> _loads;

        public FixedLoadMetrics(IReadOnlyDictionary<string, double> loads) => _loads = loads;

        public double GetLoad(string nodeId) => _loads.TryGetValue(nodeId, out var load) ? load : 0;
    }
}
