using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Routing;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Gateway;
using Xunit;

namespace PulseRPC.Server.Tests.Gateway;

/// <summary>
/// 回归测试：<see cref="GatewayFrontHub"/>（§P5 §6.3）—— 把外部客户端对 <c>Actor(hub,key)</c> 的调用
/// 经 <see cref="IPulseRouter"/> 中转，并在中转期间建立 <see cref="GatewayRelayContext"/> 作用域
/// （供 <c>ClusterPulseRouter</c> 转发到远端节点时携带发起网关/回执连接标识），HopLimit 耗尽时拒绝转发。
/// </summary>
public class GatewayFrontHubTests
{
    private static GatewayFrontHub CreateHub(out IPulseRouter router, string localNodeId = "gateway-1")
    {
        router = Substitute.For<IPulseRouter>();
        var topology = Options.Create(new ClusterTopologyOptions { LocalNodeId = localNodeId });
        return new GatewayFrontHub(router, topology, NullLogger<GatewayFrontHub>.Instance);
    }

    [Fact]
    public async Task RelayAskAsync_ForwardsToRouter_WithActorAddress_AndReturnsItsResult()
    {
        var hub = CreateHub(out var router);
        var expected = new byte[] { 1, 2, 3 };
        router.AskAsync(PulseAddress.Actor("RoomHub", "room-1"), 0x1234, Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ReadOnlyMemory<byte>>(expected));

        var result = await hub.RelayAskAsync("RoomHub", "room-1", 0x1234, new byte[] { 9 }, hopLimit: 4);

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task RelaySendAsync_ForwardsToRouter_WithActorAddress()
    {
        var hub = CreateHub(out var router);

        await hub.RelaySendAsync("RoomHub", "room-2", 0x5678, new byte[] { 7 }, hopLimit: 4);

        await router.Received(1).SendAsync(
            PulseAddress.Actor("RoomHub", "room-2"), 0x5678, Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<DeliveryMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RelayAskAsync_WithZeroHopLimit_MustThrowAndNeverCallRouter()
    {
        var hub = CreateHub(out var router);

        var act = async () => await hub.RelayAskAsync("RoomHub", "room-1", 0x1234, Array.Empty<byte>(), hopLimit: 0);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await router.DidNotReceiveWithAnyArgs().AskAsync(default, default, default, default);
    }

    [Fact]
    public async Task RelayAskAsync_EstablishesGatewayRelayContextScope_WithLocalNodeIdAndCurrentClientConnectionId()
    {
        var hub = CreateHub(out var router, localNodeId: "gateway-42");
        GatewayRelayInfo? captured = null;
        router.AskAsync(Arg.Any<PulseAddress>(), Arg.Any<ushort>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                captured = GatewayRelayContext.Current;
                return new ValueTask<ReadOnlyMemory<byte>>(Array.Empty<byte>());
            });

        using (PulseContext.SetContext(PulseContextData.CreateUserContext("user-1", connectionId: "client-conn-9")))
        {
            await hub.RelayAskAsync("RoomHub", "room-1", 0x1111, Array.Empty<byte>(), hopLimit: 4);
        }

        captured.Should().NotBeNull();
        captured!.Value.GatewayNodeId.Should().Be("gateway-42");
        captured!.Value.ClientConnectionId.Should().Be("client-conn-9");

        // 调用结束后作用域必须已释放，不残留到之后的环境上下文中。
        GatewayRelayContext.Current.Should().BeNull();
    }
}
