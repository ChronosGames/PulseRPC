namespace PulseRPC.Server.Models;

/// <summary>
/// Wrapper for service method results or exceptions.
/// </summary>
public sealed class ResponseEnvelope
{
    /// <summary>
    /// Correlation identifier (must match request RequestId).
    /// </summary>
    public Guid RequestId { get; init; }

    /// <summary>
    /// True for success, False for error.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Serialized return value (if success).
    /// Mutually exclusive with ExceptionDetails.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>
    /// Exception information (if error).
    /// Mutually exclusive with Payload.
    /// </summary>
    public ExceptionData? ExceptionDetails { get; init; }

    /// <summary>
    /// Completion timestamp (UTC).
    /// </summary>
    public DateTime CompletedAt { get; init; }

    /// <summary>
    /// Execution duration in milliseconds.
    /// </summary>
    public double DurationMs { get; init; }

    /// <summary>
    /// Creates a success response.
    /// </summary>
    public static ResponseEnvelope CreateSuccess(Guid requestId, ReadOnlyMemory<byte> payload, double durationMs)
    {
        return new ResponseEnvelope
        {
            RequestId = requestId,
            IsSuccess = true,
            Payload = payload,
            ExceptionDetails = null,
            CompletedAt = DateTime.UtcNow,
            DurationMs = durationMs
        };
    }

    /// <summary>
    /// Creates an error response.
    /// </summary>
    public static ResponseEnvelope CreateError(Guid requestId, ExceptionData exceptionDetails, double durationMs)
    {
        return new ResponseEnvelope
        {
            RequestId = requestId,
            IsSuccess = false,
            Payload = ReadOnlyMemory<byte>.Empty,
            ExceptionDetails = exceptionDetails,
            CompletedAt = DateTime.UtcNow,
            DurationMs = durationMs
        };
    }

    /// <summary>
    /// Validates that exactly one of Payload or ExceptionDetails is populated.
    /// </summary>
    public bool IsValidResponse()
    {
        return (IsSuccess && Payload.Length > 0 && ExceptionDetails == null) ||
               (!IsSuccess && Payload.Length == 0 && ExceptionDetails != null);
    }
}

/// <summary>
/// Structured exception information for error responses.
/// </summary>
public sealed class ExceptionData
{
    /// <summary>
    /// Full type name (e.g., "System.ArgumentException").
    /// </summary>
    public string ExceptionType { get; init; } = string.Empty;

    /// <summary>
    /// Exception message (sanitized).
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Stack trace (sanitized, no sensitive paths).
    /// </summary>
    public string? StackTrace { get; init; }

    /// <summary>
    /// Recursive inner exception (if present).
    /// </summary>
    public ExceptionData? InnerException { get; init; }

    /// <summary>
    /// Creates ExceptionData from an exception.
    /// </summary>
    public static ExceptionData FromException(Exception exception)
    {
        return new ExceptionData
        {
            ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            Message = exception.Message,
            StackTrace = SanitizeStackTrace(exception.StackTrace),
            InnerException = exception.InnerException != null
                ? FromException(exception.InnerException)
                : null
        };
    }

    private static string? SanitizeStackTrace(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
            return null;

        // Remove sensitive file paths (keep method names only)
        // This is a simplified implementation - production code should be more thorough
        var lines = stackTrace.Split('\n');
        var sanitized = lines.Select(line =>
        {
            // Keep method names, remove file paths
            var atIndex = line.IndexOf(" in ", StringComparison.Ordinal);
            return atIndex > 0 ? line[..atIndex] : line;
        });

        return string.Join('\n', sanitized);
    }
}
