using System;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC;
using PulseRPC.Protocol;

namespace GameServer.Contracts
{
    [ClientFacing]
    public interface ISessionHub : IPulseHub
    {
        [Protocol(0x7100)]
        Task<bool> AuthenticateAsync(string token, CancellationToken cancellationToken = default);

        [Protocol(0x7104)]
        Task<bool> LogoutAsync(CancellationToken cancellationToken = default);
    }

    [ClientFacing]
    public interface IPlayerHub : IPulseHub
    {
        [Protocol(0x7101)]
        Task<PurchaseReceipt> PurchaseAsync(PurchaseCommand command, CancellationToken cancellationToken = default);

        [Protocol(0x7102)]
        Task<PlayerSnapshot> GetStateAsync(CancellationToken cancellationToken = default);

        [Protocol(0x7103)]
        Task<string> EchoAsync(string value, CancellationToken cancellationToken = default);
    }

    // Explicit IDs and ordered, version-tolerant DTOs are part of the published contract.
    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class PurchaseCommand
    {
        [MemoryPackOrder(0)] public Guid OperationId { get; set; }
        [MemoryPackOrder(1)] public string Sku { get; set; } = "potion";
        [MemoryPackOrder(2)] public int Quantity { get; set; } = 1;
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class PurchaseReceipt
    {
        [MemoryPackOrder(0)] public Guid OperationId { get; set; }
        [MemoryPackOrder(1)] public long Balance { get; set; }
        [MemoryPackOrder(2)] public int Inventory { get; set; }
        [MemoryPackOrder(3)] public string NodeId { get; set; } = "";
        [MemoryPackOrder(4)] public long Fence { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class PlayerSnapshot
    {
        [MemoryPackOrder(0)] public long Balance { get; set; }
        [MemoryPackOrder(1)] public int Inventory { get; set; }
        [MemoryPackOrder(2)] public string NodeId { get; set; } = "";
        [MemoryPackOrder(3)] public long Fence { get; set; }
    }
}
