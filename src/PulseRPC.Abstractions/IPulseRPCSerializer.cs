using System;
using System.Buffers;
using System.Reflection;
using MemoryPack;

namespace PulseRPC.Serialization
{
    public enum MethodType
    {
        /// <summary>Single request sent from client, single response received from server.</summary>
        Unary,

        /// <summary>Stream of request sent from client, single response received from server.</summary>
        ClientStreaming,

        /// <summary>Single request sent from client, stream of responses received from server.</summary>
        ServerStreaming,

        /// <summary>Both server and client can stream arbitrary number of requests and responses simultaneously.</summary>
        DuplexStreaming
    }

    /// <summary>
    /// Provides a serializer for request/response of PulseRPC services and hub methods.
    /// </summary>
    public interface ISerializerProvider
    {

        /// <summary>
        /// Create a serializer for the service method.
        /// </summary>
        /// <param name="methodType">gRPC method type of the method.</param>
        /// <param name="methodInfo">A method info for an implementation of the service method. It is a hint that handling request parameters on the server, which may be passed null on the client.</param>
        /// <returns></returns>
        ISerializer Create(MethodType methodType, MethodInfo? methodInfo);
    }

    /// <summary>
    /// Provides a processing for message serialization.
    /// </summary>
    public interface ISerializer
    {
        void Serialize<T>(IBufferWriter<byte> writer, in T value);

        T Deserialize<T>(in ReadOnlySequence<byte> bytes);
    }

    public partial class PulseRPCSerializerProvider : ISerializerProvider
    {
        readonly MemoryPackSerializerOptions _serializerOptions;
        public static PulseRPCSerializerProvider Instance { get; } = new(MemoryPackSerializerOptions.Default);

        private PulseRPCSerializerProvider(MemoryPackSerializerOptions serializerOptions)
        {
            _serializerOptions = serializerOptions;
        }

        static PulseRPCSerializerProvider()
        {
            // DynamicArgumentTupleFormatter.Register();
        }

        public PulseRPCSerializerProvider WithOptions(MemoryPackSerializerOptions serializerOptions) => new PulseRPCSerializerProvider(serializerOptions);

        public ISerializer Create(MethodType methodType, MethodInfo? methodInfo)
        {
            return new Serializer(_serializerOptions);
        }

        private class Serializer : ISerializer
        {
            private readonly MemoryPackSerializerOptions _serializerOptions;

            public Serializer(MemoryPackSerializerOptions serializerOptions)
            {
                _serializerOptions = serializerOptions;
            }

            public void Serialize<T>(IBufferWriter<byte> writer, in T value) => MemoryPackSerializer.Serialize(writer, value, _serializerOptions);

            public T Deserialize<T>(in ReadOnlySequence<byte> bytes) => MemoryPackSerializer.Deserialize<T>(bytes, _serializerOptions)!;
        }
    }
}
