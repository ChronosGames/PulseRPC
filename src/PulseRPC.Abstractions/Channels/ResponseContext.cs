using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PulseRPC.Client.Channels;

/// <summary>
/// 响应上下文 - 用于跟踪待处理的 RPC 响应
/// </summary>
public sealed class ResponseContext
{
    /// <summary>
    /// 消息ID（用于匹配请求和响应）
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// 响应完成源
    /// </summary>
    public TaskCompletionSource<ReadOnlyMemory<byte>> Tcs { get; set; } = null!;

    /// <summary>
    /// 入队时间戳（用于超时检测）
    /// </summary>
    public long EnqueueTimestamp { get; set; }

    /// <summary>
    /// 取消令牌注册句柄
    /// </summary>
    public CancellationTokenRegistration CancellationRegistration { get; set; }
}

/// <summary>
/// 响应上下文管理器 - 三层架构优化
/// L1: 线程本地快速查找（减少锁竞争）
/// L2: 分片哈希表（减少全局锁竞争）
/// L3: 超时扫描队列（批量处理超时）
/// </summary>
public sealed class ResponseContextManager : IDisposable
{
    // L2: 分片哈希表 (减少锁竞争)
    private readonly ConcurrentDictionary<Guid, ResponseContext>[] _contextShards;
    private readonly int _shardCount;
    private readonly int _shardMask; // 用于快速取模运算

    // L3: 超时扫描
    private readonly Channel<ResponseContext> _timeoutScanQueue;
    private readonly CancellationTokenSource _cts;
    private readonly Task _timeoutScanTask;
    private readonly TimeSpan _defaultTimeout;

    private bool _disposed;

    public ResponseContextManager(int shardCount = 16, TimeSpan? defaultTimeout = null)
    {
        // 确保 shardCount 是 2 的幂次方，以便使用位运算优化
        _shardCount = RoundUpToPowerOf2(shardCount);
        _shardMask = _shardCount - 1;
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30);

        _contextShards = new ConcurrentDictionary<Guid, ResponseContext>[_shardCount];
        for (int i = 0; i < _shardCount; i++)
        {
            _contextShards[i] = new ConcurrentDictionary<Guid, ResponseContext>();
        }

        _timeoutScanQueue = Channel.CreateUnbounded<ResponseContext>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _cts = new CancellationTokenSource();
        _timeoutScanTask = Task.Run(ScanTimeoutsAsync);
    }

    /// <summary>
    /// 注册响应上下文
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Register(ResponseContext context)
    {
        var shard = GetShard(context.MessageId);
        shard[context.MessageId] = context;

        // 投入超时扫描队列
        _timeoutScanQueue.Writer.TryWrite(context);
    }

    /// <summary>
    /// 完成响应上下文（接收到响应时调用）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryComplete(Guid messageId, ReadOnlyMemory<byte> responseData)
    {
        var shard = GetShard(messageId);
        if (shard.TryRemove(messageId, out var context))
        {
            context.Tcs.TrySetResult(responseData);
            context.CancellationRegistration.Dispose();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 取消响应上下文
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryCancel(Guid messageId, OperationCanceledException exception)
    {
        var shard = GetShard(messageId);
        if (shard.TryRemove(messageId, out var context))
        {
            context.Tcs.TrySetCanceled(exception.CancellationToken);
            context.CancellationRegistration.Dispose();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 设置异常
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetException(Guid messageId, Exception exception)
    {
        var shard = GetShard(messageId);
        if (shard.TryRemove(messageId, out var context))
        {
            context.Tcs.TrySetException(exception);
            context.CancellationRegistration.Dispose();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 清空所有待处理的响应上下文（连接断开时调用）
    /// </summary>
    public void ClearAll(Exception? exception = null)
    {
        var ex = exception ?? new InvalidOperationException("Connection closed");

        foreach (var shard in _contextShards)
        {
            foreach (var kvp in shard)
            {
                if (shard.TryRemove(kvp.Key, out var context))
                {
                    context.Tcs.TrySetException(ex);
                    context.CancellationRegistration.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// 获取分片（使用位运算优化取模）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ConcurrentDictionary<Guid, ResponseContext> GetShard(Guid messageId)
    {
        // 使用 Guid 的哈希码进行分片
        var hash = messageId.GetHashCode();
        var shardIndex = hash & _shardMask; // 位运算替代取模
        return _contextShards[shardIndex];
    }

    /// <summary>
    /// 超时扫描循环（后台任务）
    /// </summary>
    private async Task ScanTimeoutsAsync()
    {
        var batch = new List<ResponseContext>(64);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                batch.Clear();

                // 批量读取待扫描的上下文
                while (batch.Count < 64 && _timeoutScanQueue.Reader.TryRead(out var context))
                {
                    batch.Add(context);
                }

                if (batch.Count == 0)
                {
                    // 等待新的上下文或取消信号
                    await _timeoutScanQueue.Reader.WaitToReadAsync(_cts.Token);
                    continue;
                }

                // 延迟一段时间后检查超时
                await Task.Delay(100, _cts.Token);

                // 检查超时
                var now = stopwatch.ElapsedTicks;
                foreach (var context in batch)
                {
                    var elapsed = TimeSpan.FromTicks(now - context.EnqueueTimestamp);
                    if (elapsed > _defaultTimeout)
                    {
                        // 超时，尝试移除并设置异常
                        var shard = GetShard(context.MessageId);
                        if (shard.TryRemove(context.MessageId, out _))
                        {
                            context.Tcs.TrySetException(new TimeoutException(
                                $"Request timed out after {elapsed.TotalSeconds:F2}s (MessageId: {context.MessageId})"));
                            context.CancellationRegistration.Dispose();
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
    }

    /// <summary>
    /// 将数字向上舍入到最接近的 2 的幂次方
    /// </summary>
    private static int RoundUpToPowerOf2(int value)
    {
        if (value <= 1) return 1;
        if ((value & (value - 1)) == 0) return value; // 已经是 2 的幂次方

        int power = 1;
        while (power < value)
        {
            power <<= 1;
        }
        return power;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _timeoutScanQueue.Writer.Complete();

        try
        {
            _timeoutScanTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // 忽略等待超时
        }

        // 清理所有待处理的上下文
        ClearAll(new ObjectDisposedException(nameof(ResponseContextManager)));

        _cts.Dispose();
    }
}

