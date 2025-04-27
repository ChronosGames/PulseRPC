using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityTCP.Memory
{
    /// <summary>
    /// 自定义对象池实现，避免频繁分配内存
    /// </summary>
    public class ObjectPool<T> where T : class
    {
        private readonly ConcurrentBag<T> _objects = new ConcurrentBag<T>();
        private readonly Func<T> _objectGenerator;

        public ObjectPool(Func<T> objectGenerator = null)
        {
            _objectGenerator = objectGenerator ?? (() => Activator.CreateInstance<T>());
        }

        public T Get()
        {
            return _objects.TryTake(out var item) ? item : _objectGenerator();
        }

        public void Return(T item)
        {
            _objects.Add(item);
        }
    }

    /// <summary>
    /// 提供零拷贝内存管理的工具类
    /// </summary>
    public static class ZeroAllocMemoryManager
    {
        // 共享内存池
        // ReSharper disable once InconsistentNaming
        private static readonly ArrayPool<byte> s_sharedPool = ArrayPool<byte>.Shared;

        /// <summary>
        /// 从共享池租用缓冲区
        /// </summary>
        public static byte[] RentBuffer(int minSize)
        {
            return s_sharedPool.Rent(minSize);
        }

        /// <summary>
        /// 归还缓冲区到共享池
        /// </summary>
        public static void ReturnBuffer(byte[] buffer)
        {
            s_sharedPool.Return(buffer);
        }

        /// <summary>
        /// 将字节数组转换为结构体（无拷贝）
        /// </summary>
        public static unsafe T BytesToStruct<T>(ReadOnlySpan<byte> bytes) where T : unmanaged
        {
            if (bytes.Length < sizeof(T))
                throw new ArgumentException($"Byte array too small for struct of type {typeof(T).Name}");

            fixed (byte* ptr = bytes)
            {
                return *(T*)ptr;
            }
        }

        /// <summary>
        /// 将结构体转换为字节数组（无拷贝）
        /// </summary>
        public static unsafe void StructToBytes<T>(T structure, Span<byte> bytes) where T : unmanaged
        {
            if (bytes.Length < sizeof(T))
                throw new ArgumentException($"Byte array too small for struct of type {typeof(T).Name}");

            fixed (byte* ptr = bytes)
            {
                *(T*)ptr = structure;
            }
        }

        /// <summary>
        /// 从非托管内存创建NativeArray（零拷贝）
        /// </summary>
        public static unsafe NativeArray<T> CreateNativeArrayFromPtr<T>(IntPtr pointer, int length,
            Allocator allocator = Allocator.Temp) where T : unmanaged
        {
            var array = new NativeArray<T>(length, allocator, NativeArrayOptions.UninitializedMemory);
            UnsafeUtility.MemCpy(array.GetUnsafePtr(), pointer.ToPointer(), length * sizeof(T));
            return array;
        }

        /// <summary>
        /// 将NativeArray直接复制到Socket缓冲区（零拷贝）
        /// </summary>
        public static unsafe void CopyNativeArrayToSocketBuffer<T>(NativeArray<T> array, byte[] socketBuffer,
            int offset = 0) where T : unmanaged
        {
            int size = array.Length * sizeof(T);
            if (socketBuffer.Length - offset < size)
                throw new ArgumentException("Socket buffer too small for array");

            fixed (byte* destPtr = &socketBuffer[offset])
            {
                void* srcPtr = array.GetUnsafeReadOnlyPtr();
                UnsafeUtility.MemCpy(destPtr, srcPtr, size);
            }
        }

        /// <summary>
        /// 将托管对象固定在内存中，防止GC移动
        /// </summary>
        public static MemoryHandle PinObject(object obj)
        {
            return GCHandle.Alloc(obj, GCHandleType.Pinned).AddrOfPinnedObject().ToMemoryHandle();
        }
    }

    /// <summary>
    /// 内存句柄扩展，用于安全地管理固定内存
    /// </summary>
    public static class MemoryHandleExtensions
    {
        /// <summary>
        /// 将指针转换为内存句柄
        /// </summary>
        public static unsafe MemoryHandle ToMemoryHandle(this IntPtr ptr)
        {
            return new MemoryHandle(ptr.ToPointer());
        }

        /// <summary>
        /// 获取内存句柄指向的指针
        /// </summary>
        public static unsafe IntPtr ToIntPtr(this MemoryHandle handle)
        {
            return new IntPtr(handle.Pointer);
        }
    }

    /// <summary>
    /// 高性能数据缓冲区，用于网络数据的临时存储
    /// 避免频繁分配内存，并支持直接内存访问
    /// </summary>
    public sealed class DataBuffer : IDisposable
    {
        private byte[] _buffer;
        private int _size;
        private bool _isDisposed;
        private readonly ArrayPool<byte> _pool;

        public ReadOnlySpan<byte> Data => new ReadOnlySpan<byte>(_buffer, 0, _size);
        public int Size => _size;

        public DataBuffer(int initialCapacity = 4096)
        {
            _pool = ArrayPool<byte>.Shared;
            _buffer = _pool.Rent(initialCapacity);
            _size = 0;
        }

        /// <summary>
        /// 将数据写入缓冲区
        /// </summary>
        public void Write(ReadOnlySpan<byte> data)
        {
            EnsureCapacity(_size + data.Length);
            data.CopyTo(new Span<byte>(_buffer, _size, data.Length));
            _size += data.Length;
        }

        /// <summary>
        /// 确保缓冲区容量足够
        /// </summary>
        private void EnsureCapacity(int required)
        {
            if (_buffer.Length >= required) return;

            int newCapacity = Math.Max(required, _buffer.Length * 2);
            byte[] newBuffer = _pool.Rent(newCapacity);

            Array.Copy(_buffer, 0, newBuffer, 0, _size);
            _pool.Return(_buffer);
            _buffer = newBuffer;
        }

        /// <summary>
        /// 清空缓冲区
        /// </summary>
        public void Clear()
        {
            _size = 0;
        }

        /// <summary>
        /// 获取缓冲区的固定内存指针，用于直接内存访问
        /// </summary>
        public unsafe IntPtr GetPinnedPtr()
        {
            fixed (byte* ptr = _buffer)
            {
                return new IntPtr(ptr);
            }
        }

        /// <summary>
        /// 将缓冲区数据复制到NativeArray（for Unity Jobs）
        /// </summary>
        public unsafe NativeArray<byte> ToNativeArray(Allocator allocator = Allocator.Temp)
        {
            var array = new NativeArray<byte>(_size, allocator, NativeArrayOptions.UninitializedMemory);
            fixed (byte* ptr = _buffer)
            {
                UnsafeUtility.MemCpy(array.GetUnsafePtr(), ptr, _size);
            }

            return array;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _pool.Return(_buffer);
            _buffer = null;
            _isDisposed = true;
        }
    }

    /// <summary>
    /// 环形缓冲区，适用于网络流数据处理
    /// 避免频繁内存复制和重新分配
    /// </summary>
    public sealed class RingBuffer : IDisposable
    {
        private byte[] _buffer;
        private int _readPos;
        private int _writePos;
        private int _bytesAvailable;
        private bool _isDisposed;
        private readonly object _lock = new object();

        public int Capacity => _buffer.Length;
        public int BytesAvailable => _bytesAvailable;
        public int FreeSpace => _buffer.Length - _bytesAvailable;

        public RingBuffer(int capacity = 65536) // 默认64KB
        {
            _buffer = new byte[capacity];
            _readPos = 0;
            _writePos = 0;
            _bytesAvailable = 0;
        }

        /// <summary>
        /// 写入数据到环形缓冲区
        /// </summary>
        public bool Write(ReadOnlySpan<byte> data)
        {
            lock (_lock)
            {
                if (data.Length > FreeSpace)
                    return false;

                // 分两段写入，如果需要绕回缓冲区起始位置
                int bytesToEnd = Math.Min(data.Length, _buffer.Length - _writePos);
                data.Slice(0, bytesToEnd).CopyTo(new Span<byte>(_buffer, _writePos, bytesToEnd));

                if (bytesToEnd < data.Length)
                {
                    data.Slice(bytesToEnd).CopyTo(new Span<byte>(_buffer, 0, data.Length - bytesToEnd));
                }

                _writePos = (_writePos + data.Length) % _buffer.Length;
                _bytesAvailable += data.Length;
                return true;
            }
        }

        /// <summary>
        /// 读取数据
        /// </summary>
        public int Read(Span<byte> destination)
        {
            lock (_lock)
            {
                if (_bytesAvailable == 0)
                    return 0;

                int bytesToRead = Math.Min(destination.Length, _bytesAvailable);

                // 分两段读取，如果需要绕回缓冲区起始位置
                int bytesToEnd = Math.Min(bytesToRead, _buffer.Length - _readPos);
                new ReadOnlySpan<byte>(_buffer, _readPos, bytesToEnd).CopyTo(destination.Slice(0, bytesToEnd));

                if (bytesToEnd < bytesToRead)
                {
                    new ReadOnlySpan<byte>(_buffer, 0, bytesToRead - bytesToEnd)
                        .CopyTo(destination.Slice(bytesToEnd));
                }

                _readPos = (_readPos + bytesToRead) % _buffer.Length;
                _bytesAvailable -= bytesToRead;
                return bytesToRead;
            }
        }

        /// <summary>
        /// 读取数据但不移动读取位置（Peek）
        /// </summary>
        public int Peek(Span<byte> destination)
        {
            lock (_lock)
            {
                if (_bytesAvailable == 0)
                    return 0;

                int bytesToRead = Math.Min(destination.Length, _bytesAvailable);
                int tempReadPos = _readPos;

                // 分两段读取，如果需要绕回缓冲区起始位置
                int bytesToEnd = Math.Min(bytesToRead, _buffer.Length - tempReadPos);
                new ReadOnlySpan<byte>(_buffer, tempReadPos, bytesToEnd).CopyTo(destination.Slice(0, bytesToEnd));

                if (bytesToEnd < bytesToRead)
                {
                    new ReadOnlySpan<byte>(_buffer, 0, bytesToRead - bytesToEnd)
                        .CopyTo(destination.Slice(bytesToEnd));
                }

                return bytesToRead;
            }
        }

        /// <summary>
        /// 跳过指定数量的字节
        /// </summary>
        public int Skip(int count)
        {
            lock (_lock)
            {
                int bytesToSkip = Math.Min(count, _bytesAvailable);
                _readPos = (_readPos + bytesToSkip) % _buffer.Length;
                _bytesAvailable -= bytesToSkip;
                return bytesToSkip;
            }
        }

        /// <summary>
        /// 清空缓冲区
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _readPos = 0;
                _writePos = 0;
                _bytesAvailable = 0;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _buffer = null;
            _isDisposed = true;
        }
    }

    /// <summary>
    /// 自定义内存分配器，减少GC压力，优化网络性能
    /// </summary>
    public class NetworkMemoryAllocator : IDisposable
    {
        private readonly ConcurrentDictionary<int, ConcurrentQueue<byte[]>> _bufferPools =
            new ConcurrentDictionary<int, ConcurrentQueue<byte[]>>();

        private readonly int[] _bufferSizes = { 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536 };
        private bool _isDisposed;

        /// <summary>
        /// 获取最合适大小的缓冲区
        /// </summary>
        public byte[] GetBuffer(int minSize)
        {
            int size = GetNearestSize(minSize);

            if (_bufferPools.TryGetValue(size, out var pool) && pool.TryDequeue(out var buffer))
            {
                return buffer;
            }

            // 没有可用的缓冲区，创建新的
            return new byte[size];
        }

        /// <summary>
        /// 归还缓冲区
        /// </summary>
        public void ReturnBuffer(byte[] buffer)
        {
            if (buffer == null || _isDisposed)
                return;

            int size = buffer.Length;
            if (!_bufferPools.TryGetValue(size, out var pool))
            {
                pool = new ConcurrentQueue<byte[]>();
                _bufferPools[size] = pool;
            }

            // 清空缓冲区 (可选)
            Array.Clear(buffer, 0, buffer.Length);
            pool.Enqueue(buffer);
        }

        /// <summary>
        /// 获取最接近的预定义缓冲区大小
        /// </summary>
        private int GetNearestSize(int size)
        {
            for (int i = 0; i < _bufferSizes.Length; i++)
            {
                if (_bufferSizes[i] >= size)
                    return _bufferSizes[i];
            }

            // 如果超出预定义大小，则向上取整到最接近的2的幂
            int power = 1;
            while (power < size)
                power *= 2;

            return power;
        }

        /// <summary>
        /// 清空所有缓冲区池
        /// </summary>
        public void ClearPools()
        {
            foreach (var pool in _bufferPools.Values)
            {
                while (pool.TryDequeue(out _))
                {
                }
            }

            _bufferPools.Clear();
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            ClearPools();
            _isDisposed = true;
        }
    }
}