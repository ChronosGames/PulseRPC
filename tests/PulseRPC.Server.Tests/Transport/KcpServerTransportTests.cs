using System.Net;
using System.Net.Sockets;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;
using PulseRPC.Shared.Kcp;
using Xunit;

namespace PulseRPC.Server.Tests.Transport;

public sealed class KcpServerTransportTests
{
    private const uint ConversationId = 0x10203040;

    [Fact]
    public async Task Receive_MustDeliverMessageLargerThanLegacyFixedBuffer()
    {
        var payload = CreatePayload(16 * 1024);
        using var fixture = new TransportFixture(maxPacketSize: 32 * 1024);
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.DataReceived += (_, args) => received.TrySetResult(args.Data.ToArray());

        fixture.Transport.Start();
        fixture.Send(payload);

        var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(payload, actual);
    }

    [Fact]
    public async Task Receive_MustDisconnectWhenMessageExceedsMaxPacketSize()
    {
        using var fixture = new TransportFixture(maxPacketSize: 4096);
        var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.StateChanged += (_, args) =>
        {
            if (args.CurrentState == ConnectionState.Disconnected)
            {
                disconnected.TrySetResult(true);
            }
        };

        fixture.Transport.Start();
        fixture.Send(CreatePayload(8192));

        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(fixture.Transport.IsConnected);
    }

    [Fact]
    public async Task Listener_RejectsLegacyHandshakeAndAcceptsWireV3Handshake()
    {
        using var listener = new KcpServerListener(0);
        var accepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.ConnectionAccepted += (_, _) => accepted.TrySetResult(true);
        await listener.StartAsync();

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndpoint = new IPEndPoint(
            IPAddress.Loopback,
            ((IPEndPoint)listener.LocalEndPoint).Port);

        client.SendTo(BitConverter.GetBytes(ConversationId), serverEndpoint);
        await Task.Delay(150);
        Assert.False(accepted.Task.IsCompleted);

        var handshake = CreateHandshake(ConversationId);
        client.SendTo(handshake, serverEndpoint);

        await accepted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(client.Poll(1_000_000, SelectMode.SelectRead));
        var response = new byte[256];
        EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
        var responseSize = client.ReceiveFrom(response, ref sender);
        Assert.True(responseSize > handshake.Length);
        Assert.Equal(ConversationId, BitConverter.ToUInt32(response, 0));
        Assert.Equal(ProtocolConstants.CurrentProtocolVersion, response[4]);
        Assert.Equal(2, response[5]);
        Assert.Equal(1, response[6]);
    }

    [Fact]
    public async Task Listener_MustRejectUnauthenticatedNatRebindingForExistingConversation()
    {
        using var listener = new KcpServerListener(0);
        var acceptedCount = 0;
        var firstAccepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.ConnectionAccepted += (_, _) =>
        {
            if (Interlocked.Increment(ref acceptedCount) == 1)
            {
                firstAccepted.TrySetResult();
            }
        };
        await listener.StartAsync();

        using var originalClient = CreateBoundUdpSocket();
        using var reboundClient = CreateBoundUdpSocket();
        var serverEndpoint = new IPEndPoint(
            IPAddress.Loopback,
            ((IPEndPoint)listener.LocalEndPoint).Port);
        var handshake = CreateHandshake(ConversationId);

        originalClient.SendTo(handshake, serverEndpoint);
        await firstAccepted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(originalClient.Poll(1_000_000, SelectMode.SelectRead));
        ReceiveDatagram(originalClient);

        reboundClient.SendTo(handshake, serverEndpoint);
        await Task.Delay(250);

        Assert.Equal(1, Volatile.Read(ref acceptedCount));
        Assert.False(reboundClient.Poll(100_000, SelectMode.SelectRead));
    }

    private static byte[] CreatePayload(int length)
    {
        var payload = new byte[length];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)((i * 31 + 17) & 0xFF);
        }

        return payload;
    }

    private static byte[] CreateHandshake(uint conversationId)
    {
        var extensions = System.Text.Encoding.UTF8.GetBytes("prpc-wire-v3|0|1024|0|");
        var handshake = new byte[8 + extensions.Length];
        BitConverter.GetBytes(conversationId).CopyTo(handshake, 0);
        handshake[4] = ProtocolConstants.CurrentProtocolVersion;
        handshake[5] = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(handshake.AsSpan(6, 2), (ushort)extensions.Length);
        extensions.CopyTo(handshake, 8);
        return handshake;
    }

    private static Socket CreateBoundUdpSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return socket;
    }

    private static byte[] ReceiveDatagram(Socket socket)
    {
        var buffer = new byte[64];
        EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
        var received = socket.ReceiveFrom(buffer, ref sender);
        return buffer[..received];
    }

    private sealed class TransportFixture : IDisposable
    {
        private readonly Socket _serverSocket;
        private readonly Socket _clientSocket;
        private readonly KcpCore _sender;

        public KcpServerTransport Transport { get; }

        public TransportFixture(int maxPacketSize)
        {
            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _serverSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _clientSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

            Transport = new KcpServerTransport(
                "kcp-server-test",
                _serverSocket,
                (IPEndPoint)_clientSocket.LocalEndPoint!,
                ConversationId,
                new KcpTransportOptions
                {
                    Interval = 5,
                    MaxPacketSize = maxPacketSize
                });

            _sender = new KcpCore(
                ConversationId,
                (buffer, size) => Transport.ProcessReceivedData(buffer, size));
            Assert.Equal(0, _sender.NoDelay(1, 5, 2, true));
            Assert.Equal(0, _sender.SetWindowSize(32, 128));
            Assert.Equal(0, _sender.SetMtu(1400));
        }

        public void Send(byte[] payload)
        {
            var wirePayload = new byte[payload.Length + 1];
            payload.CopyTo(wirePayload, 1);
            Assert.Equal(0, _sender.Send(wirePayload));
            _sender.Update(0);
        }

        public void Dispose()
        {
            Transport.Dispose();
            _sender.Dispose();
            _clientSocket.Dispose();
            _serverSocket.Dispose();
        }
    }
}
