using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

/// <summary>
/// 消息处理器接口
/// </summary>
public interface IMessageHandler;

/// <summary>
/// 命令处理器接口
/// </summary>
public interface ICommandHandler<in TCommand> : IMessageHandler where TCommand : Command
{
    Task HandleAsync(NetworkSession session, TCommand command);
}

/// <summary>
/// 请求处理器接口
/// </summary>
public interface IRequestHandler<in TRequest, TResponse> : IMessageHandler
    where TRequest : Request
    where TResponse : Response
{
    Task<TResponse> HandleAsync(NetworkSession session, TRequest request);
}
