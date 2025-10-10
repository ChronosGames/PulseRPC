using System.Buffers;
using PulseRPC.Server.Models;

namespace PulseRPC.Server.Response;

/// <summary>
/// Serializes ResponseEnvelope to wire format using MemoryPack.
/// Uses ArrayPool for efficient memory management.
/// </summary>
public sealed class ResponseSerializer
{
    private readonly ArrayPool<byte> _bufferPool;
    private readonly int _compressionThreshold;

    public ResponseSerializer(int compressionThreshold = 4096)
    {
        _bufferPool = ArrayPool<byte>.Shared;
        _compressionThreshold = compressionThreshold;
    }

    /// <summary>
    /// Serializes a ResponseEnvelope to bytes.
    /// </summary>
    public byte[] Serialize(ResponseEnvelope response)
    {
        try
        {
            // TODO: Replace with actual MemoryPack serialization when available
            // For now, use simple JSON serialization as placeholder
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                response.RequestId,
                response.IsSuccess,
                Payload = response.Payload.ToArray(),
                ExceptionDetails = response.ExceptionDetails != null ? new
                {
                    response.ExceptionDetails.ExceptionType,
                    response.ExceptionDetails.Message,
                    response.ExceptionDetails.StackTrace
                } : null,
                response.CompletedAt,
                response.DurationMs
            });

            return System.Text.Encoding.UTF8.GetBytes(json);
        }
        catch (Exception ex)
        {
            // Serialization failed - return error response
            var errorJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                RequestId = response.RequestId,
                IsSuccess = false,
                ExceptionDetails = new
                {
                    ExceptionType = "SerializationException",
                    Message = $"Failed to serialize response: {ex.Message}",
                    StackTrace = (string?)null
                },
                CompletedAt = DateTime.UtcNow,
                DurationMs = 0.0
            });

            return System.Text.Encoding.UTF8.GetBytes(errorJson);
        }
    }

    /// <summary>
    /// Serializes with ArrayPool buffer management.
    /// </summary>
    public Memory<byte> SerializeToPooledBuffer(ResponseEnvelope response, out int actualLength)
    {
        var bytes = Serialize(response);
        actualLength = bytes.Length;

        // Rent buffer from pool
        var buffer = _bufferPool.Rent(actualLength);
        bytes.CopyTo(buffer, 0);

        return buffer.AsMemory(0, actualLength);
    }

    /// <summary>
    /// Returns a rented buffer to the pool.
    /// </summary>
    public void ReturnBuffer(byte[] buffer)
    {
        _bufferPool.Return(buffer);
    }

    /// <summary>
    /// Checks if compression should be applied based on size.
    /// </summary>
    public bool ShouldCompress(int payloadSize)
    {
        return payloadSize > _compressionThreshold;
    }
}
