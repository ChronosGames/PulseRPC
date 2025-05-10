using System;
using System.Runtime.InteropServices;

namespace PulseRPC.Protocol.Messages;

public unsafe class RingBuffer : IDisposable
{
    private readonly byte* buffer;
    private readonly int capacity;
    private int writePos;
    private int readPos;
    private GCHandle handle;
    private readonly byte[] managedBuffer;

    public RingBuffer(int capacity)
    {
        this.capacity = capacity;
        managedBuffer = new byte[capacity];
        handle = GCHandle.Alloc(managedBuffer, GCHandleType.Pinned);
        buffer = (byte*)handle.AddrOfPinnedObject();
        writePos = 0;
        readPos = 0;
    }

    public int AvailableToRead => (writePos - readPos + capacity) % capacity;
    public int AvailableToWrite => capacity - AvailableToRead - 1; // 保留1字节防止读写指针重叠

    // 直接写入原始数据
    public bool TryWrite(ReadOnlySpan<byte> data)
    {
        int length = data.Length;
        if (length > AvailableToWrite)
            return false;

        if (writePos + length <= capacity)
        {
            // 不需要环绕
            fixed (byte* source = data)
            {
                Buffer.MemoryCopy(source, buffer + writePos, length, length);
            }
        }
        else
        {
            // 需要环绕
            int firstPart = capacity - writePos;
            int secondPart = length - firstPart;

            fixed (byte* source = data)
            {
                Buffer.MemoryCopy(source, buffer + writePos, firstPart, firstPart);
                Buffer.MemoryCopy(source + firstPart, buffer, secondPart, secondPart);
            }
        }

        writePos = (writePos + length) % capacity;
        return true;
    }

    // 直接写入对象
    public bool TryWrite<T>(T value) where T : unmanaged
    {
        int size = sizeof(T);
        if (size > AvailableToWrite)
            return false;

        if (writePos + size <= capacity)
        {
            // 不需要环绕
            *(T*)(buffer + writePos) = value;
        }
        else
        {
            // 需要环绕 - 这里为简化，我们可以先将对象复制到临时缓冲区
            Span<byte> temp = stackalloc byte[size];
            *(T*)temp.GetPinnableReference() = value;
            TryWrite(temp);
            return true;
        }

        writePos = (writePos + size) % capacity;
        return true;
    }

    // 读取数据到目标缓冲区
    public bool TryRead(Span<byte> destination)
    {
        int length = destination.Length;
        if (length > AvailableToRead)
            return false;

        if (readPos + length <= capacity)
        {
            // 不需要环绕
            new Span<byte>(buffer + readPos, length).CopyTo(destination);
        }
        else
        {
            // 需要环绕
            int firstPart = capacity - readPos;
            int secondPart = length - firstPart;

            new Span<byte>(buffer + readPos, firstPart).CopyTo(destination.Slice(0, firstPart));
            new Span<byte>(buffer, secondPart).CopyTo(destination.Slice(firstPart));
        }

        readPos = (readPos + length) % capacity;
        return true;
    }

    // 直接读取对象
    public bool TryRead<T>(out T value) where T : unmanaged
    {
        value = default;
        int size = sizeof(T);
        if (size > AvailableToRead)
            return false;

        if (readPos + size <= capacity)
        {
            // 不需要环绕
            value = *(T*)(buffer + readPos);
        }
        else
        {
            // 需要环绕 - 这里为简化，我们可以先将数据复制到临时缓冲区
            Span<byte> temp = stackalloc byte[size];
            if (!TryRead(temp))
            {
                return false;
            }

            value = *(T*)temp.GetPinnableReference();
            return true;
        }

        readPos = (readPos + size) % capacity;
        return true;
    }

    // 获取可连续写入的内存区域
    public Span<byte> GetWriteSpan(int minSize)
    {
        if (minSize > AvailableToWrite)
        {
            return Span<byte>.Empty;
        }

        return writePos + minSize <= capacity ?
            // 可连续写入
            new Span<byte>(buffer + writePos, Math.Min(capacity - writePos, AvailableToWrite)) :
            // 环绕情况，返回开始的连续区域
            new Span<byte>(buffer, Math.Min(readPos - 1, AvailableToWrite));
    }

    // 提交写入的数据
    public void AdvanceWrite(int count)
    {
        if (count > AvailableToWrite)
            throw new ArgumentOutOfRangeException(nameof(count));

        writePos = (writePos + count) % capacity;
    }

    public void Dispose()
    {
        if (handle.IsAllocated)
        {
            handle.Free();
        }
    }
}
