using PulseRPC.Protocol.Messages;

namespace PulseRPC.Protocol.Serialization;

/// <summary>
/// 消息注册表，管理消息ID和类型的映射关系
/// </summary>
public static partial class MessageRegistry
{
    // 类型ID到类型的映射
    private static readonly Dictionary<int, Type> _messageTypes = new Dictionary<int, Type>();

    // 消息ID到处理器类型的映射
    private static readonly Dictionary<int, Type> _handlerTypes = new Dictionary<int, Type>();

    // 类型到消息ID的映射
    private static readonly Dictionary<Type, int> _typeToIdMap = new Dictionary<Type, int>();

    /// <summary>
    /// 静态构造函数，初始化注册表
    /// 注意：此方法会被代码生成器重写以包含所有消息类型
    /// </summary>
    static MessageRegistry()
    {
    }

    /// <summary>
    /// 注册消息类型
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="messageId">消息ID</param>
    private static void RegisterMessageType<T>(int messageId) where T : IMessage
    {
        var type = typeof(T);
        _messageTypes[messageId] = type;
        _typeToIdMap[type] = messageId;
    }

    /// <summary>
    /// 根据消息ID获取消息类型
    /// </summary>
    /// <param name="messageId">消息ID</param>
    /// <returns>消息类型，如果不存在则返回null</returns>
    public static Type? GetMessageType(int messageId)
    {
        return _messageTypes.GetValueOrDefault(messageId);
    }

    /// <summary>
    /// 根据消息ID获取处理器类型
    /// </summary>
    /// <param name="messageId">消息ID</param>
    /// <returns>处理器类型，如果不存在则返回null</returns>
    public static Type? GetHandlerType(int messageId)
    {
        return _handlerTypes.GetValueOrDefault(messageId);
    }

    /// <summary>
    /// 获取消息类型的ID
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <returns>消息ID</returns>
    /// <exception cref="InvalidOperationException">当找不到对应的消息类型时抛出</exception>
    public static int GetMessageId<T>() where T : IMessage
    {
        var type = typeof(T);
        if (_typeToIdMap.TryGetValue(type, out var id))
        {
            return id;
        }

        throw new InvalidOperationException($"消息类型{type.Name}未注册");
    }

    public static int GetMessageId(Type type)
    {
        if (_typeToIdMap.TryGetValue(type, out var id))
        {
            return id;
        }

        throw new InvalidOperationException($"消息类型{type.Name}未注册");
    }
}
