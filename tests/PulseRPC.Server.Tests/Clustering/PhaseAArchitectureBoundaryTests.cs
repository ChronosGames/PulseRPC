using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using PulseRPC.Clustering;
using PulseRPC.Routing;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// Phase A 架构边界回归：节点原始帧传输与连接目录保持在抽象层，不绑定业务 DTO 或具体传输实现。
/// </summary>
public class PhaseAArchitectureBoundaryTests
{
    [Fact]
    public void ConnectionPlacement_UsesOrdinalValueSemantics()
    {
        var first = new ConnectionPlacement("gateway-a", "conn-1");
        var same = new ConnectionPlacement("gateway-a", "conn-1");
        var differentNode = new ConnectionPlacement("gateway-b", "conn-1");

        first.Equals(same).Should().BeTrue();
        first.GetHashCode().Should().Be(same.GetHashCode());
        first.Equals(differentNode).Should().BeFalse();
        first.ToString().Should().Contain("gateway-a").And.Contain("conn-1");
    }

    [Fact]
    public async Task NodeTransport_AbstractsOpaqueFrames_WithoutBusinessDtoDependency()
    {
        var transport = new CapturingNodeTransport(response: new byte[] { 9, 8, 7 });
        var frame = new byte[] { 1, 2, 3, 4, 5 };

        await transport.SendFrameAsync("node-b", frame);
        var response = await transport.AskFrameAsync("node-c", frame);

        transport.LastSendTarget.Should().Be("node-b");
        transport.LastAskTarget.Should().Be("node-c");
        transport.LastFrame.ToArray().Should().Equal(frame);
        response.ToArray().Should().Equal(new byte[] { 9, 8, 7 });
    }

    [Fact]
    public async Task ConnectionDirectory_SeparatesConnectionUserAndFanoutLookups()
    {
        var directory = new StubConnectionDirectory();

        var connection = await directory.FindConnectionAsync("conn-1");
        var userConnections = await directory.FindUserAsync("user-1");
        var groupConnections = await directory.FindMembersAsync(PulseAddress.Group("ChatReceiver", "room-1"));

        connection.Should().Be(new ConnectionPlacement("node-a", "conn-1"));
        userConnections.Should().Equal(new ConnectionPlacement("node-a", "conn-1"), new ConnectionPlacement("node-b", "conn-2"));
        groupConnections.Should().Equal(new ConnectionPlacement("node-b", "conn-3"));
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

    private sealed class StubConnectionDirectory : IConnectionDirectory
    {
        public ValueTask RegisterConnectionAsync(string connectionId, ConnectionPlacement placement, CancellationToken cancellationToken = default)
            => default;

        public ValueTask RemoveConnectionAsync(string connectionId, ConnectionPlacement placement, CancellationToken cancellationToken = default)
            => default;

        public ValueTask<ConnectionPlacement?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default)
            => new(new ConnectionPlacement("node-a", connectionId));

        public ValueTask<IReadOnlyList<ConnectionPlacement>> FindUserAsync(string userId, CancellationToken cancellationToken = default)
            => new((IReadOnlyList<ConnectionPlacement>)new[]
            {
                new ConnectionPlacement("node-a", "conn-1"),
                new ConnectionPlacement("node-b", "conn-2"),
            });

        public ValueTask<IReadOnlyList<ConnectionPlacement>> FindMembersAsync(PulseAddress membership, CancellationToken cancellationToken = default)
            => new((IReadOnlyList<ConnectionPlacement>)new[] { new ConnectionPlacement("node-b", "conn-3") });
    }
}
