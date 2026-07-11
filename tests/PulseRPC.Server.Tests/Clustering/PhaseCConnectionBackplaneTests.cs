using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PulseRPC.Clustering;
using PulseRPC.Routing;
using PulseRPC.Server.Clustering;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// Phase C 回归：连接目录经 Backplane 维护 Gateway 虚拟连接和 User/Group 成员解析。
/// </summary>
public class PhaseCConnectionBackplaneTests
{
    [Fact]
    public async Task RegisterConnectionAsync_MustStoreVirtualConnectionLookupInBackplane()
    {
        var backplane = Substitute.For<IPulseBackplane>();
        var directory = new BackplaneConnectionDirectory(backplane);
        var virtualId = "gateway-1:client-conn-9";
        var placement = new ConnectionPlacement("gateway-1", "client-conn-9");

        await directory.RegisterConnectionAsync(virtualId, placement);

        await backplane.Received(1).AddMemberAsync(
            "client-conn-9",
            PulseAddress.Connection(string.Empty, virtualId),
            "gateway-1",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindConnectionAsync_MustReturnOriginalConnectionPlacementFromBackplane()
    {
        var backplane = Substitute.For<IPulseBackplane>();
        var virtualId = "gateway-1:client-conn-9";
        backplane.ResolveAsync(PulseAddress.Connection(string.Empty, virtualId), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<BackplaneMember>>((IReadOnlyList<BackplaneMember>)new[] { new BackplaneMember("gateway-1", "client-conn-9") }));
        var directory = new BackplaneConnectionDirectory(backplane);

        var placement = await directory.FindConnectionAsync(virtualId);

        placement.Should().Be(new ConnectionPlacement("gateway-1", "client-conn-9"));
    }

    [Fact]
    public async Task FindMembersAsync_ForGroup_MustProjectBackplaneMembersToConnectionPlacements()
    {
        var backplane = Substitute.For<IPulseBackplane>();
        var group = PulseAddress.Group("ChatReceiver", "room-1");
        backplane.ResolveAsync(group, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<BackplaneMember>>((IReadOnlyList<BackplaneMember>)new[]
            {
                new BackplaneMember("node-a", "conn-1"),
                new BackplaneMember("node-b", "conn-2"),
            }));
        var directory = new BackplaneConnectionDirectory(backplane);

        var placements = await directory.FindMembersAsync(group);

        placements.Should().Equal(new ConnectionPlacement("node-a", "conn-1"), new ConnectionPlacement("node-b", "conn-2"));
    }
}
