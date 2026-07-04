using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PulseRPC.Clustering;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Gateway;
using PulseRPC.Server.Security;
using PulseRPC.Server.Transport;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// 回归测试：<see cref="ClusterInternalHub"/>（§P4/§P5 §10 多跳回执）——
/// 未通过节点互信鉴权时必须拒绝转发；携带 <c>(sourceNodeId, replyTo)</c> 的网关中转调用必须
/// 惰性注册虚拟连接并在被调 Actor 执行期间把 <see cref="PulseContext.CurrentConnectionId"/>
/// 临时切换为该虚拟连接标识（调用结束后必须恢复），从而支持后续经 <c>Clients.Client(...)</c> 多跳回执。
/// </summary>
public class ClusterInternalHubTests
{
    private static (ClusterInternalHub Hub, IServerChannelManager ChannelManager, IServiceRoutingTable RoutingTable) CreateHub()
    {
        var authenticator = Substitute.For<INodeAuthenticator>();
        var channelManager = Substitute.For<IServerChannelManager>();
        var nodeLink = Substitute.For<INodeLink>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        var routingTable = Substitute.For<IServiceRoutingTable>();

        var hub = new ClusterInternalHub(
            authenticator, channelManager, nodeLink, serviceProvider,
            NullLogger<ClusterInternalHub>.Instance, routingTable);

        return (hub, channelManager, routingTable);
    }

    private static IDisposable EnterNodeConnectionScope()
    {
        var authContext = new AuthenticationContext("peer-conn-1");
        authContext.SetServiceAuthentication("node-backend-1", "node-backend-1", token: NodeConnectionGate.NodeConnectionScope,
            scopes: new[] { NodeConnectionGate.NodeConnectionScope });
        // FromAuthenticationContext 本身不会回填 ConnectionId（真实管线中由 transport 提供），测试里显式补上，
        // 以验证虚拟连接切换前后 CurrentConnectionId 的往返。
        return PulseContext.SetContext(PulseContextData.FromAuthenticationContext(authContext) with { ConnectionId = "peer-conn-1" });
    }

    [Fact]
    public async Task SendActorAsync_WithoutNodeConnectionAuthentication_MustThrowUnauthorized()
    {
        var (hub, channelManager, _) = CreateHub();

        var act = async () => await hub.SendActorAsync("RoomHub", "room-1", 0x1111, Array.Empty<byte>());

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        channelManager.DidNotReceiveWithAnyArgs().GetOrRegisterVirtualChannel(default!, default!);
    }

    [Fact]
    public async Task SendActorAsync_WithSourceNodeIdAndReplyTo_MustRegisterVirtualChannel_AndSwitchCurrentConnectionIdDuringRouting()
    {
        var (hub, channelManager, routingTable) = CreateHub();
        var virtualChannel = Substitute.For<IServerChannel>();
        channelManager.GetOrRegisterVirtualChannel(Arg.Any<string>(), Arg.Any<Func<string, IServerChannel>>())
            .Returns(virtualChannel);

        string? connectionIdDuringRouting = null;
        routingTable.RouteByProtocolIdAsync(Arg.Any<IServiceProvider>(), 0x2222, "room-9", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                connectionIdDuringRouting = PulseContext.CurrentConnectionId;
                return new ValueTask<object?>((object?)null);
            });

        using (EnterNodeConnectionScope())
        {
            await hub.SendActorAsync("RoomHub", "room-9", 0x2222, Array.Empty<byte>(), sourceNodeId: "gateway-1", replyTo: "client-conn-3");

            // 调用结束后当前上下文（鉴权作用域）中的连接 Id 必须已恢复，不残留虚拟连接标识。
            PulseContext.CurrentConnectionId.Should().Be("peer-conn-1");
        }

        connectionIdDuringRouting.Should().Be(GatewayVirtualChannel.ComposeId("gateway-1", "client-conn-3"));
        channelManager.Received(1).GetOrRegisterVirtualChannel(
            GatewayVirtualChannel.ComposeId("gateway-1", "client-conn-3"), Arg.Any<Func<string, IServerChannel>>());
    }

    [Fact]
    public async Task SendActorAsync_WithoutReplyTo_MustNotRegisterVirtualChannel_AndLeaveConnectionIdUnchanged()
    {
        var (hub, channelManager, routingTable) = CreateHub();
        string? connectionIdDuringRouting = null;
        routingTable.RouteByProtocolIdAsync(Arg.Any<IServiceProvider>(), 0x3333, "room-1", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                connectionIdDuringRouting = PulseContext.CurrentConnectionId;
                return new ValueTask<object?>((object?)null);
            });

        using (EnterNodeConnectionScope())
        {
            await hub.SendActorAsync("RoomHub", "room-1", 0x3333, Array.Empty<byte>());
        }

        connectionIdDuringRouting.Should().Be("peer-conn-1");
        channelManager.DidNotReceiveWithAnyArgs().GetOrRegisterVirtualChannel(default!, default!);
    }

    [Fact]
    public async Task AskActorAsync_WhenRoutingResultIsNull_MustReturnEmptyArray_WithoutTouchingSerializerRegistry()
    {
        var (hub, _, routingTable) = CreateHub();
        routingTable.RouteByProtocolIdAsync(Arg.Any<IServiceProvider>(), 0x4444, "k", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<object?>((object?)null));

        byte[] result;
        using (EnterNodeConnectionScope())
        {
            result = await hub.AskActorAsync("RoomHub", "k", 0x4444, Array.Empty<byte>());
        }

        result.Should().BeEmpty();
    }

    /// <summary>
    /// §P6/§10.3：跨节点转发的 <c>SendActorAsync</c> 携带非空 MessageId 时，第二次收到同一
    /// <c>(hub,key,messageId)</c> 必须被去重跳过，不重复执行——支撑 <c>ClusterPulseRouter</c> 在
    /// <see cref="PulseRPC.DeliveryMode.ExactlyOnce"/> 下的跨节点重试不会造成远端重复生效。
    /// </summary>
    [Fact]
    public async Task SendActorAsync_WithSameMessageId_MustSkipSecondExecution()
    {
        var (hub, _, routingTable) = CreateHub();
        var callCount = 0;
        routingTable.RouteByProtocolIdAsync(Arg.Any<IServiceProvider>(), 0x6666, "room-1", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return new ValueTask<object?>((object?)null);
            });
        var messageId = Guid.NewGuid();

        using (EnterNodeConnectionScope())
        {
            await hub.SendActorAsync("RoomHub", "room-1", 0x6666, Array.Empty<byte>(), messageId: messageId);
            await hub.SendActorAsync("RoomHub", "room-1", 0x6666, Array.Empty<byte>(), messageId: messageId);
        }

        callCount.Should().Be(1, "同一 MessageId 的第二次转发应被去重跳过");
    }

    [Fact]
    public async Task SendActorAsync_WithEmptyMessageId_MustAlwaysExecute_NoDedup()
    {
        var (hub, _, routingTable) = CreateHub();
        var callCount = 0;
        routingTable.RouteByProtocolIdAsync(Arg.Any<IServiceProvider>(), 0x7777, "room-1", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return new ValueTask<object?>((object?)null);
            });

        using (EnterNodeConnectionScope())
        {
            await hub.SendActorAsync("RoomHub", "room-1", 0x7777, Array.Empty<byte>());
            await hub.SendActorAsync("RoomHub", "room-1", 0x7777, Array.Empty<byte>());
        }

        callCount.Should().Be(2, "未携带 MessageId（Guid.Empty）表示调用方未要求去重，应始终执行");
    }

    [Fact]
    public async Task SendActorAsync_WithMessageId_WhenExecutionFails_MustReleaseReservation_ForSubsequentRetry()
    {
        var (hub, _, routingTable) = CreateHub();
        var callCount = 0;
        routingTable.RouteByProtocolIdAsync(Arg.Any<IServiceProvider>(), 0x8888, "room-1", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                throw new InvalidOperationException("boom");
            });
        var messageId = Guid.NewGuid();

        using (EnterNodeConnectionScope())
        {
            var first = async () => await hub.SendActorAsync("RoomHub", "room-1", 0x8888, Array.Empty<byte>(), messageId: messageId);
            await first.Should().ThrowAsync<InvalidOperationException>();

            var second = async () => await hub.SendActorAsync("RoomHub", "room-1", 0x8888, Array.Empty<byte>(), messageId: messageId);
            await second.Should().ThrowAsync<InvalidOperationException>();
        }

        callCount.Should().Be(2, "首次执行失败应释放去重预占，第二次携带同一 MessageId 的调用应重新尝试而不是被跳过");
    }
}
