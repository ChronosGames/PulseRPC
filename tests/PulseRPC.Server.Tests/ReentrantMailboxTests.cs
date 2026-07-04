using FluentAssertions;
using PulseRPC.Server.Services;
using Xunit;

namespace PulseRPC.Server.Tests;

/// <summary>
/// 验证 P-3 方法级重入开关在 Actor（DedicatedQueue）邮箱中的读写并发语义：
/// <list type="bullet">
/// <item>读者（reentrant）之间可以并发执行；</item>
/// <item>写者（非 reentrant）彼此独占执行；</item>
/// <item>读者与写者永远不会重叠。</item>
/// </list>
/// </summary>
public class ReentrantMailboxTests
{
    /// <summary>
    /// 探针服务：记录并发读者峰值、活跃写者数，以及是否出现读写重叠。
    /// </summary>
    private sealed class ProbeService : PulseServiceBase
    {
        private int _currentReaders;
        private int _currentWriters;
        private int _maxConcurrentReaders;
        private int _maxConcurrentWriters;

        public volatile bool WriterOverlappedReader;
        public volatile bool ReaderOverlappedWriter;

        public int MaxConcurrentReaders => Volatile.Read(ref _maxConcurrentReaders);
        public int MaxConcurrentWriters => Volatile.Read(ref _maxConcurrentWriters);

        public ProbeService()
            : base("Probe", "probe-1", logger: null, executionOptions: ServiceExecutionOptions.Actor)
        {
        }

        public async Task<bool> ReadAsync(int delayMs)
        {
            var readers = Interlocked.Increment(ref _currentReaders);
            UpdateMax(ref _maxConcurrentReaders, readers);
            try
            {
                if (Volatile.Read(ref _currentWriters) > 0) ReaderOverlappedWriter = true;
                await Task.Delay(delayMs);
                if (Volatile.Read(ref _currentWriters) > 0) ReaderOverlappedWriter = true;
                return true;
            }
            finally
            {
                Interlocked.Decrement(ref _currentReaders);
            }
        }

        public async Task<bool> WriteAsync(int delayMs)
        {
            var writers = Interlocked.Increment(ref _currentWriters);
            UpdateMax(ref _maxConcurrentWriters, writers);
            try
            {
                if (Volatile.Read(ref _currentReaders) > 0) WriterOverlappedReader = true;
                await Task.Delay(delayMs);
                if (Volatile.Read(ref _currentReaders) > 0) WriterOverlappedReader = true;
                return true;
            }
            finally
            {
                Interlocked.Decrement(ref _currentWriters);
            }
        }

        private static void UpdateMax(ref int target, int value)
        {
            int current;
            while (value > (current = Volatile.Read(ref target)))
            {
                Interlocked.CompareExchange(ref target, value, current);
            }
        }
    }

    [Fact]
    public async Task ReadOnlyMethods_RunConcurrently()
    {
        await using var svc = new ProbeService();
        await svc.StartAsync();

        const int readerCount = 8;
        var tasks = new List<Task>(readerCount);
        for (int i = 0; i < readerCount; i++)
        {
            tasks.Add(svc.EnqueueReadAsync(() => svc.ReadAsync(100)));
        }

        await Task.WhenAll(tasks);

        svc.MaxConcurrentReaders.Should().BeGreaterThan(1,
            "readers marked reentrant should be dispatched concurrently in the Actor mailbox");
        svc.ReaderOverlappedWriter.Should().BeFalse();
    }

    [Fact]
    public async Task WriteMethods_AreMutuallyExclusive()
    {
        await using var svc = new ProbeService();
        await svc.StartAsync();

        const int writerCount = 6;
        var tasks = new List<Task>(writerCount);
        for (int i = 0; i < writerCount; i++)
        {
            tasks.Add(svc.EnqueueAsync(() => svc.WriteAsync(20)));
        }

        await Task.WhenAll(tasks);

        svc.MaxConcurrentWriters.Should().Be(1,
            "non-reentrant writers must execute serially and exclusively");
        svc.WriterOverlappedReader.Should().BeFalse();
    }

    [Fact]
    public async Task Readers_NeverOverlap_Writers()
    {
        await using var svc = new ProbeService();
        await svc.StartAsync();

        var tasks = new List<Task>();
        // 交错提交读者与写者，读者耗时更长以放大潜在的重叠窗口。
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(svc.EnqueueReadAsync(() => svc.ReadAsync(40)));
            tasks.Add(svc.EnqueueAsync(() => svc.WriteAsync(10)));
        }

        await Task.WhenAll(tasks);

        svc.WriterOverlappedReader.Should().BeFalse("a writer must wait for all in-flight readers to drain");
        svc.ReaderOverlappedWriter.Should().BeFalse("a reader must never run while a writer holds the mailbox");
    }

    [Fact]
    public async Task ReentrantFlagOverload_MatchesDedicatedEnqueueReadApi()
    {
        await using var svc = new ProbeService();
        await svc.StartAsync();

        var tasks = new List<Task>
        {
            svc.EnqueueAsync(() => svc.ReadAsync(80), reentrant: true),
            svc.EnqueueAsync(() => svc.ReadAsync(80), reentrant: true),
            svc.EnqueueAsync(() => svc.ReadAsync(80), reentrant: true)
        };

        await Task.WhenAll(tasks);

        svc.MaxConcurrentReaders.Should().BeGreaterThan(1);
    }
}
