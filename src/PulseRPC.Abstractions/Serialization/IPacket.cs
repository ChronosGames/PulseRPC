using MemoryPack;

namespace PulseRPC;

public enum MessageType : byte
{
    Command = 0,    // 上行命令
    Message = 1,    // 下行消息
    Request = 2,    // 请求
    Response = 3    // 响应
}

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IPacket
{
    // ushort Id { get; }
    MessageType Type { get; }
    uint SequenceId { get; set; }
}
