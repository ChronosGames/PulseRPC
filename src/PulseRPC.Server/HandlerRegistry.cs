namespace PulseRPC.Server;

public class HandlerRegistry
{
    // 命令处理器映射
    private readonly Dictionary<Type, CommandHandlerInfo> _commandHandlers = new();

    // 请求处理器映射
    private readonly Dictionary<Type, RequestHandlerInfo> _requestHandlers = new();

    // 处理器信息基类
    internal abstract class HandlerInfo(Type handlerType, HandlerThreadingPolicy policy, int priority)
    {
        public Type HandlerType { get; } = handlerType;
        public HandlerThreadingPolicy ThreadingPolicy { get; } = policy;
        public int Priority { get; } = priority;
    }

    // 命令处理器信息
    internal class CommandHandlerInfo(
        Type handlerType,
        Type commandType,
        HandlerThreadingPolicy policy,
        int priority,
        HandlerType handlerMode = HandlerType.Standard)
        : HandlerInfo(handlerType, policy, priority)
    {
        public Type CommandType { get; } = commandType;
        public HandlerType HandlerMode { get; } = handlerMode;
        public Type? ContextType { get; private set; }

        public static CommandHandlerInfo CreateContextual(
            Type handlerType,
            Type commandType,
            Type contextType,
            HandlerThreadingPolicy policy,
            int priority)
        {
            var info = new CommandHandlerInfo(handlerType, commandType, policy, priority, HandlerRegistry.HandlerType.Contextual);
            info.ContextType = contextType;
            return info;
        }
    }

    // 请求处理器信息
    internal class RequestHandlerInfo(
        Type handlerType,
        Type requestType,
        Type responseType,
        HandlerThreadingPolicy policy,
        int priority,
        HandlerType handlerMode = HandlerType.Standard)
        : HandlerInfo(handlerType, policy, priority)
    {
        public Type RequestType { get; } = requestType;
        public Type ResponseType { get; } = responseType;
        public HandlerType HandlerMode { get; } = handlerMode;
        public Type? ContextType { get; private set; }
        public Type? OptionsType { get; private set; }
        public Type? ResultType { get; private set; }

        public static RequestHandlerInfo CreateContextual(
            Type handlerType,
            Type requestType,
            Type responseType,
            Type contextType,
            HandlerThreadingPolicy policy,
            int priority)
        {
            var info = new RequestHandlerInfo(handlerType, requestType, responseType, policy, priority, HandlerRegistry.HandlerType.Contextual);
            info.ContextType = contextType;
            return info;
        }

        public static RequestHandlerInfo CreateExtended(
            Type handlerType,
            Type requestType,
            Type responseType,
            Type optionsType,
            Type resultType,
            HandlerThreadingPolicy policy,
            int priority)
        {
            var info = new RequestHandlerInfo(handlerType, requestType, responseType, policy, priority, HandlerRegistry.HandlerType.Extended);
            info.OptionsType = optionsType;
            info.ResultType = resultType;
            return info;
        }
    }

    // 枚举表示处理器类型
    internal enum HandlerType
    {
        Standard,    // 标准处理器
        Contextual,  // 带上下文的处理器
        Extended     // 带选项和结果的扩展处理器
    }

    // 注册标准命令处理器
    public void RegisterCommandHandler<TCommand>(
        Type handlerType, HandlerThreadingPolicy policy, int priority)
    {
        var info = new CommandHandlerInfo(handlerType, typeof(TCommand), policy, priority);
        _commandHandlers[typeof(TCommand)] = info;
    }

    // 注册带上下文的命令处理器
    public void RegisterContextualCommandHandler<TCommand, TContext>(
        Type handlerType, HandlerThreadingPolicy policy, int priority)
    {
        var info = CommandHandlerInfo.CreateContextual(
            handlerType, typeof(TCommand), typeof(TContext), policy, priority);
        _commandHandlers[typeof(TCommand)] = info;
    }

    // 注册标准请求处理器
    public void RegisterRequestHandler<TRequest, TResponse>(
        Type handlerType, HandlerThreadingPolicy policy, int priority)
    {
        var info = new RequestHandlerInfo(handlerType, typeof(TRequest), typeof(TResponse), policy, priority);
        _requestHandlers[typeof(TRequest)] = info;
    }

    // 注册带上下文的请求处理器
    public void RegisterContextualRequestHandler<TRequest, TResponse, TContext>(
        Type handlerType, HandlerThreadingPolicy policy, int priority)
    {
        var info = RequestHandlerInfo.CreateContextual(
            handlerType, typeof(TRequest), typeof(TResponse), typeof(TContext), policy, priority);
        _requestHandlers[typeof(TRequest)] = info;
    }

    // 注册带选项和结果的扩展请求处理器
    public void RegisterExtendedRequestHandler<TRequest, TResponse, TOptions, TResult>(
        Type handlerType, HandlerThreadingPolicy policy, int priority)
    {
        var info = RequestHandlerInfo.CreateExtended(
            handlerType, typeof(TRequest), typeof(TResponse), typeof(TOptions), typeof(TResult), policy, priority);
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
