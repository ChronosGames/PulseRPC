namespace PulseRPC.Server;

public class HandlerRegistry
{
    // 命令处理器映射
    private readonly Dictionary<Type, CommandHandlerInfo> _commandHandlers = new();

    // 请求处理器映射
    private readonly Dictionary<Type, RequestHandlerInfo> _requestHandlers = new();

    // 处理器信息基类
    internal abstract class HandlerInfo(Type handlerType, ushort messageId, HandlerThreadingPolicy policy, int priority)
    {
        public Type HandlerType { get; } = handlerType;
        public ushort MessageId { get; } = messageId;
        public HandlerThreadingPolicy ThreadingPolicy { get; } = policy;
        public int Priority { get; } = priority;
    }

    // 命令处理器信息
    internal class CommandHandlerInfo(
        Type handlerType,
        Type commandType,
        ushort messageId,
        HandlerThreadingPolicy policy,
        int priority)
        : HandlerInfo(handlerType, messageId, policy, priority)
    {
        public Type CommandType { get; } = commandType;
    }

    // 请求处理器信息
    internal class RequestHandlerInfo(
        Type handlerType,
        Type requestType,
        Type responseType,
        ushort messageId,
        HandlerThreadingPolicy policy,
        int priority)
        : HandlerInfo(handlerType, messageId, policy, priority)
    {
        public Type RequestType { get; } = requestType;
        public Type ResponseType { get; } = responseType;
    }

    // 注册命令处理器
    public void RegisterCommandHandler<TCommand>(
        Type handlerType, ushort messageId, HandlerThreadingPolicy policy, int priority)
    {
        var info = new CommandHandlerInfo(
            handlerType, typeof(TCommand), messageId, policy, priority);

        _commandHandlers[typeof(TCommand)] = info;
    }

    // 注册请求处理器
    public void RegisterRequestHandler<TRequest, TResponse>(
        Type handlerType, ushort messageId, HandlerThreadingPolicy policy, int priority)
    {
        var info = new RequestHandlerInfo(
            handlerType, typeof(TRequest), typeof(TResponse), messageId, policy, priority);

        _requestHandlers[typeof(TRequest)] = info;
    }

    // 获取命令处理器信息
    internal bool TryGetCommandHandler(Type commandType, out CommandHandlerInfo? handlerInfo)
    {
        return _commandHandlers.TryGetValue(commandType, out handlerInfo);
    }

    // 获取请求处理器信息
    internal bool TryGetRequestHandler(Type requestType, out RequestHandlerInfo? handlerInfo)
    {
        return _requestHandlers.TryGetValue(requestType, out handlerInfo);
    }
}
