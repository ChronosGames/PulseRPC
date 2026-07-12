using System.Buffers.Binary;
using MemoryPack;
using PulseRPC.Client.Channels;
using PulseRPC.Client.Tests.Helpers;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using Xunit;

namespace PulseRPC.Client.Tests;

public sealed class TransportChannelBackgroundLifecycleTests
{
    [Fact]
    public void Abort_MustUseAbortiveTransportCloseAndDisposeResources()
    {
        var transport = new MockClientTransport();
        var channel = new TransportChannel(
            transport,
            PulseRPCSerializerProvider.Instance,
            new TransportChannelOptions { HeartbeatInterval = TimeSpan.Zero });

        channel.Abort();

        Assert.Equal(1, transport.AbortCount);
        Assert.Equal(0, transport.DisconnectCount);
        Assert.Equal(1, transport.DisposeCount);
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public void Dispose_MustDisposeUnderlyingTransport()
    {
        var transport = new MockClientTransport();
        var channel = new TransportChannel(
            transport,
            PulseRPCSerializerProvider.Instance,
            new TransportChannelOptions { HeartbeatInterval = TimeSpan.Zero });

        channel.Dispose();

        Assert.Equal(0, transport.AbortCount);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task Dispose_MustWaitForCanceledReverseHandlerCleanup()
    {
        using var transport = new MockClientTransport();
        var channel = new TransportChannel(
            transport,
            PulseRPCSerializerProvider.Instance,
            new TransportChannelOptions
            {
                MessageProcessingConcurrency = 1,
                MessageQueueCapacity = 4,
                MessageProcessingTimeout = TimeSpan.FromSeconds(2),
                HeartbeatInterval = TimeSpan.Zero
            });
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = channel.RegisterRequestHandler(
            0x4401,
            async (_, cancellationToken) =>
            {
                entered.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return ReadOnlyMemory<byte>.Empty;
                }
                finally
                {
                    await Task.Delay(100);
                    cleaned.TrySetResult(true);
                }
            });

        transport.Receive(CreateFrame(new MessageHeader
        {
            Type = MessageType.ReverseRequest,
            MessageId = Guid.NewGuid(),
            ProtocolId = 0x4401
        }));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        channel.Dispose();

        Assert.True(cleaned.Task.IsCompleted);
    }

    private static byte[] CreateFrame(MessageHeader header)
    {
        var headerBytes = MemoryPackSerializer.Serialize(header);
        var frame = new byte[sizeof(int) + headerBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, headerBytes.Length);
        headerBytes.CopyTo(frame, sizeof(int));
        return frame;
    }
}
