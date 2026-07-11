using System.Buffers;
using MemoryPack;
using PulseRPC.Client;
using PulseRPC.Gateway;
using PulseRPC.Messaging;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Client.Tests;

public class GatewayActorChannelTests
{
    private interface IPlayerHub : IPulseHub
    {
    }

    [Fact]
    public async Task InvokeRawAsync_WrapsActorAddressAndUnwrapsBusinessResponse()
    {
        var expected = new byte[] { 9, 8, 7 };
        var inner = new CapturingClientChannel
        {
            AskResponse = MemoryPackSerializer.Serialize(expected),
        };
        var actor = inner.ForGatewayActor<IPlayerHub>("player-42");
        using var cts = new CancellationTokenSource();

        var result = await actor.InvokeRawAsync(0x1234, new byte[] { 1, 2, 3 }, cts.Token);

        Assert.Equal(expected, result.ToArray());
        Assert.Equal(GatewayProtocolIds.FrontRelayAsk, inner.LastProtocolId);
        Assert.Equal(cts.Token, inner.LastCancellationToken);

        var request = MemoryPackSerializer.Deserialize<(string, string, ushort, byte[], byte)>(inner.LastPayload.Span);
        Assert.Equal("PlayerHub", request.Item1);
        Assert.Equal("player-42", request.Item2);
        Assert.Equal(0x1234, request.Item3);
        Assert.Equal(new byte[] { 1, 2, 3 }, request.Item4);
        Assert.Equal(4, request.Item5);
    }

    [Fact]
    public async Task SendCommandAsync_WrapsActorAddressAsGatewayOneWayCall()
    {
        var inner = new CapturingClientChannel();
        var actor = inner.ForGatewayActor<IPlayerHub>("player-42");

        await actor.SendCommandAsync(0x4567, new byte[] { 4, 5 });

        Assert.Equal(GatewayProtocolIds.FrontRelaySend, inner.LastProtocolId);
        var command = MemoryPackSerializer.Deserialize<(string, string, ushort, byte[], byte)>(inner.LastPayload.Span);
        Assert.Equal("PlayerHub", command.Item1);
        Assert.Equal("player-42", command.Item2);
        Assert.Equal(0x4567, command.Item3);
        Assert.Equal(new byte[] { 4, 5 }, command.Item4);
        Assert.Equal(4, command.Item5);
    }

    [Fact]
    public void ForGatewayActor_RejectsEmptyKeyAndDoesNotOwnUnderlyingChannel()
    {
        var inner = new CapturingClientChannel();

        Assert.Throws<ArgumentException>(() => inner.ForGatewayActor<IPlayerHub>(" "));

        var actor = inner.ForGatewayActor<IPlayerHub>("player-42");
        actor.Dispose();

        Assert.Equal(0, inner.DisposeCount);
    }

    private sealed class CapturingClientChannel : IClientChannel
    {
        public ushort LastProtocolId { get; private set; }
        public ReadOnlyMemory<byte> LastPayload { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public ReadOnlyMemory<byte> AskResponse { get; init; }
        public int DisposeCount { get; private set; }

        public string Id => "gateway";
        public ConnectionDescriptor Descriptor { get; } = new() { Id = "gateway" };
        public ExtendedConnectionState State => ExtendedConnectionState.Connected;
        public ConnectionStatistics Statistics { get; } = new();
        public Dictionary<string, string> Tags => Descriptor.Tags;
        public bool IsConnected => true;

        public event EventHandler<TransportStateEventArgs>? ConnectionStateChanged;

        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask<ReadOnlyMemory<byte>> InvokeRawAsync(
            ushort protocolId,
            ReadOnlyMemory<byte> serializedRequest,
            CancellationToken cancellationToken = default)
        {
            LastProtocolId = protocolId;
            LastPayload = serializedRequest.ToArray();
            LastCancellationToken = cancellationToken;
            return new ValueTask<ReadOnlyMemory<byte>>(AskResponse);
        }

        public ValueTask SendCommandAsync(
            ushort protocolId,
            ReadOnlyMemory<byte> serializedCommand,
            CancellationToken cancellationToken = default)
        {
            LastProtocolId = protocolId;
            LastPayload = serializedCommand.ToArray();
            LastCancellationToken = cancellationToken;
            return default;
        }

        public ISubscriptionToken RegisterEventHandler(
            ushort protocolId,
            Action<ReadOnlyMemory<byte>> deserializeAndInvoke)
            => new SubscriptionToken(Guid.NewGuid(), protocolId.ToString(), typeof(byte[]), () => { });

        public ISubscriptionToken RegisterRequestHandler(
            ushort protocolId,
            Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> handler)
            => new SubscriptionToken(Guid.NewGuid(), protocolId.ToString(), typeof(byte[]), () => { });

        public IBufferWriter<byte> RentSerializationBuffer(int estimatedSize = 256)
            => new ArrayBufferWriter<byte>(estimatedSize);

        public void ReturnSerializationBuffer(IBufferWriter<byte> buffer)
        {
        }

        public void Dispose() => DisposeCount++;
    }
}
