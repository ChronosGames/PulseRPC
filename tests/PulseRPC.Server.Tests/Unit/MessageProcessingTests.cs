using Xunit;
using PulseRPC.Server.Engine;
using PulseRPC.Server.Memory;
using PulseRPC.Server.Scheduling;
using PulseRPC.Abstractions.Channels;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// 消息处理流程测试，验证元数据传递的正确性
/// </summary>
public class MessageProcessingTests
{
    [Fact]
    public void MessageSlot_Should_PreserveMetadata()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var header = new MessageHeader(MessageType.Request, "TestService", "TestMethod")
        {
            MessageId = messageId,
            Flags = MessageFlags.RequireResponse
        };
        var payload = new byte[] { 1, 2, 3, 4 };
        var connectionId = "test-connection-123";

        // Act
        var slot = new MessageSlot
        {
            MessageId = messageId,
            ConnectionId = connectionId,
            Header = header,
            Payload = payload.AsMemory(),
            Priority = MessagePriority.Normal,
            EnqueueTime = Stopwatch.GetTimestamp(),
            Status = MessageStatus.Pending
        };

        // Assert
        Assert.Equal(messageId, slot.MessageId);
        Assert.Equal(connectionId, slot.ConnectionId);
        Assert.Equal("TestService", slot.Header.ServiceName);
        Assert.Equal("TestMethod", slot.Header.MethodName);
        Assert.Equal(payload, slot.Payload.ToArray());
        Assert.Equal(MessagePriority.Normal, slot.Priority);
        Assert.Equal(MessageStatus.Pending, slot.Status);
    }

    [Fact]
    public void MessageEnvelope_Should_PreserveMetadata()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var header = new MessageHeader(MessageType.Request, "TestService", "TestMethod")
        {
            MessageId = messageId,
            Flags = MessageFlags.RequireResponse,
            Timestamp = DateTimeOffset.UtcNow.Ticks
        };
        var payload = new byte[] { 5, 6, 7, 8 };
        var connectionId = "test-connection-456";

        // Act
        var envelope = new MessageEnvelope
        {
            MessageId = messageId,
            ConnectionId = connectionId,
            Header = header,
            Payload = payload.AsMemory(),
            Priority = MessagePriority.High,
            EnqueueTime = Stopwatch.GetTimestamp(),
            Status = MessageStatus.Processing
        };

        // Assert
        Assert.Equal(messageId, envelope.MessageId);
        Assert.Equal(connectionId, envelope.ConnectionId);
        Assert.Equal("TestService", envelope.Header.ServiceName);
        Assert.Equal("TestMethod", envelope.Header.MethodName);
        Assert.Equal(MessageType.Request, envelope.Header.Type);
        Assert.Equal(payload, envelope.Payload.ToArray());
        Assert.Equal(MessagePriority.High, envelope.Priority);
        Assert.Equal(MessageStatus.Processing, envelope.Status);
    }

    [Fact]
    public void MessageSlot_To_MessageEnvelope_Should_PreserveMetadata()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var header = new MessageHeader(MessageType.Request, "TestService", "TestMethod")
        {
            MessageId = messageId,
            Flags = MessageFlags.RequireResponse
        };
        var payload = new byte[] { 9, 10, 11, 12 };
        var connectionId = "test-connection-789";
        var enqueueTime = Stopwatch.GetTimestamp();

        var slot = new MessageSlot
        {
            MessageId = messageId,
            ConnectionId = connectionId,
            Header = header,
            Payload = payload.AsMemory(),
            Priority = MessagePriority.Critical,
            EnqueueTime = enqueueTime,
            Status = MessageStatus.Pending
        };

        // Act - 模拟转换过程
        var envelope = new MessageEnvelope
        {
            MessageId = slot.MessageId,
            ConnectionId = slot.ConnectionId,
            Header = slot.Header,
            Payload = slot.Payload,
            Priority = slot.Priority,
            EnqueueTime = slot.EnqueueTime,
            Status = MessageStatus.Processing
        };

        // Assert
        Assert.Equal(slot.MessageId, envelope.MessageId);
        Assert.Equal(slot.ConnectionId, envelope.ConnectionId);
        Assert.Equal(slot.Header.ServiceName, envelope.Header.ServiceName);
        Assert.Equal(slot.Header.MethodName, envelope.Header.MethodName);
        Assert.Equal(slot.Header.MessageId, envelope.Header.MessageId);
        Assert.Equal(slot.Payload.ToArray(), envelope.Payload.ToArray());
        Assert.Equal(slot.Priority, envelope.Priority);
        Assert.Equal(slot.EnqueueTime, envelope.EnqueueTime);
    }

    [Fact]
    public void MessagePriority_Should_BeCorrectlyOrdered()
    {
        // Arrange & Act & Assert
        Assert.True(MessagePriority.Critical > MessagePriority.High);
        Assert.True(MessagePriority.High > MessagePriority.Normal);
        Assert.True(MessagePriority.Normal > MessagePriority.Low);
    }

    [Fact]
    public void MessageStatus_Should_HaveExpectedValues()
    {
        // Arrange & Act & Assert
        Assert.Equal(MessageStatus.Pending, (MessageStatus)0);
        Assert.Equal(MessageStatus.Processing, (MessageStatus)1);
        Assert.Equal(MessageStatus.Completed, (MessageStatus)2);
        Assert.Equal(MessageStatus.Failed, (MessageStatus)3);
    }

    [Theory]
    [InlineData("connection-1", "Service1", "Method1")]
    [InlineData("connection-2", "Service2", "Method2")]
    [InlineData("connection-3", "Service3", "Method3")]
    public void MessageSlot_Should_HandleDifferentConnectionsAndServices(
        string connectionId, string serviceName, string methodName)
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var header = new MessageHeader(MessageType.Request, serviceName, methodName)
        {
            MessageId = messageId
        };
        var payload = new byte[] { 1, 2, 3 };

        // Act
        var slot = new MessageSlot
        {
            MessageId = messageId,
            ConnectionId = connectionId,
            Header = header,
            Payload = payload.AsMemory(),
            Priority = MessagePriority.Normal,
            EnqueueTime = Stopwatch.GetTimestamp(),
            Status = MessageStatus.Pending
        };

        // Assert
        Assert.Equal(connectionId, slot.ConnectionId);
        Assert.Equal(serviceName, slot.Header.ServiceName);
        Assert.Equal(methodName, slot.Header.MethodName);
        Assert.Equal(messageId, slot.MessageId);
        Assert.Equal(messageId, slot.Header.MessageId);
    }
}


