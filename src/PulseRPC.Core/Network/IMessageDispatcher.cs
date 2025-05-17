using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Protocol.Messages;

namespace PulseRPC.Protocol.Network;

public interface IMessageDispatcher
{
    Task DispatchAsync(NetworkSession session, ushort sequenceId, IPacket packet, CancellationToken cancellationToken = default);
}
