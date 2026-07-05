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
    public async Task AddPulseClustering_DefaultNodeLink_MustFailExplicitlyUntilTransportIsRegistered()
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
        var nodeLink = provider.GetRequiredService<INodeLink>();

        var act = async () => await nodeLink.SendActorAsync(
            "node-b",
            "RoomHub",
            "room-1",
            0x1234,
            ReadOnlyMemory<byte>.Empty);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*INodeLink*");
    }
}
