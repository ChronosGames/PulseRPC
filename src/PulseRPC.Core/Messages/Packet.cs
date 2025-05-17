using System;

namespace PulseRPC;

[Flags]
public enum PacketFlags : byte
{
    None = 0,
    Compressed = 1  // 数据已压缩
}
