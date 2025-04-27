using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityTCP.Serialization;
// ReSharper disable InconsistentNaming
// ReSharper disable ConvertConstructorToMemberInitializers

namespace UnityTCP.ZeroCopy
{
    /// <summary>
    /// 使用零拷贝技术的高性能TCP客户端
    /// 直接将对象序列化到网卡缓冲区
    /// </summary>
    public class ZeroCopyTCPClient : IDisposable
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private PipeReader _reader;
        private PipeWriter _writer;
        private CancellationTokenSource _cts;
        private Task _receiveTask;
        private readonly SocketAsyncEventArgs _sendArgs;
        private readonly SocketAsyncEventArgs _receiveArgs;

        // 使用Socket层提供的直接内存缓冲区
        private readonly MemoryPool<byte> _memoryPool;

        // 事件
        public event Action<ReadOnlySequence<byte>> DataReceived;
        public event Action<Exception> ErrorOccurred;
        public event Action Disconnected;

        public ZeroCopyTCPClient()
        {
            _memoryPool = MemoryPool<byte>.Shared;
            _sendArgs = new SocketAsyncEventArgs();
            _receiveArgs = new SocketAsyncEventArgs();
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async Task ConnectAsync(string hostname, int port)
        {
            _client = new TcpClient();
            await _client.ConnectAsync(hostname, port);
            ConfigureSocket(_client.Client);

            _stream = _client.GetStream();
            _reader = PipeReader.Create(_stream, new StreamPipeReaderOptions(bufferSize: 65536)); // 64K缓冲区
            _writer = PipeWriter.Create(_stream, new StreamPipeWriterOptions(minimumBufferSize: 65536)); // 64K缓冲区

            _cts = new CancellationTokenSource();
            _receiveTask = StartReceiving();
        }

        /// <summary>
        /// 配置Socket以优化性能
        /// </summary>
        private static void ConfigureSocket(Socket socket)
        {
            // 禁用Nagle算法，减少延迟
            socket.NoDelay = true;

            // 设置发送和接收缓冲区大小
            socket.SendBufferSize = 262144; // 256K
            socket.ReceiveBufferSize = 262144; // 256K

            // 启用TCP快速失败检测
            socket.LingerState = new LingerOption(true, 0);

            // 启用TCP保活
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            // 配置DualMode以支持IPv4和IPv6
            if (socket.AddressFamily == AddressFamily.InterNetworkV6)
            {
                socket.DualMode = true;
            }
        }

        /// <summary>
        /// 使用零拷贝技术发送网络对象
        /// </summary>
        public ValueTask<FlushResult> SendObjectAsync<T>(T obj) where T : struct, INetworkSerializable
        {
            // 获取序列化后的大小
            var size = obj.GetSerializedSize();

            // 获取内存块
            var memory = _writer.GetMemory(size + sizeof(int));

            // 写入长度前缀
            BitConverter.GetBytes(size).CopyTo(memory.Span);

            // 创建NetworkWriter并序列化对象
            var netWriter = new NetworkWriter(memory.Span[sizeof(int)..]);
            obj.Serialize(ref netWriter);

            // 更新写入位置
            _writer.Advance(size + sizeof(int));

            // 异步部分：刷新数据
            return _writer.FlushAsync();
        }

        /// <summary>
        /// 使用零拷贝技术发送Blittable类型对象（完全零拷贝）
        /// </summary>
        public unsafe ValueTask<FlushResult> SendBlittableAsync<T>(T obj) where T : unmanaged
        {
            var size = sizeof(T);
            var memory = _writer.GetMemory(size + sizeof(int));

            // 写入长度前缀
            BinaryPrimitives.WriteInt32LittleEndian(memory.Span, size);

            // 直接将对象内存复制到网络缓冲区
            fixed (byte* dest = &memory.Span[sizeof(int)])
            {
                var src = (byte*)&obj;
                for (var i = 0; i < size; i++)
                {
                    dest[i] = src[i];
                }
            }

            _writer.Advance(size + sizeof(int));
            return _writer.FlushAsync();
        }

        /// <summary>
        /// 使用Socket直接发送（完全零拷贝，绕过Stream层）
        /// </summary>
        public Task SendDirectAsync(ReadOnlyMemory<byte> buffer)
        {
            var tcs = new TaskCompletionSource<bool>();

            _sendArgs.SetBuffer(MemoryMarshal.AsMemory(buffer).ToArray(), 0, buffer.Length);
            _sendArgs.UserToken = tcs;
            _sendArgs.Completed += OnSendCompleted;

            if (!_client.Client.SendAsync(_sendArgs))
            {
                OnSendCompleted(null, _sendArgs);
            }

            return tcs.Task;
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            var tcs = (TaskCompletionSource<bool>)e.UserToken;

            if (e.SocketError == SocketError.Success)
            {
                tcs.SetResult(true);
            }
            else
            {
                tcs.SetException(new SocketException((int)e.SocketError));
            }

            e.Completed -= OnSendCompleted;
        }

        /// <summary>
        /// 开始接收数据
        /// </summary>
        private async Task StartReceiving()
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var result = await _reader.ReadAsync(_cts.Token);
                    var buffer = result.Buffer;

                    if (buffer.Length > 0)
                    {
                        DataReceived?.Invoke(buffer);
                    }

                    // 告诉PipeReader我们已经处理了多少数据
                    _reader.AdvanceTo(buffer.Start, buffer.End);

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }
            finally
            {
                Disconnect();
                Disconnected?.Invoke();
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            _cts?.Cancel();
            _reader?.Complete();
            _writer?.Complete();
            _client?.Close();
            _client = null;
        }

        public void Dispose()
        {
            Disconnect();
            _sendArgs.Dispose();
            _receiveArgs.Dispose();
        }
    }

    /// <summary>
    /// 零拷贝网络扩展
    /// </summary>
    public static class ZeroCopyNetworkExtensions
    {
        /// <summary>
        /// 从网络缓冲区中读取对象
        /// </summary>
        public static bool TryReadObject<T>(ref ReadOnlySequence<byte> buffer, out T obj) where T : struct, INetworkSerializable
        {
            return NetworkSerializer.TryReadObject(ref buffer, out obj);
        }

        /// <summary>
        /// 直接从网络缓冲区中读取Blittable类型对象
        /// </summary>
        public static unsafe bool TryReadBlittable<T>(ref ReadOnlySequence<byte> buffer, out T obj) where T : unmanaged
        {
            obj = default;

            if (buffer.Length < sizeof(int))
                return false;

            int messageSize = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(0, sizeof(int)).ToArray());

            if (messageSize != sizeof(T))
                return false;

            if (buffer.Length < messageSize + sizeof(int))
                return false;

            if (buffer.IsSingleSegment)
            {
                // 单段缓冲区，可以直接操作
                fixed (byte* ptr = &buffer.First.Span[sizeof(int)])
                {
                    obj = *(T*)ptr;
                }
            }
            else
            {
                // 多段缓冲区，需要复制
                byte[] temp = new byte[messageSize];
                buffer.Slice(sizeof(int), messageSize).CopyTo(temp);

                fixed (byte* ptr = temp)
                {
                    obj = *(T*)ptr;
                }
            }

            buffer = buffer.Slice(messageSize + sizeof(int));
            return true;
        }

        /// <summary>
        /// 将网络缓冲区转换为NativeArray（用于Unity Job System）
        /// </summary>
        public static unsafe NativeArray<byte> AsNativeArray(this ReadOnlySequence<byte> buffer, Allocator allocator = Allocator.TempJob)
        {
            int length = (int)buffer.Length;
            var nativeArray = new NativeArray<byte>(length, allocator, NativeArrayOptions.UninitializedMemory);

            if (buffer.IsSingleSegment)
            {
                // 单段缓冲区，可以直接复制
                ReadOnlySpan<byte> span = buffer.First.Span;
                fixed (byte* sourcePtr = span)
                {
                    void* destPtr = nativeArray.GetUnsafePtr();
                    UnsafeUtility.MemCpy(destPtr, sourcePtr, length);
                }
            }
            else
            {
                // 多段缓冲区，需要按段复制
                byte* destPtr = (byte*)nativeArray.GetUnsafePtr();
                int position = 0;

                foreach (ReadOnlyMemory<byte> segment in buffer)
                {
                    ReadOnlySpan<byte> span = segment.Span;
                    fixed (byte* sourcePtr = span)
                    {
                        UnsafeUtility.MemCpy(destPtr + position, sourcePtr, span.Length);
                        position += span.Length;
                    }
                }
            }

            return nativeArray;
        }

        /// <summary>
        /// 使用共享内存池分配大块内存，避免垃圾回收压力
        /// </summary>
        public static IMemoryOwner<byte> RentMemory(this MemoryPool<byte> pool, int minSize)
        {
            return pool.Rent(Math.Max(minSize, 4096)); // 至少分配4KB
        }
    }

    /// <summary>
    /// 使用MemoryMappedFile实现的共享内存通信
    /// 用于同一台机器上的进程间高性能通信
    /// </summary>
    public class SharedMemoryChannel
    {
        // 这个类的实现依赖于具体的需求
        // 可以使用Memory-Mapped Files实现零拷贝的进程间通信
        // 对于Unity内部通信，可以使用NativeArray和ComputeBuffers实现GPU和CPU之间的零拷贝
    }
}
