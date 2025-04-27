using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace UnityTCP.Serialization
{
    /// <summary>
    /// 高性能序列化接口 - 定义可序列化到网络的对象
    /// </summary>
    public interface INetworkSerializable
    {
        /// <summary>
        /// 获取序列化后的大小（字节）
        /// </summary>
        int GetSerializedSize();

        /// <summary>
        /// 将对象序列化到目标缓冲区
        /// </summary>
        /// <param name="writer">目标缓冲区</param>
        void Serialize(ref NetworkWriter writer);

        /// <summary>
        /// 从缓冲区反序列化对象
        /// </summary>
        /// <param name="reader">源缓冲区</param>
        void Deserialize(ref NetworkReader reader);
    }

    /// <summary>
    /// 网络对象序列化标记，用于AOT编译优化
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct)]
    public class NetworkObjectAttribute : Attribute { }

    /// <summary>
    /// 序列化工具类，提供直接序列化和反序列化对象的功能
    /// </summary>
    public static class NetworkSerializer
    {
        // ReSharper disable once InconsistentNaming
        private static readonly ArrayPool<byte> s_bufferPool = ArrayPool<byte>.Shared;

        /// <summary>
        /// 序列化对象到字节数组
        /// </summary>
        public static byte[] Serialize<T>(T obj) where T : struct, INetworkSerializable
        {
            var size = obj.GetSerializedSize();
            var buffer = s_bufferPool.Rent(size);

            try
            {
                var writer = new NetworkWriter(buffer.AsSpan(0, size));
                obj.Serialize(ref writer);

                var result = new byte[writer.Position];
                buffer.AsSpan(0, writer.Position).CopyTo(result);
                return result;
            }
            finally
            {
                s_bufferPool.Return(buffer);
            }
        }

        /// <summary>
        /// 从字节数组反序列化对象
        /// </summary>
        public static T Deserialize<T>(byte[] data) where T : struct, INetworkSerializable
        {
            var reader = new NetworkReader(data);
            var obj = new T();
            obj.Deserialize(ref reader);
            return obj;
        }

        /// <summary>
        /// 序列化对象直接到管道写入器（Zero-Copy技术）
        /// </summary>
        public static ValueTask<FlushResult> SerializeDirectAsync<T>(T obj, PipeWriter writer) where T : struct, INetworkSerializable
        {
            var size = obj.GetSerializedSize();
            var memory = writer.GetMemory(size + sizeof(int));

            // 写入消息长度前缀
            BinaryPrimitives.WriteInt32LittleEndian(memory.Span, size);

            // 直接在PipeWriter的缓冲区上进行序列化
            var netWriter = new NetworkWriter(memory.Span[sizeof(int)..]);
            obj.Serialize(ref netWriter);

            writer.Advance(size + sizeof(int));
            return writer.FlushAsync();
        }

        /// <summary>
        /// 尝试从管道读取器读取并反序列化对象（Zero-Copy技术）
        /// </summary>
        public static bool TryReadObject<T>(ref ReadOnlySequence<byte> buffer, out T obj) where T : struct, INetworkSerializable
        {
            obj = default;

            if (buffer.Length < sizeof(int))
                return false;

            int messageSize = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(0, sizeof(int)).ToArray());

            if (buffer.Length < messageSize + sizeof(int))
                return false;

            if (buffer.IsSingleSegment)
            {
                // 单段缓冲区，可以直接操作
                var reader = new NetworkReader(buffer.Slice(sizeof(int), messageSize).First.Span);
                obj = new T();
                obj.Deserialize(ref reader);
            }
            else
            {
                // 多段缓冲区，需要复制
                byte[] temp = new byte[messageSize];
                buffer.Slice(sizeof(int), messageSize).CopyTo(temp);

                var reader = new NetworkReader(temp);
                obj = new T();
                obj.Deserialize(ref reader);
            }

            buffer = buffer.Slice(messageSize + sizeof(int));
            return true;
        }

        /// <summary>
        /// 使用不安全代码进行零拷贝序列化（仅适用于blittable类型）
        /// </summary>
        public static unsafe void SerializeBlittable<T>(T obj, Span<byte> buffer) where T : unmanaged
        {
            fixed (void* bufferPtr = &buffer[0])
            {
                *(T*)bufferPtr = obj;
            }
        }

        /// <summary>
        /// 使用不安全代码进行零拷贝反序列化（仅适用于blittable类型）
        /// </summary>
        public static unsafe T DeserializeBlittable<T>(ReadOnlySpan<byte> buffer) where T : unmanaged
        {
            fixed (byte* bufferPtr = buffer)
            {
                return *(T*)bufferPtr;
            }
        }

        /// <summary>
        /// 使用Unity的NativeArray进行高性能内存管理，可以直接与Unity的Job System集成
        /// </summary>
        public static unsafe NativeArray<byte> SerializeToNativeArray<T>(T obj) where T : struct, INetworkSerializable
        {
            int size = obj.GetSerializedSize();
            var nativeArray = new NativeArray<byte>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            byte[] tempBuffer = s_bufferPool.Rent(size);
            try
            {
                var writer = new NetworkWriter(tempBuffer.AsSpan(0, size));
                obj.Serialize(ref writer);

                // 复制到Native内存
                fixed (byte* sourcePtr = tempBuffer)
                {
                    void* destPtr = nativeArray.GetUnsafePtr();
                    UnsafeUtility.MemCpy(destPtr, sourcePtr, size);
                }

                return nativeArray;
            }
            finally
            {
                s_bufferPool.Return(tempBuffer);
            }
        }
    }

    // 示例：定义一个游戏对象位置同步的网络消息
    [NetworkObject]
    public struct PlayerPositionMessage : INetworkSerializable
    {
        public uint PlayerId;
        public Vector3 Position;
        public Quaternion Rotation;
        public byte PlayerState;
        public float Speed;

        public int GetSerializedSize()
        {
            return sizeof(uint) + // PlayerId
                   3 * sizeof(float) + // Position (Vector3)
                   4 * sizeof(float) + // Rotation (Quaternion)
                   sizeof(byte) + // PlayerState
                   sizeof(float); // Speed
        }

        public void Serialize(ref NetworkWriter writer)
        {
            writer.WriteUInt32(PlayerId);
            writer.WriteVector3(Position);
            writer.WriteQuaternion(Rotation);
            writer.WriteByte(PlayerState);
            writer.WriteFloat(Speed);
        }

        public void Deserialize(ref NetworkReader reader)
        {
            PlayerId = reader.ReadUInt32();
            Position = reader.ReadVector3();
            Rotation = reader.ReadQuaternion();
            PlayerState = reader.ReadByte();
            Speed = reader.ReadFloat();
        }
    }
}
