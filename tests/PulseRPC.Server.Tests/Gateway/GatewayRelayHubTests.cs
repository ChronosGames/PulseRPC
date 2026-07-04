using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Gateway;
using PulseRPC.Server.Security;
using PulseRPC.Server.Transport;
using Xunit;

namespace PulseRPC.Server.Tests.Gateway;

/// <summary>
/// 回归测试：<see cref="GatewayRelayHub"/>（§P5 §6.2/§6.3）—— 网关节点接受来自后端节点的
/// 推送/反向 Ask 转发请求，把它们投递给本机上真实的客户端连接；未通过节点互信鉴权的调用必须被拒绝。
/// </summary>
public class GatewayRelayHubTests
{
    private static IDisposable EnterNodeConnectionScope()
    {
        var authContext = new AuthenticationContext("peer-conn-1");
        authContext.SetServiceAuthentication("node-backend-1", "node-backend-1", token: NodeConnectionGate.NodeConnectionScope,
            scopes: new[] { NodeConnectionGate.NodeConnectionScope });
        return PulseContext.SetContext(PulseContextData.FromAuthenticationContext(authContext));
    }

    [Fact]
    public async Task PushRawFrameAsync_WhenTargetChannelExists_MustForwardFramedPacketVerbatim()
    {
        var channelManager = Substitute.For<IServerChannelManager>();
        var realChannel = Substitute.For<IServerChannel>();
        channelManager.GetChannel("real-client-1").Returns(realChannel);
        realChannel.SendAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        var hub = new GatewayRelayHub(channelManager, NullLogger<GatewayRelayHub>.Instance);
        var frame = new byte[] { 1, 2, 3, 4 };

        using (EnterNodeConnectionScope())
        {
            await hub.PushRawFrameAsync("real-client-1", frame);
        }

        await realChannel.Received(1).SendAsync(
            Arg.Is<ReadOnlyMemory<byte>>(m => m.ToArray().SequenceEqual(frame)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PushRawFrameAsync_WhenTargetChannelMissing_MustNotThrow_JustDropsFrame()
    {
        var channelManager = Substitute.For<IServerChannelManager>();
        channelManager.GetChannel("gone").Returns((IServerChannel?)null);
        var hub = new GatewayRelayHub(channelManager, NullLogger<GatewayRelayHub>.Instance);

        using (EnterNodeConnectionScope())
        {
            var act = async () => await hub.PushRawFrameAsync("gone", new byte[] { 1 });
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task PushRawFrameAsync_WithoutNodeConnectionAuthentication_MustThrowUnauthorized()
    {
        var channelManager = Substitute.For<IServerChannelManager>();
        var hub = new GatewayRelayHub(channelManager, NullLogger<GatewayRelayHub>.Instance);

        // 未建立 EnterNodeConnectionScope：无鉴权环境上下文。
        var act = async () => await hub.PushRawFrameAsync("real-client-1", new byte[] { 1 });

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        channelManager.DidNotReceiveWithAnyArgs().GetChannel(default!);
    }

    [Fact]
    public async Task AskConnectionAsync_WhenTargetChannelExists_MustInvokeClientAndReturnItsResponse()
    {
        var channelManager = Substitute.For<IServerChannelManager>();
        var realChannel = Substitute.For<IServerChannel>();
        channelManager.GetChannel("real-client-2").Returns(realChannel);
        var expected = new byte[] { 9, 9, 9 };
        realChannel.InvokeClientAsync(0x2222, Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<byte>(expected));

        var hub = new GatewayRelayHub(channelManager, NullLogger<GatewayRelayHub>.Instance);

        byte[] result;
        using (EnterNodeConnectionScope())
        {
            result = await hub.AskConnectionAsync("real-client-2", 0x2222, new byte[] { 1 }, timeoutMs: 5000);
        }

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task AskConnectionAsync_WhenTargetChannelMissing_MustThrowInvalidOperation()
    {
        var channelManager = Substitute.For<IServerChannelManager>();
        channelManager.GetChannel("gone").Returns((IServerChannel?)null);
        var hub = new GatewayRelayHub(channelManager, NullLogger<GatewayRelayHub>.Instance);

        using (EnterNodeConnectionScope())
        {
            var act = async () => await hub.AskConnectionAsync("gone", 0x1, Array.Empty<byte>(), timeoutMs: 1000);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }
}
