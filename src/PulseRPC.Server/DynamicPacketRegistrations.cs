using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using MemoryPack;

namespace PulseRPC.Server;

public class DynamicPacketRegistrations(MemoryPackSerializerOptions serializerOptions) : IPulseRPCSerializer
{
    private readonly ConcurrentDictionary<ushort, Type> _packetTypeMap = new();
    private readonly ConcurrentDictionary<Type, ushort> _typePacketMap = new();

    public void RegisterPacket(ushort packetId, Type packetType)
    {
        _packetTypeMap.TryAdd(packetId, packetType);
        _typePacketMap.TryAdd(packetType, packetId);
    }

    public void Serialize<T>(IBufferWriter<byte> writer, in T message) where T : IPacket
    {
        // 0. 获取消息ID
        var messageId = _typePacketMap.GetValueOrDefault(typeof(T));

        // 2. 为消息ID获取可写入的Span
        var idSpan = writer.GetSpan(2);
        BinaryPrimitives.WriteUInt16LittleEndian(idSpan, messageId);
        writer.Advance(2);

        // 3. 直接序列化消息到写入器
        MemoryPackSerializer.Serialize(writer, message);
    }

    public IPacket Deserialize(in ReadOnlySpan<byte> bytes)
    {
        var packetId = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        return (IPacket)MemoryPackSerializer.Deserialize(_packetTypeMap.GetValueOrDefault(packetId)!, bytes[2..], serializerOptions)!;
    }
}
