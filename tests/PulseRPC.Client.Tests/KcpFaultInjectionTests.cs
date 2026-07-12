using PulseRPC.Shared.Kcp;
using Xunit;

namespace PulseRPC.Client.Tests;

public sealed class KcpFaultInjectionTests
{
    private const uint ConversationId = 0x51A7C0DE;

    [Fact]
    public void Core_MustRecoverFromPacketLossAndReordering()
    {
        var droppedPushes = 0;
        var droppedAcks = 0;
        using var link = new SimulatedKcpLink(
            drop: (direction, command, _) =>
                direction == Direction.ClientToServer && command == KcpCommand.Push && droppedPushes++ < 3
                || direction == Direction.ServerToClient && command == KcpCommand.Ack && droppedAcks++ < 2,
            delay: (direction, command, sequence) =>
                direction == Direction.ClientToServer && command == KcpCommand.Push
                    ? sequence % 2 == 0 ? 40u : 5u
                    : 0u);
        var payload = CreatePayload(12 * 1024);

        Assert.Equal(0, link.Client.Send(payload));
        var received = link.RunUntilReceived(timeoutMilliseconds: 20_000);

        Assert.Equal(payload, received);
        Assert.True(droppedPushes >= 3);
        Assert.True(droppedAcks >= 2);
    }

    [Fact]
    public void Core_MustRecoverAfterThirtySecondNetworkBlackout()
    {
        using var link = new SimulatedKcpLink(
            drop: (_, _, now) => now < 30_000,
            delay: (_, _, _) => 0);
        var payload = CreatePayload(4096);

        Assert.Equal(0, link.Client.Send(payload));
        var received = link.RunUntilReceived(timeoutMilliseconds: 90_000);

        Assert.Equal(payload, received);
        Assert.True(link.LastReceiveTime >= 30_000);
    }

    private static byte[] CreatePayload(int length)
    {
        var payload = new byte[length];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)((i * 37 + 11) & 0xFF);
        }

        return payload;
    }

    private enum Direction
    {
        ClientToServer,
        ServerToClient
    }

    private sealed class SimulatedKcpLink : IDisposable
    {
        private readonly List<Datagram> _datagrams = new();
        private readonly Func<Direction, KcpCommand, uint, bool> _drop;
        private readonly Func<Direction, KcpCommand, int, uint> _delay;
        private int _sequence;
        private uint _now;

        public KcpCore Client { get; }
        public KcpCore Server { get; }
        public uint LastReceiveTime { get; private set; }

        public SimulatedKcpLink(
            Func<Direction, KcpCommand, uint, bool> drop,
            Func<Direction, KcpCommand, int, uint> delay)
        {
            _drop = drop;
            _delay = delay;
            Client = CreateCore(Direction.ClientToServer);
            Server = CreateCore(Direction.ServerToClient);
        }

        public byte[] RunUntilReceived(uint timeoutMilliseconds)
        {
            for (_now = 0; _now <= timeoutMilliseconds; _now += 5)
            {
                DeliverDueDatagrams();
                Client.Update(_now);
                Server.Update(_now);
                DeliverDueDatagrams();

                var messageSize = Server.PeekSize();
                if (messageSize > 0)
                {
                    var message = new byte[messageSize];
                    Assert.Equal(messageSize, Server.Recv(message));
                    LastReceiveTime = _now;
                    return message;
                }
            }

            throw new TimeoutException($"KCP message was not delivered within {timeoutMilliseconds} simulated milliseconds.");
        }

        private KcpCore CreateCore(Direction direction)
        {
            var core = new KcpCore(
                ConversationId,
                (buffer, size) => Enqueue(direction, buffer.AsSpan(0, size)));
            Assert.Equal(0, core.NoDelay(1, 10, 2, true));
            Assert.Equal(0, core.SetWindowSize(128, 128));
            Assert.Equal(0, core.SetMtu(1400));
            return core;
        }

        private void Enqueue(Direction direction, ReadOnlySpan<byte> packet)
        {
            var command = (KcpCommand)packet[sizeof(uint)];
            var sequence = _sequence++;
            if (_drop(direction, command, _now))
            {
                return;
            }

            _datagrams.Add(new Datagram(
                direction,
                packet.ToArray(),
                _now + _delay(direction, command, sequence),
                sequence));
        }

        private void DeliverDueDatagrams()
        {
            var due = _datagrams
                .Where(item => item.DeliverAt <= _now)
                .OrderByDescending(item => item.Sequence)
                .ToArray();
            foreach (var datagram in due)
            {
                _datagrams.Remove(datagram);
                var receiver = datagram.Direction == Direction.ClientToServer ? Server : Client;
                Assert.True(receiver.Input(datagram.Payload) >= 0);
            }
        }

        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }

        private sealed record Datagram(Direction Direction, byte[] Payload, uint DeliverAt, int Sequence);
    }
}
