using System;
using System.Buffers;
using MemoryPack;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using Xunit;

namespace PulseRPC.Tests.Serialization
{
    /// <summary>
    /// MemoryPack 序列化测试
    /// </summary>
    public class MemoryPackSerializationTests
    {
        private readonly ISerializerProvider _serializerProvider;

        public MemoryPackSerializationTests()
        {
            _serializerProvider = PulseRPCSerializerProvider.Instance;
        }

        [Fact]
        public void EmptyPayload_ShouldSerializeAndDeserialize()
        {
            // Arrange
            var serializer = _serializerProvider.Create(MethodType.Unary, null);
            var originalPayload = EmptyPayload.Instance;

            // Act
            var writer = new ArrayBufferWriter<byte>();
            serializer.Serialize(writer, in originalPayload);
            var serializedData = writer.WrittenMemory.ToArray();

            var deserializedPayload = MemoryPackSerializer.Deserialize<EmptyPayload>(serializedData);

            // Assert
            Assert.NotNull(serializedData);
            Assert.True(serializedData.Length > 0);
            // EmptyPayload 是空结构体，只需验证能成功序列化和反序列化
        }

        [Fact]
        public void ErrorResponse_ShouldSerializeAndDeserialize()
        {
            // Arrange
            var serializer = _serializerProvider.Create(MethodType.Unary, null);
            var originalError = ErrorResponse.Create("TEST_ERROR", "Test error message", "Stack trace details");

            // Act
            var writer = new ArrayBufferWriter<byte>();
            serializer.Serialize(writer, in originalError);
            var serializedData = writer.WrittenMemory.ToArray();

            var deserializedError = MemoryPackSerializer.Deserialize<ErrorResponse>(serializedData);

            // Assert
            Assert.NotNull(deserializedError);
            Assert.Equal(originalError.ErrorCode, deserializedError.ErrorCode);
            Assert.Equal(originalError.ErrorMessage, deserializedError.ErrorMessage);
            Assert.Equal(originalError.ErrorDetails, deserializedError.ErrorDetails);
        }

        [Fact]
        public void SuccessResponse_ShouldSerializeAndDeserialize()
        {
            // Arrange
            var serializer = _serializerProvider.Create(MethodType.Unary, null);
            var originalResponse = SuccessResponse.Create();

            // Act
            var writer = new ArrayBufferWriter<byte>();
            serializer.Serialize(writer, in originalResponse);
            var serializedData = writer.WrittenMemory.ToArray();

            var deserializedResponse = MemoryPackSerializer.Deserialize<SuccessResponse>(serializedData);

            // Assert
            Assert.True(deserializedResponse.Success);
            Assert.True(deserializedResponse.Timestamp > 0);
        }

        [Fact]
        public void PongResponse_ShouldSerializeAndDeserialize()
        {
            // Arrange
            var serializer = _serializerProvider.Create(MethodType.Unary, null);
            var originalPong = PongResponse.Create();

            // Act
            var writer = new ArrayBufferWriter<byte>();
            serializer.Serialize(writer, in originalPong);
            var serializedData = writer.WrittenMemory.ToArray();

            var deserializedPong = MemoryPackSerializer.Deserialize<PongResponse>(serializedData);

            // Assert
            Assert.True(deserializedPong.Timestamp > 0);
            Assert.Equal(originalPong.Timestamp, deserializedPong.Timestamp);
        }

        [Fact]
        public void MessageHeader_ShouldSerializeAndDeserialize()
        {
            // Arrange
            var serializer = _serializerProvider.Create(MethodType.Unary, null);
            var originalHeader = new MessageHeader
            {
                Type = MessageType.Request,
                MessageId = Guid.NewGuid(),
                ServiceName = "TestService",
                MethodName = "TestMethod"
            };

            // Act
            var writer = new ArrayBufferWriter<byte>();
            serializer.Serialize(writer, in originalHeader);
            var serializedData = writer.WrittenMemory.ToArray();

            var deserializedHeader = MemoryPackSerializer.Deserialize<MessageHeader>(serializedData);

            // Assert
            Assert.NotNull(deserializedHeader);
            Assert.Equal(originalHeader.Type, deserializedHeader.Type);
            Assert.Equal(originalHeader.MessageId, deserializedHeader.MessageId);
            Assert.Equal(originalHeader.ServiceName, deserializedHeader.ServiceName);
            Assert.Equal(originalHeader.MethodName, deserializedHeader.MethodName);
        }

        [Fact]
        public void NullPayload_ShouldHandleGracefully()
        {
            // Arrange
            var serializer = _serializerProvider.Create(MethodType.Unary, null);

            // Act & Assert - 应该使用 EmptyPayload 而不是 null
            var emptyPayload = EmptyPayload.Instance;
            var writer = new ArrayBufferWriter<byte>();

            // 这应该不会抛出异常
            Assert.DoesNotThrow(() => serializer.Serialize(writer, in emptyPayload));

            var serializedData = writer.WrittenMemory.ToArray();
            Assert.NotNull(serializedData);
            Assert.True(serializedData.Length > 0);
        }

        [Theory]
        [InlineData("VALIDATION_ERROR", "输入验证失败")]
        [InlineData("SERVER_ERROR", "服务器内部错误")]
        [InlineData("NOT_FOUND", "资源未找到")]
        public void ErrorResponse_WithDifferentErrors_ShouldSerializeCorrectly(string errorCode, string errorMessage)
        {
            // Arrange
            var serializer = _serializerProvider.Create(MethodType.Unary, null);
            var error = ErrorResponse.Create(errorCode, errorMessage);

            // Act
            var writer = new ArrayBufferWriter<byte>();
            serializer.Serialize(writer, in error);
            var serializedData = writer.WrittenMemory.ToArray();

            var deserializedError = MemoryPackSerializer.Deserialize<ErrorResponse>(serializedData);

            // Assert
            Assert.Equal(errorCode, deserializedError.ErrorCode);
            Assert.Equal(errorMessage, deserializedError.ErrorMessage);
        }

        [Fact]
        public void SerializedData_ShouldBeCompact()
        {
            // Arrange
            var serializer = _serializerProvider.Create(MethodType.Unary, null);
            var emptyPayload = EmptyPayload.Instance;

            // Act
            var writer = new ArrayBufferWriter<byte>();
            serializer.Serialize(writer, in emptyPayload);
            var serializedData = writer.WrittenMemory.ToArray();

            // Assert - 空载荷序列化后应该非常小
            Assert.True(serializedData.Length < 100, $"序列化的空载荷大小为 {serializedData.Length} 字节，应该更小");
        }
    }
}
