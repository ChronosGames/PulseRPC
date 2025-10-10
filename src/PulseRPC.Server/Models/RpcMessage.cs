using System.Diagnostics;

namespace PulseRPC.Server.Models;

/// <summary>
/// Represents a network message in the request-response pipeline.
/// </summary>
public sealed class RpcMessage
{
    /// <summary>
    /// Protocol version for compatibility checking.
    /// </summary>
    public byte ProtocolVersion { get; init; } = 1;

    /// <summary>
    /// Message type (Request, Response, Error, Ping, Pong).
    /// </summary>
    public MessageType MessageType { get; init; }

    /// <summary>
    /// Unique identifier for correlation.
    /// </summary>
    public Guid RequestId { get; init; }

    /// <summary>
    /// Target service identifier.
    /// </summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>
    /// Target method identifier.
    /// </summary>
    public string MethodName { get; init; } = string.Empty;

    /// <summary>
    /// Serialized parameters or return value.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>
    /// Headers, tracing info, priority.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// High-resolution timestamp when message was received (Stopwatch ticks).
    /// </summary>
    public long ReceivedAt { get; init; }

    /// <summary>
    /// Validates the message according to protocol rules.
    /// </summary>
    /// <returns>True if valid, false otherwise.</returns>
    public bool IsValid(out string? errorMessage)
    {
        // Check protocol version
        if (ProtocolVersion != 1)
        {
            errorMessage = $"Unsupported protocol version: {ProtocolVersion}. Expected: 1";
            return false;
        }

        // Check service name
        if (string.IsNullOrWhiteSpace(ServiceName))
        {
            errorMessage = "ServiceName cannot be null or empty";
            return false;
        }

        if (ServiceName.Length > 200)
        {
            errorMessage = $"ServiceName exceeds maximum length of 200 characters: {ServiceName.Length}";
            return false;
        }

        // Check method name
        if (string.IsNullOrWhiteSpace(MethodName))
        {
            errorMessage = "MethodName cannot be null or empty";
            return false;
        }

        if (MethodName.Length > 200)
        {
            errorMessage = $"MethodName exceeds maximum length of 200 characters: {MethodName.Length}";
            return false;
        }

        // Check payload size (10MB limit)
        const int MaxPayloadSize = 10 * 1024 * 1024; // 10MB
        if (Payload.Length > MaxPayloadSize)
        {
            errorMessage = $"Payload size {Payload.Length} exceeds maximum of {MaxPayloadSize} bytes (10MB)";
            return false;
        }

        // Check metadata count
        if (Metadata != null && Metadata.Count > 50)
        {
            errorMessage = $"Metadata count {Metadata.Count} exceeds maximum of 50 entries";
            return false;
        }

        // Check RequestId
        if (RequestId == Guid.Empty)
        {
            errorMessage = "RequestId cannot be empty";
            return false;
        }

        errorMessage = null;
        return true;
    }

    /// <summary>
    /// Creates a new RpcMessage with current timestamp.
    /// </summary>
    public static RpcMessage Create(
        Guid requestId,
        string serviceName,
        string methodName,
        ReadOnlyMemory<byte> payload,
        MessageType messageType = MessageType.Request,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new RpcMessage
        {
            RequestId = requestId,
            ServiceName = serviceName,
            MethodName = methodName,
            Payload = payload,
            MessageType = messageType,
            Metadata = metadata,
            ReceivedAt = Stopwatch.GetTimestamp(),
            ProtocolVersion = 1
        };
    }
}
