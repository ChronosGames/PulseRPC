// API Contract: Deprecated Facades
// Purpose: Backward compatibility facades for PulseServer and ServerHost
// This is a design artifact - actual implementation may vary

using System.Runtime.CompilerServices;

namespace PulseRPC.Server;

/// <summary>
/// [DEPRECATED] Facade for backward compatibility with existing PulseServer usage.
/// Delegates all operations to UnifiedPulseServer.
/// </summary>
[Obsolete(
    "PulseServer has been unified into UnifiedPulseServer. " +
    "Use UnifiedPulseServer instead for better performance and maintainability. " +
    "This class will cause compilation errors in v3.0 and be removed in v4.0. " +
    "See migration guide: https://github.com/pulseRPC/PulseRPC/docs/migration-pulseserver.md",
    error: false)]
public sealed class PulseServer : IPulseServer, IAsyncDisposable, IDisposable
{
    private readonly UnifiedPulseServer _implementation;

    /// <summary>
    /// Creates a new PulseServer instance (deprecated - use UnifiedPulseServer).
    /// </summary>
    public PulseServer(
        ILoggerFactory? loggerFactory = null,
        IOptions<ServerOptions>? serverOptions = null,
        IServerChannelManager? channelManager = null,
        ITransportIntegrationManager? transportIntegrationManager = null)
    {
        // Map old options to unified options
        var unifiedOptions = MapToUnifiedOptions(serverOptions?.Value);

        _implementation = new UnifiedPulseServer(
            loggerFactory,
            Options.Create(unifiedOptions),
            channelManager,
            transportIntegrationManager);
    }

    // === Property Delegation ===

    public ServerState State
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _implementation.State;
    }

    public bool IsRunning
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _implementation.IsRunning;
    }

    public int ActiveConnectionCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _implementation.ActiveConnectionCount;
    }

    // === Event Delegation ===

    public event EventHandler<ServerStateChangedEventArgs>? StateChanged
    {
        add => _implementation.StateChanged += value;
        remove => _implementation.StateChanged -= value;
    }

    public event EventHandler<ClientConnectedEventArgs>? ClientConnected
    {
        add => _implementation.ClientConnected += value;
        remove => _implementation.ClientConnected -= value;
    }

    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected
    {
        add => _implementation.ClientDisconnected += value;
        remove => _implementation.ClientDisconnected -= value;
    }

    // === Method Delegation ===

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddTransport(TransportChannelConfiguration config)
    {
        // Note: This is a compatibility shim - transports should be configured in options
        // Implementation will need to handle post-construction transport addition
        _implementation.AddTransport(config);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _implementation.StartAsync(cancellationToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _implementation.StopAsync(cancellationToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReadOnlyDictionary<string, TransportInfo> GetTransports()
    {
        return _implementation.GetTransports();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TransportInfo? GetDefaultTransport()
    {
        return _implementation.GetDefaultTransport();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReadOnlyList<ConnectionInfo> GetActiveConnections()
    {
        return _implementation.GetActiveConnections();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        _implementation?.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask DisposeAsync()
    {
        if (_implementation != null)
            await _implementation.DisposeAsync();
    }

    private static UnifiedServerOptions MapToUnifiedOptions(ServerOptions? options)
    {
        // Configuration mapping logic
        return new UnifiedServerOptions
        {
            DefaultOperationTimeout = options?.DefaultTimeout ?? TimeSpan.FromSeconds(30),
            ConnectionManager = new ConnectionManagerOptions
            {
                MaxConnections = options?.MaxConnections ?? 1000
            }
            // Additional mappings as needed
        };
    }
}

// ===================================================================

namespace PulseRPC.Server.Core;

/// <summary>
/// [DEPRECATED] Facade for backward compatibility with existing ServerHost usage.
/// Delegates all operations to UnifiedPulseServer.
/// </summary>
[Obsolete(
    "ServerHost has been unified into UnifiedPulseServer. " +
    "Use UnifiedPulseServer instead for better performance and maintainability. " +
    "This class will cause compilation errors in v3.0 and be removed in v4.0. " +
    "See migration guide: https://github.com/pulseRPC/PulseRPC/docs/migration-serverhost.md",
    error: false)]
public sealed class ServerHost : IDisposable
{
    private readonly UnifiedPulseServer _implementation;
    private readonly ConnectionManager _connectionManager;
    private readonly ServiceRegistry _serviceRegistry;

    /// <summary>
    /// Creates a new ServerHost instance (deprecated - use UnifiedPulseServer).
    /// </summary>
    public ServerHost(
        IPulseServerTransport transport,
        ServerHostOptions? options = null)
    {
        // Map transport to configuration
        var unifiedOptions = MapToUnifiedOptions(transport, options);

        _implementation = new UnifiedPulseServer(Options.Create(unifiedOptions));

        // Create wrapper instances for exposed properties
        _connectionManager = new ConnectionManager(options?.ConnectionManagerOptions);
        _serviceRegistry = new ServiceRegistry(options?.ServiceRegistryOptions);
    }

    // === Property Delegation ===

    public bool IsRunning
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _implementation.IsRunning;
    }

    public ConnectionManager ConnectionManager
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _connectionManager;
    }

    public ServiceRegistry ServiceRegistry
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _serviceRegistry;
    }

    // === Method Delegation ===

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _implementation.StartAsync(cancellationToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _implementation.StopAsync(cancellationToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RegisterService<TService>(
        string serviceName,
        TService serviceInstance,
        ServiceOptions? options = null)
        where TService : class
    {
        _implementation.RegisterService(serviceName, serviceInstance, options);
        _serviceRegistry.RegisterService(serviceName, serviceInstance, options);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool UnregisterService(string serviceName)
    {
        _serviceRegistry.UnregisterService(serviceName);
        return _implementation.UnregisterService(serviceName);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ServerHealthStatus GetHealthStatus()
    {
        var metrics = _implementation.GetPerformanceMetrics();
        var services = _implementation.GetRegisteredServices();

        return new ServerHealthStatus
        {
            IsRunning = IsRunning,
            ActiveConnections = metrics.ActiveConnections,
            TotalMessagesReceived = metrics.TotalMessagesProcessed,
            RegisteredServiceCount = services.Count
            // Additional mappings as needed
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        _implementation?.Dispose();
        _connectionManager?.Dispose();
    }

    private static UnifiedServerOptions MapToUnifiedOptions(
        IPulseServerTransport transport,
        ServerHostOptions? options)
    {
        // Create transport configuration from IPulseServerTransport
        var transportConfig = TransportChannelConfiguration.FromTransport(transport);

        return new UnifiedServerOptions
        {
            Transports = new List<TransportChannelConfiguration> { transportConfig },
            MessageReceiver = options?.MessageReceiverOptions ?? new(),
            MessageDispatcher = options?.MessageDispatcherOptions ?? new(),
            ResponseTransmitter = options?.ResponseTransmitterOptions ?? new(),
            ConnectionManager = options?.ConnectionManagerOptions ?? new(),
            ServiceRegistry = options?.ServiceRegistryOptions ?? new(),
            BackpressurePolicy = options?.BackpressurePolicyOptions ?? new()
        };
    }
}
