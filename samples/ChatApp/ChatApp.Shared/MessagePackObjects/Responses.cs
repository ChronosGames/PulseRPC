using MemoryPack;

namespace ChatApp.Shared.Models
{
    /// <summary>
    /// Message information
    /// </summary>
    [MemoryPackable]
    public partial struct MessageResponse
    {
        [MemoryPackOrder(0)]
        public string UserName { get; set; }

        [MemoryPackOrder(1)]
        public string Message { get; set; }
    }
}
