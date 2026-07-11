using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PulseRPC.Messaging;
using PulseRPC.Routing;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Security;
using PulseRPC.Server.Services;
using PulseRPC.Server.Transport;
using PulseRPC.Server.Routing;
using Xunit;

namespace PulseRPC.Server.Tests;

/// <summary>
/// 回归测试：<see cref="LocalPulseRouter"/> 把 <see cref="PulseAddress"/> 解析为本地投递
/// （§P3 local-router）。覆盖 Connection/AllClients/Group/Except 的 Fan-out 语义与
/// Connection 地址的 Ask（反向请求）语义。
/// </summary>
public class LocalPulseRouterTests
{
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }


    private static (LocalPulseRouter Router, ServerChannelManager ChannelManager, GroupManager GroupManager, UserConnectionMapping UserMapping) CreateRouter(
        IServiceRoutingTable? routingTable = null,
        MessageDeduplicationCache? deduplicationCache = null,
        DeliveryRetryOptions? retryOptions = null)
    {
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var groupManager = new GroupManager();
        var userMapping = new UserConnectionMapping();

        var router = new LocalPulseRouter(
            channelManager,
            groupManager,
            userMapping,
            EmptyServiceProvider.Instance,
            NullLogger<LocalPulseRouter>.Instance,
            routingTable,
            responseSerializerRegistry: null,
            deduplicationCache,
            retryOptions);

        return (router, channelManager, groupManager, userMapping);
    }

    private static (IServerChannel Channel, MockServerTransport Transport) AddAuthenticatedChannel(ServerChannelManager channelManager, string connectionId)
    {
        var transport = new MockServerTransport(connectionId);
        var channel = channelManager.AddChannel(transport);
        var authContext = new AuthenticationContext(connectionId);
        authContext.SetClientAuthentication(connectionId, connectionId);
        channel.SetAuthentication(authContext);
        return (channel, transport);
    }

    [Fact]
    public async Task SendAsync_AllClients_MustDeliverToEveryAuthenticatedConnection()
    {
        var (router, channelManager, _, _) = CreateRouter();
        var (_, transportA) = AddAuthenticatedChannel(channelManager, "conn-a");
        var (_, transportB) = AddAuthenticatedChannel(channelManager, "conn-b");

        var body = MemoryPackSerializer.Serialize("hello");
        await router.SendAsync(PulseAddress.AllClients("TestHub"), 0x1001, body);

        transportA.SentFrames.Should().HaveCount(1);
        transportB.SentFrames.Should().HaveCount(1);

        MessagePacket.TryReadFrom(transportA.SentFrames[0], out var packet).Should().BeTrue();
        packet.Header.Type.Should().Be(MessageType.Event);
        packet.Header.ProtocolId.Should().Be(0x1001);
        MemoryPackSerializer.Deserialize<string>(packet.Payload).Should().Be("hello");
    }

    [Fact]
    public async Task SendAsync_Group_MustOnlyDeliverToGroupMembers()
    {
        var (router, channelManager, groupManager, _) = CreateRouter();
        var (_, transportA) = AddAuthenticatedChannel(channelManager, "conn-a");
        var (_, transportB) = AddAuthenticatedChannel(channelManager, "conn-b");

        await groupManager.AddToGroupAsync("conn-a", "room-1");

        var body = MemoryPackSerializer.Serialize("room-message");
        await router.SendAsync(PulseAddress.Group("TestHub", "room-1"), 0x2002, body);

        transportA.SentFrames.Should().HaveCount(1, "conn-a 在 room-1 组内");
        transportB.SentFrames.Should().BeEmpty("conn-b 不在 room-1 组内");
    }

    [Fact]
    public async Task SendAsync_Except_MustExcludeSpecifiedConnection()
    {
        var (router, channelManager, _, _) = CreateRouter();
        var (_, transportA) = AddAuthenticatedChannel(channelManager, "conn-a");
        var (_, transportB) = AddAuthenticatedChannel(channelManager, "conn-b");

        var body = MemoryPackSerializer.Serialize("broadcast-except");
        await router.SendAsync(PulseAddress.Except("TestHub", "conn-a"), 0x3003, body);

        transportA.SentFrames.Should().BeEmpty("conn-a 被显式排除");
        transportB.SentFrames.Should().HaveCount(1);
    }

    [Fact]
    public async Task SendAsync_Connection_MustDeliverOnlyToTargetConnection()
    {
        var (router, channelManager, _, _) = CreateRouter();
        var (_, transportA) = AddAuthenticatedChannel(channelManager, "conn-a");
        var (_, transportB) = AddAuthenticatedChannel(channelManager, "conn-b");

        var body = MemoryPackSerializer.Serialize("direct");
        await router.SendAsync(PulseAddress.Connection("TestHub", "conn-a"), 0x4004, body);

        transportA.SentFrames.Should().HaveCount(1);
        transportB.SentFrames.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_UnknownConnection_MustNotThrow()
    {
        var (router, _, _, _) = CreateRouter();

        var body = MemoryPackSerializer.Serialize("nobody-home");
        var act = async () => await router.SendAsync(PulseAddress.Connection("TestHub", "missing-conn"), 0x5005, body);

        await act.Should().NotThrowAsync("目标连接不存在时应静默跳过，而非抛出异常（与生成的 Fan-out 代理一致）");
    }

    [Fact]
    public async Task AskAsync_Connection_MustSendReverseRequestAndReturnClientResponse()
    {
        var (router, channelManager, _, _) = CreateRouter();
        var (_, transport) = AddAuthenticatedChannel(channelManager, "conn-a");

        var requestBody = MemoryPackSerializer.Serialize("ping");
        var askTask = router.AskAsync(PulseAddress.Connection("TestHub", "conn-a"), 0x6006, requestBody).AsTask();

        // 反向请求应在首次挂起前同步发出
        transport.SentFrames.Should().HaveCount(1);
        MessagePacket.TryReadFrom(transport.SentFrames[0], out var sentPacket).Should().BeTrue();
        sentPacket.Header.Type.Should().Be(MessageType.ReverseRequest);
        sentPacket.Header.ProtocolId.Should().Be(0x6006);

        var messageId = sentPacket.Header.MessageId;
        var responseBody = MemoryPackSerializer.Serialize("pong");
        var responseHeader = new MessageHeader(MessageType.Response, string.Empty, string.Empty) { MessageId = messageId };
        var responsePacket = new MessagePacket(responseHeader, responseBody);
        var responseBuffer = new byte[responsePacket.EstimateSize() + 64];
        var written = responsePacket.WriteTo(responseBuffer);
        transport.SimulateDataReceived(responseBuffer.AsMemory(0, written));

        var result = await askTask;
        MemoryPackSerializer.Deserialize<string>(result.Span).Should().Be("pong");
    }

    [Fact]
    public async Task AskAsync_FanoutAddressKind_MustThrowNotSupported()
    {
        var (router, _, _, _) = CreateRouter();

        var act = async () => await router.AskAsync(PulseAddress.AllClients("TestHub"), 0x7007, ReadOnlyMemory<byte>.Empty);

        await act.Should().ThrowAsync<NotSupportedException>("AskAsync 要求解析为单一目标，Fan-out 地址应使用 SendAsync");
    }

    [Fact]
    public async Task AskAsync_ActorAddress_WithoutRoutingTable_MustThrowClearError()
    {
        var (router, _, _, _) = CreateRouter();

        var act = async () => await router.AskAsync(PulseAddress.Actor("ChatRoomHub", "room-42"), 0x8008, ReadOnlyMemory<byte>.Empty);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("IServiceRoutingTable");
    }

    private static DeliveryRetryOptions FastRetryOptions() => new()
    {
        MaxAttempts = 3,
        BaseDelay = TimeSpan.FromMilliseconds(1),
        MaxDelay = TimeSpan.FromMilliseconds(5),
    };

    /// <summary>
    /// §P6/§10.3：<see cref="DeliveryMode.AtMostOnce"/>（默认档）对 Actor 地址失败时不应重试，
    /// 必须与既有单节点行为完全一致（发送后不重试，失败即上抛）。
    /// </summary>
    [Fact]
    public async Task SendAsync_ActorAddress_AtMostOnce_OnFailure_MustNotRetry()
    {
        var routingTable = Substitute.For<IServiceRoutingTable>();
        var callCount = 0;
        routingTable.RouteByProtocolIdAsync(Arg.Any<IServiceProvider>(), "RoomHub", 0x1, "room-1", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                throw new InvalidOperationException("boom");
            });
        var (router, _, _, _) = CreateRouter(routingTable, retryOptions: FastRetryOptions());

        var act = async () => await router.SendAsync(PulseAddress.Actor("RoomHub", "room-1"), 0x1, ReadOnlyMemory<byte>.Empty);

        await act.Should().ThrowAsync<InvalidOperationException>();
        callCount.Should().Be(1);
    }

    /// <summary>
    /// §P6/§10.3：<see cref="DeliveryMode.AtLeastOnce"/> 对 Actor 地址失败时应重试，直至成功。
    /// </summary>
    [Fact]
    public async Task SendAsync_ActorAddress_AtLeastOnce_OnTransientFailure_MustRetryUntilSuccess()
    {
        var routingTable = Substitute.For<IServiceRoutingTable>();
        var callCount = 0;
        routingTable.RouteByProtocolIdAsync(Arg.Any<IServiceProvider>(), "RoomHub", 0x2, "room-1", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount < 3)
                {
                    throw new InvalidOperationException("transient");
                }

                return new ValueTask<object?>((object?)null);
            });
        var (router, _, _, _) = CreateRouter(routingTable, retryOptions: FastRetryOptions());

        await router.SendAsync(PulseAddress.Actor("RoomHub", "room-1"), 0x2, ReadOnlyMemory<byte>.Empty, DeliveryMode.AtLeastOnce);

        callCount.Should().Be(3);
    }

    /// <summary>
    /// §P6/§10.3：<see cref="DeliveryMode.ExactlyOnce"/> 对同一显式 MessageId 的第二次调用必须被去重跳过，
    /// 不再次执行 Actor 方法（"效果幂等"）。
    /// </summary>
    [Fact]
    public async Task SendAsync_ActorAddress_ExactlyOnce_WithSameMessageId_MustSkipSecondExecution()
    {
        var routingTable = Substitute.For<IServiceRoutingTable>();
        var callCount = 0;
        routingTable.RouteByProtocolIdAsync(Arg.Any<IServiceProvider>(), "RoomHub", 0x3, "room-1", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return new ValueTask<object?>((object?)null);
            });
        var (router, _, _, _) = CreateRouter(routingTable, retryOptions: FastRetryOptions());
        var messageId = Guid.NewGuid();

        await router.SendAsync(PulseAddress.Actor("RoomHub", "room-1"), 0x3, ReadOnlyMemory<byte>.Empty, DeliveryMode.ExactlyOnce, messageId: messageId);
        await router.SendAsync(PulseAddress.Actor("RoomHub", "room-1"), 0x3, ReadOnlyMemory<byte>.Empty, DeliveryMode.ExactlyOnce, messageId: messageId);

        callCount.Should().Be(1, "第二次调用携带相同 MessageId，应被去重跳过，而不是重复执行");
    }

    /// <summary>
    /// §P6/§10.3：<see cref="DeliveryMode.ExactlyOnce"/> 对不同 MessageId 的调用必须视为不同消息，都执行。
    /// </summary>
    [Fact]
    public async Task SendAsync_ActorAddress_ExactlyOnce_WithDifferentMessageIds_MustExecuteBoth()
    {
        var routingTable = Substitute.For<IServiceRoutingTable>();
        var callCount = 0;
        routingTable.RouteByProtocolIdAsync(Arg.Any<IServiceProvider>(), "RoomHub", 0x4, "room-1", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return new ValueTask<object?>((object?)null);
            });
        var (router, _, _, _) = CreateRouter(routingTable, retryOptions: FastRetryOptions());

        await router.SendAsync(PulseAddress.Actor("RoomHub", "room-1"), 0x4, ReadOnlyMemory<byte>.Empty, DeliveryMode.ExactlyOnce, messageId: Guid.NewGuid());
        await router.SendAsync(PulseAddress.Actor("RoomHub", "room-1"), 0x4, ReadOnlyMemory<byte>.Empty, DeliveryMode.ExactlyOnce, messageId: Guid.NewGuid());

        callCount.Should().Be(2);
    }

    /// <summary>
    /// §P6/§10.3：<see cref="DeliveryMode.ExactlyOnce"/> 重试耗尽仍失败时必须释放去重预占，
    /// 使发起端之后携带同一 MessageId 的合法重试不会被永久性地误判为重复（不能"失败了却被当成已处理"）。
    /// </summary>
    [Fact]
    public async Task SendAsync_ActorAddress_ExactlyOnce_WhenExecutionUltimatelyFails_MustReleaseReservation()
    {
        var routingTable = Substitute.For<IServiceRoutingTable>();
        var callCount = 0;
        routingTable.RouteByProtocolIdAsync(Arg.Any<IServiceProvider>(), "RoomHub", 0x5, "room-1", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                throw new InvalidOperationException("永久失败");
            });
        var (router, _, _, _) = CreateRouter(routingTable, retryOptions: FastRetryOptions());
        var messageId = Guid.NewGuid();

        var firstAttempt = async () => await router.SendAsync(
            PulseAddress.Actor("RoomHub", "room-1"), 0x5, ReadOnlyMemory<byte>.Empty, DeliveryMode.ExactlyOnce, messageId: messageId);
        await firstAttempt.Should().ThrowAsync<InvalidOperationException>();
        var callsAfterFirstAttempt = callCount;

        var secondAttempt = async () => await router.SendAsync(
            PulseAddress.Actor("RoomHub", "room-1"), 0x5, ReadOnlyMemory<byte>.Empty, DeliveryMode.ExactlyOnce, messageId: messageId);
        await secondAttempt.Should().ThrowAsync<InvalidOperationException>();

        callCount.Should().BeGreaterThan(callsAfterFirstAttempt, "去重预占应已释放：第二次携带相同 MessageId 的调用应重新尝试执行，而不是被当成重复直接跳过");
    }

    /// <summary>
    /// §P6/§10.3：Fan-out（AllClients 等）下，单个目标在 <see cref="DeliveryMode.AtLeastOnce"/> 时
    /// 应对失败的目标重试，最终仍需成功送达；不应因重试而影响其它目标。
    /// </summary>
    [Fact]
    public async Task SendAsync_AllClients_AtLeastOnce_RetriesFailingTargetUntilDelivered()
    {
        var (router, channelManager, _, _) = CreateRouter(retryOptions: FastRetryOptions());
        var (_, flakyTransport) = AddAuthenticatedChannel(channelManager, "conn-flaky");
        flakyTransport.FailNextSendAttempts(2);
        var (_, healthyTransport) = AddAuthenticatedChannel(channelManager, "conn-healthy");

        var body = MemoryPackSerializer.Serialize("retry-me");
        await router.SendAsync(PulseAddress.AllClients("TestHub"), 0x9001, body, DeliveryMode.AtLeastOnce);

        flakyTransport.SentFrames.Should().HaveCount(1, "前两次失败重试后第三次应成功送达");
        healthyTransport.SentFrames.Should().HaveCount(1, "健康目标不应受另一个目标重试的影响");
    }
}
