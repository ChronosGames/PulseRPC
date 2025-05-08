using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server;

/// <summary>
/// 消息处理器工厂，负责创建和管理消息处理器实例
/// </summary>
public class MessageHandlerFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MessageHandlerFactory> _logger;
    private readonly ConcurrentDictionary<Type, IMessageHandler> _handlerInstances = new();

    /// <summary>
    /// 消息ID到处理器类型的映射
    /// </summary>
    private readonly ConcurrentDictionary<int, Type> _handlerTypes = new();

    /// <summary>
    /// 类型到消息ID的映射，用于反向查找
    /// </summary>
    private readonly ConcurrentDictionary<Type, int> _messageIdByType = new();

    /// <summary>
    /// 初始化消息处理器工厂
    /// </summary>
    /// <param name="serviceProvider">服务提供程序</param>
    /// <param name="logger">日志记录器</param>
    public MessageHandlerFactory(IServiceProvider serviceProvider, ILogger<MessageHandlerFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 注册消息处理器类型
    /// </summary>
    /// <param name="messageTypeInfo">消息类型信息</param>
    /// <param name="handlerType">处理器类型</param>
    public void RegisterHandler(MessageTypeInfo messageTypeInfo, Type handlerType)
    {
        if (!typeof(IMessageHandler).IsAssignableFrom(handlerType))
        {
            throw new ArgumentException($"处理器类型 {handlerType.Name} 未实现 IMessageHandler 接口", nameof(handlerType));
        }

        _handlerTypes[messageTypeInfo.MessageId] = handlerType;
        _messageIdByType[messageTypeInfo.MessageType] = messageTypeInfo.MessageId;
        _logger.LogDebug("注册消息处理器: ID={MessageId}, 消息={MessageType}, 处理器={HandlerType}",
            messageTypeInfo.MessageId, messageTypeInfo.MessageType.Name, handlerType.Name);
    }

    /// <summary>
    /// 获取指定消息ID的处理器类型
    /// </summary>
    /// <param name="messageId">消息ID</param>
    /// <returns>处理器类型，如果不存在返回null</returns>
    public Type? GetHandlerType(int messageId)
    {
        return _handlerTypes.GetValueOrDefault(messageId);
    }

    /// <summary>
    /// 获取指定消息类型的消息ID
    /// </summary>
    /// <param name="messageType">消息类型</param>
    /// <returns>消息ID，如果不存在返回-1</returns>
    public int GetMessageId(Type messageType)
    {
        return _messageIdByType.GetValueOrDefault(messageType, -1);
    }

    /// <summary>
    /// 获取已注册的所有处理器类型
    /// </summary>
    /// <returns>处理器类型集合</returns>
    public IEnumerable<Type> GetAllHandlerTypes()
    {
        return _handlerTypes.Values;
    }

    /// <summary>
    /// 获取或创建消息处理器实例
    /// </summary>
    /// <typeparam name="T">处理器类型</typeparam>
    /// <returns>处理器实例</returns>
    public T GetOrCreate<T>() where T : class, IMessageHandler
    {
        return (T)GetOrCreate(typeof(T));
    }

    /// <summary>
    /// 获取或创建消息处理器实例
    /// </summary>
    /// <param name="handlerType">处理器类型</param>
    /// <returns>处理器实例</returns>
    public IMessageHandler GetOrCreate(Type handlerType)
    {
        return _handlerInstances.GetOrAdd(handlerType, CreateHandler);
    }

    /// <summary>
    /// 创建处理器实例
    /// </summary>
    /// <param name="handlerType">处理器类型</param>
    /// <returns>新创建的处理器实例</returns>
    private IMessageHandler CreateHandler(Type handlerType)
    {
        try
        {
            // 首先尝试从DI容器获取
            var handler = _serviceProvider.GetService(handlerType) as IMessageHandler;

            // 如果DI容器中不存在，则尝试直接创建实例
            if (handler == null)
            {
                _logger.LogDebug("从DI容器中未找到处理器 {HandlerType}，尝试直接创建实例", handlerType.Name);
                handler = (IMessageHandler)Activator.CreateInstance(handlerType)!;
            }

            return handler ?? throw new InvalidOperationException($"无法创建处理器 {handlerType.Name} 的实例");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建处理器 {HandlerType} 实例时出错", handlerType.Name);
            throw;
        }
    }
}
