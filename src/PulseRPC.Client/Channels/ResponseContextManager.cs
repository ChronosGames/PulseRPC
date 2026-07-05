using System.Collections.Concurrent;
using System.Diagnostics;
using PulseRPC.Channels;

namespace PulseRPC.Client.Channels;

/// <summary>
/// 响应上下文管理器 - 管理待处理的RPC响应（零拷贝优化）
/// </summary>
/// <remarks>
/// 使用分片字典减少锁竞争，支持高并发场景
/// </remarks>
public sealed class ResponseContextManager : IDisposable
{
    private readonly ConcurrentDictionary<Guid, ResponseContext>[] _shards;
    private readonly int _shardCount;
    private readonly TimeSpan _defaultTimeout;
    private readonly Timer _timeoutCheckTimer;
    private bool _disposed;

    /// <summary>
    /// 创建响应上下文管理器
    /// </summary>
    /// <param name="shardCount">分片数量（建议2的幂次方）</param>
    /// <param name="defaultTimeout">默认超时时间</param>
    public ResponseContextManager(int shardCount = 16, TimeSpan defaultTimeout = default)
    {
        if (shardCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(shardCount), "Shard count must be positive");

        _shardCount = shardCount;
        _defaultTimeout = defaultTimeout == default ? TimeSpan.FromSeconds(30) : defaultTimeout;
        _shards = new ConcurrentDictionary<Guid, ResponseContext>[shardCount];

        for (int i = 0; i < shardCount; i++)
        {
            _shards[i] = new ConcurrentDictionary<Guid, ResponseContext>();
        }

        // 启动超时检测定时器
        _timeoutCheckTimer = new Timer(CheckTimeouts, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 注册待处理的响应上下文
    /// </summary>
    public void Register(ResponseContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var shard = GetShard(context.MessageId);
        if (!shard.TryAdd(context.MessageId, context))
        {
            throw new InvalidOperationException($"Response context for message {context.MessageId} already exists");
        }
    }

    /// <summary>
    /// 尝试完成响应（零拷贝路径）
    /// </summary>
    /// <param name="messageId">消息ID</param>
    /// <param name="responseBody">响应体（ReadOnlyMemory避免额外拷贝）</param>
    /// <returns>如果找到并完成了响应上下文返回true，否则返回false</returns>
    public bool TryComplete(Guid messageId, ReadOnlyMemory<byte> responseBody)
    {
        var shard = GetShard(messageId);
        if (!shard.TryRemove(messageId, out var context))
        {
            return false;
        }

        try
        {
            // 取消超时处理
            context.CancellationRegistration.Dispose();

            // 完成任务
            context.Tcs.TrySetResult(responseBody);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 尝试完成响应（向后兼容的byte[]版本）
    /// </summary>
    public bool TryComplete(Guid messageId, byte[] responseBody)
    {
        return TryComplete(messageId, new ReadOnlyMemory<byte>(responseBody));
    }

    /// <summary>
    /// 尝试取消响应
    /// </summary>
    public void TryCancel(Guid messageId, OperationCanceledException exception, bool disposeRegistration = true)
    {
        var shard = GetShard(messageId);
        if (!shard.TryRemove(messageId, out var context))
        {
            return;
        }

        try
        {
            if (disposeRegistration)
            {
                context.CancellationRegistration.Dispose();
            }

            context.Tcs.TrySetCanceled(exception.CancellationToken);
        }
        catch
        {
            // 忽略取消失败
        }
    }

    /// <summary>
    /// 尝试设置异常
    /// </summary>
    /// <returns>如果找到并完成了对应的等待中请求返回 <c>true</c>，否则（消息ID未匹配/已完成/已超时移除）返回 <c>false</c>。</returns>
    public bool TrySetException(Guid messageId, Exception exception)
    {
        var shard = GetShard(messageId);
        if (!shard.TryRemove(messageId, out var context))
        {
            return false;
        }

        try
        {
            context.CancellationRegistration.Dispose();
            return context.Tcs.TrySetException(exception);
        }
        catch
        {
            // 忽略设置异常失败
            return false;
        }
    }

    /// <summary>
    /// 将所有挂起响应立即置为异常。
    /// </summary>
    internal int FailAll(Exception exception)
    {
        if (exception == null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        var failed = 0;

        foreach (var shard in _shards)
        {
            foreach (var kvp in shard)
            {
                if (!shard.TryRemove(kvp.Key, out var context))
                {
                    continue;
                }

                try
                {
                    context.CancellationRegistration.Dispose();
                    if (context.Tcs.TrySetException(exception))
                    {
                        failed++;
                    }
                }
                catch
                {
                    // 忽略设置异常失败
                }
            }
        }

        return failed;
    }

    /// <summary>
    /// 获取指定消息ID的分片
    /// </summary>
    private ConcurrentDictionary<Guid, ResponseContext> GetShard(Guid messageId)
    {
        // 使用Guid的哈希码进行分片
        var hash = messageId.GetHashCode();
        var index = (hash & 0x7FFFFFFF) % _shardCount;
        return _shards[index];
    }

    /// <summary>
    /// 定期检查超时的响应上下文
    /// </summary>
    private void CheckTimeouts(object? state)
    {
        if (_disposed)
            return;

        var now = Stopwatch.GetTimestamp();
        var timeoutTicks = (long)(_defaultTimeout.TotalSeconds * Stopwatch.Frequency);

        foreach (var shard in _shards)
        {
            var timedOutContexts = new List<KeyValuePair<Guid, ResponseContext>>();

            foreach (var kvp in shard)
            {
                var elapsed = now - kvp.Value.EnqueueTimestamp;
                if (elapsed > timeoutTicks)
                {
                    timedOutContexts.Add(kvp);
                }
            }

            foreach (var kvp in timedOutContexts)
            {
                if (shard.TryRemove(kvp.Key, out var context))
                {
                    try
                    {
                        context.CancellationRegistration.Dispose();
                        context.Tcs.TrySetException(new TimeoutException($"Request {kvp.Key} timed out after {_defaultTimeout}"));
                    }
                    catch
                    {
                        // 忽略超时处理错误
                    }
                }
            }
        }
    }

    /// <summary>
    /// 获取当前待处理的响应数量
    /// </summary>
    public int PendingCount
    {
        get
        {
            int count = 0;
            foreach (var shard in _shards)
            {
                count += shard.Count;
            }
            return count;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // 停止超时检测
        _timeoutCheckTimer?.Dispose();

        // 清理所有待处理的上下文
        foreach (var shard in _shards)
        {
            foreach (var kvp in shard)
            {
                if (shard.TryRemove(kvp.Key, out var context))
                {
                    try
                    {
                        context.CancellationRegistration.Dispose();
                        context.Tcs.TrySetException(new ObjectDisposedException(nameof(ResponseContextManager)));
                    }
                    catch
                    {
                        // 忽略清理错误
                    }
                }
            }
            shard.Clear();
        }
    }
}
