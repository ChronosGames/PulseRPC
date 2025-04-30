using MemoryPack;

namespace ChatApp.Shared.Models
{
    /// <summary>
    /// Room participation information
    /// </summary>
    [MemoryPackable]
    public partial struct JoinRequest
    {
        [MemoryPackOrder(0)]
        public string RoomName { get; set; }

        [MemoryPackOrder(1)]
        public string UserName { get; set; }
    }
}
