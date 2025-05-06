using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Network;
using PulseRPC.Protocol.Serialization;

namespace PulseRPC.Server;

/// <summary>
/// 消息分发器，负责将接收到的消息分发到相应的处理程序
/// </summary>
public class MessageDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MessageDispatcher> _logger;

    // 缓存的处理器实例
    private readonly Dictionary<Type, object> _handlerInstances = new Dictionary<Type, object>();

    // 消息ID到处理器类型的映射
    private readonly Dictionary<int, Type> _handlerTypes = new Dictionary<int, Type>();

    /// <summary>
    /// 初始化消息分发器
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <param name="logger">日志记录器</param>
    public MessageDispatcher(IServiceProvider serviceProvider, ILogger<MessageDispatcher> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 分发消息到相应的处理程序
    /// </summary>
    /// <param name="messageId">消息ID</param>
    /// <param name="data">消息数据</param>
    /// <param name="context">会话上下文</param>
    /// <returns>处理任务</returns>
    public async Task DispatchAsync(int messageId, byte[] data, SessionContext context)
    {
        // 获取处理器类型
        var handlerType = GetHandlerType(messageId);
        if (handlerType == null)
        {
            _logger.LogWarning("找不到消息ID {MessageId} 的处理器", messageId);
            return;
        }

        try
        {
            // 通过代码生成器，这段逻辑会被替换成高性能的switch语句
            // 这里提供一个基本实现，使用反射调用处理器

            // 获取消息类型
            var messageType = MessageRegistry.GetMessageType(messageId);
            if (messageType == null)
            {
                _logger.LogWarning("找不到消息ID {MessageId} 的类型", messageId);
                return;
            }

            // 反序列化消息
            var message = MessageSerializer.Deserialize(messageId, data);

            // 获取或创建处理器实例
            var handler = GetOrCreateHandler(handlerType);

            // 获取处理方法
            var handleMethod = handlerType.GetMethod("HandleAsync");
            if (handleMethod == null)
            {
                _logger.LogError("处理器 {HandlerType} 没有实现 HandleAsync 方法", handlerType.Name);
                return;
            }

            // 调用处理方法
            await (Task)handleMethod.Invoke(handler, [context, message])!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理消息 {MessageId} 时发生错误", messageId);
            throw;
        }
    }

    /// <summary>
    /// 获取或创建处理器实例
    /// </summary>
    /// <param name="handlerType">处理器类型</param>
    /// <returns>处理器实例</returns>
    private object GetOrCreateHandler(Type handlerType)
    {
        if (_handlerInstances.TryGetValue(handlerType, out var handler))
        {
            return handler;
        }

        // 从服务容器获取或创建新实例
        handler = _serviceProvider.GetService(handlerType) ?? Activator.CreateInstance(handlerType);
        _handlerInstances[handlerType] = handler!;

        return handler!;
    }

    /// <summary>
    /// 在应用程序启动时预热处理器实例
    /// </summary>
    public void InitializeHandlers()
    {
        // 这部分在实际使用中应该由代码生成器生成
        // 预创建所有已知的处理器实例

        // 示例预热
        foreach (var handlerType in _handlerTypes.Values)
        {
            GetOrCreateHandler(handlerType);
        }
    }

    /// <summary>
    /// 注册处理器类型
    /// </summary>
    /// <param name="messageId">消息ID</param>
    /// <param name="handlerType">处理器类型</param>
    public void RegisterHandlerType(int messageId, Type? handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType, nameof(handlerType));

        _handlerTypes[messageId] = handlerType;
        _logger.LogDebug("已注册消息处理器: 消息ID={MessageId}, 处理器={HandlerType}", messageId, handlerType.Name);
    }

    /// <summary>
    /// 获取处理器类型
    /// </summary>
    /// <param name="messageId">消息ID</param>
    /// <returns>处理器类型，如果未注册则返回null</returns>
    private Type? GetHandlerType(int messageId)
    {
        return _handlerTypes.GetValueOrDefault(messageId);
    }
}
