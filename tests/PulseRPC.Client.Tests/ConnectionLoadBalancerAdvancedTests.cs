using System.Collections.Concurrent;
using Moq;
using PulseRPC.Client.Configuration;
using PulseRPC.Messaging;
using Xunit;

namespace PulseRPC.Client.Tests;

public sealed class ConnectionLoadBalancerAdvancedTests
{
    [Fact]
    public void WeightedRoundRobinMustFollowConfiguredWeights()
    {
        var connections = new[]
        {
            CreateChannel("a", 5),
            CreateChannel("b", 1)
        };
        var loadBalancer = new ConnectionLoadBalancer(LoadBalancingStrategy.WeightedRoundRobin);

        var selected = Enumerable.Range(0, 60)
            .Select(_ => loadBalancer.SelectConnection(connections)!.Id)
            .GroupBy(id => id)
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.Equal(50, selected["a"]);
        Assert.Equal(10, selected["b"]);
    }

    [Fact]
    public void WeightedRoundRobinMustReadDynamicProviderOnEverySelection()
    {
        var provider = new MutableWeightProvider(("a", 1), ("b", 1));
        var options = new ConnectionLoadBalancingOptions { WeightProvider = provider };
        var loadBalancer = new ConnectionLoadBalancer(
            LoadBalancingStrategy.WeightedRoundRobin,
            options,
            logger: null);
        var connections = new[] { CreateChannel("a"), CreateChannel("b") };

        var equal = SelectCounts(loadBalancer, connections, 20);
        provider.Set("b", 3);
        var updated = SelectCounts(loadBalancer, connections, 40);

        Assert.Equal(10, equal["a"]);
        Assert.Equal(10, equal["b"]);
        Assert.Equal(10, updated["a"]);
        Assert.Equal(30, updated["b"]);
    }

    [Fact]
    public void WeightedRoundRobinMustRemainExactUnderConcurrency()
    {
        var connections = new[] { CreateChannel("a", 5), CreateChannel("b", 1) };
        var loadBalancer = new ConnectionLoadBalancer(LoadBalancingStrategy.WeightedRoundRobin);
        var counts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        Parallel.For(0, 6_000, _ =>
        {
            var selected = loadBalancer.SelectConnection(connections)!;
            counts.AddOrUpdate(selected.Id, 1, (_, count) => count + 1);
        });

        Assert.Equal(5_000, counts["a"]);
        Assert.Equal(1_000, counts["b"]);
    }

    [Fact]
    public void WeightedRoundRobinMustRejectNonPositiveDynamicWeight()
    {
        var provider = new MutableWeightProvider(("a", 0));
        var loadBalancer = new ConnectionLoadBalancer(
            LoadBalancingStrategy.WeightedRoundRobin,
            new ConnectionLoadBalancingOptions { WeightProvider = provider },
            logger: null);

        var action = () => loadBalancer.SelectConnection(new[] { CreateChannel("a") });

        var exception = Assert.Throws<InvalidOperationException>(action);
        Assert.Contains("positive", exception.Message);
    }

    [Fact]
    public void ConsistentHashMustMapSameKeyIndependentlyOfInputOrder()
    {
        var firstOrder = new[] { CreateChannel("a"), CreateChannel("b"), CreateChannel("c") };
        var reverseOrder = firstOrder.Reverse().ToArray();
        var first = new ConnectionLoadBalancer(LoadBalancingStrategy.ConsistentHash);
        var second = new ConnectionLoadBalancer(LoadBalancingStrategy.ConsistentHash);
        var context = new LoadBalancingContext(stickyKey: "tenant-42");

        var expected = ((IContextualLoadBalancer)first).SelectConnection(firstOrder, context);
        for (var index = 0; index < 20; index++)
        {
            var actual = ((IContextualLoadBalancer)second).SelectConnection(reverseOrder, context);
            Assert.Equal(expected!.Id, actual!.Id);
        }
    }

    [Fact]
    public void ConsistentHashAddingConnectionMustOnlyMoveKeysToNewConnection()
    {
        var beforeConnections = new[] { CreateChannel("a"), CreateChannel("b"), CreateChannel("c") };
        var afterConnections = beforeConnections.Append(CreateChannel("d")).ToArray();
        var before = new ConnectionLoadBalancer(LoadBalancingStrategy.ConsistentHash);
        var after = new ConnectionLoadBalancer(LoadBalancingStrategy.ConsistentHash);
        var movedToNewConnection = 0;

        for (var index = 0; index < 500; index++)
        {
            var context = new LoadBalancingContext(stickyKey: $"tenant-{index}");
            var oldSelection = ((IContextualLoadBalancer)before).SelectConnection(beforeConnections, context)!;
            var newSelection = ((IContextualLoadBalancer)after).SelectConnection(afterConnections, context)!;
            if (newSelection.Id == "d")
            {
                movedToNewConnection++;
            }
            else
            {
                Assert.Equal(oldSelection.Id, newSelection.Id);
            }
        }

        Assert.True(movedToNewConnection > 0);
    }

    [Fact]
    public void ConsistentHashMustFailWithoutStableStickyKey()
    {
        var loadBalancer = new ConnectionLoadBalancer(LoadBalancingStrategy.ConsistentHash);

        var action = () => loadBalancer.SelectConnection(new[] { CreateChannel("a") });

        var exception = Assert.Throws<InvalidOperationException>(action);
        Assert.Contains("StickyKey", exception.Message);
    }

    [Fact]
    public void ConsistentHashMustRejectDuplicateConnectionIds()
    {
        var loadBalancer = new ConnectionLoadBalancer(LoadBalancingStrategy.ConsistentHash);
        var connections = new[] { CreateChannel("a"), CreateChannel("a") };

        var action = () => ((IContextualLoadBalancer)loadBalancer).SelectConnection(
            connections,
            new LoadBalancingContext(stickyKey: "user-7"));

        var exception = Assert.Throws<InvalidOperationException>(action);
        Assert.Contains("duplicate", exception.Message);
    }

    [Fact]
    public void ConsistentHashMustExcludeUnhealthyConnections()
    {
        var loadBalancer = new ConnectionLoadBalancer(LoadBalancingStrategy.ConsistentHash);
        var connections = new[]
        {
            CreateChannel("healthy"),
            CreateChannel("failed", state: ExtendedConnectionState.Failed)
        };

        for (var index = 0; index < 50; index++)
        {
            var selected = ((IContextualLoadBalancer)loadBalancer).SelectConnection(
                connections,
                new LoadBalancingContext(stickyKey: $"user-{index}"));
            Assert.Equal("healthy", selected!.Id);
        }
    }

    [Fact]
    public async Task RouteOptionsMustForwardStickyKeyToContextualManager()
    {
        var expected = CreateChannel("a");
        var manager = new Mock<IContextualConnectionManager>();
        manager
            .Setup(item => item.RouteAsync(
                "IChatHub",
                It.Is<LoadBalancingContext>(context => context.StickyKey == "user-7"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var actual = await manager.Object.RouteWithOptionsAsync(
            "IChatHub",
            new ServiceProxyOptions
            {
                LoadBalancingHint = LoadBalancingHint.Sticky,
                StickyKey = "user-7"
            });

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task RouteOptionsMustFailWhenCustomManagerCannotPreserveContext()
    {
        var manager = new Mock<IConnectionManager>();

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await manager.Object.RouteWithOptionsAsync(
                "IChatHub",
                new ServiceProxyOptions { StickyKey = "user-7" }));
    }

    private static Dictionary<string, int> SelectCounts(
        ConnectionLoadBalancer loadBalancer,
        IReadOnlyList<IClientChannel> connections,
        int count)
    {
        return Enumerable.Range(0, count)
            .Select(_ => loadBalancer.SelectConnection(connections)!.Id)
            .GroupBy(id => id)
            .ToDictionary(group => group.Key, group => group.Count());
    }

    private static IClientChannel CreateChannel(
        string id,
        int weight = 1,
        ExtendedConnectionState state = ExtendedConnectionState.Connected)
    {
        var descriptor = new ConnectionDescriptor
        {
            Id = id,
            Name = id,
            Weight = weight
        };
        var channel = new Mock<IClientChannel>();
        channel.SetupGet(item => item.Id).Returns(id);
        channel.SetupGet(item => item.State).Returns(state);
        channel.SetupGet(item => item.Descriptor).Returns(descriptor);
        channel.SetupGet(item => item.Statistics).Returns(new ConnectionStatistics { ConnectionId = id });
        return channel.Object;
    }

    private sealed class MutableWeightProvider : IConnectionWeightProvider
    {
        private readonly ConcurrentDictionary<string, int> _weights;

        public MutableWeightProvider(params (string Id, int Weight)[] weights)
        {
            _weights = new ConcurrentDictionary<string, int>(
                weights.ToDictionary(item => item.Id, item => item.Weight),
                StringComparer.Ordinal);
        }

        public int GetWeight(IClientChannel connection) => _weights[connection.Id];

        public void Set(string connectionId, int weight) => _weights[connectionId] = weight;
    }
}
