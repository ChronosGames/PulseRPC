using System.Diagnostics;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using PulseRPC.Client.Transport;
using PulseRPC.Shared;
using PulseRPC.Shared.Kcp;
using Xunit;

namespace PulseRPC.Client.Tests;

public sealed class KcpClientTransportTests
{
    private const uint ConversationId = 0x10203040;

    [Theory]
    [InlineData(64)]
    [InlineData(16 * 1024)]
    public async Task ConnectAsync_MustStartUpdateLoopAndDeliverCompleteMessages(int payloadSize)
    {
        var payload = CreatePayload(payloadSize);
        using var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        var serverEndpoint = (IPEndPoint)serverSocket.LocalEndPoint!;
        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = RunServerAsync(serverSocket, payload, serverCts.Token);

        using var transport = new KcpClientTransport(
            "kcp-client-test",
            new KcpTransportOptions
            {
                ConversationId = ConversationId,
                Interval = 5,
                RecvBufferSize = 4096,
                MaxPacketSize = 32 * 1024,
                HandshakeTimeout = 2000,
                HandshakeRetryCount = 1,
                EnableNetworkDiagnostics = false
            });

        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.DataReceived += (_, args) => received.TrySetResult(args.Data.ToArray());

        try
        {
            await transport.ConnectAsync(IPAddress.Loopback.ToString(), serverEndpoint.Port, serverCts.Token);

            var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(payload, actual);

            await transport.DisconnectAsync(serverCts.Token);
        }
        finally
        {
            serverCts.Cancel();
            await serverTask;
        }
    }

    [Fact]
    public async Task ConnectAsync_AcceptsConfirmationDelayedBeyondUdpReceiveTimeout()
    {
        using var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndpoint = (IPEndPoint)serverSocket.LocalEndPoint!;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = Task.Run(async () =>
        {
            var buffer = new byte[1024];
            var received = await serverSocket.ReceiveFromAsync(
                buffer,
                SocketFlags.None,
                new IPEndPoint(IPAddress.Any, 0),
                cts.Token);
            await Task.Delay(250, cts.Token);
            await serverSocket.SendToAsync(
                CreateHandshakeResponse(buffer.AsSpan(0, received.ReceivedBytes)),
                SocketFlags.None,
                received.RemoteEndPoint,
                cts.Token);
        }, cts.Token);

        using var transport = new KcpClientTransport(
            "delayed-handshake",
            new KcpTransportOptions
            {
                ConversationId = ConversationId,
                HandshakeTimeout = 1000,
                UdpReceiveTimeout = 25,
                HandshakeRetryCount = 1,
                EnableNetworkDiagnostics = false
            });

        await transport.ConnectAsync(IPAddress.Loopback.ToString(), serverEndpoint.Port, cts.Token);
        Assert.True(transport.IsConnected);
        await serverTask;
    }

    [Fact]
    public async Task ConnectedTransport_IgnoresKcpDatagramsFromUnexpectedEndpoint()
    {
        using var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        using var attackerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        attackerSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndpoint = (IPEndPoint)serverSocket.LocalEndPoint!;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var handshakeTask = CompleteHandshakeAsync(serverSocket, TimeSpan.Zero, cts.Token);

        using var transport = new KcpClientTransport(
            "source-validation",
            new KcpTransportOptions
            {
                ConversationId = ConversationId,
                Interval = 5,
                HandshakeTimeout = 1000,
                HandshakeRetryCount = 1,
                EnableNetworkDiagnostics = false
            });
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.DataReceived += (_, args) => received.TrySetResult(args.Data.ToArray());
        await transport.ConnectAsync(IPAddress.Loopback.ToString(), serverEndpoint.Port, cts.Token);
        await handshakeTask;

        using var attacker = new KcpCore(
            ConversationId,
            (buffer, size) => attackerSocket.SendTo(
                buffer,
                0,
                size,
                SocketFlags.None,
                new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)transport.LocalEndPoint).Port)));
        attacker.NoDelay(1, 5, 2, true);
        attacker.SetWindowSize(32, 128);
        attacker.SetMtu(1400);
        Assert.Equal(0, attacker.Send(CreatePayload(64)));
        attacker.Update(0);

        await Task.Delay(250, cts.Token);
        Assert.False(received.Task.IsCompleted);
    }

    [Fact]
    public async Task AutoReconnect_ReusesBoundSocketAfterInitialHandshakeFailure()
    {
        using var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverEndpoint = (IPEndPoint)serverSocket.LocalEndPoint!;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var clientPorts = new List<int>();
        var serverTask = Task.Run(async () =>
        {
            var buffer = new byte[1024];
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var received = await serverSocket.ReceiveFromAsync(
                    buffer,
                    SocketFlags.None,
                    new IPEndPoint(IPAddress.Any, 0),
                    cts.Token);
                clientPorts.Add(((IPEndPoint)received.RemoteEndPoint).Port);
                if (attempt == 1)
                {
                    await serverSocket.SendToAsync(
                        CreateHandshakeResponse(buffer.AsSpan(0, received.ReceivedBytes)),
                        SocketFlags.None,
                        received.RemoteEndPoint,
                        cts.Token);
                }
            }
        }, cts.Token);

        using var transport = new KcpClientTransport(
            "auto-reconnect",
            new KcpTransportOptions
            {
                ConversationId = ConversationId,
                HandshakeTimeout = 100,
                HandshakeRetryCount = 1,
                AutoReconnect = true,
                ReconnectInterval = 25,
                MaxReconnectAttempts = 2,
                EnableNetworkDiagnostics = false
            });
        var reconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.StateChanged += (_, args) =>
        {
            if (args.CurrentState == ConnectionState.Connected)
            {
                reconnected.TrySetResult(true);
            }
        };

        await Assert.ThrowsAsync<HandshakeException>(() =>
            transport.ConnectAsync(IPAddress.Loopback.ToString(), serverEndpoint.Port, cts.Token));
        await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await serverTask;

        Assert.Equal(2, clientPorts.Count);
        Assert.Equal(clientPorts[0], clientPorts[1]);
        Assert.True(transport.IsConnected);
    }

    private static async Task RunServerAsync(Socket socket, byte[] payload, CancellationToken cancellationToken)
    {
        var handshakeBuffer = new byte[1024];
        EndPoint anyEndpoint = new IPEndPoint(IPAddress.Any, 0);
        var handshake = await socket.ReceiveFromAsync(
            handshakeBuffer,
            SocketFlags.None,
            anyEndpoint,
            cancellationToken);

        Assert.True(handshake.ReceivedBytes > sizeof(uint) + sizeof(byte));
        Assert.Equal(ConversationId, BitConverter.ToUInt32(handshakeBuffer, 0));
        Assert.Equal(ProtocolConstants.CurrentProtocolVersion, handshakeBuffer[sizeof(uint)]);

        var remoteEndpoint = handshake.RemoteEndPoint;
        await socket.SendToAsync(
            CreateHandshakeResponse(handshakeBuffer.AsSpan(0, handshake.ReceivedBytes)),
            SocketFlags.None,
            remoteEndpoint,
            cancellationToken);

        using var sender = new KcpCore(
            ConversationId,
            (buffer, size) => socket.SendTo(buffer, 0, size, SocketFlags.None, remoteEndpoint));
        Assert.Equal(0, sender.NoDelay(1, 5, 2, true));
        Assert.Equal(0, sender.SetWindowSize(32, 128));
        Assert.Equal(0, sender.SetMtu(1400));
        var wirePayload = new byte[payload.Length + 1];
        payload.CopyTo(wirePayload, 1);
        Assert.Equal(0, sender.Send(wirePayload));

        var stopwatch = Stopwatch.StartNew();
        while (!cancellationToken.IsCancellationRequested)
        {
            sender.Update((uint)stopwatch.ElapsedMilliseconds);

            try
            {
                await Task.Delay(5, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task CompleteHandshakeAsync(
        Socket socket,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        var received = await socket.ReceiveFromAsync(
            buffer,
            SocketFlags.None,
            new IPEndPoint(IPAddress.Any, 0),
            cancellationToken);
        Assert.True(received.ReceivedBytes > sizeof(uint) + sizeof(byte));
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }

        await socket.SendToAsync(
            CreateHandshakeResponse(buffer.AsSpan(0, received.ReceivedBytes)),
            SocketFlags.None,
            received.RemoteEndPoint,
            cancellationToken);
    }

    private static byte[] CreateHandshakeResponse(ReadOnlySpan<byte> request)
    {
        Assert.Equal(ConversationId, BinaryPrimitives.ReadUInt32LittleEndian(request));
        Assert.Equal(ProtocolConstants.CurrentProtocolVersion, request[4]);
        Assert.Equal(1, request[5]);
        var extensionLength = BinaryPrimitives.ReadUInt16LittleEndian(request.Slice(6, 2));
        Assert.Equal(8 + extensionLength, request.Length);

        var response = new byte[11 + extensionLength];
        BinaryPrimitives.WriteUInt32LittleEndian(response, ConversationId);
        response[4] = ProtocolConstants.CurrentProtocolVersion;
        response[5] = 2;
        response[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(7, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(9, 2), extensionLength);
        request.Slice(8, extensionLength).CopyTo(response.AsSpan(11));
        return response;
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
}
