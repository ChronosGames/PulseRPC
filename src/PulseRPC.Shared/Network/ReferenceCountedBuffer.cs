using System;
using System.Threading;

namespace PulseRPC.Network;

/// <summary>
/// 可释放的带有引用计数的内存缓冲区
/// </summary>
public class ReferenceCountedBuffer : IDisposable
{
    private readonly byte[] _buffer;
    private int _refCount = 1;
    private bool _isDisposed;

    /// <summary>
    /// 内部缓冲区
    /// </summary>
    public Memory<byte> Memory => _buffer;

    /// <summary>
    /// 构造函数
    /// </summary>
    public ReferenceCountedBuffer(int size)
    {
        _buffer = NetworkBufferPool.Instance.Rent(size);
    }

    /// <summary>
    /// 增加引用计数
    /// </summary>
    public ReferenceCountedBuffer AddReference()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ReferenceCountedBuffer));

        Interlocked.Increment(ref _refCount);
        return this;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        if (Interlocked.Decrement(ref _refCount) != 0)
        {
            return;
        }

        NetworkBufferPool.Instance.Return(_buffer);
        _isDisposed = true;
    }
}
