using FluentAssertions;
using MemoryPack;
using PulseRPC.Server.Models;
using PulseRPC.Server.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// Unit tests for MessageReceiver (T037).
/// Tests buffering of incomplete messages, oversized message handling, connection management.
/// </summary>
public class MessageReceiverTests
{
    [Fact]
    public void MessageReceiver_ShouldInitialize_WithValidTransport()
    {
        // Arrange: Create mock transport
        var transport = new MockPulseServerTransport();

        // Act: Create receiver
        var receiver = new MessageReceiver(transport);

        // Assert: Should initialize successfully
        receiver.Should().NotBeNull();
        receiver.TotalMessagesReceived.Should().Be(0);
        receiver.TotalParseErrors.Should().Be(0);
    }

    [Fact]
    public void MessageReceiver_ShouldThrow_WhenTransportIsNull()
    {
        // Act & Assert: Should throw ArgumentNullException
        Assert.Throws<ArgumentNullException>(() => new MessageReceiver(null!));
    }

    [Fact]
    public async Task MessageReceiver_ShouldStartAndStop_Successfully()
    {
        // Arrange
        var transport = new MockPulseServerTransport();
        var receiver = new MessageReceiver(transport);

        // Act: Start and stop
        await receiver.StartAsync();
        transport.IsListening.Should().BeTrue();

        await receiver.StopAsync();
        transport.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task MessageReceiver_ShouldReceiveMessage_WhenCompleteMessageArrives()
    {
        // Arrange
        var transport = new MockPulseServerTransport();
        var receiver = new MessageReceiver(transport);

        MessageReceivedEventArgs? receivedEvent = null;
        receiver.MessageReceived += (sender, e) => receivedEvent = e;

        await receiver.StartAsync();

        // Create a valid message
        var message = new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Request,
            RequestId = Guid.NewGuid(),
            ServiceName = "TestService",
            MethodName = "TestMethod",
            Payload = new byte[] { 1, 2, 3 }
        };

        var serialized = MemoryPackSerializer.Serialize(message);
        var framed = FrameMessage(serialized);

        // Act: Simulate data received
        transport.SimulateConnectionAccepted("conn1");
        transport.SimulateDataReceived("conn1", framed);

        await Task.Delay(100); // Allow async processing

        // Assert: Message should be received
        receivedEvent.Should().NotBeNull();
        receivedEvent!.Message.RequestId.Should().Be(message.RequestId);
        receivedEvent.ConnectionId.Should().Be("conn1");
        receiver.TotalMessagesReceived.Should().Be(1);
    }

    [Fact]
    public async Task MessageReceiver_ShouldBufferIncompleteMessage_AndProcessWhenComplete()
    {
        // Arrange
        var transport = new MockPulseServerTransport();
        var receiver = new MessageReceiver(transport);

        var receivedMessages = new List<RpcMessage>();
        receiver.MessageReceived += (sender, e) => receivedMessages.Add(e.Message);

        await receiver.StartAsync();

        var message = new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Request,
            RequestId = Guid.NewGuid(),
            ServiceName = "TestService",
            MethodName = "TestMethod"
        };

        var serialized = MemoryPackSerializer.Serialize(message);
        var framed = FrameMessage(serialized);

        transport.SimulateConnectionAccepted("conn1");

        // Act: Send message in two parts
        var firstPart = framed.Slice(0, framed.Length / 2);
        var secondPart = framed.Slice(framed.Length / 2);

        transport.SimulateDataReceived("conn1", firstPart);
        await Task.Delay(50);
        receivedMessages.Should().BeEmpty("First part is incomplete");

        transport.SimulateDataReceived("conn1", secondPart);
        await Task.Delay(100);

        // Assert: Message should be received after second part arrives
        receivedMessages.Should().ContainSingle();
        receivedMessages[0].RequestId.Should().Be(message.RequestId);
    }

    [Fact]
    public async Task MessageReceiver_ShouldHandleMultipleMessagesInOneFrame()
    {
        // Arrange
        var transport = new MockPulseServerTransport();
        var receiver = new MessageReceiver(transport);

        var receivedMessages = new List<RpcMessage>();
        receiver.MessageReceived += (sender, e) => receivedMessages.Add(e.Message);

        await receiver.StartAsync();

        // Create 3 messages
        var messages = Enumerable.Range(0, 3)
            .Select(i => new RpcMessage
            {
                ProtocolVersion = 1,
                MessageType = MessageType.Request,
                RequestId = Guid.NewGuid(),
                ServiceName = $"Service{i}",
                MethodName = $"Method{i}"
            })
            .ToList();

        // Frame all messages together
        var frames = messages.Select(m =>
        {
            var serialized = MemoryPackSerializer.Serialize(m);
            return FrameMessage(serialized);
        }).ToArray();

        var combined = new byte[frames.Sum(f => f.Length)];
        var offset = 0;
        foreach (var frame in frames)
        {
            frame.Span.CopyTo(combined.AsSpan(offset));
            offset += frame.Length;
        }

        transport.SimulateConnectionAccepted("conn1");

        // Act: Send all messages at once
        transport.SimulateDataReceived("conn1", combined);
        await Task.Delay(150);

        // Assert: All 3 messages should be received
        receivedMessages.Should().HaveCount(3);
        receivedMessages.Select(m => m.ServiceName).Should().BeEquivalentTo(new[] { "Service0", "Service1", "Service2" });
    }

    [Fact]
    public async Task MessageReceiver_ShouldRaiseParseError_OnInvalidMessage()
    {
        // Arrange
        var transport = new MockPulseServerTransport();
        var receiver = new MessageReceiver(transport);

        MessageParseErrorEventArgs? errorEvent = null;
        receiver.ParseError += (sender, e) => errorEvent = e;

        await receiver.StartAsync();

        // Invalid message data
        var invalidData = FrameMessage(new byte[] { 99, 0xFF, 0xFE }); // Protocol version 99

        transport.SimulateConnectionAccepted("conn1");

        // Act: Send invalid data
        transport.SimulateDataReceived("conn1", invalidData);
        await Task.Delay(100);

        // Assert: Parse error should be raised
        errorEvent.Should().NotBeNull();
        errorEvent!.ConnectionId.Should().Be("conn1");
        errorEvent.ErrorType.Should().Contain("Protocol");
        receiver.TotalParseErrors.Should().Be(1);
    }

    [Fact]
    public async Task MessageReceiver_ShouldClearBuffer_WhenConnectionClosed()
    {
        // Arrange
        var transport = new MockPulseServerTransport();
        var receiver = new MessageReceiver(transport);

        await receiver.StartAsync();

        var message = new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Request,
            RequestId = Guid.NewGuid(),
            ServiceName = "TestService",
            MethodName = "TestMethod"
        };

        var serialized = MemoryPackSerializer.Serialize(message);
        var framed = FrameMessage(serialized);

        transport.SimulateConnectionAccepted("conn1");

        // Send incomplete message
        var partial = framed.Slice(0, framed.Length / 2);
        transport.SimulateDataReceived("conn1", partial);

        // Act: Close connection
        transport.SimulateConnectionClosed("conn1");

        // Send rest of message after closure (should be ignored)
        var rest = framed.Slice(framed.Length / 2);
        transport.SimulateDataReceived("conn1", rest);

        await Task.Delay(100);

        // Assert: Message should not be received (buffer was cleared)
        receiver.TotalMessagesReceived.Should().Be(0);
    }

    [Fact]
    public void MessageReceiver_ShouldDisposeCleanly()
    {
        // Arrange
        var transport = new MockPulseServerTransport();
        var receiver = new MessageReceiver(transport);

        // Act: Dispose
        receiver.Dispose();

        // Assert: Should not throw
        // Multiple disposes should be safe
        receiver.Dispose();
    }

    [Fact]
    public async Task MessageReceiver_ShouldRejectOversizedMessage()
    {
        // Arrange: Receiver with 1MB max buffer
        var transport = new MockPulseServerTransport();
        var options = new MessageReceiverOptions { MaxBufferSize = 1024 * 1024 };
        var receiver = new MessageReceiver(transport, options);

        MessageParseErrorEventArgs? errorEvent = null;
        receiver.ParseError += (sender, e) => errorEvent = e;

        await receiver.StartAsync();

        // Create message claiming to be 2MB (exceeds buffer)
        var fakeLength = 2 * 1024 * 1024;
        var frame = new byte[4 + 100]; // Length header + some data
        BitConverter.GetBytes(fakeLength).CopyTo(frame, 0);

        transport.SimulateConnectionAccepted("conn1");

        // Act: Send oversized message frame
        transport.SimulateDataReceived("conn1", frame);
        await Task.Delay(100);

        // Assert: Should handle gracefully (no crash)
        receiver.TotalMessagesReceived.Should().Be(0);
    }

    /// <summary>
    /// Frames a message with length prefix (4 bytes, little-endian).
    /// </summary>
    private static ReadOnlyMemory<byte> FrameMessage(ReadOnlyMemory<byte> message)
    {
        var framed = new byte[4 + message.Length];
        BitConverter.GetBytes(message.Length).CopyTo(framed, 0);
        message.Span.CopyTo(framed.AsSpan(4));
        return framed;
    }
}
