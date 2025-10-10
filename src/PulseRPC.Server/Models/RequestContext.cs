using System.Diagnostics;

namespace PulseRPC.Server.Models;

/// <summary>
/// Contextual information passed to service methods during invocation.
/// Immutable after creation.
/// </summary>
public sealed class RpcRequestContext : IDisposable
{
    private readonly CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>
    /// Correlation identifier.
    /// </summary>
    public Guid RequestId { get; init; }

    /// <summary>
    /// Client identifier (IP:Port).
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// Connection identifier.
    /// </summary>
    public string ConnectionId { get; init; } = string.Empty;

    /// <summary>
    /// Request headers and metadata (immutable).
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Timeout and disconnect cancellation.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// High-resolution start time (Stopwatch ticks).
    /// </summary>
    public long StartTimestamp { get; init; }

    /// <summary>
    /// Distributed tracing context.
    /// </summary>
    public ActivityContext TraceContext { get; init; }

    /// <summary>
    /// Service name being invoked.
    /// </summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>
    /// Method name being invoked.
    /// </summary>
    public string MethodName { get; init; } = string.Empty;

    private RpcRequestContext(CancellationTokenSource? cts)
    {
        _cts = cts;
    }

    /// <summary>
    /// Creates a new RpcRequestContext from an RpcMessage.
    /// </summary>
    public static RpcRequestContext Create(
        RpcMessage message,
        ServerConnection connection,
        TimeSpan timeout,
        Activity? activity = null)
    {
        var cts = new CancellationTokenSource(timeout);

        return new RpcRequestContext(cts)
        {
            RequestId = message.RequestId,
            ClientId = connection.ClientAddress?.ToString() ?? "unknown",
            ConnectionId = connection.ConnectionId,
            Metadata = message.Metadata,
            CancellationToken = cts.Token,
            StartTimestamp = Stopwatch.GetTimestamp(),
            TraceContext = activity?.Context ?? default,
            ServiceName = message.ServiceName,
            MethodName = message.MethodName
        };
    }

    /// <summary>
    /// Gets the elapsed time since context creation.
    /// </summary>
    public TimeSpan GetElapsedTime()
    {
        var elapsed = Stopwatch.GetTimestamp() - StartTimestamp;
        return TimeSpan.FromTicks((long)(elapsed * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency)));
    }

    /// <summary>
    /// Disposes the context and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _cts?.Dispose();
        _disposed = true;
    }
}
