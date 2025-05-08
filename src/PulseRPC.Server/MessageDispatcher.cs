using System.Reflection;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol;
using PulseRPC.Protocol.Attributes;
using PulseRPC.Protocol.Network;
using PulseRPC.Protocol.Serialization;

namespace PulseRPC.Server;

/// <summary>
/// 消息分发器，负责将接收到的消息分发到相应的处理程序
/// </summary>
public class MessageDispatcher
{
    private readonly MessageHandlerFactory _handlerFactory;
    private readonly ILogger<MessageDispatcher> _logger;

    // 消息ID到处理器的映射
    private readonly Dictionary<int, IMessageHandler> _handlers = new();

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
    public async Task DispatchAsync(int messageId, byte[] data, SessionContext context)
    {
        // 获取处理器类型
        var handlerType = _handlerFactory.GetHandlerType(messageId);
        if (handlerType == null)
        {
            _logger.LogWarning("找不到消息ID {MessageId} 的处理器", messageId);

            // 尝试记录更多有用的诊断信息
            if (data.Length > 0)
            {
                try
                {
                    _logger.LogDebug("消息长度: {Length} 字节", data.Length);

                    // 输出前32个字节的十六进制表示，帮助调试
                    var dataPrefix = data.Length > 32 ? data.Take(32).ToArray() : data;
                    _logger.LogDebug("消息数据前缀: {DataPrefix}",
                        BitConverter.ToString(dataPrefix).Replace("-", " "));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "尝试记录消息数据时出错");
                }
            }

            return;
        }

        try
        {
            _logger.LogDebug("正在反序列化消息ID {MessageId}", messageId);

            // 获取消息类型
            var messageType = MessageRegistry.GetMessageType(messageId);
            if (messageType == null)
            {
                _logger.LogWarning("找不到消息ID {MessageId} 的类型", messageId);
                return;
            }

            // 反序列化消息
            var message = MessageSerializer.Deserialize(messageId, data);

            _logger.LogDebug("成功反序列化消息ID {MessageId}, 类型: {MessageType}",
                messageId, message.GetType().Name);

            // 获取处理器实例
            var handler = _handlerFactory.GetOrCreate(handlerType);

            // 调用处理方法
            await InvokeHandlerAsync(handler, context, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理消息 {MessageId} 时发生错误", messageId);
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
    private async Task InvokeHandlerAsync(IMessageHandler handler, SessionContext context, object message)
    {
        var handlerType = handler.GetType();
        var messageType = message.GetType();

        // 查找并缓存泛型HandleAsync方法
        if (!_genericHandleMethodCache.TryGetValue(handlerType, out var handleMethod))
        {
            // 查找泛型接口实现
            var interfaceType = handlerType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType &&
                                    i.GetGenericTypeDefinition() == typeof(IMessageHandler<>) &&
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
        await (Task)handleMethod.Invoke(handler, new[] { context, message })!;
    }

    /// <summary>
    /// 注册处理器类型
    /// </summary>
    /// <typeparam name="TMessage">消息类型</typeparam>
    /// <typeparam name="THandler">处理器类型</typeparam>
    public void RegisterHandler<TMessage, THandler>()
        where TMessage : class, IMessage
        where THandler : class, IMessageHandler<TMessage>
    {
        // 获取消息属性
        var messageAttr = typeof(TMessage).GetCustomAttribute<MessageAttribute>();
        if (messageAttr == null)
        {
            throw new InvalidOperationException($"消息类型 {typeof(TMessage).Name} 未标记 MessageAttribute");
        }

        var messageTypeInfo = new MessageTypeInfo(messageAttr.Id, typeof(TMessage));
        _handlerFactory.RegisterHandler(messageTypeInfo, typeof(THandler));
    }

    /// <summary>
    /// 注册处理器类型
    /// </summary>
    /// <param name="messageId">消息ID</param>
    /// <param name="messageType">消息类型</param>
    /// <param name="handlerType">处理器类型</param>
    public void RegisterHandler(int messageId, Type messageType, Type handlerType)
    {
        var messageTypeInfo = new MessageTypeInfo(messageId, messageType);
        _handlerFactory.RegisterHandler(messageTypeInfo, handlerType);
    }

    /// <summary>
    /// 通过反射自动注册程序集中的所有消息处理器
    /// </summary>
    /// <param name="assembly">包含处理器的程序集</param>
    public void RegisterHandlersFromAssembly(Assembly assembly)
    {
        try
        {
            // 查找所有实现了IMessageHandler<>的类型
            var handlerTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces()
                    .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMessageHandler<>)));

            foreach (var handlerType in handlerTypes)
            {
                // 查找实现的IMessageHandler<>接口
                var handlerInterface = handlerType.GetInterfaces()
                    .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMessageHandler<>));

                // 获取消息类型
                var messageType = handlerInterface.GetGenericArguments()[0];

                // 获取消息ID属性
                var messageAttr = messageType.GetCustomAttribute<MessageAttribute>();
                if (messageAttr == null)
                {
                    _logger.LogWarning("忽略处理器 {HandlerType}，消息类型 {MessageType} 未标记 MessageAttribute",
                        handlerType.Name, messageType.Name);
                    continue;
                }

                // 注册处理器
                RegisterHandler(messageAttr.Id, messageType, handlerType);
                _logger.LogInformation("自动注册处理器: {HandlerType} -> 消息: {MessageType} (ID={MessageId})",
                    handlerType.Name, messageType.Name, messageAttr.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从程序集 {Assembly} 注册处理器时出错", assembly.FullName);
            throw;
        }
    }

    /// <summary>
    /// 注册当前运行的应用程序的所有消息处理器
    /// </summary>
    public void RegisterHandlersFromCurrentAppDomain()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // 忽略系统程序集和第三方库程序集
            if (!assembly.FullName!.StartsWith("System.") &&
                !assembly.FullName.StartsWith("Microsoft.") &&
                !assembly.IsDynamic)
            {
                try
                {
                    RegisterHandlersFromAssembly(assembly);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "从程序集 {Assembly} 注册处理器时出错，已跳过", assembly.FullName);
                }
            }
        }
    }
}
