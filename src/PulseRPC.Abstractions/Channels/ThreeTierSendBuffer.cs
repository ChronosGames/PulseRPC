using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PulseRPC.Transport;

namespace PulseRPC.Client.Channels;

/// <summary>
/// 三层网络发送缓冲 - 批量零拷贝发送
/// L1: 线程本地批量缓冲（无锁）
/// L2: 全局发送队列（支持批量出队）
/// L3: Scatter-Gather I/O 零拷贝发送到网卡
/// </summary>
public sealed class ThreeTierSendBuffer : IDisposable
{
    // L1: 线程本地批量缓冲 (无锁)
    [ThreadStatic]
    private static List<ReadOnlyMemory<byte>>? _threadLocalBatch;

    // L2: 全局发送队列（使用 SendItem 封装缓冲区所有权）
    private readonly Channel<SendItem> _sendQueue;

    // L3: 传输层（Socket）
    private readonly IClientTransport _transport;

    // 配置参数
    private readonly int _l1BatchSize;    // L1 批量大小
    private readonly int _l2BatchSize;    // L2 批量大小
    private readonly int _queueCapacity;  // L2 队列容量

    // 后台发送任务
    private readonly CancellationTokenSource _cts;
    private readonly Task _sendTask;

    private bool _disposed;

    public ThreeTierSendBuffer(
        IClientTransport transport,
        int l1BatchSize = 16,
        int l2BatchSize = 64,
        int queueCapacity = 1024)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _l1BatchSize = l1BatchSize;
        _l2BatchSize = l2BatchSize;
        _queueCapacity = queueCapacity;

        _sendQueue = Channel.CreateBounded<SendItem>(new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        _cts = new CancellationTokenSource();
        _sendTask = Task.Run(BatchSendLoopAsync);
    }

    /// <summary>
    /// L1: 本地批量入队（无锁，超快）
    /// </summary>
    public async ValueTask EnqueueAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ThreeTierSendBuffer));

        _threadLocalBatch ??= new List<ReadOnlyMemory<byte>>(_l1BatchSize);
        _threadLocalBatch.Add(message);

        // 达到批量阈值时刷新到 L2
        if (_threadLocalBatch.Count >= _l1BatchSize)
        {
            await FlushLocalBatchAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 强制刷新本地批量（用于确保消息发送）
    /// </summary>
    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        return FlushLocalBatchAsync(cancellationToken);
    }

    /// <summary>
    /// L1 → L2: 刷新线程本地批量到全局队列
    /// </summary>
    private async ValueTask FlushLocalBatchAsync(CancellationToken cancellationToken = default)
    {
        if (_threadLocalBatch == null || _threadLocalBatch.Count == 0)
            return;

        // 优化：在这里复制数据到池化缓冲区，而不是在调用方复制
        // ThreeTierSendBuffer 负责管理这些缓冲区的生命周期
        foreach (var msg in _threadLocalBatch)
        {
            // 从池中租借缓冲区并复制数据
            var buffer = ArrayPool<byte>.Shared.Rent(msg.Length);
            msg.Span.CopyTo(buffer);

            var item = new SendItem(buffer, msg.Length);
            await _sendQueue.Writer.WriteAsync(item, cancellationToken);
        }

        _threadLocalBatch.Clear();
    }

    /// <summary>
    /// 发送项 - 封装池化缓冲区及其生命周期
    /// </summary>
    private readonly struct SendItem
    {
        private readonly byte[] _buffer;
        public readonly int Length;

        public SendItem(byte[] buffer, int length)
        {
            _buffer = buffer;
            Length = length;
        }

        public ReadOnlyMemory<byte> Memory => new ReadOnlyMemory<byte>(_buffer, 0, Length);

        public void ReturnToPool()
        {
            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
            }
        }
    }

    /// <summary>
    /// L2: 批量发送循环（后台任务）
    /// </summary>
    private async Task BatchSendLoopAsync()
    {
        var items = new List<SendItem>(_l2BatchSize);

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                items.Clear();

                // 批量读取消息
                await _sendQueue.Reader.WaitToReadAsync(_cts.Token);

                while (items.Count < _l2BatchSize && _sendQueue.Reader.TryRead(out var item))
                {
                    items.Add(item);
                }

                if (items.Count == 0)
                    continue;

                // L3: 使用 Scatter-Gather I/O 零拷贝发送
                await SendBatchAsync(items, _cts.Token);

                // 发送完成后归还所有缓冲区到池
                foreach (var item in items)
                {
                    item.ReturnToPool();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
    }

    /// <summary>
    /// L3: Scatter-Gather 零拷贝发送
    /// </summary>
    private async ValueTask SendBatchAsync(List<SendItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return;

        try
        {
#if NET6_0_OR_GREATER
            // .NET 6+ 支持 Scatter-Gather I/O
            var segments = new ArraySegment<byte>[items.Count];
            bool canUseScatterGather = true;

            // 尝试将所有 Memory 转换为 ArraySegment（零拷贝前提）
            for (int i = 0; i < items.Count; i++)
            {
                if (!MemoryMarshal.TryGetArray(items[i].Memory, out segments[i]))
                {
                    // 如果某个 Memory 不是数组支持的，降级到合并模式
                    canUseScatterGather = false;
                    break;
                }
            }

            if (canUseScatterGather)
            {
                // 零拷贝路径：Scatter-Gather I/O
                // Socket 内核会直接使用 DMA 从多个缓冲区读取并发送
                await SendScatterGatherAsync(segments, cancellationToken);
                return;
            }
#endif
            // 降级路径：合并后发送（存在一次用户态拷贝）
            var batch = new List<ReadOnlyMemory<byte>>(items.Count);
            foreach (var item in items)
            {
                batch.Add(item.Memory);
            }
            await SendMergedAsync(batch, cancellationToken);
        }
        catch (Exception ex)
        {
            // 记录错误但不中断发送循环
            Console.Error.WriteLine($"[ThreeTierSendBuffer] Send failed: {ex.Message}");
        }
    }

#if NET6_0_OR_GREATER
    /// <summary>
    /// 零拷贝发送：使用 Scatter-Gather I/O
    /// </summary>
    private async ValueTask SendScatterGatherAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken)
    {
        // 调用传输层的批量发送接口
        // 注意：这里假设 IClientTransport 实现了批量发送方法
        // 如果没有，需要回退到逐个发送或合并发送
        if (_transport is IScatterGatherTransport scatterGatherTransport)
        {
            await scatterGatherTransport.SendBatchAsync(segments, cancellationToken);
        }
        else
        {
            // 回退：逐个发送（虽然不是最优，但保证兼容性）
            foreach (var segment in segments)
            {
                await _transport.SendAsync(segment, cancellationToken);
            }
        }
    }
#endif

    /// <summary>
    /// 降级发送：合并多个消息后发送
    /// </summary>
    private async ValueTask SendMergedAsync(List<ReadOnlyMemory<byte>> batch, CancellationToken cancellationToken)
    {
        var totalSize = batch.Sum(b => b.Length);
        var combinedBuffer = ArrayPool<byte>.Shared.Rent(totalSize);

        try
        {
            var offset = 0;
            foreach (var msg in batch)
            {
                msg.CopyTo(combinedBuffer.AsMemory(offset));
                offset += msg.Length;
            }

            await _transport.SendAsync(combinedBuffer.AsMemory(0, totalSize), cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(combinedBuffer);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // 刷新所有线程的本地批量（尽力而为）
        try
        {
            FlushLocalBatchAsync().AsTask().Wait(TimeSpan.FromMilliseconds(100));
        }
        catch
        {
            // 忽略
        }

        // 停止后台发送任务
        _sendQueue.Writer.Complete();
        _cts.Cancel();

        try
        {
            _sendTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // 忽略等待超时
        }

        _cts.Dispose();
    }
}

/// <summary>
/// Scatter-Gather 传输接口扩展
/// 实现此接口的传输层可享受零拷贝优化
/// </summary>
public interface IScatterGatherTransport
{
    /// <summary>
    /// 批量发送多个缓冲区（Scatter-Gather I/O）
    /// </summary>
    ValueTask<bool> SendBatchAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken = default);
}
