using PulseRPC.Server.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Abstractions;

/// <summary>
/// Orchestrates message dispatching from the network layer to registered service handlers.
/// Responsibilities:
/// - Route messages to appropriate service handlers
/// - Maintain FIFO ordering per connection
/// - Apply priority-based scheduling across connections
/// - Implement backpressure when queue capacity is exceeded
/// - Manage lifecycle (start/stop)
/// </summary>
public interface IMessageDispatcher
{
    /// <summary>
    /// Starts the message dispatcher and begins processing messages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop startup.</param>
    /// <returns>Task that completes when the dispatcher is running.</returns>
    /// <exception cref="InvalidOperationException">If the dispatcher is already started.</exception>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the message dispatcher and waits for pending messages to complete.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to force shutdown (abandon pending messages).</param>
    /// <returns>Task that completes when the dispatcher has stopped.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the dispatcher is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Dispatches a message to the appropriate service handler.
    /// </summary>
    /// <param name="message">The RPC message to dispatch.</param>
    /// <param name="cancellationToken">Cancellation token for the dispatch operation.</param>
    /// <returns>Dispatch result indicating success or error.</returns>
    /// <exception cref="InvalidOperationException">If the dispatcher is not running.</exception>
    /// <exception cref="ArgumentNullException">If message is null.</exception>
    Task<DispatchResult> DispatchMessageAsync(RpcMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a service handler for the specified service name.
    /// </summary>
    /// <param name="serviceName">Unique name identifying the service.</param>
    /// <param name="handler">Handler implementation for the service.</param>
    /// <exception cref="ArgumentNullException">If serviceName or handler is null.</exception>
    /// <exception cref="ArgumentException">If serviceName is empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">If a service with the same name is already registered.</exception>
    void RegisterServiceHandler(string serviceName, IServiceHandler handler);

    /// <summary>
    /// Unregisters a service handler.
    /// </summary>
    /// <param name="serviceName">Name of the service to unregister.</param>
    /// <returns>True if the service was unregistered, false if it was not found.</returns>
    bool UnregisterServiceHandler(string serviceName);

    /// <summary>
    /// Gets a registered service handler by name.
    /// </summary>
    /// <param name="serviceName">Name of the service.</param>
    /// <returns>The service handler, or null if not found.</returns>
    IServiceHandler? GetServiceHandler(string serviceName);

    /// <summary>
    /// Gets the current queue depth (number of pending messages).
    /// </summary>
    int QueueDepth { get; }

    /// <summary>
    /// Gets the total number of messages dispatched since start.
    /// </summary>
    long TotalMessagesDispatched { get; }

    /// <summary>
    /// Gets the number of registered services.
    /// </summary>
    int RegisteredServiceCount { get; }
}

/// <summary>
/// Result of a dispatch operation.
/// </summary>
public class DispatchResult
{
    /// <summary>
    /// Gets or sets whether the dispatch succeeded.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Gets or sets the error type if dispatch failed.
    /// </summary>
    public string? ErrorType { get; set; }

    /// <summary>
    /// Gets or sets the error message if dispatch failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets whether backpressure was applied during dispatch.
    /// </summary>
    public bool IsBackpressureApplied { get; set; }

    /// <summary>
    /// Creates a successful dispatch result.
    /// </summary>
    public static DispatchResult Success() => new() { IsSuccess = true };

    /// <summary>
    /// Creates a successful dispatch result with backpressure indication.
    /// </summary>
    public static DispatchResult SuccessWithBackpressure() => new()
    {
        IsSuccess = true,
        IsBackpressureApplied = true
    };

    /// <summary>
    /// Creates a failed dispatch result.
    /// </summary>
    public static DispatchResult Failure(string errorType, string errorMessage) => new()
    {
        IsSuccess = false,
        ErrorType = errorType,
        ErrorMessage = errorMessage
    };
}
