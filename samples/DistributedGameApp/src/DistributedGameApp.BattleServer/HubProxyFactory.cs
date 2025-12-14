using DistributedGameApp.Infrastructure.ServiceClient;
using DistributedGameApp.Shared.Hubs;
using PulseRPC;
using PulseRPC.Client;

namespace DistributedGameApp.BattleServer;

[PulseClientGeneration(typeof(IBackendHub))]
public static class HubClientGeneration
{
}

public class HubProxyFactory : IHubProxyFactory
{
    public THub? Create<THub>(IClientChannel channel) where THub : class, IPulseHub
    {
        return channel.GetHub<THub>();
    }
}
