using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;

namespace PulseRPC.Server.Abstractions;

/// <summary>
/// Provides contextual information about an RPC request during service method invocation.
/// This context is passed to service methods and provides access to request metadata,
/// cancellation tokens, tracing information, and authenticated user identity.
/// </summary>
public interface IRequestContext
{
    /// <summary>
    /// Gets the unique identifier for this request.
    /// Correlates requests with responses and enables distributed tracing.
    /// </summary>
    Guid RequestId { get; }

    /// <summary>
    /// Gets the unique identifier for the client connection.
    /// </summary>
    string ConnectionId { get; }

    /// <summary>
    /// Gets the client identifier (typically IP:Port).
    /// </summary>
    string ClientId { get; }

    /// <summary>
    /// Gets the service name being invoked.
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// Gets the method name being invoked.
    /// </summary>
    string MethodName { get; }

    /// <summary>
    /// Gets read-only metadata associated with the request.
    /// Metadata can include custom headers, tracing information, correlation IDs, etc.
    /// </summary>
    IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Gets the cancellation token for this request.
    /// The token is cancelled when:
    /// - The request times out
    /// - The client connection is lost
    /// - The server is shutting down
    ///
    /// Service methods should respect this token to enable graceful cancellation.
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets the high-resolution timestamp when the request was received (Stopwatch ticks).
    /// Use Stopwatch.GetElapsedTime(StartTimestamp) to measure elapsed time.
    /// </summary>
    long StartTimestamp { get; }

    /// <summary>
    /// Gets the distributed tracing activity context for W3C Trace Context propagation.
    /// Use this to create child activities for nested operations.
    /// </summary>
    ActivityContext TraceContext { get; }

    /// <summary>
    /// Gets the authenticated user's claims principal.
    /// Null if authentication is disabled or the request is unauthenticated.
    /// </summary>
    ClaimsPrincipal? User { get; }

    /// <summary>
    /// Gets whether the request has been cancelled.
    /// </summary>
    bool IsCancelled { get; }
}
