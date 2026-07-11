using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using PulseRPC.Shared.Kcp;
using Xunit;

namespace PulseRPC.Client.Tests;

public sealed class KcpCoreTests
{
    private const uint ConversationId = 0x10203040;
    private const int Mtu = 1400;
    private const int Mss = Mtu - KcpSegmentHeader.HeaderSize;

    [Theory]
    [InlineData(1)]
    [InlineData(Mss)]
    [InlineData(Mss + 1)]
    [InlineData(Mss * 3)]
    [InlineData(Mss * 16)]
    public void RoundTrip_MustPreserveMessageBoundariesAndPayload(int payloadSize)
    {
        var senderPackets = new List<byte[]>();
        var receiverPackets = new List<byte[]>();

        using var sender = CreateCore(senderPackets);
        using var receiver = CreateCore(receiverPackets);

        var payload = CreatePayload(payloadSize);
        Assert.Equal(0, sender.Send(payload));

        sender.Update(0);
        Deliver(senderPackets, receiver);

        Assert.Equal(payload.Length, receiver.PeekSize());
        var received = new byte[payload.Length];
        Assert.Equal(payload.Length, receiver.Recv(received));
        Assert.Equal(payload, received);

        // 将 ACK 回送给发送端，再验证下一条消息没有被上一条分片污染。
        receiver.Update(0);
        Deliver(receiverPackets, sender);

        var followUp = CreatePayload(37);
        Assert.Equal(0, sender.Send(followUp));

        sender.Update(10);
        Deliver(senderPackets, receiver);

        Assert.Equal(followUp.Length, receiver.PeekSize());
        var followUpReceived = new byte[followUp.Length];
        Assert.Equal(followUp.Length, receiver.Recv(followUpReceived));
        Assert.Equal(followUp, followUpReceived);
    }

    [Fact]
    public void RoundTrip_MustPreserveFragmentedMessage_WhenPacketsAreReorderedAndDuplicated()
    {
        var senderPackets = new List<byte[]>();
        using var sender = CreateCore(senderPackets);
        using var receiver = CreateCore(new List<byte[]>());

        var payload = CreatePayload(Mss * 3);
        Assert.Equal(0, sender.Send(payload));
        sender.Update(0);

        Assert.Equal(3, senderPackets.Count);
        foreach (var packet in senderPackets.AsEnumerable().Reverse())
        {
            Assert.Equal(0, receiver.Input(packet));
            Assert.Equal(0, receiver.Input(packet));
        }

        Assert.Equal(payload.Length, receiver.PeekSize());
        var received = new byte[payload.Length];
        Assert.Equal(payload.Length, receiver.Recv(received));
        Assert.Equal(payload, received);
    }

    [Fact]
    public void Recv_MustWaitUntilEveryFragmentHasArrived()
    {
        var senderPackets = new List<byte[]>();
        using var sender = CreateCore(senderPackets);
        using var receiver = CreateCore(new List<byte[]>());

        var payload = CreatePayload(Mss * 3);
        Assert.Equal(0, sender.Send(payload));
        sender.Update(0);
        Assert.Equal(3, senderPackets.Count);

        Assert.Equal(0, receiver.Input(senderPackets[0]));
        Assert.Equal(0, receiver.Input(senderPackets[1]));
        Assert.Equal(-1, receiver.PeekSize());
        Assert.Equal(-2, receiver.Recv(new byte[payload.Length]));

        Assert.Equal(0, receiver.Input(senderPackets[2]));
        Assert.Equal(payload.Length, receiver.PeekSize());

        var received = new byte[payload.Length];
        Assert.Equal(payload.Length, receiver.Recv(received));
        Assert.Equal(payload, received);
    }

    [Fact]
    public async Task ConcurrentOperations_MustPreserveEveryMessage()
    {
        const int producerCount = 8;
        const int messagesPerProducer = 128;
        const int totalMessages = producerCount * messagesPerProducer;

        var senderPackets = new ConcurrentQueue<byte[]>();
        var receiverPackets = new ConcurrentQueue<byte[]>();
        var receivedMessages = new ConcurrentDictionary<int, byte>();
        var producersCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var start = new Barrier(producerCount + 2);

        using var sender = CreateConcurrentCore(senderPackets);
        using var receiver = CreateConcurrentCore(receiverPackets);

        var producers = Enumerable.Range(0, producerCount)
            .Select(producerIndex => Task.Run(() =>
            {
                start.SignalAndWait();

                for (var i = 0; i < messagesPerProducer; i++)
                {
                    var sequence = producerIndex * messagesPerProducer + i;
                    var payload = new byte[sizeof(int)];
                    BinaryPrimitives.WriteInt32LittleEndian(payload, sequence);
                    Assert.Equal(0, sender.Send(payload));
                }
            }))
            .ToArray();

        var pumpTask = Task.Run(() =>
        {
            start.SignalAndWait();

            var stopwatch = Stopwatch.StartNew();
            uint current = 0;

            while (!producersCompleted.Task.IsCompleted || receivedMessages.Count < totalMessages)
            {
                sender.Update(current);
                Deliver(senderPackets, receiver);

                while (receiver.PeekSize() > 0)
                {
                    var message = new byte[sizeof(int)];
                    Assert.Equal(message.Length, receiver.Recv(message));
                    var sequence = BinaryPrimitives.ReadInt32LittleEndian(message);
                    Assert.True(receivedMessages.TryAdd(sequence, 0), $"Duplicate message {sequence}");
                }

                receiver.Update(current);
                Deliver(receiverPackets, sender);

                current += 10;
                if (stopwatch.Elapsed > TimeSpan.FromSeconds(10))
                {
                    throw new TimeoutException(
                        $"KCP concurrent pump timed out after receiving {receivedMessages.Count}/{totalMessages} messages.");
                }

                Thread.Yield();
            }
        });

        var observerTask = Task.Run(() =>
        {
            start.SignalAndWait();
            uint current = 0;

            while (!producersCompleted.Task.IsCompleted)
            {
                _ = sender.Check(current);
                _ = receiver.Check(current);
                _ = sender.PeekSize();
                _ = receiver.PeekSize();
                Assert.Equal(0, sender.SetMtu(Mtu));
                Assert.Equal(0, receiver.SetWindowSize(32, 128));
                Assert.Equal(0, sender.NoDelay(1, 10, 2, true));
                current += 10;
                Thread.Yield();
            }
        });

        var producersTask = Task.WhenAll(producers);
        _ = producersTask.ContinueWith(
            _ => producersCompleted.TrySetResult(true),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        await Task.WhenAll(producersTask, pumpTask, observerTask);

        Assert.Equal(totalMessages, receivedMessages.Count);
        for (var sequence = 0; sequence < totalMessages; sequence++)
        {
            Assert.True(receivedMessages.ContainsKey(sequence), $"Missing message {sequence}");
        }
    }

    [Fact]
    public async Task Dispose_DuringBlockedOutput_MustNotCorruptUpdateOrHoldCoreLock()
    {
        var outputEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseOutput = new ManualResetEventSlim();
        var core = new KcpCore(
            ConversationId,
            (_, _) =>
            {
                outputEntered.TrySetResult(true);
                if (!releaseOutput.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Timed out while waiting to release the KCP output callback.");
                }
            });

        try
        {
            Assert.Equal(0, core.SetMtu(Mtu));
            Assert.Equal(0, core.SetWindowSize(32, 128));
            Assert.Equal(0, core.NoDelay(1, 10, 2, true));
            Assert.Equal(0, core.Send(CreatePayload(Mss * 3)));

            var updateTask = Task.Run(() => core.Update(0));
            await outputEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var disposeTask = Task.Run(core.Dispose);
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

            releaseOutput.Set();
            await updateTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(-1, core.Send(new byte[] { 1 }));
            Assert.Equal(-1, core.Input(new byte[KcpSegmentHeader.HeaderSize]));
            Assert.Equal(-1, core.Recv(new byte[1]));
            Assert.Equal(-1, core.PeekSize());
        }
        finally
        {
            releaseOutput.Set();
            core.Dispose();
        }
    }

    private static KcpCore CreateCore(List<byte[]> outputPackets)
    {
        var core = new KcpCore(
            ConversationId,
            (buffer, size) => outputPackets.Add(buffer.AsSpan(0, size).ToArray()));

        Assert.Equal(0, core.SetMtu(Mtu));
        Assert.Equal(0, core.SetWindowSize(32, 128));
        Assert.Equal(0, core.NoDelay(1, 10, 2, true));
        return core;
    }

    private static KcpCore CreateConcurrentCore(ConcurrentQueue<byte[]> outputPackets)
    {
        var core = new KcpCore(
            ConversationId,
            (buffer, size) => outputPackets.Enqueue(buffer.AsSpan(0, size).ToArray()));

        Assert.Equal(0, core.SetMtu(Mtu));
        Assert.Equal(0, core.SetWindowSize(32, 128));
        Assert.Equal(0, core.NoDelay(1, 10, 2, true));
        return core;
    }

    private static void Deliver(List<byte[]> packets, KcpCore destination)
    {
        foreach (var packet in packets)
        {
            Assert.Equal(0, destination.Input(packet));
        }

        packets.Clear();
    }

    private static void Deliver(ConcurrentQueue<byte[]> packets, KcpCore destination)
    {
        while (packets.TryDequeue(out var packet))
        {
            Assert.Equal(0, destination.Input(packet));
        }
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
