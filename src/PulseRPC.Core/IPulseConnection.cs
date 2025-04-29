using PulseRPC.Protocol;

namespace PulseRPC;

/// <summary>
/// Represents a connection between a client and a server.
/// </summary>
public interface IPulseConnection : IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether the connection is currently active.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Establishes a connection to the remote endpoint.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the remote endpoint.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Sends a request and waits for a response.
    /// </summary>
    Task<PulseResponse> SendRequestAsync(PulseRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Occurs when an event message is received from the server.
    /// </summary>
    event Func<PulseEvent, Task>? OnEventReceived;
}
