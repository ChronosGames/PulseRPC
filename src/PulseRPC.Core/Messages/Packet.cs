using System;
using MemoryPack;

namespace PulseRPC;

[Flags]
public enum PacketFlags : byte
{
    None = 0,
    Compressed = 1  // 数据已压缩
}

[MemoryPackable(GenerateType.NoGenerate)]
public abstract partial class Command : IPacket
{
    public uint SequenceId { get; set; }
}

/// <summary>
/// 命令批处理包
/// </summary>
[MemoryPackable(GenerateType.NoGenerate)]
public partial class CommandBatch : Command
{
    public Command[] Commands { get; set; } = Array.Empty<Command>();
}

[MemoryPackable(GenerateType.NoGenerate)]
public abstract partial class Message : IPacket
{
    public uint SequenceId { get; set; }
}

[MemoryPackable(GenerateType.NoGenerate)]
public abstract partial class Request : IPacket
{
    public uint SequenceId { get; set; }
}

[MemoryPackable(GenerateType.NoGenerate)]
public abstract partial class Response : IPacket
{
    public uint SequenceId { get; set; }
}
