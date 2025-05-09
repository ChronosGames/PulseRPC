using System.Reflection;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

/// <summary>
/// 消息分发器，负责将接收到的消息分发到相应的处理程序
/// </summary>
public class MessageDispatcher
{
    private readonly MessageHandlerFactory _handlerFactory;
    private readonly ILogger<MessageDispatcher> _logger;

    // 缓存泛型处理方法的调用委托
    private readonly Dictionary<Type, MethodInfo> _genericHandleMethodCache = new();

    /// <summary>
    /// 初始化消息分发器
    /// </summary>
    /// <param name="handlerFactory">处理器工厂</param>
    /// <param name="logger">日志记录器</param>
    public MessageDispatcher(MessageHandlerFactory handlerFactory, ILogger<MessageDispatcher> logger)
    {
        _handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 分发消息到处理器
    /// </summary>
    public async Task DispatchAsync(Command command, NetworkSession context)
    {
        // 获取处理器类型
        var handlerType = _handlerFactory.GetHandlerType(command.GetType());
        if (handlerType == null)
        {
            _logger.LogWarning($"找不到消息ID {command.GetType().Name} 的处理器");
            return;
        }

        try
        {
            // 获取处理器实例
            var handler = _handlerFactory.GetOrCreate(handlerType);

            // 调用处理方法
            await InvokeHandlerAsync(handler, context, command);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"处理消息 {command.GetType().Name} 时发生错误");
            throw;
        }
    }

    /// <summary>
    /// 调用处理器的HandleAsync方法
    /// </summary>
    /// <param name="handler">处理器实例</param>
    /// <param name="context">会话上下文</param>
    /// <param name="message">消息对象</param>
    /// <returns>处理任务</returns>
    private async Task InvokeHandlerAsync(IMessageHandler handler, NetworkSession context, object message)
    {
        var handlerType = handler.GetType();
        var messageType = message.GetType();

        // 查找并缓存泛型HandleAsync方法
        if (!_genericHandleMethodCache.TryGetValue(handlerType, out var handleMethod))
        {
            // 查找泛型接口实现
            var interfaceType = handlerType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType &&
                                    i.GetGenericTypeDefinition() == typeof(ICommandHandler<>) &&
                                    i.GetGenericArguments()[0] == messageType);

            if (interfaceType == null)
            {
                throw new InvalidOperationException($"处理器 {handlerType.Name} 没有实现 IMessageHandler<{messageType.Name}>");
            }

            // 获取HandleAsync方法
            handleMethod = interfaceType.GetMethod("HandleAsync") ??
                throw new InvalidOperationException($"接口 {interfaceType.Name} 没有定义 HandleAsync 方法");

            _genericHandleMethodCache[handlerType] = handleMethod;
        }

        // 调用HandleAsync方法
        await (Task)handleMethod.Invoke(handler, [context, message])!;
    }
}
