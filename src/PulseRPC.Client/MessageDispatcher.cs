using System.Collections.Concurrent;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Client;

public interface IMSGHandler : IPacketHandler
{
    Task HandleAsync(NetworkSession context, IMessage packet, CancellationToken cancellationToken = default);
}

public class MessageDispatcher : IMessageDispatcher
{
    private readonly ConcurrentDictionary<Type, IMSGHandler> _handlers = new ConcurrentDictionary<Type, IMSGHandler>();

    public void RegisterHandler(Type packetType, IMSGHandler handler)
    {
        _handlers.TryAdd(packetType, handler);
    }

    public Task DispatchAsync(NetworkSession context, ushort sequenceId, IPacket packet, CancellationToken cancellationToken = default)
    {
        switch (packet)
        {
            case IMessage msg:
            {
                if (!_handlers.TryGetValue(msg.GetType(), out var handler))
                {
                    //throw new KeyNotFoundException($"未找到消息处理器: {msg.GetType().Name}");
                    return Task.CompletedTask;
                }

                return handler.HandleAsync(context, msg, cancellationToken);
            }
            case IResponse _:
                return Task.CompletedTask;
            default:
                throw new NotSupportedException(packet.GetType().Name);
        }
    }
}
