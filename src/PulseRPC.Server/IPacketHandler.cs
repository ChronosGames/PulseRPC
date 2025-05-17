using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class PacketHandlerAttribute(bool isInternal = false) : Attribute
{
    /// <summary>
    /// 是否为内部处理器
    /// </summary>
    public bool IsInternal { get; } = isInternal;

    // 可选：处理优先级
    public int Priority { get; set; } = 0;

    // 可选：处理线程策略
    public HandlerThreadingPolicy ThreadingPolicy { get; set; } = HandlerThreadingPolicy.WorkerThread;
}

/// <summary>
/// 命令处理器接口
/// </summary>
public interface ICommandHandler<in TCommand> : IPacketHandler where TCommand : ICommand
{
    Task HandleAsync(NetworkSession session, TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// 请求处理器接口
/// </summary>
public interface IRequestHandler<in TRequest, TResponse> : IPacketHandler
    where TRequest : IRequest
    where TResponse : IResponse
{
    Task<TResponse> HandleAsync(NetworkSession session, TRequest request, CancellationToken cancellationToken);
}
