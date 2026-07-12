using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using PulseRPC.Client.Transport;
using PulseRPC.Shared;
using PulseRPC.Shared.Kcp;
using Xunit;

namespace PulseRPC.Client.Tests;

public sealed class AbortiveTransportTests
{
    [Fact]
    public async Task TcpAbort_MustCloseSocketImmediatelyWithResetSemantics()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var accept = listener.AcceptAsync();

        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await clientSocket.ConnectAsync((IPEndPoint)listener.LocalEndPoint!);
        using var serverSocket = await accept;
        using var transport = new AttachedTcpClientTransport(clientSocket);

        var stopwatch = Stopwatch.StartNew();
        ((IAbortableClientTransport)transport).Abort();
        stopwatch.Stop();

        Assert.Equal(ConnectionState.Disconnected, transport.State);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));

        var resetOrClosed = false;
        try
        {
            var buffer = new byte[1];
            var received = await serverSocket.ReceiveAsync(buffer).WaitAsync(TimeSpan.FromSeconds(2));
            resetOrClosed = received == 0;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted)
        {
            resetOrClosed = true;
        }

        Assert.True(resetOrClosed);
    }

    [Fact]
    public void KcpAbort_MustCloseSocketWithoutSendingGracefulDisconnectDatagram()
    {
        using var serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        using var transport = new AttachedKcpClientTransport((IPEndPoint)serverSocket.LocalEndPoint!);

        ((IAbortableClientTransport)transport).Abort();

        Assert.Equal(ConnectionState.Disconnected, transport.State);
        Assert.False(serverSocket.Poll(100_000, SelectMode.SelectRead));
    }

    private sealed class AttachedTcpClientTransport : TcpClientTransport
    {
        public AttachedTcpClientTransport(Socket socket)
            : base("abortive-tcp", new TcpTransportOptions { AutoReconnect = false })
        {
            _socket = socket;
            _stream = new NetworkStream(socket, ownsSocket: true);
            _state = ConnectionState.Connected;
            _handshakeCompleted = true;
        }
    }

    private sealed class AttachedKcpClientTransport : KcpClientTransport
    {
        public AttachedKcpClientTransport(IPEndPoint remoteEndpoint)
            : base(
                "abortive-kcp",
                new KcpTransportOptions { AutoReconnect = false, EnableNetworkDiagnostics = false })
        {
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _localEndpoint = (IPEndPoint)_socket.LocalEndPoint!;
            _remoteEndpoint = remoteEndpoint;
            _state = ConnectionState.Connected;
        }
    }
}
