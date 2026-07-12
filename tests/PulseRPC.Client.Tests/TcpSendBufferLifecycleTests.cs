using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Shared;
using PulseRPC.Shared.Tcp;
using Xunit;

namespace PulseRPC.Client.Tests;

public sealed class TcpSendBufferLifecycleTests
{
    [Fact]
    public async Task SendAsync_MustCompleteOnlyAfterUnderlyingWriteFinishes()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connectTask = clientSocket.ConnectAsync(listener.LocalEndpoint);
        using var serverSocket = await listener.AcceptSocketAsync();
        await connectTask;

        var blockingStream = new BlockingNetworkStream(clientSocket);
        using var transport = new QueueTestTcpTransport(
            clientSocket,
            blockingStream,
            ArrayPool<byte>.Shared,
            new TcpTransportOptions { SendQueueCapacity = 1 });

        var send = transport.SendAsync(new byte[32]);
        await blockingStream.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.False(send.IsCompleted);
        blockingStream.ReleaseWrites();
        Assert.True(await send.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task FullQueueCancellationAndDispose_MustReturnEveryRentedBufferExactlyOnce()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connectTask = clientSocket.ConnectAsync(listener.LocalEndpoint);
        using var serverSocket = await listener.AcceptSocketAsync();
        await connectTask;

        var pool = new TrackingArrayPool();
        var blockingStream = new BlockingNetworkStream(clientSocket);
        using var transport = new QueueTestTcpTransport(
            clientSocket,
            blockingStream,
            pool,
            new TcpTransportOptions
            {
                MaxPacketSize = 1024,
                SendQueueCapacity = 1,
            });

        var payload = new byte[128];
        var firstSend = transport.SendAsync(payload);
        await blockingStream.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var secondSend = transport.SendAsync(payload);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.False(await transport.SendAsync(payload, canceled.Token));
        Assert.Equal(3, pool.RentCount);
        Assert.Equal(1, pool.ReturnCount);

        transport.Dispose();

        Assert.False(await firstSend.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.False(await secondSend.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(3, pool.ReturnCount);
        Assert.Equal(0, pool.DuplicateReturnCount);
        Assert.Equal(0, pool.OutstandingCount);
    }

    [Fact]
    public async Task FullQueueCompletion_MustReturnWaitingAndQueuedBuffersExactlyOnce()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connectTask = clientSocket.ConnectAsync(listener.LocalEndpoint);
        using var serverSocket = await listener.AcceptSocketAsync();
        await connectTask;

        var pool = new TrackingArrayPool();
        var blockingStream = new BlockingNetworkStream(clientSocket);
        using var transport = new QueueTestTcpTransport(
            clientSocket,
            blockingStream,
            pool,
            new TcpTransportOptions
            {
                MaxPacketSize = 1024,
                SendQueueCapacity = 1,
            });

        var payload = new byte[128];
        var firstSend = transport.SendAsync(payload);
        await blockingStream.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var secondSend = transport.SendAsync(payload);

        var waitingSend = transport.SendAsync(payload);
        Assert.False(waitingSend.IsCompleted);
        Assert.Equal(3, pool.RentCount);

        blockingStream.ReleaseWrites();

        Assert.True(await firstSend.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(await secondSend.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(await waitingSend.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(3, pool.ReturnCount);
        Assert.Equal(0, pool.DuplicateReturnCount);
        Assert.Equal(0, pool.OutstandingCount);
    }

    private sealed class QueueTestTcpTransport : TcpTransport
    {
        public QueueTestTcpTransport(
            Socket socket,
            NetworkStream stream,
            ArrayPool<byte> sendBufferPool,
            TcpTransportOptions options)
            : base(options, NullLogger.Instance, sendBufferPool)
        {
            _socket = socket;
            _stream = stream;
            _state = ConnectionState.Connected;
            StartSendTask();
        }

        public override string Id => "tcp-send-buffer-test";
    }

    private sealed class BlockingNetworkStream : NetworkStream
    {
        public BlockingNetworkStream(Socket socket)
            : base(socket, ownsSocket: false)
        {
        }

        public TaskCompletionSource<bool> WriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> _writeGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseWrites() => _writeGate.TrySetResult(true);

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            WriteStarted.TrySetResult(true);
            await _writeGate.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        private readonly object _syncRoot = new();
        private readonly HashSet<byte[]> _outstanding = new(ReferenceEqualityComparer.Instance);
        private int _rentCount;
        private int _returnCount;
        private int _duplicateReturnCount;

        public int RentCount => Volatile.Read(ref _rentCount);
        public int ReturnCount => Volatile.Read(ref _returnCount);
        public int DuplicateReturnCount => Volatile.Read(ref _duplicateReturnCount);

        public int OutstandingCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _outstanding.Count;
                }
            }
        }

        public override byte[] Rent(int minimumLength)
        {
            var buffer = new byte[minimumLength];
            lock (_syncRoot)
            {
                _outstanding.Add(buffer);
            }
            Interlocked.Increment(ref _rentCount);
            return buffer;
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            lock (_syncRoot)
            {
                if (!_outstanding.Remove(array))
                {
                    Interlocked.Increment(ref _duplicateReturnCount);
                }
            }
            Interlocked.Increment(ref _returnCount);
        }
    }
}
