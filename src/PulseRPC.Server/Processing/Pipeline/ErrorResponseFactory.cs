using MemoryPack;
using PulseRPC.Server.Health; using PulseRPC.Server.Processing; using PulseRPC.Server.Channels; using PulseRPC.Server.Services; using PulseRPC.Server.Services.Scheduling;
using System;
using System.Collections.Generic;
using System.Text;

namespace PulseRPC.Server.Processing.Pipeline;

/// <summary>
/// Factory for creating structured error response payloads.
/// Handles exception serialization with stack trace sanitization.
/// </summary>
public sealed class ErrorResponseFactory
{
    /// <summary>
    /// Creates a serialized error payload.
    /// </summary>
    public ReadOnlyMemory<byte> CreateErrorPayload(
        string errorType,
        string errorMessage,
        string? stackTrace)
    {
        var errorData = new ErrorData
        {
            ErrorType = errorType ?? "UnknownError",
            ErrorMessage = errorMessage ?? "An error occurred",
            StackTrace = SanitizeStackTrace(stackTrace),
            Timestamp = DateTime.UtcNow
        };

        return MemoryPackSerializer.Serialize(errorData);
    }

    /// <summary>
    /// Creates an error payload from an exception, including inner exceptions.
    /// </summary>
    public ReadOnlyMemory<byte> CreateErrorPayloadFromException(Exception exception)
    {
        if (exception == null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        var innerExceptions = new List<InnerExceptionData>();
        var currentException = exception.InnerException;

        while (currentException != null)
        {
            innerExceptions.Add(new InnerExceptionData
            {
                ErrorType = currentException.GetType().FullName ?? "Exception",
                ErrorMessage = currentException.Message
            });

            currentException = currentException.InnerException;
        }

        var errorData = new ErrorData
        {
            ErrorType = exception.GetType().FullName ?? "Exception",
            ErrorMessage = exception.Message,
            StackTrace = SanitizeStackTrace(exception.StackTrace),
            InnerExceptions = innerExceptions.Count > 0 ? innerExceptions.ToArray() : null,
            Timestamp = DateTime.UtcNow
        };

        return MemoryPackSerializer.Serialize(errorData);
    }

    /// <summary>
    /// Creates a protocol error response.
    /// </summary>
    public static ResponseEnvelope CreateProtocolError(Guid requestId, string errorMessage)
    {
        return new ResponseEnvelope
        {
            RequestId = requestId,
            IsSuccess = false,
            ExceptionDetails = new ExceptionData
            {
                ExceptionType = "ProtocolError",
                Message = errorMessage
            },
            CompletedAt = DateTime.UtcNow,
            DurationMs = 0
        };
    }

    /// <summary>
    /// Creates a service not found error response.
    /// </summary>
    public static ResponseEnvelope CreateServiceNotFoundError(Guid requestId, string serviceName)
    {
        return new ResponseEnvelope
        {
            RequestId = requestId,
            IsSuccess = false,
            ExceptionDetails = new ExceptionData
            {
                ExceptionType = "ServiceNotFound",
                Message = $"Service '{serviceName}' not found"
            },
            CompletedAt = DateTime.UtcNow,
            DurationMs = 0
        };
    }

    /// <summary>
    /// Creates a method not found error response.
    /// </summary>
    public static ResponseEnvelope CreateMethodNotFoundError(Guid requestId, string serviceName, string methodName)
    {
        return new ResponseEnvelope
        {
            RequestId = requestId,
            IsSuccess = false,
            ExceptionDetails = new ExceptionData
            {
                ExceptionType = "MethodNotFound",
                Message = $"Method '{methodName}' not found on service '{serviceName}'"
            },
            CompletedAt = DateTime.UtcNow,
            DurationMs = 0
        };
    }

    /// <summary>
    /// Sanitizes stack trace to remove sensitive file paths.
    /// </summary>
    private static string? SanitizeStackTrace(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
        {
            return null;
        }

        var lines = stackTrace.Split('\n');
        var sanitized = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Remove full file paths but keep method names and line numbers
            var atIndex = trimmedLine.IndexOf(" at ", StringComparison.Ordinal);
            if (atIndex >= 0)
            {
                var inIndex = trimmedLine.IndexOf(" in ", StringComparison.Ordinal);
                if (inIndex > atIndex)
                {
                    // Keep only method name and line number
                    var methodPart = trimmedLine.Substring(0, inIndex);
                    var lineIndex = trimmedLine.IndexOf(":line ", StringComparison.Ordinal);
                    if (lineIndex > inIndex)
                    {
                        var lineNumber = trimmedLine.Substring(lineIndex);
                        sanitized.AppendLine($"{methodPart} {lineNumber}");
                    }
                    else
                    {
                        sanitized.AppendLine(methodPart);
                    }
                }
                else
                {
                    sanitized.AppendLine(trimmedLine);
                }
            }
            else
            {
                sanitized.AppendLine(trimmedLine);
            }
        }

        return sanitized.ToString();
    }
}

/// <summary>
/// Structured error data for serialization.
/// </summary>
[MemoryPackable]
public sealed partial class ErrorData
{
    public string ErrorType { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public InnerExceptionData[]? InnerExceptions { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Inner exception data for nested exceptions.
/// </summary>
[MemoryPackable]
public sealed partial class InnerExceptionData
{
    public string ErrorType { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
