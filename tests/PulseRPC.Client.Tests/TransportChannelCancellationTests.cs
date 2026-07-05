using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC.Client.Channels;
using PulseRPC.Client.Tests.Helpers;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Client.Tests;

public sealed class TransportChannelCancellationTests
{
    private static TransportChannel CreateChannel(MockClientTransport transport)
    {
        var options = new TransportChannelOptions
        {
            MessageProcessingConcurrency = 1,
            MessageQueueCapacity = 16,
            HeartbeatInterval = TimeSpan.Zero,
        };

        return new TransportChannel(transport, PulseRPCSerializerProvider.Instance, options);
    }

    private static (MessageHeader Header, byte[] Body) ParseFrame(byte[] frame)
    {
        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(0, 4));
        var header = MemoryPackSerializer.Deserialize<MessageHeader>(frame.AsSpan(4, headerLength))!;
        var bodyStart = 4 + headerLength;
        return (header, frame.AsSpan(bodyStart).ToArray());
    }

    [Fact]
    public async Task InvokeRawAsync_WhenCanceled_MustSendCancelFrame()
    {
        using var transport = new MockClientTransport();
        using var channel = CreateChannel(transport);
        using var cts = new CancellationTokenSource();

        var task = channel.InvokeRawAsync(0x1234, new byte[] { 1, 2, 3 }, cts.Token).AsTask();
        var requestFrame = transport.WaitForSentFrame();
        Assert.NotNull(requestFrame);
        var request = ParseFrame(requestFrame!);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        var cancelFrame = transport.WaitForSentFrame();
        Assert.NotNull(cancelFrame);
        var cancel = ParseFrame(cancelFrame!);

        Assert.Equal(MessageType.Request, request.Header.Type);
        Assert.Equal(MessageType.Cancel, cancel.Header.Type);
        Assert.Equal(request.Header.MessageId, cancel.Header.MessageId);
        Assert.Empty(cancel.Body);
    }

    [Fact]
    public async Task InvokeRawAsync_WhenTransportDisconnects_MustFailPendingRequestImmediately()
    {
        using var transport = new MockClientTransport();
        using var channel = CreateChannel(transport);

        var task = channel.InvokeRawAsync(0x1234, ReadOnlyMemory<byte>.Empty).AsTask();
        Assert.NotNull(transport.WaitForSentFrame());

        transport.SimulateStateChanged(ConnectionState.Connected, ConnectionState.Disconnected);

        var ex = await Assert.ThrowsAsync<IOException>(() => task);
        Assert.Contains("挂起请求", ex.Message);
    }
}
