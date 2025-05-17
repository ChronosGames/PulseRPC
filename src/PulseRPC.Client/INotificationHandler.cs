using PulseRPC.Network;

namespace PulseRPC.Client;

public interface INotificationHandler<in TNotification> : IPacketHandler
{
    Task HandleAsync(NetworkSession session, TNotification notification, CancellationToken cancellationToken);
}
