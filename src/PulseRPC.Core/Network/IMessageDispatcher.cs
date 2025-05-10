using System.Threading.Tasks;
using PulseRPC.Protocol.Messages;

namespace PulseRPC.Protocol.Network;

public interface IMessageDispatcher
{
    Task DispatchAsync(NetworkSession context, IPacket packet);
}
