using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Messaging;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Shared;
using Xunit;
using MemMessageStatus = PulseRPC.Server.Processing.Memory.MessageStatus;

namespace PulseRPC.Server.Tests.Processing;

/// <summary>
/// P1-8：<see cref="TieredMessageProcessor"/> 载荷所有权（<see cref="MessageSlot.PayloadOwner"/>）
/// 在各分支的确定性归还测试。
/// 断言：处理完成、背压拒收等路径下，每个载荷所有者被<strong>恰好归还一次</strong>——
/// 既无泄漏（未归还），也无二次归还（UAF/double-free）。
/// <para>
/// 重要约束：每个 <see cref="TieredMessageProcessor"/> 的 L1 环形缓冲为<strong>单生产者</strong>设计
/// （生产环境每连接仅一个收包循环入队），因此测试从<strong>单线程</strong>入队；并在断言前等待处理排空，
/// 使 <c>DisposeAsync</c> 能立即返回（避免关闭时的 10s 排空超时）。
/// </para>
/// </summary>
public class TieredMessageProcessorLifecycleTests
{
    /// <summary>记录被 Dispose 的次数，用于断言恰好一次。</summary>
    private sealed class CountingDisposable : IDisposable
    {
        private int _count;
        public int DisposeCount => Volatile.Read(ref _count);
        public void Dispose() => Interlocked.Increment(ref _count);
    }

    private static MessageSlot MakeSlot(CountingDisposable owner, byte[] payload, PulseRPC.MessagePriority priority)
    {
        var messageId = Guid.NewGuid();
        var header = new MessageHeader(MessageType.Request, "svc", "m") { MessageId = messageId };
        return new MessageSlot
        {
            MessageId = messageId,
            ConnectionId = "conn-1",
            Header = header,
            Payload = payload,
            PayloadOwner = owner,
            Priority = priority,
            EnqueueTime = Stopwatch.GetTimestamp(),
            Status = MemMessageStatus.Pending
        };
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (predicate()) return true;
            await Task.Delay(10);
        }
        return predicate();
    }

    [Fact]
    public async Task ProcessedSlots_AreDisposedExactlyOnce_AndPayloadIsIntact()
    {
        const int total = 2000;

        var options = new TieredMessageProcessorOptions
        {
            L1BufferSize = 8192,        // 足够大，避免入队被拒
            MaxBatchSize = 64,
            EnableAdaptiveBatching = false, // 关闭 L2 自适应定时器，保持测试确定、可快速退出
            EnableDetailedLogging = false
        };

        var consumed = new ConcurrentDictionary<Guid, byte[]>();

        Func<MessageSlot, CancellationToken, ValueTask<ProcessingResult>> handler = (slot, ct) =>
        {
            // 模拟 handler 调用期间同步消费载荷（如反序列化）：拷出内容供事后比对。
            consumed[slot.MessageId] = slot.Payload.ToArray();
            return new ValueTask<ProcessingResult>(ProcessingResult.SuccessResult(null));
        };

        await using var processor = new TieredMessageProcessor("p-process", options, handler, NullLogger.Instance);

        var owners = new CountingDisposable[total];
        var expected = new Guid[total];
        var expectedPayload = new byte[total][];

        // 单生产者顺序入队（匹配 L1 单生产者契约）
        for (var i = 0; i < total; i++)
        {
            var owner = new CountingDisposable();
            owners[i] = owner;

            var payload = new byte[16 + (i % 200)];
            payload[0] = (byte)i;
            payload[^1] = (byte)(i >> 8);

            var slot = MakeSlot(owner, payload, PulseRPC.MessagePriority.Critical);
            expected[i] = slot.MessageId;
            expectedPayload[i] = payload;

            processor.TryEnqueueMessageSlot(slot).Should().BeTrue("L1 足够大，入队不应被拒");
        }

        var settled = await WaitUntilAsync(
            () => owners.Sum(o => o.DisposeCount) >= total,
            TimeSpan.FromSeconds(30));

        // 断言归并为标量后再断言一次，避免 FluentAssertions 对大集合做昂贵的
        // caller 源码解析 / 对象图格式化（会表现为“卡死”）。
        settled.Should().BeTrue("所有已处理消息的载荷所有者都应在处理完成后被归还");

        var notExactlyOnce = owners.Count(o => o.DisposeCount != 1);
        notExactlyOnce.Should().Be(0, "每个载荷必须恰好归还一次（无泄漏、无二次归还）");

        consumed.Count.Should().Be(total);
        var contentMismatches = 0;
        for (var i = 0; i < total; i++)
        {
            if (!consumed.TryGetValue(expected[i], out var got) || !got.AsSpan().SequenceEqual(expectedPayload[i]))
            {
                contentMismatches++;
            }
        }
        contentMismatches.Should().Be(0, "载荷内容必须完好，无缓冲复用污染");
    }

    [Fact]
    public async Task Backpressure_ProcessorOwnsAccepted_CallerOwnsRejected_EachExactlyOnce()
    {
        // 小 L1 + 慢 handler 制造背压，验证所有权边界：
        // TryEnqueueMessageSlot 返回 true → processor 归还；返回 false → 调用方归还。合起来恰好一次。
        const int total = 4000;

        var options = new TieredMessageProcessorOptions
        {
            L1BufferSize = 64,
            MaxBatchSize = 16,
            EnableAdaptiveBatching = false,
            EnableDetailedLogging = false
        };

        Func<MessageSlot, CancellationToken, ValueTask<ProcessingResult>> handler = async (slot, ct) =>
        {
            await Task.Delay(1, ct); // 拖慢处理，逼出 L1 满 → 拒收
            return ProcessingResult.SuccessResult(null);
        };

        await using var processor = new TieredMessageProcessor("p-bp", options, handler, NullLogger.Instance);

        var owners = new CountingDisposable[total];
        var rejected = 0;

        // 单生产者顺序入队
        for (var i = 0; i < total; i++)
        {
            var owner = new CountingDisposable();
            owners[i] = owner;
            var slot = MakeSlot(owner, new byte[32], PulseRPC.MessagePriority.Normal);

            if (!processor.TryEnqueueMessageSlot(slot))
            {
                rejected++;
                owner.Dispose(); // 调用方对被拒消息负责归还
            }
        }

        rejected.Should().BeGreaterThan(0, "小 L1 + 慢 handler 应产生背压拒收");

        var settled = await WaitUntilAsync(
            () => owners.Sum(o => o.DisposeCount) >= total,
            TimeSpan.FromSeconds(30));

        settled.Should().BeTrue();
        var notExactlyOnce = owners.Count(o => o.DisposeCount != 1);
        notExactlyOnce.Should().Be(0, "接受与被拒消息各自恰好归还一次");
    }
}
