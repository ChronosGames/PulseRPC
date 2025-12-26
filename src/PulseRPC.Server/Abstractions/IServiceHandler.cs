using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PulseRPC.Server.Contexts;

namespace PulseRPC.Server.Abstractions;

/// <summary>
/// Handles invocation of service methods for a registered RPC service.
/// Implementations are responsible for:
/// - Deserializing method parameters from payload
/// - Invoking the target service method
/// - Serializing the return value or capturing exceptions
/// - Providing method discovery for clients
/// </summary>
public interface IServiceHandler
{
    /// <summary>
    /// Invokes a service method with the provided parameters and context.
    /// </summary>
    /// <param name="methodName">Name of the method to invoke.</param>
    /// <param name="parameters">Serialized method parameters (MemoryPack format).</param>
    /// <param name="context">Request context containing metadata and cancellation.</param>
    /// <returns>Invocation result containing the serialized return value or error details.</returns>
    /// <exception cref="ArgumentException">If methodName is null or empty.</exception>
    Task<InvocationResult> InvokeAsync(
        string methodName,
        ReadOnlyMemory<byte> parameters,
        IPulseContext context);

    /// <summary>
    /// Gets the list of available method names for this service.
    /// Used for service discovery and client code generation.
    /// </summary>
    /// <returns>Read-only collection of method names.</returns>
    IReadOnlyList<string> GetMethodNames();
}

/// <summary>
/// Result of a service method invocation.
/// </summary>
public class InvocationResult
{
    /// <summary>
    /// Gets or sets whether the invocation succeeded.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Gets or sets the serialized return value (if successful).
    /// Empty for void methods or failures.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; set; }

    /// <summary>
    /// Gets or sets the error type if invocation failed.
    /// Contains exception type name (e.g., "System.ArgumentException").
    /// </summary>
    public string? ErrorType { get; set; }

    /// <summary>
    /// Gets or sets the error message if invocation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the stack trace if invocation failed.
    /// Should be sanitized to remove sensitive paths.
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Gets or sets the execution duration in milliseconds.
    /// </summary>
    public double DurationMs { get; set; }

    /// <summary>
    /// Creates a successful invocation result.
    /// </summary>
    public static InvocationResult Success(ReadOnlyMemory<byte> payload, double durationMs) => new()
    {
        IsSuccess = true,
        Payload = payload,
        DurationMs = durationMs
    };

    /// <summary>
    /// Creates a successful invocation result for void methods.
    /// </summary>
    public static InvocationResult SuccessVoid(double durationMs) => new()
    {
        IsSuccess = true,
        Payload = ReadOnlyMemory<byte>.Empty,
        DurationMs = durationMs
    };

    /// <summary>
    /// Creates a failed invocation result.
    /// </summary>
    public static InvocationResult Failure(string errorType, string errorMessage, string? stackTrace, double durationMs) => new()
    {
        IsSuccess = false,
        ErrorType = errorType,
        ErrorMessage = errorMessage,
        StackTrace = stackTrace,
        DurationMs = durationMs
    };
}
