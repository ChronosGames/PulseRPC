using System.Net;
using PulseRPC.Abstractions.Transport.Batching;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Client.Tests;

public sealed class BatchedTransportLifecycleTests
{
    [Fact]
    public void DropOldest_MustFailFastUntilDroppedRequestCompletionCanBeReported()
    {
        using var inner = new BlockingTransport();

        Assert.Throws<NotSupportedException>(() => new BatchedTransport(
            inner,
            CreateOptions(TransportBackpressureStrategy.DropOldest)));
    }

    [Fact]
    public async Task DisposeAsync_MustDrainAcceptedRequestsAndWaitForTheirSendCompletion()
    {
        using var inner = new BlockingTransport();
        var transport = new BatchedTransport(inner, CreateOptions(TransportBackpressureStrategy.Block));

        var sends = new List<Task<bool>> { transport.SendAsync(new byte[] { 1 }) };
        await inner.FirstSendEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (var index = 0; index < 5; index++)
        {
            sends.Add(transport.SendAsync(new byte[] { (byte)(index + 2) }));
        }

        var disposal = transport.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);

        inner.ReleaseFirstSend();
        await disposal.WaitAsync(TimeSpan.FromSeconds(3));
        var results = await Task.WhenAll(sends).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.All(results, Assert.True);
        Assert.Equal(6, inner.SendCount);
    }

    [Fact]
    public async Task DropNewest_WhenQueueIsSaturated_MustReturnFalseWithoutOrphaningCompletions()
    {
        using var inner = new BlockingTransport();
        await using var transport = new BatchedTransport(
            inner,
            CreateOptions(TransportBackpressureStrategy.DropNewest));

        var sends = new List<Task<bool>> { transport.SendAsync(new byte[] { 1 }) };
        await inner.FirstSendEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (var index = 0; index < 20; index++)
        {
            sends.Add(transport.SendAsync(new byte[] { (byte)(index + 2) }));
        }

        var completedBeforeRelease = sends.Where(send => send.IsCompleted).ToArray();
        Assert.NotEmpty(completedBeforeRelease);
        Assert.Contains(false, await Task.WhenAll(completedBeforeRelease));

        inner.ReleaseFirstSend();
        var results = await Task.WhenAll(sends).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Contains(false, results);
        Assert.Contains(true, results);
    }

    [Fact]
    public async Task PreCanceledSend_MustReturnFalseWithoutEnteringTheQueue()
    {
        using var inner = new BlockingTransport();
        await using var transport = new BatchedTransport(
            inner,
            CreateOptions(TransportBackpressureStrategy.Block));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.False(await transport.SendAsync(new byte[] { 1 }, cancellation.Token));
        Assert.Equal(0, inner.SendCount);
    }

    private static BatchedTransportOptions CreateOptions(TransportBackpressureStrategy strategy) => new()
    {
        BatchThreshold = 1,
        BatchSizeThreshold = 1024,
        FlushInterval = TimeSpan.FromMilliseconds(10),
        QueueCapacity = 10,
        BackpressureStrategy = strategy,
        EnableMetrics = false,
        TransportId = "batched-lifecycle-test"
    };

    private sealed class BlockingTransport : ITransport
    {
        private readonly TaskCompletionSource<bool> _firstSendGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _sendCount;

        public TaskCompletionSource<bool> FirstSendEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SendCount => Volatile.Read(ref _sendCount);
        public string Id => "inner";
        public TransportType Type => TransportType.TCP;
        public bool IsConnected => true;
        public ConnectionState State => ConnectionState.Connected;
        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 1);
        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 2);
        public event EventHandler<TransportStateEventArgs>? StateChanged;
        public event EventHandler<TransportDataEventArgs>? DataReceived;

        public async Task<bool> SendAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _sendCount) == 1)
            {
                FirstSendEntered.TrySetResult(true);
                await _firstSendGate.Task.WaitAsync(cancellationToken);
            }

            return true;
        }

        public void ReleaseFirstSend() => _firstSendGate.TrySetResult(true);

        public void Dispose()
        {
            _firstSendGate.TrySetResult(false);
        }
    }
}
