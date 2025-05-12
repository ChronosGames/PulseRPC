using System.Reflection;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

/// <summary>
/// 消息分发器，负责将接收到的消息分发到相应的处理程序
/// </summary>
public class MessageDispatcher : IMessageDispatcher
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
    public async Task DispatchAsync(NetworkSession context, IPacket packet)
    {
        // 获取处理器类型
        var handlerType = _handlerFactory.GetHandlerType(packet.GetType());
        if (handlerType == null)
        {
            _logger.LogWarning("找不到消息ID {Name} 的处理器", packet.GetType().Name);
            return;
        }

        try
        {
            // 获取处理器实例
            var handler = _handlerFactory.GetOrCreate(handlerType);

            // 调用处理方法
            await InvokeHandlerAsync(handler, context, packet);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理消息 {Name} 时发生错误", packet.GetType().Name);
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
    private async Task InvokeHandlerAsync(IMessageHandler handler, NetworkSession context, IPacket message)
    {
        switch (message)
        {
            case CommandBatch batch:
            {
                // 直接创建任务数组，避免List<Task>的额外开销
                var commands = batch.Commands;
                var taskCount = commands.Length;

                if (taskCount == 0)
                {
                    break; // 如果没有命令，立即退出
                }
                else if (taskCount == 1)
                {
                    // 单个命令不需要并行处理
                    await InvokeHandlerAsync(handler, context, commands[0]);
                }
                else
                {
                    // 预分配固定大小数组，避免List动态扩容
                    var tasks = new Task[taskCount];

                    // 直接填充数组而不是使用LINQ
                    for (var i = 0; i < taskCount; i++)
                    {
                        tasks[i] = InvokeHandlerAsync(handler, context, commands[i]);
                    }

                    // 等待所有处理完成
                    await Task.WhenAll(tasks);
                }
                break;
            }
            case Command command:
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
                        throw new InvalidOperationException($"处理器 {handlerType.Name} 没有实现 ICommandHandler<{messageType.Name}>");
                    }

                    // 获取HandleAsync方法
                    handleMethod = interfaceType.GetMethod("HandleAsync") ??
                                   throw new InvalidOperationException($"接口 {interfaceType.Name} 没有定义 HandleAsync 方法");

                    _genericHandleMethodCache[handlerType] = handleMethod;
                }

                // 调用HandleAsync方法
                await (Task)handleMethod.Invoke(handler, [context, command])!;
                break;
            }
            case Request request:
            {
                var handlerType = handler.GetType();
                var messageType = message.GetType();

                // 查找并缓存泛型HandleAsync方法
                if (!_genericHandleMethodCache.TryGetValue(handlerType, out var handleMethod))
                {
                    // 查找泛型接口实现
                    var interfaceType = handlerType.GetInterfaces()
                        .FirstOrDefault(i => i.IsGenericType &&
                                             i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) &&
                                             i.GetGenericArguments()[0] == messageType);

                    if (interfaceType == null)
                    {
                        throw new InvalidOperationException($"处理器 {handlerType.Name} 没有实现 IRequestHandler<{messageType.Name}, ResponseType>");
                    }

                    // 获取HandleAsync方法
                    handleMethod = interfaceType.GetMethod("HandleAsync") ??
                                   throw new InvalidOperationException($"接口 {interfaceType.Name} 没有定义 HandleAsync 方法");

                    _genericHandleMethodCache[handlerType] = handleMethod;
                }

                // 调用HandleAsync方法
                await (Task)handleMethod.Invoke(handler, [context, request])!;
                break;
            }
        }
    }
}
