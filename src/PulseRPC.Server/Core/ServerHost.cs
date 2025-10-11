using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Models;
using PulseRPC.Server.Pipeline;
using PulseRPC.Transport;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Core;

/// <summary>
/// Orchestrates all server pipeline components and manages server lifecycle.
/// Connects MessageReceiver → MessageDispatcher → ServiceInvokers → ResponseTransmitter.
/// Handles startup/shutdown coordination and health checks.
/// </summary>
public sealed class ServerHost : IDisposable
{
    private readonly IPulseServerTransport _transport;
    private readonly MessageReceiver _messageReceiver;
    private readonly MessageDispatcher _messageDispatcher;
    private readonly ResponseTransmitter _responseTransmitter;
    private readonly ConnectionManager _connectionManager;
    private readonly ServiceRegistry _serviceRegistry;
    private readonly BackpressurePolicy _backpressurePolicy;
    private readonly ServerHostOptions _options;

    private int _isRunning;
    private bool _disposed;

    /// <summary>
    /// Gets whether the server is currently running.
    /// </summary>
    public bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1;

    /// <summary>
    /// Gets the connection manager.
    /// </summary>
    public ConnectionManager ConnectionManager => _connectionManager;

    /// <summary>
    /// Gets the service registry.
    /// </summary>
    public ServiceRegistry ServiceRegistry => _serviceRegistry;

    /// <summary>
    /// Gets the backpressure policy.
    /// </summary>
    public BackpressurePolicy BackpressurePolicy => _backpressurePolicy;

    public ServerHost(
        IPulseServerTransport transport,
        ServerHostOptions? options = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? new ServerHostOptions();

        // Initialize core components
        _connectionManager = new ConnectionManager(_options.ConnectionManagerOptions);
        _serviceRegistry = new ServiceRegistry(_options.ServiceRegistryOptions);
        _backpressurePolicy = new BackpressurePolicy(_options.BackpressurePolicyOptions);

        // Initialize pipeline components
        _messageReceiver = new MessageReceiver(transport, _options.MessageReceiverOptions);
        _messageDispatcher = new MessageDispatcher(_options.MessageDispatcherOptions);
        _responseTransmitter = new ResponseTransmitter(transport, _options.ResponseTransmitterOptions);

        // Wire up events
        _messageReceiver.MessageReceived += OnMessageReceived;
        _messageReceiver.ParseError += OnParseError;
        _transport.ConnectionAccepted += OnConnectionAccepted;
        _transport.ConnectionClosed += OnConnectionClosed;
    }

    /// <summary>
    /// Starts the server and all pipeline components.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) == 1)
        {
            throw new InvalidOperationException("ServerHost is already running");
        }

        try
        {
            // Start components in order
            await _messageReceiver.StartAsync(cancellationToken).ConfigureAwait(false);
            await _messageDispatcher.StartAsync(cancellationToken).ConfigureAwait(false);
            await _responseTransmitter.StartAsync(cancellationToken).ConfigureAwait(false);

            Debug.WriteLine($"ServerHost started successfully on {_transport.LocalEndPoint}");
        }
        catch (Exception)
        {
            // Rollback on failure
            Interlocked.Exchange(ref _isRunning, 0);
            throw;
        }
    }

    /// <summary>
    /// Stops the server gracefully.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 0, 1) == 0)
        {
            return; // Already stopped
        }

        try
        {
            // Stop components in reverse order
            await _responseTransmitter.StopAsync(cancellationToken).ConfigureAwait(false);
            await _messageDispatcher.StopAsync(cancellationToken).ConfigureAwait(false);
            await _messageReceiver.StopAsync(cancellationToken).ConfigureAwait(false);

            // Close all connections
            await _connectionManager.CloseAllConnectionsAsync(cancellationToken).ConfigureAwait(false);

            Debug.WriteLine("ServerHost stopped successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during ServerHost shutdown: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Registers a service with the server.
    /// </summary>
    public void RegisterService<TService>(string serviceName, TService serviceInstance, ServiceOptions? options = null)
        where TService : class
    {
        // Register in service registry
        _serviceRegistry.RegisterService(serviceName, serviceInstance, options);

        // Register handler in dispatcher
        var handler = _serviceRegistry.GetServiceHandler(serviceName);
        if (handler != null)
        {
            _messageDispatcher.RegisterServiceHandler(serviceName, handler);
        }
    }

    /// <summary>
    /// Unregisters a service from the server.
    /// </summary>
    public bool UnregisterService(string serviceName)
    {
        _messageDispatcher.UnregisterServiceHandler(serviceName);
        return _serviceRegistry.UnregisterService(serviceName);
    }

    /// <summary>
    /// Gets server health status.
    /// </summary>
    public ServerHealthStatus GetHealthStatus()
    {
        var connectionStats = _connectionManager.GetStatistics();
        var backpressureStats = _backpressurePolicy.GetStatistics();

        return new ServerHealthStatus
        {
            IsRunning = IsRunning,
            ActiveConnections = connectionStats.ActiveConnections,
            TotalMessagesReceived = _messageReceiver.TotalMessagesReceived,
            TotalMessagesDispatched = _messageDispatcher.TotalMessagesDispatched,
            TotalResponsesSent = _responseTransmitter.TotalResponsesSent,
            RegisteredServiceCount = _serviceRegistry.RegisteredServiceCount,
            BackpressureLevel = backpressureStats.CurrentLevel,
            QueueDepth = _messageDispatcher.QueueDepth
        };
    }

    private async void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        try
        {
            // Check backpressure
            var maxCapacity = (_options.MessageDispatcherOptions?.MaxQueueDepthPerPriority ?? 1000) * 4;
            var backpressureLevel = _backpressurePolicy.UpdateQueueDepth(_messageDispatcher.QueueDepth, maxCapacity);

            var decision = _backpressurePolicy.ShouldAcceptRequest();
            if (!decision.Accept)
            {
                // Reject due to backpressure
                await SendErrorResponseAsync(
                    e.ConnectionId,
                    e.Message.RequestId,
                    "ServiceOverloaded",
                    decision.Reason ?? "Server is overloaded").ConfigureAwait(false);
                return;
            }

            // Dispatch message
            var dispatchResult = await _messageDispatcher.DispatchMessageAsync(e.Message).ConfigureAwait(false);

            if (!dispatchResult.IsSuccess)
            {
                // Dispatch failed
                await SendErrorResponseAsync(
                    e.ConnectionId,
                    e.Message.RequestId,
                    dispatchResult.ErrorType ?? "DispatchFailed",
                    dispatchResult.ErrorMessage ?? "Failed to dispatch message").ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error processing message: {ex.Message}");

            await SendErrorResponseAsync(
                e.ConnectionId,
                e.Message.RequestId,
                "UnexpectedError",
                $"Unexpected error: {ex.Message}").ConfigureAwait(false);
        }
    }

    private async void OnParseError(object? sender, MessageParseErrorEventArgs e)
    {
        Debug.WriteLine($"Parse error on connection {e.ConnectionId}: {e.ErrorMessage}");

        // Send error response if possible
        try
        {
            await SendErrorResponseAsync(
                e.ConnectionId,
                Guid.Empty,
                e.ErrorType,
                e.ErrorMessage).ConfigureAwait(false);
        }
        catch
        {
            // Best effort - ignore if can't send
        }
    }

    private void OnConnectionAccepted(object? sender, ServerConnectionEventArgs e)
    {
        var connection = new ServerConnection(
            e.Transport.Id,
            e.Transport.RemoteEndPoint as System.Net.IPEndPoint,
            Models.TransportType.TCP);

        _connectionManager.TryAddConnection(connection);

        Debug.WriteLine($"Connection accepted: {e.Transport.Id}");
    }

    private void OnConnectionClosed(object? sender, ConnectionClosedEventArgs e)
    {
        _connectionManager.TryRemoveConnection(e.ConnectionId);

        Debug.WriteLine($"Connection closed: {e.ConnectionId}");
    }

    private async Task SendErrorResponseAsync(
        string connectionId,
        Guid requestId,
        string errorType,
        string errorMessage)
    {
        try
        {
            var builder = new ResponseBuilder();
            var errorResponse = builder.BuildExceptionResponse(
                requestId,
                new Exception($"{errorType}: {errorMessage}"));

            await _responseTransmitter.SendResponseAsync(connectionId, errorResponse).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to send error response: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Stop if running
        if (IsRunning)
        {
            StopAsync().GetAwaiter().GetResult();
        }

        // Dispose components
        _messageReceiver.Dispose();
        _messageDispatcher.Dispose();
        _responseTransmitter.Dispose();
        _connectionManager.Dispose();
    }
}

/// <summary>
/// Configuration options for ServerHost.
/// </summary>
public sealed class ServerHostOptions
{
    public MessageReceiverOptions? MessageReceiverOptions { get; set; }
    public MessageDispatcherOptions? MessageDispatcherOptions { get; set; }
    public ResponseTransmitterOptions? ResponseTransmitterOptions { get; set; }
    public ConnectionManagerOptions? ConnectionManagerOptions { get; set; }
    public ServiceRegistryOptions? ServiceRegistryOptions { get; set; }
    public BackpressurePolicyOptions? BackpressurePolicyOptions { get; set; }
}

/// <summary>
/// Server health status information.
/// </summary>
public sealed class ServerHealthStatus
{
    public bool IsRunning { get; init; }
    public int ActiveConnections { get; init; }
    public long TotalMessagesReceived { get; init; }
    public long TotalMessagesDispatched { get; init; }
    public long TotalResponsesSent { get; init; }
    public int RegisteredServiceCount { get; init; }
    public BackpressureLevel BackpressureLevel { get; init; }
    public int QueueDepth { get; init; }
}
