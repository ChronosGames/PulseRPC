using System;
using System.Threading;

namespace PulseRPC.Server.Memory;

/// <summary>
/// 引用计数缓冲区 - 支持安全的零拷贝内存共享
/// </summary>
public sealed class ReferenceCountedBuffer : IDisposable
{
    private readonly byte[] _buffer;
    private readonly Action<byte[]> _returnToPool;
    private volatile int _refCount = 1;
    private volatile bool _disposed = false;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="buffer">缓冲区</param>
    /// <param name="returnToPool">归还到池的回调</param>
    public ReferenceCountedBuffer(byte[] buffer, Action<byte[]> returnToPool)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _returnToPool = returnToPool ?? throw new ArgumentNullException(nameof(returnToPool));
    }

    /// <summary>
    /// 获取内存视图
    /// </summary>
    public Memory<byte> Memory
    {
        get
        {
            ThrowIfDisposed();
            return _buffer.AsMemory();
        }
    }

    /// <summary>
    /// 获取缓冲区大小
    /// </summary>
    public int Length => _buffer.Length;

    /// <summary>
    /// 当前引用计数
    /// </summary>
    public int ReferenceCount => _refCount;

    /// <summary>
    /// 获取底层缓冲区（用于归还内存池）
    /// </summary>
    public byte[] GetBuffer()
    {
        ThrowIfDisposed();
        return _buffer;
    }

    /// <summary>
    /// 增加引用计数（克隆）
    /// </summary>
    /// <returns>当前实例</returns>
    public ReferenceCountedBuffer AddReference()
    {
        var currentCount = Interlocked.Increment(ref _refCount);
        if (currentCount <= 1 || _disposed)
        {
            Interlocked.Decrement(ref _refCount);
            throw new ObjectDisposedException(nameof(ReferenceCountedBuffer));
        }
        return this;
    }

    /// <summary>
    /// 减少引用计数
    /// </summary>
    public void Release()
    {
        var newCount = Interlocked.Decrement(ref _refCount);
        if (newCount == 0)
        {
            // 最后一个引用，可以安全释放
            DisposeCore();
        }
        else if (newCount < 0)
        {
            throw new InvalidOperationException("引用计数不能小于0");
        }
    }

    /// <summary>
    /// 检查是否已释放
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ReferenceCountedBuffer));
        }
    }

    /// <summary>
    /// 核心释放逻辑
    /// </summary>
    private void DisposeCore()
    {
        if (!_disposed)
        {
            _disposed = true;
            _returnToPool(_buffer);
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        Release();
    }
}
