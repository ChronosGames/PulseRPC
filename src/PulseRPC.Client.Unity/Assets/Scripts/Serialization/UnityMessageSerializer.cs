using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MemoryPack;
using PulseRPC.Protocol.Serialization;
using UnityEngine;

namespace PulseRPC.Serialization
{
    /// <summary>
    /// Unity特定的消息序列化器，提供AOT支持和值类型的零拷贝序列化
    /// </summary>
    public static class UnityMessageSerializer
    {
        /// <summary>
        /// 序列化消息，支持值类型的零拷贝
        /// </summary>
        /// <typeparam name="T">消息类型</typeparam>
        /// <param name="message">消息实例</param>
        /// <returns>序列化后的字节数组</returns>
        public static byte[] Serialize<T>(T message) where T : struct
        {
            // 对于值类型，使用零拷贝方式序列化
            if (typeof(T).IsValueType && !typeof(T).IsPrimitive)
            {
                return ZeroCopySerialize(message);
            }

            // 对于其他类型，使用标准序列化
            return MemoryPackSerializer.Serialize(message);
        }

        /// <summary>
        /// 反序列化消息
        /// </summary>
        /// <typeparam name="T">消息类型</typeparam>
        /// <param name="data">序列化数据</param>
        /// <returns>反序列化的消息对象</returns>
        public static T Deserialize<T>(byte[] data)
        {
            // 对于值类型，使用零拷贝方式反序列化
            if (typeof(T).IsValueType && !typeof(T).IsPrimitive)
            {
                return ZeroCopyDeserialize<T>(data);
            }

            // 对于引用类型，使用标准反序列化
            if (typeof(T).IsClass)
            {
                return MemoryPackSerializer.Deserialize<T>(data);
            }

            // 对于基本类型，使用标准反序列化
            var result = MemoryPackSerializer.Deserialize<T>(data);
            if (result == null)
            {
                throw new InvalidOperationException($"反序列化{typeof(T).Name}失败");
            }
            return result;
        }

        /// <summary>
        /// 值类型的零拷贝序列化
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="value">要序列化的值</param>
        /// <returns>序列化后的字节数组</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe byte[] ZeroCopySerialize<T>(T value) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] array = new byte[size];

            fixed (byte* ptr = array)
            {
                Marshal.StructureToPtr(value, (IntPtr)ptr, false);
            }

            return array;
        }

        /// <summary>
        /// 值类型的零拷贝反序列化
        /// </summary>
        /// <typeparam name="T">值类型</typeparam>
        /// <param name="data">序列化数据</param>
        /// <returns>反序列化的值</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe T ZeroCopyDeserialize<T>(byte[] data) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            if (data.Length < size)
            {
                throw new ArgumentException($"数据长度不足，无法反序列化为{typeof(T).Name}");
            }

            T result = default;
            fixed (byte* ptr = data)
            {
                result = Marshal.PtrToStructure<T>((IntPtr)ptr);
            }

            return result;
        }
    }
}
