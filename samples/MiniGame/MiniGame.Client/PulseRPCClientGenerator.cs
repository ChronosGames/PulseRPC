using PulseRPC.Client;
using PulseRPC.Samples.Shared.Messages;

namespace PulseRPC.Samples.Client;

[PulseClientGeneration(typeof(LoginRequest))]
public partial class PulseRPCClientGenerator
{
    static partial void RegisterMessages();

    public static void Initialize()
    {
        RegisterMessages();
    }
}
