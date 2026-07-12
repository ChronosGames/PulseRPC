using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MemoryPack;
using PulseRPC.Messaging;
using PulseRPC.Server.Transport;
using Xunit;

namespace PulseRPC.Server.Tests;

/// <summary>
/// 验证 P-4「服务端→客户端反向 Ask（Reverse Ask）」在 <see cref="ServerTransportChannel"/> 上的行为：
/// <list type="bullet">
/// <item>发起时发送 <see cref="MessageType.ReverseRequest"/> 帧（携带 ProtocolId + RequireResponse 标志）；</item>
/// <item>客户端以 <see cref="MessageType.Response"/> 应答 → 任务完成并返回响应字节；</item>
/// <item>客户端以 <see cref="MessageType.Error"/> 应答 → 抛出 <see cref="PulseReverseCallException"/>（含错误码/消息）；</item>
/// <item>无应答超时 → 抛出 <see cref="TimeoutException"/>；</item>
/// <item>连接断开 / 释放（断线兜底）→ 抛出 <see cref="PulseReverseCallException"/>；</item>
/// <item>底层发送失败 → 抛出 <see cref="PulseReverseCallException"/>。</item>
/// </list>
/// </summary>
public class ServerReverseCallTests
{
    private static ServerTransportChannel CreateChannel(out MockServerTransport transport)
    {
        transport = new MockServerTransport("conn-1");
        return new ServerTransportChannel(transport);
    }

    private static byte[] BuildReply(Guid messageId, MessageType type, ReadOnlySpan<byte> body)
    {
        var header = new MessageHeader(type, string.Empty, string.Empty)
        {
            MessageId = messageId,
            Flags = type == MessageType.Error ? MessageFlags.Error : MessageFlags.None,
        };
        var packet = new MessagePacket(header, body);
        var buffer = new byte[packet.EstimateSize() + 64];
        var written = packet.WriteTo(buffer);
        return buffer.AsSpan(0, written).ToArray();
    }

    [Fact]
    public async Task Dispose_MustWaitForInFlightPongSend()
    {
        var channel = CreateChannel(out var transport);
        var sendEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.SendHandler = async (_, _) =>
        {
            sendEntered.TrySetResult();
            await releaseSend.Task;
            return true;
        };

        var pingHeader = new MessageHeader(MessageType.Ping, string.Empty, string.Empty);
        var ping = new MessagePacket(pingHeader, ReadOnlySpan<byte>.Empty);
        var buffer = new byte[ping.EstimateSize()];
        var written = ping.WriteTo(buffer);
        transport.SimulateDataReceived(buffer.AsMemory(0, written));
        await sendEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var disposal = Task.Run(channel.Dispose);
        await Task.Delay(50);
        disposal.IsCompleted.Should().BeFalse("accepted background sends must finish before channel disposal returns");

        releaseSend.TrySetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task InvokeClientAsync_SendsReverseRequest_AndCompletesWithClientResponse()
    {
        var channel = CreateChannel(out var transport);
        try
        {
            var request = MemoryPackSerializer.Serialize("ping");
            var task = channel.InvokeClientAsync(0x1234, request, TimeSpan.FromSeconds(5));

            // 发送应在首个异步挂起前同步完成，帧已被捕获
            transport.SentFrames.Should().HaveCount(1);
            MessagePacket.TryReadFrom(transport.SentFrames[0], out var sent).Should().BeTrue();
            sent.Header.Type.Should().Be(MessageType.ReverseRequest);
            sent.Header.ProtocolId.Should().Be(0x1234);
            sent.Header.Flags.HasFlag(MessageFlags.RequireResponse).Should().BeTrue();
            var messageId = sent.Header.MessageId;

            // 模拟客户端成功应答
            var responseBody = MemoryPackSerializer.Serialize("pong");
            transport.SimulateDataReceived(BuildReply(messageId, MessageType.Response, responseBody));

            var result = await task;
            MemoryPackSerializer.Deserialize<string>(result.Span).Should().Be("pong");
        }
        finally
        {
            channel.Dispose();
        }
    }

    [Fact]
    public async Task InvokeClientAsync_Throws_WhenClientRepliesWithError()
    {
        var channel = CreateChannel(out var transport);
        try
        {
            var task = channel.InvokeClientAsync(0x0002, ReadOnlyMemory<byte>.Empty, TimeSpan.FromSeconds(5));

            MessagePacket.TryReadFrom(transport.SentFrames[0], out var sent).Should().BeTrue();
            var messageId = sent.Header.MessageId;

            var errorBody = MemoryPackSerializer.Serialize(ErrorResponse.Create("BOOM", "客户端处理失败"));
            transport.SimulateDataReceived(BuildReply(messageId, MessageType.Error, errorBody));

            Func<Task> act = async () => await task;
            var assertion = await act.Should().ThrowAsync<PulseReverseCallException>();
            assertion.Which.ErrorCode.Should().Be("BOOM");
            assertion.Which.Message.Should().Be("客户端处理失败");
        }
        finally
        {
            channel.Dispose();
        }
    }

    [Fact]
    public async Task InvokeClientAsync_TimesOut_WhenNoClientReply()
    {
        var channel = CreateChannel(out _);
        try
        {
            Func<Task> act = async () =>
                await channel.InvokeClientAsync(0x0003, ReadOnlyMemory<byte>.Empty, TimeSpan.FromMilliseconds(150));

            await act.Should().ThrowAsync<TimeoutException>();
        }
        finally
        {
            channel.Dispose();
        }
    }

    [Fact]
    public async Task InvokeClientAsync_FailsPending_OnDispose()
    {
        var channel = CreateChannel(out _);

        var task = channel.InvokeClientAsync(0x0004, ReadOnlyMemory<byte>.Empty, TimeSpan.FromSeconds(30));

        channel.Dispose();

        Func<Task> act = async () => await task;
        await act.Should().ThrowAsync<PulseReverseCallException>();
    }

    [Fact]
    public async Task InvokeClientAsync_FailsPending_OnDisconnect()
    {
        var channel = CreateChannel(out var transport);
        try
        {
            var task = channel.InvokeClientAsync(0x0005, ReadOnlyMemory<byte>.Empty, TimeSpan.FromSeconds(30));

            transport.SimulateStateChanged(PulseRPC.Shared.ConnectionState.Disconnected);

            Func<Task> act = async () => await task;
            await act.Should().ThrowAsync<PulseReverseCallException>();
        }
        finally
        {
            channel.Dispose();
        }
    }

    [Fact]
    public async Task InvokeClientAsync_Throws_WhenSendFails()
    {
        var channel = CreateChannel(out var transport);
        try
        {
            transport.SendResult = false;

            Func<Task> act = async () =>
                await channel.InvokeClientAsync(0x0006, ReadOnlyMemory<byte>.Empty, TimeSpan.FromSeconds(5));

            await act.Should().ThrowAsync<PulseReverseCallException>();
        }
        finally
        {
            channel.Dispose();
        }
    }

    [Fact]
    public async Task InvokeClientAsync_UsesDefaultTimeout_WhenTimeoutNonPositive()
    {
        // timeout <= 0 使用通道默认（30s），不会立即超时；提供应答即可完成。
        var channel = CreateChannel(out var transport);
        try
        {
            var task = channel.InvokeClientAsync(0x0007, ReadOnlyMemory<byte>.Empty, TimeSpan.Zero);

            MessagePacket.TryReadFrom(transport.SentFrames[0], out var sent).Should().BeTrue();
            var messageId = sent.Header.MessageId;

            var responseBody = MemoryPackSerializer.Serialize(42);
            transport.SimulateDataReceived(BuildReply(messageId, MessageType.Response, responseBody));

            var result = await task;
            MemoryPackSerializer.Deserialize<int>(result.Span).Should().Be(42);
        }
        finally
        {
            channel.Dispose();
        }
    }
}
