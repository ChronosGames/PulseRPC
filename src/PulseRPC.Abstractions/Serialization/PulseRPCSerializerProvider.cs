using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using MemoryPack;

namespace PulseRPC;

public partial class PulseRPCSerializerProvider : IPulseRPCSerializerProvider
{
    private readonly MemoryPackSerializerOptions _serializerOptions;

    public static PulseRPCSerializerProvider Instance { get; } = new (MemoryPackSerializerOptions.Default);

    private PulseRPCSerializerProvider(MemoryPackSerializerOptions serializerOptions)
    {
        _serializerOptions = serializerOptions;
    }

    static PulseRPCSerializerProvider()
    {
        //DynamicArgumentTupleFormatter.Register();
    }

    public PulseRPCSerializerProvider WithOptions(MemoryPackSerializerOptions serializerOptions)
        => new PulseRPCSerializerProvider(serializerOptions);

    public IPulseRPCSerializer Create(MethodInfo? methodInfo)
    {
        return new PulseRPCSerializer(_serializerOptions);
    }

    private class PulseRPCSerializer(MemoryPackSerializerOptions serializerOptions) : IPulseRPCSerializer
    {
        public void Serialize<T>(IBufferWriter<byte> writer, in T value)
            => MemoryPackSerializer.Serialize(writer, value, serializerOptions);

        public T Deserialize<T>(in ReadOnlySequence<byte> bytes)
            => MemoryPackSerializer.Deserialize<T>(bytes, serializerOptions)!;

        private ushort GetMessageId(Type packetType)
        {
            throw new NotImplementedException();
        }

        public byte[] Serialize2<T>(in T message) where T : IPacket
        {
            // 0. 获取消息ID
            var messageId = GetMessageId(typeof(T));

            // 1. 创建缓冲区写入器
            var writer = new ArrayBufferWriter<byte>();

            // 2. 为消息ID获取可写入的Span
            var idSpan = writer.GetSpan(2);
            BinaryPrimitives.WriteUInt16LittleEndian(idSpan, messageId);
            writer.Advance(2);

            // 3. 直接序列化消息到写入器
            MemoryPackSerializer.Serialize(writer, message);

            // 4. 复制到结果数组
            return writer.WrittenSpan.ToArray();
        }

        public IPacket Deserialize2(in ReadOnlySpan<byte> bytes)
        {
            var messageId = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
            switch (messageId)
            {
                // case 0x01:
                //     return MemoryPackSerializer.Deserialize<CommandBatch>(bytes, serializerOptions)!;
                default:
                    throw new NotSupportedException($"MessageId {messageId} is not supported.");
            }
        }
    }
}
