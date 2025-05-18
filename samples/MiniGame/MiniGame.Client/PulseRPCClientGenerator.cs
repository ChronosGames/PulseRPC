using PulseRPC.Client;
using PulseRPC.Samples.Shared.Messages;

namespace PulseRPC.Samples.Client;

[PulseClientGeneration(typeof(IAuthStreamingHub))]
[PulseClientGeneration(typeof(IUserStreamingHub))]
public partial class PulseRPCClientGenerator;
