using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

namespace PulseRPC.Serialization
{
    /// <summary>
    /// PulseRPC 默认序列化器
    /// </summary>
    public class PulseRPCSerializer : ISerializer
    {
        private readonly BinaryFormatter _formatter;

        /// <summary>
        /// 构造函数
        /// </summary>
        public PulseRPCSerializer()
        {
            _formatter = new BinaryFormatter();
        }

        /// <summary>
        /// 序列化对象为字节数组
        /// </summary>
        public byte[] Serialize(object obj)
        {
            if (obj == null)
                return Array.Empty<byte>();

            try
            {
                using (var stream = new MemoryStream())
                {
                    _formatter.Serialize(stream, obj);
                    return stream.ToArray();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"序列化失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 序列化特定类型的对象为字节数组
        /// </summary>
        public byte[] Serialize<T>(T obj)
        {
            return Serialize((object)obj);
        }

        /// <summary>
        /// 反序列化字节数组为对象
        /// </summary>
        public object Deserialize(Type type, byte[] data)
        {
            if (data == null || data.Length == 0)
                return null;

            try
            {
                using (var stream = new MemoryStream(data))
                {
                    return _formatter.Deserialize(stream);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"反序列化失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 反序列化字节数组为特定类型的对象
        /// </summary>
        public T Deserialize<T>(byte[] data)
        {
            return (T)Deserialize(typeof(T), data);
        }
    }

    /// <summary>
    /// JSON序列化器
    /// </summary>
    public class JsonSerializer : ISerializer
    {
        /// <summary>
        /// 序列化对象为字节数组
        /// </summary>
        public byte[] Serialize(object obj)
        {
            if (obj == null)
                return Array.Empty<byte>();

            try
            {
                string json = JsonUtility.ToJson(obj);
                return System.Text.Encoding.UTF8.GetBytes(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"JSON序列化失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 序列化特定类型的对象为字节数组
        /// </summary>
        public byte[] Serialize<T>(T obj)
        {
            return Serialize((object)obj);
        }

        /// <summary>
        /// 反序列化字节数组为对象
        /// </summary>
        public object Deserialize(Type type, byte[] data)
        {
            if (data == null || data.Length == 0)
                return null;

            try
            {
                string json = System.Text.Encoding.UTF8.GetString(data);
                return JsonUtility.FromJson(json, type);
            }
            catch (Exception ex)
            {
                Debug.LogError($"JSON反序列化失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 反序列化字节数组为特定类型的对象
        /// </summary>
        public T Deserialize<T>(byte[] data)
        {
            return (T)Deserialize(typeof(T), data);
        }
    }
}
