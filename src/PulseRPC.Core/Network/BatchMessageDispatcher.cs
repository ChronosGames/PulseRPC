using System;
using MemoryPack;
using PulseRPC.Protocol.Messages;

namespace PulseRPC.Protocol.Network;

/// <summary>
/// 命令批处理包
/// </summary>
[MemoryPackable]
public partial class CommandBatch : Command
{
    public Command[] Commands { get; set; } = Array.Empty<Command>();
}
