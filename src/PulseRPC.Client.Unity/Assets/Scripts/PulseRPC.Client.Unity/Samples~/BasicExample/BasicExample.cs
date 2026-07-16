using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC;

namespace PulseRPC.Samples.BasicExample
{
    [MemoryPackable]
    public partial class EchoRequest
    {
        public string Message { get; set; } = string.Empty;
    }

    [MemoryPackable]
    public partial class EchoReply
    {
        public string Message { get; set; } = string.Empty;
    }

    [Channel("BASIC_EXAMPLE")]
    public interface IBasicExampleHub : IPulseHub
    {
        Task<EchoReply> EchoAsync(
            EchoRequest request,
            CancellationToken cancellationToken = default);
    }

    [Channel("CLIENT")]
    public interface IBasicExampleReceiver : IPulseHub
    {
        Task OnEchoedAsync(
            EchoReply reply,
            CancellationToken cancellationToken = default);
    }

    [PulseClientGeneration(typeof(IBasicExampleHub))]
    [PulseClientGeneration(typeof(IBasicExampleReceiver))]
    public static class BasicExampleGenerationMarker
    {
    }
}
