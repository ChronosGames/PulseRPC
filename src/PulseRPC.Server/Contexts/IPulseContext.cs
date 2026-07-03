using System.Diagnostics;
using System.Security.Claims;
using PulseRPC.Authentication;
using PulseRPC.Transport;

namespace PulseRPC.Server.Contexts;

/// <summary>
/// Unified request context that combines RPC, authentication, and transport information.
/// </summary>
/// <remarks>
/// <para>
/// This interface merges the functionality of the former <c>IRequestContext</c>,
/// <c>IServiceRequestContext</c>, and <c>TransportContextScope</c> into a single unified context.
/// </para>
/// <para>
/// <strong>Access via <see cref="PulseContext.Current"/></strong>:
/// </para>
/// <code>
/// var context = PulseContext.Current;
/// var userId = context?.UserId;
/// var transport = context?.Transport;
/// </code>
/// </remarks>
public interface IPulseContext : IServiceRequestContext, IDisposable
{
    // ═══════════════════════════════════════════════════════════════════════════
    // RPC Information
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Request correlation identifier.
    /// Used for distributed tracing and request/response correlation.
    /// </summary>
    Guid RequestId { get; }

    /// <summary>
    /// Connection identifier.
    /// </summary>
    string? ConnectionId { get; }

    /// <summary>
    /// Target service name being invoked.
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// Target service instance key (the routed keyed-hub / Actor instance identifier,
    /// i.e. the <c>BusinessId</c> part of the full ServiceId <c>"ServiceName:BusinessId"</c>).
    /// </summary>
    /// <remarks>
    /// Populated from the envelope header (<see cref="PulseRPC.Messaging.MessageHeader.ServiceKey"/>) that a
    /// gateway used to route the request. Empty string when no instance key was supplied.
    /// </remarks>
    string ServiceKey => string.Empty;

    /// <summary>
    /// Target method name being invoked.
    /// </summary>
    string MethodName { get; }

    /// <summary>
    /// Request metadata/headers.
    /// May contain custom headers, tracing information, correlation IDs, etc.
    /// </summary>
    IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Cancellation token for request timeout/disconnect.
    /// The token is cancelled when:
    /// - The request times out
    /// - The client connection is lost
    /// - The server is shutting down
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// High-resolution start timestamp (Stopwatch ticks).
    /// Use <see cref="GetElapsedTime"/> to measure elapsed time.
    /// </summary>
    long StartTimestamp { get; }

    /// <summary>
    /// Distributed tracing context for W3C Trace Context propagation.
    /// Use this to create child activities for nested operations.
    /// </summary>
    ActivityContext TraceContext { get; }

    /// <summary>
    /// Authenticated user's claims principal.
    /// Null if authentication is disabled or the request is unauthenticated.
    /// </summary>
    ClaimsPrincipal? User { get; }

    /// <summary>
    /// Whether the request has been cancelled.
    /// </summary>
    bool IsCancelled { get; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Transport Information
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Underlying transport connection.
    /// Used for sending responses and server-to-client pushes.
    /// </summary>
    IServerTransport? Transport { get; }

    /// <summary>
    /// Client address (typically IP:Port).
    /// </summary>
    string? ClientAddress { get; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Custom Properties
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mutable dictionary for storing custom properties during request processing.
    /// Useful for storing session-specific data like room IDs, usernames, etc.
    /// </summary>
    IDictionary<string, object?> Properties { get; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets the elapsed time since request started.
    /// </summary>
    TimeSpan GetElapsedTime();

    /// <summary>
    /// Gets a header value from metadata.
    /// </summary>
    /// <param name="name">Header name</param>
    /// <returns>Header value, or null if not found</returns>
    string? GetHeader(string name);
}
