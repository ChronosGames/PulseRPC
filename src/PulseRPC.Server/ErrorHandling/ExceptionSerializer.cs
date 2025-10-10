using PulseRPC.Server.Models;

namespace PulseRPC.Server.ErrorHandling;

/// <summary>
/// Safe exception serialization with recursive inner exception handling.
/// </summary>
public static class ExceptionSerializer
{
    /// <summary>
    /// Serializes an exception to ExceptionData with sanitization.
    /// </summary>
    public static ExceptionData Serialize(Exception exception)
    {
        return SerializeRecursive(exception, maxDepth: 10);
    }

    private static ExceptionData SerializeRecursive(Exception exception, int maxDepth, int currentDepth = 0)
    {
        if (currentDepth >= maxDepth)
        {
            return new ExceptionData
            {
                ExceptionType = "System.Exception",
                Message = "[Max inner exception depth reached]",
                StackTrace = null,
                InnerException = null
            };
        }

        return new ExceptionData
        {
            ExceptionType = GetSafeTypeName(exception),
            Message = SanitizeMessage(exception.Message),
            StackTrace = SanitizeStackTrace(exception.StackTrace),
            InnerException = exception.InnerException != null
                ? SerializeRecursive(exception.InnerException, maxDepth, currentDepth + 1)
                : null
        };
    }

    private static string GetSafeTypeName(Exception exception)
    {
        try
        {
            return exception.GetType().FullName ?? exception.GetType().Name;
        }
        catch
        {
            return "System.Exception";
        }
    }

    private static string SanitizeMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return "No error message available";

        // Remove potential sensitive information (file paths, connection strings, etc.)
        var sanitized = message;

        // Remove absolute file paths (Windows and Unix)
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"[A-Za-z]:\\[\w\s\\\.\-]+|\\/[\w\s\\/\.\-]+",
            "[path]"
        );

        // Remove connection strings
        if (sanitized.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
            sanitized.Contains("Password=", StringComparison.OrdinalIgnoreCase))
        {
            sanitized = "[connection string details removed]";
        }

        return sanitized;
    }

    private static string? SanitizeStackTrace(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
            return null;

        var lines = stackTrace.Split('\n');
        var sanitizedLines = new List<string>();

        foreach (var line in lines)
        {
            var sanitized = line;

            // Remove file paths but keep method names
            var atIndex = sanitized.IndexOf(" in ", StringComparison.Ordinal);
            if (atIndex > 0)
            {
                sanitized = sanitized[..atIndex]; // Keep only method signature
            }

            // Remove line numbers
            var lineIndex = sanitized.IndexOf(":line ", StringComparison.Ordinal);
            if (lineIndex > 0)
            {
                sanitized = sanitized[..lineIndex];
            }

            sanitizedLines.Add(sanitized.TrimEnd());
        }

        return string.Join('\n', sanitizedLines);
    }
}
