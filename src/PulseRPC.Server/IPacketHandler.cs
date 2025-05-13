using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class PacketHandlerAttribute(bool isInternal = false, ushort packetId = 0) : Attribute
{
    public ushort PacketId { get; } = packetId;
    public bool IsInternal { get; } = isInternal;

    // 可选：处理优先级
    public int Priority { get; set; } = 0;

    // 可选：处理线程策略
    public HandlerThreadingPolicy ThreadingPolicy { get; set; } = HandlerThreadingPolicy.WorkerThread;
}

/// <summary>
/// 消息处理器接口
/// </summary>
public interface IPacketHandler;

/// <summary>
/// 命令处理器接口
/// </summary>
public interface ICommandHandler<in TCommand> : IPacketHandler where TCommand : Command
{
    Task HandleAsync(NetworkSession session, TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// 请求处理器接口
/// </summary>
public interface IRequestHandler<in TRequest, TResponse> : IPacketHandler
    where TRequest : Request
    where TResponse : Response
{
    Task<TResponse> HandleAsync(NetworkSession session, TRequest request);
}
