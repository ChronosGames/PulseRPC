using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Server;
using PulseRPC.Server.Services;
using PulseRPC.Transport;
using Xunit;

namespace PulseRPC.Tests.Integration
{
    /// <summary>
    /// MemoryPack 集成测试 - 验证完整的序列化修复方案
    /// </summary>
    public class MemoryPackIntegrationTests
    {
        [Fact]
        public void EmptyPayload_ShouldReplaceObjectSuccessfully()
        {
            // Arrange - 验证 EmptyPayload 可以替代 object 类型

            // Act
            var emptyPayload = EmptyPayload.Instance;
            var serializer = PulseRPCSerializerProvider.Instance.Create(MethodType.Unary, null);

            // Assert - 应该能够序列化而不抛出异常
            Assert.DoesNotThrow(() =>
            {
                var writer = new System.Buffers.ArrayBufferWriter<byte>();
                serializer.Serialize(writer, in emptyPayload);
                var data = writer.WrittenMemory.ToArray();
                Assert.NotNull(data);
                Assert.True(data.Length > 0);
            });
        }

        [Fact]
        public void ErrorResponse_ShouldSerializeWithoutExceptions()
        {
            // Arrange
            var errorResponse = ErrorResponse.Create("TEST_ERROR", "Test message", "Stack trace");
            var serializer = PulseRPCSerializerProvider.Instance.Create(MethodType.Unary, null);

            // Act & Assert - 应该能够序列化而不抛出 MemoryPackSerializationException
            Assert.DoesNotThrow(() =>
            {
                var writer = new System.Buffers.ArrayBufferWriter<byte>();
                serializer.Serialize(writer, in errorResponse);
                var data = writer.WrittenMemory.ToArray();
                Assert.NotNull(data);
                Assert.True(data.Length > 0);
            });
        }

        [Fact]
        public void ServerManager_ShouldUseNewMessageTypes()
        {
            // Arrange
            var serviceRegistry = ServiceRegistry.Instance;
            var serializerProvider = PulseRPCSerializerProvider.Instance;

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IServerChannelManager, TestServerChannelManager>();

            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var channelManager = serviceProvider.GetRequiredService<IServerChannelManager>();

            // Act - 创建 ServerManager 应该不抛出异常
            Assert.DoesNotThrow(() =>
            {
                var serverManager = new ServerManager(
                    serviceRegistry,
                    serializerProvider,
                    channelManager,
                    loggerFactory);

                // 验证可以添加传输而不出错
                serverManager.AddTransport("test", TransportType.Tcp, 8080);
            });
        }

        [Fact]
        public void MessageHeader_ShouldSerializeAndDeserializeCorrectly()
        {
            // Arrange
            var originalHeader = new MessageHeader
            {
                Type = MessageType.Response,
                MessageId = Guid.NewGuid(),
                ServiceName = "TestService",
                MethodName = "TestMethod"
            };

            var serializer = PulseRPCSerializerProvider.Instance.Create(MethodType.Unary, null);

            // Act
            var writer = new System.Buffers.ArrayBufferWriter<byte>();
            serializer.Serialize(writer, in originalHeader);
            var serializedData = writer.WrittenMemory.ToArray();

            var deserializedHeader = MemoryPack.MemoryPackSerializer.Deserialize<MessageHeader>(serializedData);

            // Assert
            Assert.NotNull(deserializedHeader);
            Assert.Equal(originalHeader.Type, deserializedHeader.Type);
            Assert.Equal(originalHeader.MessageId, deserializedHeader.MessageId);
            Assert.Equal(originalHeader.ServiceName, deserializedHeader.ServiceName);
            Assert.Equal(originalHeader.MethodName, deserializedHeader.MethodName);
        }

        [Fact]
        public void AllNewMessageTypes_ShouldBeMemoryPackable()
        {
            // Arrange & Act & Assert - 验证所有新消息类型都有 MemoryPackable 特性
            var emptyPayloadType = typeof(EmptyPayload);
            var errorResponseType = typeof(ErrorResponse);
            var successResponseType = typeof(SuccessResponse);
            var pongResponseType = typeof(PongResponse);

            Assert.True(HasMemoryPackableAttribute(emptyPayloadType), "EmptyPayload 应该有 MemoryPackable 特性");
            Assert.True(HasMemoryPackableAttribute(errorResponseType), "ErrorResponse 应该有 MemoryPackable 特性");
            Assert.True(HasMemoryPackableAttribute(successResponseType), "SuccessResponse 应该有 MemoryPackable 特性");
            Assert.True(HasMemoryPackableAttribute(pongResponseType), "PongResponse 应该有 MemoryPackable 特性");
        }

        private static bool HasMemoryPackableAttribute(Type type)
        {
            return type.GetCustomAttributes(typeof(MemoryPack.MemoryPackableAttribute), false).Length > 0;
        }

        /// <summary>
        /// 测试用的服务器通道管理器
        /// </summary>
        private class TestServerChannelManager : IServerChannelManager
        {
            public void AddChannel(IServerChannel channel) { }
            public IServerChannel? GetChannel(string name) => null;
            public void RemoveChannel(string name) { }
            public void Dispose() { }
        }
    }
}
