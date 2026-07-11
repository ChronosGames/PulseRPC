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
        var transport = new CapturingNodeTransport(MemoryPackSerializer.Serialize(response));
        var link = new TransportBackedNodeLink(transport);
        var body = MemoryPackSerializer.Serialize("hello");

        var result = await link.AskActorAsync(
            "node-b", "RoomHub", "room-1", 0x1234, body,
            sourceNodeId: "gateway-a", replyTo: "conn-9");

        result.ToArray().Should().Equal(response);
        transport.LastAskTarget.Should().Be("node-b");
        EnvelopeRelay.TryReadHeader(transport.LastFrame, out var header, out var readBody).Should().BeTrue();
        header.Type.Should().Be(MessageType.Request);
        header.Hub.Should().Be(NodeWireProtocol.ClusterInternalHubName);
        header.Key.Should().BeEmpty();
        header.MethodId.Should().Be(0xFD7F);
        header.Flags.Should().Be(MessageFlags.RequireResponse);
        header.MessageId.Should().NotBe(Guid.Empty);

        var request = MemoryPackSerializer.Deserialize<(string, string, ushort, byte[], string, string)>(readBody.Span);
        request.Item1.Should().Be("RoomHub");
        request.Item2.Should().Be("room-1");
        request.Item3.Should().Be(0x1234);
        request.Item4.Should().Equal(body);
        request.Item5.Should().Be("gateway-a");
        request.Item6.Should().Be("conn-9");
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
        header.MessageId.Should().NotBe(Guid.Empty);
        header.Hub.Should().Be(NodeWireProtocol.ClusterInternalHubName);
        header.Key.Should().BeEmpty();
        header.MethodId.Should().Be(0x33A0);

        var command = MemoryPackSerializer.Deserialize<(string, string, ushort, byte[], string, string, Guid)>(readBody.Span);
        command.Item1.Should().Be("RoomHub");
        command.Item2.Should().Be("room-1");
        command.Item3.Should().Be(0x4321);
        command.Item4.Should().Equal(body);
        command.Item7.Should().Be(messageId);
    }

    [Fact]
    public async Task TransportBackedNodeLink_SendToConnectionAsync_UsesGatewayRelayProtocol()
    {
        var transport = new CapturingNodeTransport(Array.Empty<byte>());
        var link = new TransportBackedNodeLink(transport);
        var clientFrame = new byte[] { 8, 7, 6 };

        await link.SendToConnectionAsync("gateway-a", "conn-9", clientFrame);

        EnvelopeRelay.TryReadHeader(transport.LastFrame, out var header, out var body).Should().BeTrue();
        header.Type.Should().Be(MessageType.OneWay);
        header.Hub.Should().Be(NodeWireProtocol.GatewayRelayHubName);
        header.MethodId.Should().Be(PulseRPC.Gateway.GatewayProtocolIds.RelayPushFrame);

        var command = MemoryPackSerializer.Deserialize<(string, byte[])>(body.Span);
        command.Item1.Should().Be("conn-9");
        command.Item2.Should().Equal(clientFrame);
    }

    [Fact]
    public async Task TransportBackedNodeLink_AskConnectionAsync_UsesGatewayRelayProtocolAndUnwrapsResponse()
    {
        var clientResponse = new byte[] { 4, 5, 6 };
        var transport = new CapturingNodeTransport(MemoryPackSerializer.Serialize(clientResponse));
        var link = new TransportBackedNodeLink(transport);

        var result = await link.AskConnectionAsync(
            "gateway-a", "conn-9", 0x2345, new byte[] { 1, 2 }, TimeSpan.FromSeconds(3));

        result.ToArray().Should().Equal(clientResponse);
        EnvelopeRelay.TryReadHeader(transport.LastFrame, out var header, out var body).Should().BeTrue();
        header.Type.Should().Be(MessageType.Request);
        header.Hub.Should().Be(NodeWireProtocol.GatewayRelayHubName);
        header.MethodId.Should().Be(PulseRPC.Gateway.GatewayProtocolIds.RelayAskConnection);

        var request = MemoryPackSerializer.Deserialize<(string, ushort, byte[], int)>(body.Span);
        request.Item1.Should().Be("conn-9");
        request.Item2.Should().Be(0x2345);
        request.Item3.Should().Equal(new byte[] { 1, 2 });
        request.Item4.Should().Be(3000);
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
