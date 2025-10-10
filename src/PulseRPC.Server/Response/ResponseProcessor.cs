using PulseRPC.Server.Models;
using System.Diagnostics;

namespace PulseRPC.Server.Response;

/// <summary>
/// Generates ResponseEnvelope from service method results or exceptions.
/// </summary>
public sealed class ResponseProcessor
{
    /// <summary>
    /// Creates a success response from a method result.
    /// </summary>
    public ResponseEnvelope CreateSuccessResponse(
        Guid requestId,
        object? result,
        long startTimestamp)
    {
        var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        var durationMs = elapsed * 1000.0 / Stopwatch.Frequency;

        // Serialize result to bytes
        // TODO: Use actual serializer (MemoryPack) when available
        var payload = SerializeResult(result);

        return ResponseEnvelope.CreateSuccess(requestId, payload, durationMs);
    }

    /// <summary>
    /// Creates an error response from an exception.
    /// </summary>
    public ResponseEnvelope CreateErrorResponse(
        Guid requestId,
        Exception exception,
        long startTimestamp)
    {
        var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        var durationMs = elapsed * 1000.0 / Stopwatch.Frequency;

        var exceptionData = ExceptionData.FromException(exception);

        return ResponseEnvelope.CreateError(requestId, exceptionData, durationMs);
    }

    private ReadOnlyMemory<byte> SerializeResult(object? result)
    {
        if (result == null)
            return ReadOnlyMemory<byte>.Empty;

        // Simplified serialization for now
        // TODO: Integrate MemoryPack serializer
        if (result is string str)
        {
            return System.Text.Encoding.UTF8.GetBytes(str);
        }

        if (result is byte[] bytes)
        {
            return bytes;
        }

        // For other types, use System.Text.Json as fallback
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }
}
