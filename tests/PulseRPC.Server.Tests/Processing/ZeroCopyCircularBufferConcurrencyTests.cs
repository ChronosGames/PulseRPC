using PulseRPC.Memory;
using Xunit;

namespace PulseRPC.Server.Tests.Processing;

public sealed class ZeroCopyCircularBufferConcurrencyTests
{
    [Fact]
    public async Task ConcurrentProducersAndConsumers_MustDeliverEveryItemExactlyOnce()
    {
        const int producerCount = 8;
        const int consumerCount = 4;
        const int itemsPerProducer = 5_000;
        const int totalItems = producerCount * itemsPerProducer;

        using var buffer = new ZeroCopyCircularBuffer<int>(64);
        using var start = new Barrier(producerCount + consumerCount);
        var seen = new int[totalItems];
        var consumed = 0;
        var producersCompleted = 0;

        var consumers = Enumerable.Range(0, consumerCount)
            .Select(_ => Task.Run(() =>
            {
                start.SignalAndWait();

                while (true)
                {
                    if (buffer.TryDequeue(out var item))
                    {
                        Assert.InRange(item, 0, totalItems - 1);
                        Interlocked.Increment(ref seen[item]);
                        Interlocked.Increment(ref consumed);
                        continue;
                    }

                    if (Volatile.Read(ref producersCompleted) == producerCount && buffer.IsEmpty)
                    {
                        return;
                    }

                    Thread.Yield();
                }
            }))
            .ToArray();

        var producers = Enumerable.Range(0, producerCount)
            .Select(producerIndex => Task.Run(() =>
            {
                start.SignalAndWait();

                var startValue = producerIndex * itemsPerProducer;
                for (var i = 0; i < itemsPerProducer; i++)
                {
                    var value = startValue + i;
                    while (!buffer.TryEnqueue(value))
                    {
                        Thread.Yield();
                    }
                }

                Interlocked.Increment(ref producersCompleted);
            }))
            .ToArray();

        await Task.WhenAll(producers.Concat(consumers)).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(totalItems, consumed);
        Assert.All(seen, count => Assert.Equal(1, count));
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void ZeroCopyBatchResult_MustRemainStableAfterSlotsAreReused()
    {
        using var buffer = new ZeroCopyCircularBuffer<int>(64);
        var original = Enumerable.Range(0, 64).ToArray();
        Assert.Equal(64, buffer.TryEnqueueBatch(original));

        var batch = buffer.TryDequeueBatch(64);
        Assert.Equal(64, batch.Length);

        var replacement = Enumerable.Range(1000, 64).ToArray();
        Assert.Equal(64, buffer.TryEnqueueBatch(replacement));

        Assert.Equal(original, batch.ToArray());
    }
}
