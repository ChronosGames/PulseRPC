using FluentAssertions;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Clustering;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// P8：<see cref="StaticNodeEndpointResolver"/> 从 <see cref="ClusterTopologyOptions.Members"/> 解析节点端点。
/// </summary>
public class StaticNodeEndpointResolverTests
{
    private static StaticNodeEndpointResolver Create()
    {
        var topology = new ClusterTopologyOptions
        {
            LocalNodeId = "node-a",
            Members =
            {
                new ClusterNodeEndpoint { NodeId = "node-a", Host = "10.0.0.1", Port = 5000 },
                new ClusterNodeEndpoint { NodeId = "node-b", Host = "10.0.0.2", Port = 5001 },
            },
        };
        return new StaticNodeEndpointResolver(Options.Create(topology));
    }

    [Fact]
    public void TryResolve_KnownNode_ReturnsEndpoint()
    {
        var resolver = Create();

        resolver.TryResolve("node-b", out var endpoint).Should().BeTrue();
        endpoint.Host.Should().Be("10.0.0.2");
        endpoint.Port.Should().Be(5001);
    }

    [Fact]
    public void TryResolve_UnknownNode_ReturnsFalse()
    {
        var resolver = Create();

        resolver.TryResolve("node-zzz", out _).Should().BeFalse();
    }
}
