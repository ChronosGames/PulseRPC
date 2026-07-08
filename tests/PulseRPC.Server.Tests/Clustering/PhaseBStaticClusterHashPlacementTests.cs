using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using PulseRPC;
using PulseRPC.Clustering;
using PulseRPC.Messaging;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Extensions;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// Phase B 回归：静态成员 + hash placement + 基于 INodeTransport 的最小节点链路。
/// </summary>
public class PhaseBStaticClusterHashPlacementTests
{
    [Fact]
    public void HashPlacementStrategy_MapsHubAndKeyToStableOwnerAcrossRingInstances()
    {
        var nodes = new[] { "node-a", "node-b", "node-c" };
        var strategyA = new HashPlacementStrategy(new NodeConsistentHashRing(nodes));
        var strategyB = new HashPlacementStrategy(new NodeConsistentHashRing(new[] { "node-c", "node-a", "node-b" }));

        var ownerA = strategyA.SelectOwner("RoomHub", "room-42");
        var ownerB = strategyB.SelectOwner("RoomHub", "room-42");

        ownerA.Should().Be(ownerB);
        nodes.Should().Contain(ownerA);
        HashPlacementStrategy.BuildIdentity("RoomHub", "room-42").Should().Be("RoomHub:room-42");
    }

    [Fact]
    public async Task TransportBackedNodeLink_AskActorAsync_FramesActorRequestAndReturnsTransportResponse()
    {
        var response = MemoryPackSerializer.Serialize("ok");
        var transport = new CapturingNodeTransport(response);
        var link = new TransportBackedNodeLink(transport);
        var body = MemoryPackSerializer.Serialize("hello");

        var result = await link.AskActorAsync(
            "node-b", "RoomHub", "room-1", 0x1234, body,
            sourceNodeId: "gateway-a", replyTo: "conn-9");

        result.ToArray().Should().Equal(response);
        transport.LastAskTarget.Should().Be("node-b");
        EnvelopeRelay.TryReadHeader(transport.LastFrame, out var header, out var readBody).Should().BeTrue();
        header.Type.Should().Be(MessageType.Request);
        header.Hub.Should().Be("RoomHub");
        header.Key.Should().Be("room-1");
        header.MethodId.Should().Be(0x1234);
        header.Flags.Should().Be(MessageFlags.RequireResponse);
        header.SourceNodeId.Should().Be("gateway-a");
        header.ReplyTo.Should().Be("conn-9");
        header.MessageId.Should().NotBe(Guid.Empty);
        readBody.ToArray().Should().Equal(body);
    }

    [Fact]
    public async Task TransportBackedNodeLink_SendActorAsync_PreservesMessageIdInOneWayFrame()
    {
        var transport = new CapturingNodeTransport(Array.Empty<byte>());
        var link = new TransportBackedNodeLink(transport);
        var messageId = Guid.NewGuid();
        var body = new byte[] { 1, 2, 3 };

        await link.SendActorAsync("node-b", "RoomHub", "room-1", 0x4321, body, messageId: messageId);

        transport.LastSendTarget.Should().Be("node-b");
        EnvelopeRelay.TryReadHeader(transport.LastFrame, out var header, out var readBody).Should().BeTrue();
        header.Type.Should().Be(MessageType.OneWay);
        header.MessageId.Should().Be(messageId);
        header.Hub.Should().Be("RoomHub");
        header.Key.Should().Be("room-1");
        header.MethodId.Should().Be(0x4321);
        readBody.ToArray().Should().Equal(body);
    }

    [Fact]
    public void AddPulseClustering_WhenNodeTransportRegistered_UsesTransportBackedNodeLink()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<INodeTransport>(new CapturingNodeTransport(Array.Empty<byte>()));

        services.AddPulseClustering(
            topology =>
            {
                topology.LocalNodeId = "node-a";
                topology.Members.Add(new ClusterNodeEndpoint { NodeId = "node-a", Host = "127.0.0.1", Port = 5001 });
                topology.Members.Add(new ClusterNodeEndpoint { NodeId = "node-b", Host = "127.0.0.1", Port = 5002 });
            },
            auth => auth.SharedSecret = "secret");

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<INodeLink>().Should().BeOfType<TransportBackedNodeLink>();
        provider.GetRequiredService<IActorPlacementStrategy>().Should().BeOfType<HashPlacementStrategy>();
    }

    private sealed class CapturingNodeTransport : INodeTransport
    {
        private readonly ReadOnlyMemory<byte> _response;

        public CapturingNodeTransport(ReadOnlyMemory<byte> response) => _response = response;

        public string LastSendTarget { get; private set; } = string.Empty;

        public string LastAskTarget { get; private set; } = string.Empty;

        public ReadOnlyMemory<byte> LastFrame { get; private set; }

        public ValueTask SendFrameAsync(string targetNodeId, ReadOnlyMemory<byte> framedPacket, CancellationToken cancellationToken = default)
        {
            LastSendTarget = targetNodeId;
            LastFrame = framedPacket;
            return default;
        }

        public ValueTask<ReadOnlyMemory<byte>> AskFrameAsync(string targetNodeId, ReadOnlyMemory<byte> framedPacket, CancellationToken cancellationToken = default)
        {
            LastAskTarget = targetNodeId;
            LastFrame = framedPacket;
            return new ValueTask<ReadOnlyMemory<byte>>(_response);
        }
    }
}
