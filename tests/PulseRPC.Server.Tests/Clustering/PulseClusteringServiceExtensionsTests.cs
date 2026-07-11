using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Clustering;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Extensions;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

public class PulseClusteringServiceExtensionsTests
{
    [Fact]
    public void AddPulseClustering_DefaultNodeLink_MustUseBuiltInTcpNodeTransport()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPulseClustering(
            topology =>
            {
                topology.LocalNodeId = "node-a";
                topology.Members.Add(new ClusterNodeEndpoint
                {
                    NodeId = "node-a",
                    Host = "127.0.0.1",
                    Port = 18080
                });
            },
            auth => auth.SharedSecret = "test-secret");
        services.Configure<TcpNodeTransportOptions>(options =>
            options.SecurityMode = NodeTransportSecurityMode.InsecureDevelopment);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<INodeTransport>().Should().BeOfType<TcpNodeTransport>();
        provider.GetRequiredService<INodeLink>().Should().BeOfType<TransportBackedNodeLink>();
    }

    [Fact]
    public void AddPulseClustering_WithoutExplicitTransportProtection_MustFailClosed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPulseClustering(
            topology =>
            {
                topology.LocalNodeId = "node-a";
                topology.Members.Add(new ClusterNodeEndpoint
                {
                    NodeId = "node-a",
                    Host = "127.0.0.1",
                    Port = 18080
                });
            },
            auth => auth.SharedSecret = "test-secret");

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<INodeTransport>();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SecurityMode*");
    }

    [Fact]
    public void AddPulseClustering_MultiNodeWithInMemoryLeaseStore_MustFailClosedOnDirectoryResolution()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPulseClustering(
            topology =>
            {
                topology.LocalNodeId = "node-a";
                topology.Members.Add(new ClusterNodeEndpoint { NodeId = "node-a", Host = "127.0.0.1", Port = 18080 });
                topology.Members.Add(new ClusterNodeEndpoint { NodeId = "node-b", Host = "127.0.0.1", Port = 18081 });
            },
            auth => auth.SharedSecret = "test-secret");

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IActorDirectory>();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*InMemoryActorLeaseStore*");
    }

    [Fact]
    public void AddPulseClustering_HeartbeatNotShorterThanLease_MustFailClosed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPulseClustering(
            topology =>
            {
                topology.LocalNodeId = "node-a";
                topology.Members.Add(new ClusterNodeEndpoint
                {
                    NodeId = "node-a",
                    Host = "127.0.0.1",
                    Port = 18080
                });
            },
            auth => auth.SharedSecret = "test-secret",
            lease => lease.LeaseDuration = TimeSpan.FromSeconds(5));
        services.Configure<ActorLeaseHeartbeatOptions>(heartbeat =>
            heartbeat.Interval = TimeSpan.FromSeconds(5));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IActorLeaseHeartbeat>();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Interval*LeaseDuration*");
    }
}
