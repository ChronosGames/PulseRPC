# Unified Server Internal Model

**Feature**: `006-unify-dual-server`
**Date**: 2025-10-13

---

## Architecture Overview

```
┌────────────────────────────────────────────────────────────────────────────┐
│                          PulseServer (Unified)                             │
│  - Public API: IPulseServer interface                                      │
│  - State: ServerState (via ServerLifecycleCoordinator)                    │
│  - Configuration: ServerConfiguration                                       │
└────────────────────────────────────────────────────────────────────────────┘
         │                          │                          │
         ▼                          ▼                          ▼
┌────────────────────┐   ┌──────────────────────┐   ┌────────────────────────┐
│ Lifecycle          │   │ TransportOrchestrator│   │ PipelineCoordinator    │
│ Coordinator        │   │ - Multi-transport    │   │ - MessageReceiver      │
│ - State machine    │   │   management         │   │ - MessageDispatcher    │
│ - Event emission   │   │ - Connection routing │   │ - ResponseTransmitter  │
└────────────────────┘   └──────────────────────┘   └────────────────────────┘
         │                          │                          │
         └──────────────────────────┼──────────────────────────┘
                                    ▼
                     ┌──────────────────────────────┐
                     │   IServerChannelManager      │
                     │   - Active connections       │
                     │   - Channel lifecycle        │
                     └──────────────────────────────┘
```

---

## Core Components

### 1. PulseServer (Unified Public API)

**Responsibility**: Single public entry point, implements IPulseServer interface, delegates to internal coordinators

**Fields**:
```csharp
private readonly ServerConfiguration _configuration;
private readonly ServerLifecycleCoordinator _lifecycle;
private readonly TransportOrchestrator _transportOrchestrator;
private readonly PipelineCoordinator _pipelineCoordinator;
private readonly IServerChannelManager _channelManager;
private readonly ILoggerFactory _loggerFactory;
```

**Public API** (implements IPulseServer):
```csharp
// Lifecycle
Task StartAsync(CancellationToken ct = default)
Task StopAsync(CancellationToken ct = default)
ServerState State { get; }
bool IsRunning { get; }

// Transport Management
void AddTransport(TransportChannelConfiguration config)
IReadOnlyDictionary<string, TransportInfo> GetTransports()
TransportInfo? GetDefaultTransport()

// Connection Management
int ActiveConnectionCount { get; }
IReadOnlyList<ConnectionInfo> GetActiveConnections()
Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, ...)
Task<bool> SendAsync(string connectionId, ReadOnlyMemory<byte> data, ...)

// Service Management (NEW - from ServerHost)
void RegisterService<T>(string name, T instance, ServiceOptions? options = null)
bool UnregisterService(string name)

// Observability (MERGED from both implementations)
ServerPerformanceMetrics GetPerformanceMetrics()
ServerHealthStatus GetHealthStatus()
void ResetPerformanceMetrics()

// Events
event EventHandler<ServerStateChangedEventArgs> StateChanged
event EventHandler<ClientConnectedEventArgs> ClientConnected
event EventHandler<ClientDisconnectedEventArgs> ClientDisconnected

// Dispose
void Dispose()
ValueTask DisposeAsync()
```

**Delegation Pattern**:
- Lifecycle → `_lifecycle.StartAsync()`, `_lifecycle.StopAsync()`
- Transport → `_transportOrchestrator.AddTransport()`, `_transportOrchestrator.GetTransports()`
- Pipeline → `_pipelineCoordinator.RegisterService()`, `_pipelineCoordinator.GetHealthStatus()`
- Channel → `_channelManager.GetAllChannels()`, `_channelManager.BroadcastAsync()`

---

### 2. ServerLifecycleCoordinator

**Responsibility**: Manages server state machine (Stopped → Starting → Running → Stopping → Stopped)

**State Machine**:
```
    ┌──────────┐
    │ Stopped  │ ◄──────────────────────────────┐
    └──────────┘                                │
         │ StartAsync()                         │
         ▼                                      │
    ┌──────────┐                                │
    │ Starting │                                │
    └──────────┘                                │
         │ All transports started              │
         ▼                                      │
    ┌──────────┐                                │
    │ Running  │                                │
    └──────────┘                                │
         │ StopAsync()                          │
         ▼                                      │
    ┌──────────┐                                │
    │ Stopping │ ───────────────────────────────┘
    └──────────┘  All transports stopped + cleanup
```

**Methods**:
```csharp
Task TransitionToStartingAsync()
Task TransitionToRunningAsync()
Task TransitionToStoppingAsync()
Task TransitionToStoppedAsync()
ServerState CurrentState { get; }
event EventHandler<ServerStateChangedEventArgs> StateChanged
```

**Thread Safety**: Uses `Lock` (C# 13) for state transitions

---

### 3. TransportOrchestrator

**Responsibility**: Manages multiple transport listeners (TCP, KCP), coordinates parallel start/stop, routes connections

**Fields**:
```csharp
private readonly ITransportIntegrationManager _transportManager;
private readonly ConcurrentDictionary<string, IServerListener> _listeners;
private readonly ConcurrentDictionary<string, TransportChannelConfiguration> _configurations;
private readonly IServerChannelManager _channelManager;
```

**Methods**:
```csharp
void AddTransport(TransportChannelConfiguration config)
Task StartAllTransportsAsync(CancellationToken ct)
Task StopAcceptingAsync(CancellationToken ct) // Phase 1 of shutdown
Task StopAllTransportsAsync(CancellationToken ct) // Phase 2 of shutdown
IReadOnlyDictionary<string, TransportInfo> GetTransports()
TransportInfo? GetDefaultTransport()
```

**Workflow**:
1. **AddTransport**: Validate config, store in `_configurations`
2. **StartAllTransportsAsync**: Parallel startup using `Task.WhenAll`
   - For each config: `_transportManager.CreateListener(config)`
   - Wire up `ConnectionAccepted` event → route to `_channelManager`
   - Call `listener.StartAsync()`
   - Store in `_listeners`
3. **StopAcceptingAsync**: Stop accepting new connections (keep existing alive)
4. **StopAllTransportsAsync**: Close all listeners, cleanup

---

### 4. PipelineCoordinator

**Responsibility**: Coordinates ServerHost's pipeline components (MessageReceiver/Dispatcher/Transmitter), integrates with transport layer

**Fields**:
```csharp
private readonly MessageReceiver _receiver;
private readonly MessageDispatcher _dispatcher;
private readonly ResponseTransmitter _transmitter;
private readonly ServiceRegistry _serviceRegistry;
private readonly BackpressurePolicy _backpressurePolicy;
private readonly ConnectionManager _connectionManager;
```

**Methods**:
```csharp
Task StartPipelineAsync(CancellationToken ct)
Task DrainPipelinesAsync(CancellationToken ct) // Graceful shutdown: wait for in-flight messages
Task StopPipelineAsync(CancellationToken ct)

// Service Registration (from ServerHost)
void RegisterService<T>(string name, T instance, ServiceOptions? options)
bool UnregisterService(string name)

// Health Monitoring (from ServerHost)
ServerHealthStatus GetHealthStatus()

// Pipeline Component Access (for advanced scenarios)
ConnectionManager ConnectionManager { get; }
ServiceRegistry ServiceRegistry { get; }
BackpressurePolicy BackpressurePolicy { get; }
```

**Pipeline Wiring**:
```csharp
// Constructor wires up events:
_receiver.MessageReceived += async (sender, e) =>
{
    // Check backpressure
    if (!_backpressurePolicy.ShouldAcceptRequest().Accept)
    {
        await SendErrorResponseAsync(e.ConnectionId, "ServiceOverloaded");
        return;
    }

    // Dispatch message
    await _dispatcher.DispatchMessageAsync(e.Message);
};

_dispatcher.InvocationCompleted += async (sender, e) =>
{
    // Send response
    await _transmitter.SendResponseAsync(e.ConnectionId, e.Response);
};
```

---

## ServerHost Facade Model

**Pattern**: Thin wrapper with direct delegation to unified PulseServer

**Implementation**:
```csharp
[Obsolete("ServerHost is deprecated. Use PulseServer instead. See migration guide at docs/quickstart.md", false)]
public sealed class ServerHost : IDisposable
{
    private readonly PulseServer _unifiedServer;
    private readonly string _transportName; // For pipeline access

    public ServerHost(IPulseServerTransport transport, ServerHostOptions? options = null)
    {
        // Convert ServerHostOptions → ServerConfiguration
        var config = new ServerConfiguration
        {
            Transports = new List<TransportChannelConfiguration>
            {
                new TransportChannelConfiguration
                {
                    Name = "default",
                    Type = transport.Type,
                    Port = transport.LocalEndPoint.Port,
                    // Map ServerHostOptions to transport options
                    ReceiverOptions = options?.MessageReceiverOptions,
                    DispatcherOptions = options?.MessageDispatcherOptions,
                    TransmitterOptions = options?.ResponseTransmitterOptions
                }
            },
            ShutdownTimeout = TimeSpan.FromSeconds(30)
        };

        _unifiedServer = new PulseServer(config);
        _transportName = "default";
    }

    // Direct delegation (zero-overhead)
    public async Task StartAsync(CancellationToken ct = default)
        => await _unifiedServer.StartAsync(ct);

    public async Task StopAsync(CancellationToken ct = default)
        => await _unifiedServer.StopAsync(ct);

    public bool IsRunning => _unifiedServer.IsRunning;

    // Pipeline component access (ServerHost-exclusive API)
    public ConnectionManager ConnectionManager
        => _unifiedServer.GetPipelineCoordinator().ConnectionManager;

    public ServiceRegistry ServiceRegistry
        => _unifiedServer.GetPipelineCoordinator().ServiceRegistry;

    public BackpressurePolicy BackpressurePolicy
        => _unifiedServer.GetPipelineCoordinator().BackpressurePolicy;

    // Service registration delegation
    public void RegisterService<T>(string name, T instance, ServiceOptions? options = null)
        => _unifiedServer.RegisterService(name, instance, options);

    public bool UnregisterService(string name)
        => _unifiedServer.UnregisterService(name);

    // Health status delegation
    public ServerHealthStatus GetHealthStatus()
        => _unifiedServer.GetHealthStatus();

    public void Dispose()
        => _unifiedServer.Dispose();
}
```

**Deprecation Strategy**:
- `[Obsolete]` attribute with clear message pointing to migration guide
- Warning level: `false` (compiler warning, not error)
- No removal timeline (maintain indefinitely for maximum compatibility)

---

## Configuration Model

### ServerConfiguration (Unified)

**Merges**: `ServerOptions` (PulseServer) + `ServerHostOptions` (ServerHost)

```csharp
public sealed class ServerConfiguration
{
    // Transport Configuration (from PulseServer)
    public List<TransportChannelConfiguration> Transports { get; set; } = new();

    // Pipeline Options (from ServerHost)
    public MessageReceiverOptions? ReceiverOptions { get; set; }
    public MessageDispatcherOptions? DispatcherOptions { get; set; }
    public ResponseTransmitterOptions? TransmitterOptions { get; set; }

    // Connection Management (from ServerHost)
    public ConnectionManagerOptions? ConnectionOptions { get; set; }
    public ServiceRegistryOptions? ServiceRegistryOptions { get; set; }
    public BackpressurePolicyOptions? BackpressureOptions { get; set; }

    // Graceful Shutdown (NEW)
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // Logging (from PulseServer)
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;
}
```

### ShutdownOptions (NEW)

```csharp
public sealed class ShutdownOptions
{
    // Total timeout for graceful shutdown
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    // Timeout for draining in-flight messages
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(20);

    // Timeout for closing connections
    public TimeSpan ConnectionCloseTimeout { get; set; } = TimeSpan.FromSeconds(5);

    // Timeout for stopping transports
    public TimeSpan TransportStopTimeout { get; set; } = TimeSpan.FromSeconds(5);

    // Force shutdown if timeout exceeded
    public bool ForceShutdownOnTimeout { get; set; } = true;
}
```

---

## Lifecycle Workflows

### Startup Sequence

```
StartAsync() invoked
    │
    ├─> ServerLifecycleCoordinator.TransitionToStartingAsync()
    │   └─> State: Stopped → Starting
    │   └─> Emit StateChanged event
    │
    ├─> TransportOrchestrator.StartAllTransportsAsync()
    │   ├─> For each transport config:
    │   │   ├─> ITransportIntegrationManager.CreateListener(config)
    │   │   ├─> Wire ConnectionAccepted → route to IServerChannelManager
    │   │   └─> listener.StartAsync()
    │   └─> Task.WhenAll (parallel)
    │
    ├─> PipelineCoordinator.StartPipelineAsync()
    │   ├─> MessageReceiver.StartAsync()
    │   ├─> MessageDispatcher.StartAsync() (spawn workers)
    │   └─> ResponseTransmitter.StartAsync() (spawn sender)
    │
    ├─> ServerLifecycleCoordinator.TransitionToRunningAsync()
    │   └─> State: Starting → Running
    │   └─> Emit StateChanged event
    │
    └─> StartAsync() returns
```

### Shutdown Sequence (Graceful)

```
StopAsync(timeout=30s) invoked
    │
    ├─> ServerLifecycleCoordinator.TransitionToStoppingAsync()
    │   └─> State: Running → Stopping
    │   └─> Emit StateChanged event
    │
    ├─> CancellationTokenSource.CancelAfter(30s)
    │
    ├─> TransportOrchestrator.StopAcceptingAsync(ct)
    │   └─> Stop accepting new connections (listeners remain open)
    │
    ├─> PipelineCoordinator.DrainPipelinesAsync(ct)
    │   ├─> Wait for MessageDispatcher queue to drain
    │   ├─> Wait for ResponseTransmitter queue to flush
    │   └─> Timeout: 20s (or throw OperationCanceledException)
    │
    ├─> IServerChannelManager.CloseAllChannelsAsync(ct)
    │   └─> Close all active connections gracefully
    │   └─> Timeout: 5s
    │
    ├─> TransportOrchestrator.StopAllTransportsAsync(ct)
    │   └─> Stop and dispose all listeners
    │   └─> Timeout: 5s
    │
    ├─> ServerLifecycleCoordinator.TransitionToStoppedAsync()
    │   └─> State: Stopping → Stopped
    │   └─> Emit StateChanged event
    │
    └─> StopAsync() returns

    [If timeout exceeded]:
    └─> Catch OperationCanceledException
        └─> Log warning: "Graceful shutdown timed out"
        └─> ForceShutdownAsync() (best-effort cleanup)
```

---

## Performance Considerations

1. **Zero-Allocation Delegation**: Facade methods create no heap objects (direct field access, no closures)
2. **Parallel Transport Operations**: Use `Task.WhenAll` for concurrent start/stop
3. **Async Throughout**: No blocking calls (all I/O is async)
4. **Lock-Free Where Possible**: Use `ConcurrentDictionary` for listeners/transports
5. **Pipeline Efficiency**: Reuse ServerHost's optimized components (pooled buffers, channels-based queues)
6. **Graceful Shutdown**: Coordinated phases prevent hung servers while allowing in-flight completion

---

## Next Steps

- Generate public API contracts (`contracts/IPulseServer.cs`, `contracts/ServerConfiguration.cs`)
- Write migration guide (`quickstart.md`)
- Update agent context with unified architecture
