using System;
using System.Buffers.Binary;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC.Client.Channels;
using PulseRPC.Client.Tests.Helpers;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using Xunit;

namespace PulseRPC.Client.Tests;

public sealed class HubAddressedTransportChannelTests
{
    [Theory]
    [InlineData(nameof(IClientChannel.InvokeRawAsync))]
    [InlineData(nameof(IClientChannel.SendCommandAsync))]
    public void HublessRawApis_MustProvideStrictRoutingMigrationDiagnostic(string methodName)
    {
        var method = typeof(IClientChannel).GetMethod(methodName);
        var obsolete = method?.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.Contains(nameof(IHubAddressedClientChannel), obsolete!.Message);
        Assert.False(obsolete.IsError);
    }

    [Fact]
    public async Task InvokeHubRawAsync_MustWriteCanonicalHubIntoMessageHeader()
    {
        using var transport = new MockClientTransport();
        using var channel = new TransportChannel(
            transport,
            PulseRPCSerializerProvider.Instance,
            new TransportChannelOptions
            {
                MessageProcessingConcurrency = 1,
                MessageQueueCapacity = 16,
                HeartbeatInterval = TimeSpan.Zero,
            });
        using var cts = new CancellationTokenSource();

        var task = ((IHubAddressedClientChannel)channel)
            .InvokeHubRawAsync("InventoryHub", 0x4321, new byte[] { 1, 2 }, cts.Token)
            .AsTask();

        var frame = transport.WaitForSentFrame();
        Assert.NotNull(frame);
        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(frame!.AsSpan(0, 4));
        var header = MemoryPackSerializer.Deserialize<MessageHeader>(frame.AsSpan(4, headerLength));

        Assert.NotNull(header);
        Assert.Equal("InventoryHub", header!.ServiceName);
        Assert.Equal((ushort)0x4321, header.ProtocolId);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task SendHubCommandAsync_MustWriteCanonicalHubIntoMessageHeader()
    {
        using var transport = new MockClientTransport();
        using var channel = new TransportChannel(
            transport,
            PulseRPCSerializerProvider.Instance,
            new TransportChannelOptions
            {
                MessageProcessingConcurrency = 1,
                MessageQueueCapacity = 16,
                HeartbeatInterval = TimeSpan.Zero,
            });

        await ((IHubAddressedClientChannel)channel)
            .SendHubCommandAsync("InventoryHub", 0x4322, ReadOnlyMemory<byte>.Empty);

        var frame = transport.WaitForSentFrame();
        Assert.NotNull(frame);
        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(frame!.AsSpan(0, 4));
        var header = MemoryPackSerializer.Deserialize<MessageHeader>(frame.AsSpan(4, headerLength));

        Assert.NotNull(header);
        Assert.Equal("InventoryHub", header!.ServiceName);
        Assert.Equal((ushort)0x4322, header.ProtocolId);
    }
}
