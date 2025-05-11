using System.Buffers;
using System.Reflection;
using MemoryPack;

namespace PulseRPC.Serialization.MemoryPack;

public partial class MemoryPackPulseRPCSerializerProvider : IPulseRPCSerializerProvider
{
    readonly MemoryPackSerializerOptions _serializerOptions;

    public static MemoryPackPulseRPCSerializerProvider Instance { get; } = new (MemoryPackSerializerOptions.Default);

    private MemoryPackPulseRPCSerializerProvider(MemoryPackSerializerOptions serializerOptions)
    {
        _serializerOptions = serializerOptions;
    }

    static MemoryPackPulseRPCSerializerProvider()
    {
        DynamicArgumentTupleFormatter.Register();
    }

    public MemoryPackPulseRPCSerializerProvider WithOptions(MemoryPackSerializerOptions serializerOptions)
        => new MemoryPackPulseRPCSerializerProvider(serializerOptions);

    // public IPulseRPCSerializer Create(MethodType methodType, MethodInfo? methodInfo)
    // {
    //     return new PulseRPCSerializer(_serializerOptions);
    // }

    private class PulseRPCSerializer(MemoryPackSerializerOptions serializerOptions) : IPulseRPCSerializer
    {
        public void Serialize<T>(IBufferWriter<byte> writer, in T value)
            => MemoryPackSerializer.Serialize(writer, value, serializerOptions);

        public T Deserialize<T>(in ReadOnlySequence<byte> bytes)
            => MemoryPackSerializer.Deserialize<T>(bytes, serializerOptions)!;
    }
}
