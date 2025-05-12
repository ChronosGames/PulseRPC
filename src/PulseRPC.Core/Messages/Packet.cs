using System;
using MemoryPack;

namespace PulseRPC.Protocol.Messages;

public enum MessageType : byte
{
    Command = 0,    // 上行命令
    Message = 1,    // 下行消息
    Request = 2,    // 请求
    Response = 3    // 响应
}

[Flags]
public enum PacketFlags : byte
{
    None = 0,
    Compressed = 1  // 数据已压缩
}

public static class PacketHeader
{
    // 计算头部大小
    public static int GetHeaderSize(MessageType type)
    {
        // 基本头部: 类型(1) + 标志(1) + 序列号(4)
        var size = 6;

        // 请求/响应额外包含请求ID
        if (type is MessageType.Request or MessageType.Response)
        {
            size += 4;
        }

        return size;
    }
}

[MemoryPackable(GenerateType.NoGenerate)]
public partial interface IPacket
{
    MessageType Type { get; }
    uint SequenceId { get; set; }
}

[MemoryPackable(GenerateType.NoGenerate)]
public abstract partial class Command : IPacket
{
    public uint SequenceId { get; set; }
    public MessageType Type => MessageType.Command;
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
    public MessageType Type => MessageType.Message;
}

[MemoryPackable(GenerateType.NoGenerate)]
public abstract partial class Request : IPacket
{
    public uint SequenceId { get; set; }
    public uint RequestId { get; set; }
    public MessageType Type => MessageType.Request;
}

[MemoryPackable(GenerateType.NoGenerate)]
public abstract partial class Response : IPacket
{
    public uint SequenceId { get; set; }
    public uint RequestId { get; set; }
    public MessageType Type => MessageType.Response;
}
