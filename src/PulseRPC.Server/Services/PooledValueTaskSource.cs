using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;

namespace PulseRPC.Server.Services;

/// <summary>
/// 池化的 ValueTaskSource - 用于减少高频 EnqueueAsync 调用的内存分配
/// </summary>
/// <remarks>
/// <para>
/// <strong>性能优势</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>避免每次调用都创建新的 TaskCompletionSource</description></item>
/// <item><description>通过对象池复用 IValueTaskSource 实例</description></item>
/// <item><description>减少 GC 压力，提高吞吐量</description></item>
/// </list>
/// <para>
/// <strong>使用示例</strong>：
/// </para>
/// <code>
/// var source = PooledValueTaskSource&lt;int&gt;.Create();
/// var valueTask = source.GetValueTask();
///
/// // 在其他地方设置结果
/// source.SetResult(42);
///
/// // 等待结果
/// var result = await valueTask;
/// </code>
/// </remarks>
/// <typeparam name="TResult">结果类型</typeparam>
public sealed class PooledValueTaskSource<TResult> : IValueTaskSource<TResult>, IValueTaskSource
{
    /// <summary>
    /// 简单的对象池实现（基于 ConcurrentBag）
    /// </summary>
    private static readonly ConcurrentBag<PooledValueTaskSource<TResult>> _pool = new();
    private static readonly int _maxPoolSize = Environment.ProcessorCount * 4;

    /// <summary>
    /// 核心实现
    /// </summary>
    private ManualResetValueTaskSourceCore<TResult> _core;

    /// <summary>
    /// 是否已完成
    /// </summary>
    private volatile bool _hasResult;

    /// <summary>
    /// 从池中获取实例
    /// </summary>
    public static PooledValueTaskSource<TResult> Create()
    {
        if (_pool.TryTake(out var source))
        {
            source._hasResult = false;
            return source;
        }

        return new PooledValueTaskSource<TResult>();
    }

    /// <summary>
    /// 获取 ValueTask
    /// </summary>
    public ValueTask<TResult> GetValueTask()
    {
        return new ValueTask<TResult>(this, _core.Version);
    }

    /// <summary>
    /// 获取非泛型 ValueTask（用于 void 返回值场景）
    /// </summary>
    public ValueTask GetValueTaskNonGeneric()
    {
        return new ValueTask(this, _core.Version);
    }

    /// <summary>
    /// 设置成功结果
    /// </summary>
    public void SetResult(TResult result)
    {
        if (_hasResult)
        {
            throw new InvalidOperationException("Result has already been set");
        }

        _hasResult = true;
        _core.SetResult(result);
    }

    /// <summary>
    /// 设置异常
    /// </summary>
    public void SetException(Exception exception)
    {
        if (_hasResult)
        {
            throw new InvalidOperationException("Result has already been set");
        }

        _hasResult = true;
        _core.SetException(exception);
    }

    /// <summary>
    /// 尝试设置成功结果
    /// </summary>
    public bool TrySetResult(TResult result)
    {
        if (_hasResult)
        {
            return false;
        }

        _hasResult = true;
        _core.SetResult(result);
        return true;
    }

    /// <summary>
    /// 尝试设置异常
    /// </summary>
    public bool TrySetException(Exception exception)
    {
        if (_hasResult)
        {
            return false;
        }

        _hasResult = true;
        _core.SetException(exception);
        return true;
    }

    #region IValueTaskSource<TResult> 实现

    TResult IValueTaskSource<TResult>.GetResult(short token)
    {
        try
        {
            return _core.GetResult(token);
        }
        finally
        {
            // 返回到池中
            ReturnToPool();
        }
    }

    void IValueTaskSource.GetResult(short token)
    {
        try
        {
            _core.GetResult(token);
        }
        finally
        {
            // 返回到池中
            ReturnToPool();
        }
    }

    ValueTaskSourceStatus IValueTaskSource<TResult>.GetStatus(short token)
    {
        return _core.GetStatus(token);
    }

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
    {
        return _core.GetStatus(token);
    }

    void IValueTaskSource<TResult>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        _core.OnCompleted(continuation, state, token, flags);
    }

    void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        _core.OnCompleted(continuation, state, token, flags);
    }

    #endregion

    /// <summary>
    /// 返回到池中
    /// </summary>
    private void ReturnToPool()
    {
        _core.Reset();
        _hasResult = false;

        // 只有在池未满时才返回
        if (_pool.Count < _maxPoolSize)
        {
            _pool.Add(this);
        }
    }
}

/// <summary>
/// 非泛型版本的池化 ValueTaskSource（用于没有返回值的场景）
/// </summary>
public sealed class PooledValueTaskSource : IValueTaskSource
{
    private static readonly ConcurrentBag<PooledValueTaskSource> _pool = new();
    private static readonly int _maxPoolSize = Environment.ProcessorCount * 4;

    private ManualResetValueTaskSourceCore<byte> _core;
    private volatile bool _hasResult;

    public static PooledValueTaskSource Create()
    {
        if (_pool.TryTake(out var source))
        {
            source._hasResult = false;
            return source;
        }

        return new PooledValueTaskSource();
    }

    public ValueTask GetValueTask()
    {
        return new ValueTask(this, _core.Version);
    }

    public void SetResult()
    {
        if (_hasResult)
        {
            throw new InvalidOperationException("Result has already been set");
        }

        _hasResult = true;
        _core.SetResult(0);
    }

    public void SetException(Exception exception)
    {
        if (_hasResult)
        {
            throw new InvalidOperationException("Result has already been set");
        }

        _hasResult = true;
        _core.SetException(exception);
    }

    public bool TrySetResult()
    {
        if (_hasResult)
        {
            return false;
        }

        _hasResult = true;
        _core.SetResult(0);
        return true;
    }

    public bool TrySetException(Exception exception)
    {
        if (_hasResult)
        {
            return false;
        }

        _hasResult = true;
        _core.SetException(exception);
        return true;
    }

    void IValueTaskSource.GetResult(short token)
    {
        try
        {
            _core.GetResult(token);
        }
        finally
        {
            ReturnToPool();
        }
    }

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
    {
        return _core.GetStatus(token);
    }

    void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        _core.OnCompleted(continuation, state, token, flags);
    }

    private void ReturnToPool()
    {
        _core.Reset();
        _hasResult = false;

        // 只有在池未满时才返回
        if (_pool.Count < _maxPoolSize)
        {
            _pool.Add(this);
        }
    }
}

