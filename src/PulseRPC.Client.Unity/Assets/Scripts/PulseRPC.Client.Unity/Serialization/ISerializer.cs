using System;

namespace PulseRPC.Serialization
{
    /// <summary>
    /// 序列化器接口
    /// </summary>
    public interface ISerializer
    {
        /// <summary>
        /// 序列化对象为字节数组
        /// </summary>
        /// <param name="obj">要序列化的对象</param>
        /// <returns>序列化后的字节数组</returns>
        byte[] Serialize(object obj);

        /// <summary>
        /// 序列化特定类型的对象为字节数组
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="obj">要序列化的对象</param>
        /// <returns>序列化后的字节数组</returns>
        byte[] Serialize<T>(T obj);

        /// <summary>
        /// 反序列化字节数组为对象
        /// </summary>
        /// <param name="type">目标类型</param>
        /// <param name="data">序列化的字节数组</param>
        /// <returns>反序列化后的对象</returns>
        object Deserialize(Type type, byte[] data);

        /// <summary>
        /// 反序列化字节数组为特定类型的对象
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="data">序列化的字节数组</param>
        /// <returns>反序列化后的对象</returns>
        T Deserialize<T>(byte[] data);
    }
}
