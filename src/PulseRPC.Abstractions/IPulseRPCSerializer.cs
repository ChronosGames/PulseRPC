using MemoryPack;

namespace PulseRPC.Serialization;

/// <summary>
/// 序列化接口
/// </summary>
public interface ISerializer
{
    byte[] Serialize<T>(T obj);
    T Deserialize<T>(byte[] data);
    object Deserialize(byte[] data, Type type);
}

/// <summary>
/// 高性能二进制序列化实现
/// </summary>
public class PulseRPCSerializer : ISerializer
{
    public byte[] Serialize<T>(T obj)
    {
        return MemoryPackSerializer.Serialize<T>(obj);
    }

    public T Deserialize<T>(byte[] data)
    {
        return MemoryPackSerializer.Deserialize<T>(data)!;
    }

    public object Deserialize(byte[] data, Type type)
    {
        return MemoryPackSerializer.Deserialize(type, data)!;
    }
}
