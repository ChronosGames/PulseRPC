using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC;
using PulseRPC.Protocol;

namespace GameServer.LegacyContracts
{
    // Frozen V1 consumer contract: no dependency on GameServer.Contracts V2.
    public interface ISessionHub : IPulseHub
    {
        [Protocol(0x7100)]
        Task AuthenticateAsync(string token, CancellationToken cancellationToken = default);
    }

    public interface IPlayerHub : IPulseHub
    {
        // Different source-level name, same explicit wire ID and compatible return shape.
        [Protocol(0x7102)]
        Task<PlayerSnapshot> ReadStateV1Async(CancellationToken cancellationToken = default);
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class PlayerSnapshot
    {
        [MemoryPackOrder(0)] public long Balance { get; set; }
        [MemoryPackOrder(1)] public int Inventory { get; set; }
    }
}
