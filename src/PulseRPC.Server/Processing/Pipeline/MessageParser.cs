using MemoryPack;
using PulseRPC.Server.Health; using PulseRPC.Server.Processing; using PulseRPC.Server.Channels; using PulseRPC.Server.Services; using PulseRPC.Server.Services.Scheduling;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Processing.Pipeline;

/// <summary>
/// Parses network message bytes into RpcMessage instances.
/// Handles protocol validation, deserialization, and error detection.
/// </summary>
public sealed class MessageParser
{
    private const int MaxPayloadSize = 10 * 1024 * 1024; // 10MB
    private const byte SupportedProtocolVersion = 1;

    /// <summary>
    /// Parses a raw message buffer into an RpcMessage.
    /// </summary>
    /// <param name="buffer">The raw message bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parse result containing the message or error information.</returns>
    public ValueTask<ParseResult> ParseAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check minimum message size
            if (buffer.Length < 1)
            {
                return new ValueTask<ParseResult>(ParseResult.Failure("EmptyMessage", "Message buffer is empty"));
            }

            // Validate protocol version (first byte)
            var protocolVersion = buffer.Span[0];
            if (protocolVersion != SupportedProtocolVersion)
            {
                return new ValueTask<ParseResult>(ParseResult.Failure(
                    "ProtocolVersionMismatch",
                    $"Unsupported protocol version: {protocolVersion}. Expected: {SupportedProtocolVersion}"));
            }

            // Check payload size before deserialization
            if (buffer.Length > MaxPayloadSize)
            {
                return new ValueTask<ParseResult>(ParseResult.Failure(
                    "PayloadTooLarge",
                    $"Message size {buffer.Length} exceeds maximum of {MaxPayloadSize} bytes"));
            }

            // Deserialize using MemoryPack
            RpcMessage? message;
            try
            {
                message = MemoryPackSerializer.Deserialize<RpcMessage>(buffer.Span);
            }
            catch (MemoryPackSerializationException ex)
            {
                return new ValueTask<ParseResult>(ParseResult.Failure(
                    "DeserializationFailed",
                    $"Failed to deserialize message: {ex.Message}"));
            }

            if (message == null)
            {
                return new ValueTask<ParseResult>(ParseResult.Failure(
                    "DeserializationFailed",
                    "Deserialization returned null"));
            }

            // Stamp received time (create new instance with updated timestamp)
            var stampedMessage = new RpcMessage
            {
                ProtocolVersion = message.ProtocolVersion,
                MessageType = message.MessageType,
                RequestId = message.RequestId,
                ServiceName = message.ServiceName,
                MethodName = message.MethodName,
                Payload = message.Payload,
                Metadata = message.Metadata,
                ReceivedAt = Stopwatch.GetTimestamp()
            };

            // Validate message fields
            if (!stampedMessage.IsValid(out var validationError))
            {
                return new ValueTask<ParseResult>(ParseResult.Failure(
                    "ValidationFailed",
                    validationError ?? "Message validation failed"));
            }

            return new ValueTask<ParseResult>(ParseResult.Success(stampedMessage));
        }
        catch (Exception ex)
        {
            return new ValueTask<ParseResult>(ParseResult.Failure(
                "UnexpectedError",
                $"Unexpected error during parsing: {ex.Message}"));
        }
    }

    /// <summary>
    /// Tries to parse multiple messages from a buffer (for batch processing).
    /// </summary>
    /// <param name="buffer">Buffer containing multiple messages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of parse results.</returns>
    public async ValueTask<ParseResult[]> ParseBatchAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var results = new System.Collections.Generic.List<ParseResult>();
        var offset = 0;

        while (offset < buffer.Length)
        {
            // Read message length (4 bytes, little-endian)
            if (buffer.Length - offset < 4)
            {
                results.Add(ParseResult.Failure("IncompleteMessage", "Not enough bytes for message length"));
                break;
            }

            var lengthSpan = buffer.Slice(offset, 4).Span;
            var messageLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(lengthSpan);

            offset += 4;

            // Validate message length
            if (messageLength <= 0 || messageLength > MaxPayloadSize)
            {
                results.Add(ParseResult.Failure("InvalidMessageLength", $"Invalid message length: {messageLength}"));
                break;
            }

            if (buffer.Length - offset < messageLength)
            {
                results.Add(ParseResult.Failure("IncompleteMessage", $"Expected {messageLength} bytes, but only {buffer.Length - offset} available"));
                break;
            }

            // Parse this message
            var messageBuffer = buffer.Slice(offset, messageLength);
            var result = await ParseAsync(messageBuffer, cancellationToken);
            results.Add(result);

            offset += messageLength;
        }

        return results.ToArray();
    }
}

/// <summary>
/// Result of a message parsing operation.
/// </summary>
public sealed class ParseResult
{
    /// <summary>
    /// Gets whether parsing succeeded.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Gets the parsed message (if successful).
    /// </summary>
    public RpcMessage? Message { get; init; }

    /// <summary>
    /// Gets the error type (if failed).
    /// </summary>
    public string? ErrorType { get; init; }

    /// <summary>
    /// Gets the error message (if failed).
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful parse result.
    /// </summary>
    public static ParseResult Success(RpcMessage message) => new()
    {
        IsSuccess = true,
        Message = message
    };

    /// <summary>
    /// Creates a failed parse result.
    /// </summary>
    public static ParseResult Failure(string errorType, string errorMessage) => new()
    {
        IsSuccess = false,
        ErrorType = errorType,
        ErrorMessage = errorMessage
    };
}
