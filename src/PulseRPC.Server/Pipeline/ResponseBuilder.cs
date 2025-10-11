using MemoryPack;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Models;
using System;
using System.Diagnostics;

namespace PulseRPC.Server.Pipeline;

/// <summary>
/// Builds RpcMessage responses from invocation results.
/// Handles FR-021 to FR-026: response serialization, error response generation.
/// </summary>
public sealed class ResponseBuilder
{
    private readonly ErrorResponseFactory _errorFactory;

    public ResponseBuilder()
    {
        _errorFactory = new ErrorResponseFactory();
    }

    /// <summary>
    /// Builds a success response message.
    /// </summary>
    public RpcMessage BuildSuccessResponse(Guid requestId, ReadOnlyMemory<byte> payload)
    {
        return new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Response,
            RequestId = requestId,
            ServiceName = string.Empty,
            MethodName = string.Empty,
            Payload = payload,
            ReceivedAt = Stopwatch.GetTimestamp()
        };
    }

    /// <summary>
    /// Builds an error response message from an InvocationResult.
    /// </summary>
    public RpcMessage BuildErrorResponse(Guid requestId, InvocationResult result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var errorPayload = _errorFactory.CreateErrorPayload(
            result.ErrorType ?? "UnknownError",
            result.ErrorMessage ?? "An error occurred",
            result.StackTrace);

        return new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Error,
            RequestId = requestId,
            ServiceName = string.Empty,
            MethodName = string.Empty,
            Payload = errorPayload,
            ReceivedAt = Stopwatch.GetTimestamp()
        };
    }

    /// <summary>
    /// Builds a response from an InvocationResult (success or error).
    /// </summary>
    public RpcMessage BuildResponse(Guid requestId, InvocationResult result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (result.IsSuccess)
        {
            return BuildSuccessResponse(requestId, result.Payload);
        }
        else
        {
            return BuildErrorResponse(requestId, result);
        }
    }

    /// <summary>
    /// Builds an error response from an exception.
    /// </summary>
    public RpcMessage BuildExceptionResponse(Guid requestId, Exception exception)
    {
        if (exception == null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        var errorPayload = _errorFactory.CreateErrorPayload(
            exception.GetType().FullName ?? "Exception",
            exception.Message,
            exception.StackTrace);

        return new RpcMessage
        {
            ProtocolVersion = 1,
            MessageType = MessageType.Error,
            RequestId = requestId,
            ServiceName = string.Empty,
            MethodName = string.Empty,
            Payload = errorPayload,
            ReceivedAt = Stopwatch.GetTimestamp()
        };
    }

    /// <summary>
    /// Serializes a response message to bytes for transmission.
    /// </summary>
    public ReadOnlyMemory<byte> SerializeResponse(RpcMessage response)
    {
        try
        {
            return MemoryPackSerializer.Serialize(response);
        }
        catch (Exception ex)
        {
            // If serialization fails, create a fallback error response
            var fallbackError = new RpcMessage
            {
                ProtocolVersion = 1,
                MessageType = MessageType.Error,
                RequestId = response.RequestId,
                ServiceName = string.Empty,
                MethodName = string.Empty,
                Payload = _errorFactory.CreateErrorPayload(
                    "SerializationFailed",
                    $"Failed to serialize response: {ex.Message}",
                    null),
                ReceivedAt = Stopwatch.GetTimestamp()
            };

            return MemoryPackSerializer.Serialize(fallbackError);
        }
    }
}
