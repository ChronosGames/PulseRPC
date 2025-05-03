using MemoryPack;

namespace PulseRPC.Protocol.Serialization;

/// <summary>
/// 消息序列化助手类
/// </summary>
public static partial class MessageSerializer
{
    /// <summary>
    /// 序列化消息
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="message">消息实例</param>
    /// <returns>序列化后的字节数组</returns>
    public static byte[] Serialize<T>(T message) where T : class
    {
        return MemoryPackSerializer.Serialize(message);
    }

    /// <summary>
    /// 反序列化消息
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="data">序列化数据</param>
    /// <returns>反序列化的消息对象</returns>
    public static T? Deserialize<T>(byte[] data) where T : class
    {
        return MemoryPackSerializer.Deserialize<T>(data);
    }

    /// <summary>
    /// 根据消息ID反序列化消息
    /// </summary>
    /// <param name="messageId">消息ID</param>
    /// <param name="data">序列化数据</param>
    /// <returns>反序列化的消息对象</returns>
    /// <exception cref="InvalidOperationException">当找不到对应的消息类型时抛出</exception>
    public static object Deserialize(int messageId, byte[] data)
    {
        // 该方法将被代码生成器扩展，根据消息ID反序列化为对应的类型
        // 这里提供一个基本实现，实际使用时应由代码生成器生成完整的switch语句

        var messageType = MessageRegistry.GetMessageType(messageId);
        if (messageType == null)
        {
            throw new InvalidOperationException($"找不到ID为{messageId}的消息类型");
        }

        // 使用通用反射方法反序列化
        // 注意：这不是最佳性能的实现，应该被代码生成的强类型转换替代
        var method = typeof(MemoryPackSerializer)
            .GetMethod(nameof(MemoryPackSerializer.Deserialize), [typeof(byte[])])
            ?.MakeGenericMethod(messageType);
        if (method == null)
        {
            throw new InvalidOperationException($"找不到反序列化方法");
        }

        return method.Invoke(null, [data])!;
    }
}
