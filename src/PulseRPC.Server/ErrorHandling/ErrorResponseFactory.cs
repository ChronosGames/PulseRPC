using PulseRPC.Server.Models;

namespace PulseRPC.Server.ErrorHandling;

/// <summary>
/// Factory for creating structured error responses.
/// </summary>
public static class ErrorResponseFactory
{
    /// <summary>
    /// Creates a protocol error response (version mismatch, parse failure).
    /// </summary>
    public static ResponseEnvelope CreateProtocolError(Guid requestId, string message, double durationMs = 0)
    {
        var exceptionData = new ExceptionData
        {
            ExceptionType = "ProtocolException",
            Message = message,
            StackTrace = null
        };

        return ResponseEnvelope.CreateError(requestId, exceptionData, durationMs);
    }

    /// <summary>
    /// Creates a service not found error response.
    /// </summary>
    public static ResponseEnvelope CreateServiceNotFoundError(Guid requestId, string serviceName, double durationMs = 0)
    {
        var exceptionData = new ExceptionData
        {
            ExceptionType = "ServiceNotFoundException",
            Message = $"Service '{serviceName}' not found in registry",
            StackTrace = null
        };

        return ResponseEnvelope.CreateError(requestId, exceptionData, durationMs);
    }

    /// <summary>
    /// Creates a timeout error response.
    /// </summary>
    public static ResponseEnvelope CreateTimeoutError(Guid requestId, string serviceName, string methodName, double durationMs)
    {
        var exceptionData = new ExceptionData
        {
            ExceptionType = "TimeoutException",
            Message = $"Method '{serviceName}.{methodName}' timed out after {durationMs:F2}ms",
            StackTrace = null
        };

        return ResponseEnvelope.CreateError(requestId, exceptionData, durationMs);
    }

    /// <summary>
    /// Creates a serialization error response.
    /// </summary>
    public static ResponseEnvelope CreateSerializationError(Guid requestId, string message, double durationMs = 0)
    {
        var exceptionData = new ExceptionData
        {
            ExceptionType = "SerializationException",
            Message = message,
            StackTrace = null
        };

        return ResponseEnvelope.CreateError(requestId, exceptionData, durationMs);
    }

    /// <summary>
    /// Creates a method not found error response.
    /// </summary>
    public static ResponseEnvelope CreateMethodNotFoundError(Guid requestId, string serviceName, string methodName, double durationMs = 0)
    {
        var exceptionData = new ExceptionData
        {
            ExceptionType = "MethodNotFoundException",
            Message = $"Method '{methodName}' not found in service '{serviceName}'",
            StackTrace = null
        };

        return ResponseEnvelope.CreateError(requestId, exceptionData, durationMs);
    }
}
