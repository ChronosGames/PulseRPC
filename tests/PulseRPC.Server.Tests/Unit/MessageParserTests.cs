using FluentAssertions;
using MemoryPack;
using PulseRPC.Server.Models;
using PulseRPC.Server.Pipeline;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// Unit tests for MessageParser (T038).
/// Tests protocol version validation, deserialization, size limits, and error handling.
/// </summary>
public class MessageParserTests
{
    private readonly MessageParser _parser;

    public MessageParserTests()
    {
        _parser = new MessageParser();
    }

    [Fact]
    public async Task ParseAsync_ShouldSucceed_WithValidMessage()
    {
        // Arrange: Create a valid RPC message
        var message = new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Request,
            RequestId = Guid.NewGuid(),
            ServiceName = "TestService",
            MethodName = "TestMethod",
            Payload = new byte[] { 1, 2, 3, 4 },
            Metadata = new Dictionary<string, string> { ["key"] = "value" }
        };

        var serialized = MemoryPackSerializer.Serialize(message);

        // Act: Parse the serialized message
        var result = await _parser.ParseAsync(serialized);

        // Assert: Parsing succeeded
        result.IsSuccess.Should().BeTrue();
        result.Message.Should().NotBeNull();
        result.Message!.RequestId.Should().Be(message.RequestId);
        result.Message.ServiceName.Should().Be("TestService");
        result.Message.MethodName.Should().Be("TestMethod");
        result.Message.Payload.Length.Should().Be(4);
    }

    [Fact]
    public async Task ParseAsync_ShouldFail_WithUnsupportedProtocolVersion()
    {
        // Arrange: Message with protocol version 99
        var message = new RpcMessage
        {
            ProtocolVersion = 99, // Unsupported
            MessageType = MessageType.Request,
            RequestId = Guid.NewGuid(),
            ServiceName = "TestService",
            MethodName = "TestMethod"
        };

        var serialized = MemoryPackSerializer.Serialize(message);

        // Act: Attempt to parse
        var result = await _parser.ParseAsync(serialized);

        // Assert: Should fail with protocol version error
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Contain("Protocol");
        result.ErrorMessage.Should().Contain("99");
    }

    [Fact]
    public async Task ParseAsync_ShouldFail_WithEmptyBuffer()
    {
        // Arrange: Empty buffer
        var emptyBuffer = ReadOnlyMemory<byte>.Empty;

        // Act: Attempt to parse
        var result = await _parser.ParseAsync(emptyBuffer);

        // Assert: Should fail with empty message error
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Be("EmptyMessage");
    }

    [Fact]
    public async Task ParseAsync_ShouldFail_WithOversizedPayload()
    {
        // Arrange: Message larger than 10MB
        var largePayload = new byte[11 * 1024 * 1024]; // 11MB
        var message = new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Request,
            RequestId = Guid.NewGuid(),
            ServiceName = "TestService",
            MethodName = "TestMethod",
            Payload = largePayload
        };

        var serialized = MemoryPackSerializer.Serialize(message);

        // Act: Attempt to parse
        var result = await _parser.ParseAsync(serialized);

        // Assert: Should fail with size limit error
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Contain("Payload");
    }

    [Fact]
    public async Task ParseAsync_ShouldFail_WithCorruptedData()
    {
        // Arrange: Corrupted/invalid data
        var corruptedData = new byte[] { 1, 0xFF, 0xFE, 0xFD, 0xFC, 0xFB };

        // Act: Attempt to parse
        var result = await _parser.ParseAsync(corruptedData);

        // Assert: Should fail with deserialization error
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Contain("Deserialization");
    }

    [Fact]
    public async Task ParseAsync_ShouldFail_WithEmptyServiceName()
    {
        // Arrange: Message with empty service name
        var message = new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Request,
            RequestId = Guid.NewGuid(),
            ServiceName = "", // Empty
            MethodName = "TestMethod"
        };

        var serialized = MemoryPackSerializer.Serialize(message);

        // Act: Attempt to parse
        var result = await _parser.ParseAsync(serialized);

        // Assert: Should fail validation
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Be("ValidationFailed");
        result.ErrorMessage.Should().Contain("ServiceName");
    }

    [Fact]
    public async Task ParseAsync_ShouldFail_WithEmptyRequestId()
    {
        // Arrange: Message with empty GUID
        var message = new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Request,
            RequestId = Guid.Empty, // Invalid
            ServiceName = "TestService",
            MethodName = "TestMethod"
        };

        var serialized = MemoryPackSerializer.Serialize(message);

        // Act: Attempt to parse
        var result = await _parser.ParseAsync(serialized);

        // Assert: Should fail validation
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Be("ValidationFailed");
        result.ErrorMessage.Should().Contain("RequestId");
    }

    [Fact]
    public async Task ParseAsync_ShouldSucceed_WithDifferentMessageTypes()
    {
        // Arrange: Test all message types
        var messageTypes = new[]
        {
            MessageType.Request,
            MessageType.Response,
            MessageType.Error,
            MessageType.Ping,
            MessageType.Pong
        };

        foreach (var messageType in messageTypes)
        {
            var message = new RpcMessage
            {
                ProtocolVersion = 1,
                MessageType = messageType,
                RequestId = Guid.NewGuid(),
                ServiceName = "TestService",
                MethodName = "TestMethod"
            };

            var serialized = MemoryPackSerializer.Serialize(message);

            // Act: Parse
            var result = await _parser.ParseAsync(serialized);

            // Assert: Should succeed for all types
            result.IsSuccess.Should().BeTrue($"MessageType {messageType} should parse successfully");
            result.Message!.MessageType.Should().Be(messageType);
        }
    }

    [Fact]
    public async Task ParseAsync_ShouldPreserveMetadata()
    {
        // Arrange: Message with multiple metadata entries
        var metadata = new Dictionary<string, string>
        {
            ["TraceId"] = "trace-123",
            ["UserId"] = "user-456",
            ["Priority"] = "High"
        };

        var message = new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Request,
            RequestId = Guid.NewGuid(),
            ServiceName = "TestService",
            MethodName = "TestMethod",
            Metadata = metadata
        };

        var serialized = MemoryPackSerializer.Serialize(message);

        // Act: Parse
        var result = await _parser.ParseAsync(serialized);

        // Assert: Metadata preserved
        result.IsSuccess.Should().BeTrue();
        result.Message!.Metadata.Should().NotBeNull();
        result.Message.Metadata.Should().ContainKey("TraceId");
        result.Message.Metadata!["TraceId"].Should().Be("trace-123");
        result.Message.Metadata.Should().ContainKey("UserId");
        result.Message.Metadata["UserId"].Should().Be("user-456");
    }

    [Fact]
    public async Task ParseAsync_ShouldFail_WithTooManyMetadataEntries()
    {
        // Arrange: Message with > 50 metadata entries
        var metadata = new Dictionary<string, string>();
        for (int i = 0; i < 51; i++)
        {
            metadata[$"key{i}"] = $"value{i}";
        }

        var message = new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Request,
            RequestId = Guid.NewGuid(),
            ServiceName = "TestService",
            MethodName = "TestMethod",
            Metadata = metadata
        };

        var serialized = MemoryPackSerializer.Serialize(message);

        // Act: Parse
        var result = await _parser.ParseAsync(serialized);

        // Assert: Should fail validation
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Be("ValidationFailed");
        result.ErrorMessage.Should().Contain("Metadata");
    }

    [Fact]
    public async Task ParseAsync_ShouldStampReceivedTimestamp()
    {
        // Arrange: Valid message
        var message = new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Request,
            RequestId = Guid.NewGuid(),
            ServiceName = "TestService",
            MethodName = "TestMethod",
            ReceivedAt = 0 // Will be overwritten
        };

        var serialized = MemoryPackSerializer.Serialize(message);

        // Act: Parse
        var result = await _parser.ParseAsync(serialized);

        // Assert: ReceivedAt timestamp should be set
        result.IsSuccess.Should().BeTrue();
        result.Message!.ReceivedAt.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ParseAsync_ShouldHandleNullPayload()
    {
        // Arrange: Message with empty payload (valid for void methods)
        var message = new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Request,
            RequestId = Guid.NewGuid(),
            ServiceName = "TestService",
            MethodName = "VoidMethod",
            Payload = ReadOnlyMemory<byte>.Empty
        };

        var serialized = MemoryPackSerializer.Serialize(message);

        // Act: Parse
        var result = await _parser.ParseAsync(serialized);

        // Assert: Should succeed with empty payload
        result.IsSuccess.Should().BeTrue();
        result.Message!.Payload.Length.Should().Be(0);
    }

    [Fact]
    public async Task ParseAsync_ShouldFail_WithServiceNameTooLong()
    {
        // Arrange: Service name > 200 characters
        var longServiceName = new string('A', 201);
        var message = new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Request,
            RequestId = Guid.NewGuid(),
            ServiceName = longServiceName,
            MethodName = "TestMethod"
        };

        var serialized = MemoryPackSerializer.Serialize(message);

        // Act: Parse
        var result = await _parser.ParseAsync(serialized);

        // Assert: Should fail validation
        result.IsSuccess.Should().BeFalse();
        result.ErrorType.Should().Be("ValidationFailed");
        result.ErrorMessage.Should().Contain("ServiceName");
        result.ErrorMessage.Should().Contain("200");
    }

    [Fact]
    public async Task ParseAsync_ShouldSucceed_WithExactly200CharServiceName()
    {
        // Arrange: Service name exactly 200 characters (boundary test)
        var serviceName = new string('A', 200);
        var message = new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Request,
            RequestId = Guid.NewGuid(),
            ServiceName = serviceName,
            MethodName = "TestMethod"
        };

        var serialized = MemoryPackSerializer.Serialize(message);

        // Act: Parse
        var result = await _parser.ParseAsync(serialized);

        // Assert: Should succeed (200 is valid)
        result.IsSuccess.Should().BeTrue();
        result.Message!.ServiceName.Length.Should().Be(200);
    }
}
