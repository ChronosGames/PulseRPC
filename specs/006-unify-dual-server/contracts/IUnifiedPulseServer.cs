// API Contract: IUnifiedPulseServer
// Purpose: Main interface for the unified server implementation
// This is a design artifact - actual implementation may vary

namespace PulseRPC.Server;

/// <summary>
/// Unified server interface consolidating PulseServer and ServerHost functionality.
/// Manages transport listeners, message processing pipeline, and service lifecycle.
/// </summary>
public interface IUnifiedPulseServer : IPulseServer
{
    // === Lifecycle Management ===

    /// <summary>
    /// Starts the server and all configured transports asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    /// <exception cref="InvalidOperationException">Server is already running</exception>
    /// <exception cref="InvalidOperationException">No transports configured</exception>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the server gracefully, draining in-flight messages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    // === Service Registration ===

    /// <summary>
    /// Registers a service instance with the server for RPC invocation.
    /// </summary>
    /// <typeparam name="TService">Service type</typeparam>
    /// <param name="serviceName">Unique service name</param>
    /// <param name="serviceInstance">Service instance</param>
    /// <param name="options">Optional service-specific options</param>
    /// <exception cref="ArgumentException">Service name already registered</exception>
    void RegisterService<TService>(
        string serviceName,
        TService serviceInstance,
        ServiceOptions? options = null)
        where TService : class;

    /// <summary>
    /// Unregisters a service from the server.
    /// </summary>
    /// <param name="serviceName">Service name to unregister</param>
    /// <returns>True if service was unregistered, false if not found</returns>
    bool UnregisterService(string serviceName);

    // === Server Information ===

    /// <summary>
    /// Gets all configured transports and their status.
    /// </summary>
    IReadOnlyDictionary<string, TransportInfo> GetTransports();

    /// <summary>
    /// Gets the default transport configuration.
    /// </summary>
    TransportInfo? GetDefaultTransport();

    /// <summary>
    /// Gets all active client connections.
    /// </summary>
    IReadOnlyList<ConnectionInfo> GetActiveConnections();

    /// <summary>
    /// Gets all registered services.
    /// </summary>
    IReadOnlyList<ServiceInfo> GetRegisteredServices();

    /// <summary>
    /// Gets current server performance metrics.
    /// </summary>
    ServerPerformanceMetrics GetPerformanceMetrics();

    /// <summary>
    /// Resets all performance metrics to zero.
    /// </summary>
    void ResetPerformanceMetrics();

    // === Broadcasting ===

    /// <summary>
    /// Broadcasts a message to all connected clients.
    /// </summary>
    /// <param name="data">Message data to broadcast</param>
    /// <param name="filter">Optional filter to select specific connections</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of clients that received the message</returns>
    Task<int> BroadcastAsync(
        ReadOnlyMemory<byte> data,
        Func<TransportContext, bool>? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to a specific connection.
    /// </summary>
    /// <param name="connectionId">Target connection ID</param>
    /// <param name="data">Message data to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if message was sent successfully</returns>
    Task<bool> SendAsync(
        string connectionId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);
}
