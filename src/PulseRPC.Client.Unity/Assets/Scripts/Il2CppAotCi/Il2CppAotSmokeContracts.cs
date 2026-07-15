using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC;
using UnityEngine.Scripting;

namespace PulseRPC.Il2CppAotCi
{
    [MemoryPackable]
    public partial class Il2CppAotPayload
    {
        public int Sequence { get; set; }
    }

    [MemoryPackable]
    public partial class Il2CppAotReply
    {
        public bool Accepted { get; set; }
    }

    [Channel("IL2CPP_AOT_CI")]
    public interface IIl2CppAotHub : IPulseHub
    {
        Task PingAsync(CancellationToken cancellationToken = default);

        Task<Il2CppAotReply> RoundTripAsync(
            Il2CppAotPayload payload,
            CancellationToken cancellationToken = default);

        Task SendPairAsync(
            Il2CppAotPayload payload,
            int sequence,
            CancellationToken cancellationToken = default);
    }

    [Channel("CLIENT")]
    public interface IIl2CppAotReceiver : IPulseHub
    {
        Task OnPayloadAsync(
            Il2CppAotPayload payload,
            CancellationToken cancellationToken = default);

        Task<Il2CppAotReply> ConfirmAsync(
            Il2CppAotPayload payload,
            CancellationToken cancellationToken = default);
    }

    [PulseClientGeneration(typeof(IIl2CppAotHub))]
    [PulseClientGeneration(typeof(IIl2CppAotReceiver))]
    public static class Il2CppAotGenerationMarker
    {
    }

    [Preserve]
    public static class Il2CppAotSmokeRoots
    {
        [Preserve]
        public static void TouchGeneratedTypes()
        {
            _ = typeof(IIl2CppAotHubStub);
            _ = typeof(Il2CppAotReceiverDispatcher);
            _ = typeof(global::PulseRPC.Client.Generated.PulseRPCAotPreservation);
        }
    }
}
