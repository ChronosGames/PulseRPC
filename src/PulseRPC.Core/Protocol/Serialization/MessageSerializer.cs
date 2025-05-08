using System.Collections.Concurrent;
using System.Reflection;
using MemoryPack;

namespace PulseRPC.Protocol.Serialization;

/// <summary>
/// 消息序列化助手类
/// </summary>
public static partial class MessageSerializer
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> _deserializerCache = new();
    private const string MessageTypeNotFoundError = "找不到ID为{0}的消息类型";
    private const string DeserializeMethodNotFoundError = "找不到类型 {0} 的反序列化方法";

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

    public static object Deserialize(int messageId, byte[] data)
    {
        var messageType = GetAndValidateMessageType(messageId);

        try
        {
            // 尝试使用 MemoryPackSerializer 直接反序列化
            var genericMethod = typeof(MemoryPackSerializer).GetMethod(nameof(MemoryPackSerializer.Deserialize), new[] { typeof(byte[]) });
            if (genericMethod != null)
            {
                var method = genericMethod.MakeGenericMethod(messageType);
                var result = method.Invoke(null, new object[] { data });
                if (result != null)
                {
                    return result;
                }
            }

            // 如果直接反序列化失败，则使用缓存的反序列化方法
            var deserializeMethod = GetDeserializeMethod(messageType);
            return InvokeDeserializer(deserializeMethod, data);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"反序列化消息 {messageId} 时出错: {ex.Message}");
            Console.WriteLine($"消息类型: {messageType.FullName}");
            Console.WriteLine($"数据长度: {data.Length} 字节");
            Console.WriteLine($"异常堆栈: {ex.StackTrace}");

            throw new MessageDeserializationException(
                $"反序列化消息 {messageId} ({messageType.Name}) 时出错: {ex.Message}", ex);
        }
    }

    private static Type GetAndValidateMessageType(int messageId)
    {
        var messageType = MessageRegistry.GetMessageType(messageId);
        if (messageType == null)
        {
            throw new MessageDeserializationException(string.Format(MessageTypeNotFoundError, messageId));
        }

        return messageType;
    }

    private static MethodInfo GetDeserializeMethod(Type messageType)
    {
        return _deserializerCache.GetOrAdd(messageType, type =>
        {
            var method = typeof(MemoryPackSerializer)
                .GetMethod(nameof(MemoryPackSerializer.Deserialize), [typeof(byte[])])
                ?.MakeGenericMethod(type);

            if (method == null)
            {
                throw new MessageDeserializationException(
                    string.Format(DeserializeMethodNotFoundError, messageType.Name));
            }

            return method;
        });
    }

    private static object InvokeDeserializer(MethodInfo method, byte[] data)
    {
        try
        {
            return method.Invoke(null, [data])!;
        }
        catch (Exception ex)
        {
            throw new MessageDeserializationException("反序列化过程中发生错误", ex);
        }
    }
}

public class MessageDeserializationException : Exception
{
    public MessageDeserializationException(string message) : base(message) { }

    public MessageDeserializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
