using System;
using MemoryPack;
using System.Collections.Concurrent;

namespace PulseRPC.Protocol.Serialization;

/// <summary>
/// PulseRPC 消息格式化器提供程序
/// </summary>
public class PulseRPCFormatterProvider
{
    private static readonly ConcurrentDictionary<Type, object> _formatters = new();
    private static readonly ConcurrentDictionary<Type, int> _messageIds = new();

    /// <summary>
    /// 注册消息类型
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="messageId">消息ID</param>
    public static void RegisterMessageType<T>(int messageId) where T : class, IMessage
    {
        var type = typeof(T);
        _messageIds[type] = messageId;
        _formatters[type] = new PulseRPCFormatter<T>(messageId);
    }

    public static int GetMessageId(Type type)
    {
        if (_messageIds.TryGetValue(type, out var messageId))
        {
            return messageId;
        }

        throw new InvalidOperationException($"消息类型 {type.Name} 未注册");
    }

    /// <summary>
    /// 获取消息ID
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <returns>消息ID</returns>
    public static int GetMessageId<T>() where T : class, IMessage
    {
        var type = typeof(T);
        if (_messageIds.TryGetValue(type, out var messageId))
        {
            return messageId;
        }

        throw new InvalidOperationException($"消息类型 {type.Name} 未注册");
    }

    /// <summary>
    /// 获取消息格式化器
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <returns>消息格式化器</returns>
    public static IMemoryPackFormatter<T> GetFormatter<T>() where T : class, IMessage
    {
        var type = typeof(T);
        if (_formatters.TryGetValue(type, out var formatter))
        {
            return (IMemoryPackFormatter<T>)formatter;
        }

        throw new InvalidOperationException($"消息类型 {type.Name} 未注册");
    }

    /// <summary>
    /// 获取消息类型
    /// </summary>
    /// <param name="messageId"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static Type GetMessageType(int messageId)
    {
        foreach (var pair in _messageIds)
        {
            if (pair.Value == messageId)
            {
                return pair.Key;
            }
        }
        throw new InvalidOperationException($"未找到消息ID {messageId} 对应的类型");
    }
}
