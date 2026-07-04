using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PulseRPC.Clustering;
using PulseRPC.Server.Gateway;
using Xunit;

namespace PulseRPC.Server.Tests.Gateway;

/// <summary>
/// 回归测试：<see cref="GatewayVirtualChannel"/>（§P5 §6.2 虚拟连接）—— 对该通道的
/// <see cref="GatewayVirtualChannel.SendAsync"/>/<see cref="GatewayVirtualChannel.InvokeClientAsync"/>
/// 必须转译为对 <see cref="INodeLink"/> 的推送/反向 Ask 调用，而不是尝试写任何本地传输。
/// </summary>
public class GatewayVirtualChannelTests
{
    [Fact]
    public void Id_IsComposedFromGatewayNodeIdAndOriginalConnectionId()
    {
        var channel = new GatewayVirtualChannel("gateway-1", "real-conn-9", Substitute.For<INodeLink>());

        channel.Id.Should().Be("gateway-1:real-conn-9");
        GatewayVirtualChannel.ComposeId("gateway-1", "real-conn-9").Should().Be(channel.Id);
    }

    [Fact]
    public async Task SendAsync_DelegatesToNodeLink_SendToConnectionAsync_WithFramedBytesVerbatim()
    {
        var nodeLink = Substitute.For<INodeLink>();
        var channel = new GatewayVirtualChannel("gateway-1", "real-conn-9", nodeLink);
        var frame = new byte[] { 1, 2, 3 };

        var result = await channel.SendAsync(frame);

        result.Should().BeTrue();
        await nodeLink.Received(1).SendToConnectionAsync(
            "gateway-1", "real-conn-9",
            Arg.Is<ReadOnlyMemory<byte>>(m => m.ToArray().SequenceEqual(frame)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_WhenNodeLinkThrows_MustReturnFalse_NotPropagateException()
    {
        var nodeLink = Substitute.For<INodeLink>();
        nodeLink.SendToConnectionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask(Task.FromException(new InvalidOperationException("boom"))));
        var channel = new GatewayVirtualChannel("gateway-1", "real-conn-9", nodeLink);

        var result = await channel.SendAsync(new byte[] { 1 });

        result.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeClientAsync_DelegatesToNodeLink_AskConnectionAsync_AndReturnsItsResult()
    {
        var nodeLink = Substitute.For<INodeLink>();
        var expected = new byte[] { 7, 7 };
        nodeLink.AskConnectionAsync("gateway-1", "real-conn-9", 0x99, Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ReadOnlyMemory<byte>>(expected));
        var channel = new GatewayVirtualChannel("gateway-1", "real-conn-9", nodeLink);

        var result = await channel.InvokeClientAsync(0x99, new byte[] { 1 }, TimeSpan.FromSeconds(5));

        result.ToArray().Should().BeEquivalentTo(expected);
    }
}
