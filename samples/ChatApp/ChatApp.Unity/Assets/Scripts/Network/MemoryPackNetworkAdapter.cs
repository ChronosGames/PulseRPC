using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityTCP.MemoryPackIntegration
{
    /// <summary>
    /// MemoryPack适配器，将我们的网络库与MemoryPack集成
    /// </summary>
    public static class MemoryPackNetworkAdapter
    {
        /// <summary>
        /// 使用MemoryPack序列化对象并直接写入网络流
        /// </summary>
        public static ValueTask<FlushResult> SerializeToNetworkAsync<T>(PipeWriter writer, T value)
        {
            // 先序列化到byte[]以确定大小
            MemoryPack.MemoryPackSerializer.Serialize(writer, value);

            // 获取内存块用于写入长度前缀
            // var lengthBuffer = writer.GetMemory(4);

            // 写入长度前缀
            // BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer.Span, messageSize);
            // writer.Advance(4);

            // 写入序列化数据
            // var dataBuffer = writer.GetMemory(messageSize);
            // serialized.CopyTo(dataBuffer);
            // writer.Advance(messageSize);

            // 刷新管道，发送数据
            return writer.FlushAsync();
        }

        /// <summary>
        /// 尝试从网络缓冲区反序列化MemoryPack对象
        /// </summary>
        public static bool TryDeserialize<T>(ref ReadOnlySequence<byte> buffer, out T value)
        {
            value = default;

            if (buffer.Length < sizeof(int))
                return false;

            // 读取长度前缀
            var messageSize = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(0, sizeof(int)).ToArray());

            if (buffer.Length < messageSize + sizeof(int))
                return false;

            // 获取消息体
            var messageData = buffer.Slice(sizeof(int), messageSize);

            if (messageData.IsSingleSegment)
            {
                // 单段缓冲区，直接反序列化
                value = MemoryPack.MemoryPackSerializer.Deserialize<T>(messageData.First.Span);
            }
            else
            {
                // 多段缓冲区，需要复制
                var temp = new byte[messageSize];
                messageData.CopyTo(temp);
                value = MemoryPack.MemoryPackSerializer.Deserialize<T>(temp);
            }

            // 推进缓冲区位置
            buffer = buffer.Slice(messageSize + sizeof(int));
            return true;
        }

        /// <summary>
        /// 使用MemoryPack序列化对象到NativeArray（用于Unity JobSystem）
        /// </summary>
        public static unsafe NativeArray<byte> SerializeToNativeArray<T>(T value, Allocator allocator = Allocator.TempJob)
        {
            // 使用MemoryPack序列化
            var serialized = MemoryPack.MemoryPackSerializer.Serialize(value);

            // 创建带有长度前缀的NativeArray
            var totalSize = serialized.Length + sizeof(int);
            var nativeArray = new NativeArray<byte>(totalSize, allocator, NativeArrayOptions.UninitializedMemory);

            // 写入长度前缀
            fixed (byte* lengthPtr = &serialized[0])
            {
                var dstPtr = (byte*)nativeArray.GetUnsafePtr();
                *(int*)dstPtr = serialized.Length;

                // 复制序列化后的数据
                UnsafeUtility.MemCpy(dstPtr + sizeof(int), lengthPtr, serialized.Length);
            }

            return nativeArray;
        }

        /// <summary>
        /// 从NativeArray反序列化MemoryPack对象
        /// </summary>
        public static unsafe T DeserializeFromNativeArray<T>(NativeArray<byte> nativeArray)
        {
            if (!nativeArray.IsCreated || nativeArray.Length < sizeof(int))
                return default;

            // 读取长度前缀
            var srcPtr = (byte*)nativeArray.GetUnsafeReadOnlyPtr();
            var messageSize = *(int*)srcPtr;

            if (nativeArray.Length < messageSize + sizeof(int))
                return default;

            // 从NativeArray创建Span，不需要fixed语句，因为srcPtr已经是指针
            var dataSpan = new ReadOnlySpan<byte>(srcPtr + sizeof(int), messageSize);

            // 反序列化
            return MemoryPack.MemoryPackSerializer.Deserialize<T>(dataSpan);
        }
    }

    /// <summary>
    /// MemoryPack客户端扩展
    /// </summary>
    public static class MemoryPackClientExtensions
    {
        /// <summary>
        /// 扩展ZeroCopyTCPClient，添加MemoryPack支持
        /// </summary>
        public static ValueTask<FlushResult> SendMemoryPackObjectAsync<T>(this ZeroCopy.ZeroCopyTCPClient client, T value)
        {
            // 先序列化为内存数据
            var serialized = MemoryPack.MemoryPackSerializer.Serialize(value);

            // 准备网络数据（长度前缀 + 序列化数据）
            var memory = client.GetWriteMemory(serialized.Length + sizeof(int));

            // 写入长度前缀
            BinaryPrimitives.WriteInt32LittleEndian(memory.Span, serialized.Length);

            // 复制序列化数据
            serialized.CopyTo(memory[sizeof(int)..]);

            // 提交数据
            client.AdvanceWriter(serialized.Length + sizeof(int));

            return client.FlushAsync();
        }
    }

    /// <summary>
    /// MemoryPack服务器扩展
    /// </summary>
    public static class MemoryPackServerExtensions
    {
        /// <summary>
        /// 扩展EnhancedTCPServer，添加MemoryPack广播支持
        /// </summary>
        public static async Task BroadcastMemoryPackObjectAsync<T>(this ZeroCopy.EnhancedTCPServer server, T value)
        {
            // 先序列化为内存数据
            byte[] serialized = MemoryPack.MemoryPackSerializer.Serialize(value);

            // 创建带有长度前缀的完整消息
            byte[] message = new byte[serialized.Length + sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(message, 0, sizeof(int)), serialized.Length);
            Buffer.BlockCopy(serialized, 0, message, sizeof(int), serialized.Length);

            // 广播消息
            await server.BroadcastDataAsync(message);
        }

        /// <summary>
        /// 扩展EnhancedTCPServer，添加MemoryPack单客户端发送支持
        /// </summary>
        public static async Task SendMemoryPackObjectToClientAsync<T>(this ZeroCopy.EnhancedTCPServer server, string clientId, T value)
        {
            // 先序列化为内存数据
            byte[] serialized = MemoryPack.MemoryPackSerializer.Serialize(value);

            // 创建带有长度前缀的完整消息
            byte[] message = new byte[serialized.Length + sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(message, 0, sizeof(int)), serialized.Length);
            Buffer.BlockCopy(serialized, 0, message, sizeof(int), serialized.Length);

            // 发送消息到指定客户端
            await server.SendDataToClientAsync(clientId, message);
        }
    }

    // 实现示例，这需要改为实际的ZeroCopyTCPClient类方法
    public static class ClientExtensions
    {
        public static Memory<byte> GetWriteMemory(this ZeroCopy.ZeroCopyTCPClient client, int minSize)
        {
            // 实际实现应该从客户端的PipeWriter获取内存
            return new Memory<byte>(new byte[minSize]);
        }

        public static void AdvanceWriter(this ZeroCopy.ZeroCopyTCPClient client, int count)
        {
            // 实际实现应该前进客户端的PipeWriter
        }

        public static ValueTask<FlushResult> FlushAsync(this ZeroCopy.ZeroCopyTCPClient client)
        {
            // 实际实现应该刷新客户端的PipeWriter
            return default;
        }
    }

    // 实现示例，这需要改为实际的EnhancedTCPServer类方法
    public static class ServerExtensions
    {
        public static Task BroadcastDataAsync(this ZeroCopy.EnhancedTCPServer server, byte[] data)
        {
            // 实际实现应该广播数据到所有客户端
            return Task.CompletedTask;
        }

        public static Task SendDataToClientAsync(this ZeroCopy.EnhancedTCPServer server, string clientId, byte[] data)
        {
            // 实际实现应该发送数据到指定客户端
            return Task.CompletedTask;
        }
    }
}
