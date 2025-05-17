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
public partial interface ICommand : IPacket
{
}

/// <summary>
/// 命令批处理包
/// </summary>
[MemoryPackable(GenerateType.NoGenerate)]
public partial class CommandBatch : ICommand
{
    public ICommand[] Commands { get; set; } = Array.Empty<ICommand>();
}

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IMessage : IPacket
{
}

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IRequest : IPacket
{
}

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IResponse : IPacket
{
}
