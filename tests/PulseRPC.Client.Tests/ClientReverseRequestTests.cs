using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC.Client.Channels;
using PulseRPC.Client.Tests.Helpers;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using Xunit;

namespace PulseRPC.Client.Tests;

/// <summary>
/// [P-4] 客户端反向 Ask 分发单元测试：验证 <see cref="TransportChannel"/> 收到
/// <see cref="MessageType.ReverseRequest"/> 后，调用已注册处理器并以 Response/Error 回显 MessageId 应答。
/// </summary>
public sealed class ClientReverseRequestTests
{
    private const ushort TestProtocolId = 0x1234;

    private static TransportChannel CreateChannel(MockClientTransport transport)
    {
        // 降低并发以便测试确定性，队列足够即可。
        var options = new TransportChannelOptions
        {
            MessageProcessingConcurrency = 1,
            MessageQueueCapacity = 16,
        };
        return new TransportChannel(transport, PulseRPCSerializerProvider.Instance, options);
    }

    /// <summary>构建服务端→客户端的反向请求帧：[4字节LE头长度][MemoryPack(MessageHeader)][body]。</summary>
    private static byte[] BuildReverseRequestFrame(Guid messageId, ushort protocolId, ReadOnlyMemory<byte> body)
    {
        var header = new MessageHeader
        {
            Type = MessageType.ReverseRequest,
            MessageId = messageId,
            ProtocolId = protocolId,
            ServiceName = string.Empty,
            MethodName = string.Empty,
            Flags = MessageFlags.RequireResponse,
            Timestamp = DateTimeOffset.UtcNow.Ticks,
        };

        var headerBytes = MemoryPackSerializer.Serialize(header);
        var frame = new byte[4 + headerBytes.Length + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), headerBytes.Length);
        headerBytes.CopyTo(frame.AsSpan(4));
        body.Span.CopyTo(frame.AsSpan(4 + headerBytes.Length));
        return frame;
    }

    /// <summary>解析客户端回传的应答帧。</summary>
    private static (MessageHeader Header, byte[] Body) ParseFrame(byte[] frame)
    {
        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(0, 4));
        var header = MemoryPackSerializer.Deserialize<MessageHeader>(frame.AsSpan(4, headerLength))!;
        var bodyStart = 4 + headerLength;
        var body = frame.AsSpan(bodyStart).ToArray();
        return (header, body);
    }

    [Fact]
    public void RegisterRequestHandler_DuplicateProtocol_Throws()
    {
        using var transport = new MockClientTransport();
        var channel = CreateChannel(transport);
        try
        {
            channel.RegisterRequestHandler(TestProtocolId, (_, _) => new ValueTask<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty));

            Assert.Throws<InvalidOperationException>(() =>
                channel.RegisterRequestHandler(TestProtocolId, (_, _) => new ValueTask<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty)));
        }
        finally
        {
            ((IDisposable)channel).Dispose();
        }
    }

    [Fact]
    public void ReverseRequest_HandlerSucceeds_RepliesResponseWithResultAndEchoesMessageId()
    {
        using var transport = new MockClientTransport();
        var channel = CreateChannel(transport);
        try
        {
            var requestBody = MemoryPackSerializer.Serialize(21);
            byte[]? received = null;

            channel.RegisterRequestHandler(TestProtocolId, (data, _) =>
            {
                received = data.ToArray();
                var arg = MemoryPackSerializer.Deserialize<int>(data.Span);
                var resultBytes = MemoryPackSerializer.Serialize(arg * 2);
                return new ValueTask<ReadOnlyMemory<byte>>(resultBytes);
            });

            var messageId = Guid.NewGuid();
            transport.Receive(BuildReverseRequestFrame(messageId, TestProtocolId, requestBody));

            var replyFrame = transport.WaitForSentFrame();
            Assert.NotNull(replyFrame);

            var (header, body) = ParseFrame(replyFrame!);
            Assert.Equal(MessageType.Response, header.Type);
            Assert.Equal(messageId, header.MessageId);

            // handler 收到的请求体应与下发一致
            Assert.NotNull(received);
            Assert.Equal(requestBody, received!);

            // 应答体应为 handler 的序列化结果（21 * 2 = 42）
            Assert.Equal(42, MemoryPackSerializer.Deserialize<int>(body));
        }
        finally
        {
            ((IDisposable)channel).Dispose();
        }
    }

    [Fact]
    public void ReverseRequest_HandlerThrows_RepliesErrorWithExceptionInfo()
    {
        using var transport = new MockClientTransport();
        var channel = CreateChannel(transport);
        try
        {
            channel.RegisterRequestHandler(TestProtocolId, (_, _) =>
                throw new InvalidOperationException("boom"));

            var messageId = Guid.NewGuid();
            transport.Receive(BuildReverseRequestFrame(messageId, TestProtocolId, ReadOnlyMemory<byte>.Empty));

            var replyFrame = transport.WaitForSentFrame();
            Assert.NotNull(replyFrame);

            var (header, body) = ParseFrame(replyFrame!);
            Assert.Equal(MessageType.Error, header.Type);
            Assert.Equal(messageId, header.MessageId);

            var error = MemoryPackSerializer.Deserialize<ErrorResponse>(body)!;
            Assert.Equal(nameof(InvalidOperationException), error.ErrorCode);
            Assert.Equal("boom", error.ErrorMessage);
        }
        finally
        {
            ((IDisposable)channel).Dispose();
        }
    }

    [Fact]
    public void ReverseRequest_NoHandlerRegistered_RepliesHandlerNotFoundError()
    {
        using var transport = new MockClientTransport();
        var channel = CreateChannel(transport);
        try
        {
            var messageId = Guid.NewGuid();
            transport.Receive(BuildReverseRequestFrame(messageId, 0x4321, ReadOnlyMemory<byte>.Empty));

            var replyFrame = transport.WaitForSentFrame();
            Assert.NotNull(replyFrame);

            var (header, body) = ParseFrame(replyFrame!);
            Assert.Equal(MessageType.Error, header.Type);
            Assert.Equal(messageId, header.MessageId);

            var error = MemoryPackSerializer.Deserialize<ErrorResponse>(body)!;
            Assert.Equal("REVERSE_HANDLER_NOT_FOUND", error.ErrorCode);
        }
        finally
        {
            ((IDisposable)channel).Dispose();
        }
    }

    [Fact]
    public void ReverseRequest_AfterUnsubscribe_RepliesHandlerNotFoundError()
    {
        using var transport = new MockClientTransport();
        var channel = CreateChannel(transport);
        try
        {
            var token = channel.RegisterRequestHandler(TestProtocolId,
                (_, _) => new ValueTask<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty));
            token.Dispose();

            var messageId = Guid.NewGuid();
            transport.Receive(BuildReverseRequestFrame(messageId, TestProtocolId, ReadOnlyMemory<byte>.Empty));

            var replyFrame = transport.WaitForSentFrame();
            Assert.NotNull(replyFrame);

            var (header, body) = ParseFrame(replyFrame!);
            Assert.Equal(MessageType.Error, header.Type);
            var error = MemoryPackSerializer.Deserialize<ErrorResponse>(body)!;
            Assert.Equal("REVERSE_HANDLER_NOT_FOUND", error.ErrorCode);
        }
        finally
        {
            ((IDisposable)channel).Dispose();
        }
    }
}
