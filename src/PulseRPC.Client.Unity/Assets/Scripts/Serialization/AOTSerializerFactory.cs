using System;
using System.Collections.Generic;
using MemoryPack;
using PulseRPC.Protocol.Serialization;

namespace PulseRPC.Serialization
{
    /// <summary>
    /// AOT友好的序列化器工厂，为IL2CPP环境提供序列化支持
    /// </summary>
    public static class AOTSerializerFactory
    {
        private static readonly Dictionary<Type, object> _serializerCache = new Dictionary<Type, object>();

        /// <summary>
        /// 获取指定类型的序列化器
        /// </summary>
        /// <typeparam name="T">要序列化的类型</typeparam>
        /// <returns>序列化器实例</returns>
        public static ISerializer<T> GetSerializer<T>()
        {
            var type = typeof(T);

            if (_serializerCache.TryGetValue(type, out var serializer))
            {
                return (ISerializer<T>)serializer;
            }

            // 根据类型创建对应的序列化器
            ISerializer<T> newSerializer;

            if (type.IsValueType && !type.IsPrimitive)
            {
                // 值类型使用结构体序列化器
                newSerializer = new ValueTypeSerializer<T>();
            }
            else
            {
                // 引用类型使用MemoryPack序列化器
                newSerializer = new MemoryPackSerializer<T>();
            }

            _serializerCache[type] = newSerializer;
            return newSerializer;
        }

        /// <summary>
        /// 序列化器接口
        /// </summary>
        /// <typeparam name="T">要序列化的类型</typeparam>
        public interface ISerializer<T>
        {
            byte[] Serialize(T value);
            T Deserialize(byte[] data);
        }

        /// <summary>
        /// 值类型序列化器，使用零拷贝方式序列化结构体
        /// </summary>
        /// <typeparam name="T">要序列化的值类型</typeparam>
        private class ValueTypeSerializer<T> : ISerializer<T> where T : struct
        {
            public byte[] Serialize(T value)
            {
                return UnityMessageSerializer.Serialize(value);
            }

            public T Deserialize(byte[] data)
            {
                return UnityMessageSerializer.Deserialize<T>(data);
            }
        }

        /// <summary>
        /// MemoryPack序列化器，使用MemoryPack序列化引用类型
        /// </summary>
        /// <typeparam name="T">要序列化的类型</typeparam>
        private class MemoryPackSerializer<T> : ISerializer<T>
        {
            public byte[] Serialize(T value)
            {
                return MemoryPackSerializer.Serialize(value);
            }

            public T Deserialize(byte[] data)
            {
                var result = MemoryPackSerializer.Deserialize<T>(data);
                if (result == null && typeof(T).IsValueType)
                {
                    throw new InvalidOperationException($"反序列化{typeof(T).Name}失败");
                }
                return result;
            }
        }
    }
}
