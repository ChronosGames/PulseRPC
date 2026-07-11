using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Processing.Pipeline;
using PulseRPC.Server.Processing.Serialization;
using Xunit;

namespace PulseRPC.Server.Tests.Processing;

public class ResponseProcessorTests
{
    [Fact]
    public async Task StartAsync_WhenRoutingTableHasMissingResponseSerializer_MustFail()
    {
        using var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        using var processor = new ResponseProcessor(
            channelManager,
            options: new ResponseProcessorOptions { ProcessorThreadCount = 1, ChannelCapacity = 16 },
            responseSerializerRegistry: new TestResponseSerializerRegistry(),
            routingTable: new TestRoutingTable(0x7777));

        var act = async () => await processor.StartAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*0x7777*");
    }

    [Fact]
    public async Task ProcessMessageResultAsync_WhenSuccessSerializationFails_MustSendErrorFrame()
    {
        using var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var transport = new MockServerTransport("conn-serialize-fail");
        channelManager.AddChannel(transport);

        using var processor = new ResponseProcessor(
            channelManager,
            options: new ResponseProcessorOptions { ProcessorThreadCount = 1, ChannelCapacity = 16 },
            responseSerializerRegistry: new TestResponseSerializerRegistry(new ThrowingResponseSerializer(0x1234)),
            routingTable: new TestRoutingTable(0x1234));

        await processor.StartAsync();

        var messageId = Guid.NewGuid();
        var callContext = new ServiceCallContext(
            connectionId: "conn-serialize-fail",
            messageId: messageId,
            serviceName: "RoomHub",
            methodName: "Send",
            protocolId: 0x1234,
            requestData: null,
            messageType: MessageType.Request,
            receivedTime: DateTime.UtcNow,
            processorId: 0,
            flags: MessageFlags.None);

        await processor.ProcessMessageResultAsync(new MessageProcessedEventArgs(
            callContext,
            result: "this result cannot be serialized by the registered serializer",
            processingTime: TimeSpan.FromMilliseconds(1),
            dispatcherId: 0,
            success: true));

        await SpinUntilAsync(() => transport.SentFrames.Count == 1);

        MessagePacket.TryReadFrom(transport.SentFrames[0], out var packet).Should().BeTrue();
        packet.Header.Type.Should().Be(MessageType.Error);
        packet.Header.MessageId.Should().Be(messageId);
        packet.Header.ProtocolId.Should().Be(0x1234);

        var error = MemoryPackSerializer.Deserialize<ErrorResponse>(packet.Payload)!;
        error.ErrorCode.Should().Be("INVALID_OPERATION");
        error.ErrorMessage.Should().Contain("响应序列化失败");
    }

    private static async Task SpinUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(20, cts.Token);
        }
    }

    private sealed class TestRoutingTable : IServiceRoutingTable
    {
        private readonly ushort[] _protocolIds;

        public TestRoutingTable(params ushort[] protocolIds)
        {
            _protocolIds = protocolIds;
        }

        public ReadOnlySpan<ushort> EnumerateProtocolIds() => _protocolIds;

        public bool IsProtocolIdValid(string hub, ushort protocolId)
            => _protocolIds.Contains(protocolId);

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            ushort protocolId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            string hub,
            ushort protocolId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            ushort protocolId,
            string serviceKey,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            string hub,
            ushort protocolId,
            string serviceKey,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class TestResponseSerializerRegistry : IResponseSerializerRegistry
    {
        private readonly IResponseSerializer[] _serializers;
        private readonly Dictionary<ushort, IResponseSerializer> _byProtocolId = new();

        public TestResponseSerializerRegistry(params IResponseSerializer[] serializers)
        {
            _serializers = serializers;
            foreach (var serializer in serializers)
            {
                _byProtocolId[serializer.ProtocolId] = serializer;
            }
        }

        public bool TryGetSerializer(ushort protocolId, [NotNullWhen(true)] out IResponseSerializer? serializer)
            => _byProtocolId.TryGetValue(protocolId, out serializer);

        public ReadOnlySpan<IResponseSerializer> EnumerateSerializers() => _serializers;
    }

    private sealed class ThrowingResponseSerializer : IResponseSerializer
    {
        public ThrowingResponseSerializer(ushort protocolId)
        {
            ProtocolId = protocolId;
        }

        public ushort ProtocolId { get; }

        public void Serialize(object response, IBufferWriter<byte> writer)
            => throw new InvalidOperationException("serializer failed");

        public ValueTask SerializeAsync(object response, IBufferWriter<byte> writer, CancellationToken cancellationToken = default)
        {
            Serialize(response, writer);
            return ValueTask.CompletedTask;
        }

        public bool TryGetTypedSerializer<T>(out Action<T, IBufferWriter<byte>> serializer)
        {
            serializer = null!;
            return false;
        }
    }
}
