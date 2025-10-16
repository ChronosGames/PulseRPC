# Data Model: Unified Server Implementation

**Date**: 2025-10-16
**Feature**: 006-unify-dual-server
**Purpose**: Define the data structures, entities, and their relationships for the unified server implementation

---

## Overview

The unified server implementation consolidates PulseServer and ServerHost into a single `UnifiedPulseServer` class. This document defines the core entities, configuration objects, state models, and their relationships.

---

## 1. Core Entities

### 1.1 UnifiedPulseServer

**Purpose**: Primary server class orchestrating transport management and message processing pipeline

**Namespace**: `PulseRPC.Server`

**Properties**:
```csharp
public sealed class UnifiedPulseServer : IPulseServer, IAsyncDisposable, IDisposable
{
    // State
    public ServerState State { get; }
    public bool IsRunning { get; }
    public int ActiveConnectionCount { get; }

    // Events
    public event EventHandler<ServerStateChangedEventArgs>? StateChanged;
    public event EventHandler<ClientConnectedEventArgs>? ClientConnected;
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;
}
```

**Relationships**:
- Owns: ITransportIntegrationManager
- Owns: IServerChannelManager
- Owns: IMessageReceiver (internal)
- Owns: IMessageDispatcher (internal)
- Owns: IServiceRegistry (internal)
- Owns: IResponseTransmitter (internal)
- Owns: IBackpressurePolicy (internal)
- Manages: Multiple IServerListener instances (via dictionary)

**Lifecycle States**: See Section 3.1

---

### 1.2 ServerState (Enum)

**Purpose**: Represents server lifecycle states

```csharp
public enum ServerState
{
    Stopped = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3
}
```

**State Transitions**:
```
Stopped → Starting → Running → Stopping → Stopped
         ↑__________________________|
```

**Validation Rules**:
- Cannot start from Running or Starting
- Cannot stop from Stopped or Stopping
- Thread-safe transitions via lock

---

### 1.3 UnifiedServerOptions

**Purpose**: Consolidated configuration for unified server

**Namespace**: `PulseRPC.Server`

```csharp
public sealed class UnifiedServerOptions
{
    // Transport Configuration (from PulseServer)
    public List<TransportChannelConfiguration> Transports { get; set; } = new();

    // Pipeline Configuration (from ServerHost)
    public MessageReceiverOptions MessageReceiver { get; set; } = new();
    public MessageDispatcherOptions MessageDispatcher { get; set; } = new();
    public ResponseTransmitterOptions ResponseTransmitter { get; set; } = new();
    public ConnectionManagerOptions ConnectionManager { get; set; } = new();
    public ServiceRegistryOptions ServiceRegistry { get; set; } = new();
    public BackpressurePolicyOptions BackpressurePolicy { get; set; } = new();

    // General Server Options
    public TimeSpan? DefaultOperationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxConcurrentOperations { get; set; } = 1000;
    public bool EnableDetailedLogging { get; set; } = false;
}
```

**Validation Rules**:
- At least one transport must be configured
- Exactly one transport must be marked as default
- Timeouts must be positive
- Max concurrent operations must be > 0

---

### 1.4 TransportChannelConfiguration

**Purpose**: Configuration for a single transport channel

**Namespace**: `PulseRPC.Server`

```csharp
public sealed class TransportChannelConfiguration
{
    public string Name { get; set; }
    public TransportType Type { get; set; }
    public int Port { get; set; }
    public bool IsDefault { get; set; }
    public TransportOptions? Options { get; set; }

    // Factory methods
    public static TransportChannelConfiguration Tcp(string name, int port,
        TcpTransportOptions? options = null, bool isDefault = false);

    public static TransportChannelConfiguration Kcp(string name, int port,
        KcpTransportOptions? options = null, bool isDefault = false);
}
```

**Validation Rules**:
- Name must be unique within server
- Port must be in range 1-65535
- Type must be supported by TransportIntegrationManager

---

## 2. Facade Entities

### 2.1 PulseServer (Deprecated Facade)

**Purpose**: Backward compatibility facade for existing PulseServer users

**Namespace**: `PulseRPC.Server`

```csharp
[Obsolete(
    "PulseServer has been unified into UnifiedPulseServer. " +
    "Use UnifiedPulseServer instead. This class will be removed in v3.0.",
    error: false)]
public sealed class PulseServer : IPulseServer, IAsyncDisposable, IDisposable
{
    // Delegation target
    private readonly UnifiedPulseServer _implementation;

    // Same public API as before
    public ServerState State => _implementation.State;
    public bool IsRunning => _implementation.IsRunning;
    // ... all properties/methods delegate to _implementation
}
```

**Mapping**:
- Constructor parameters → UnifiedServerOptions mapping
- AddTransport() calls → Transports collection
- All method calls → AggressiveInlining delegation

---

### 2.2 ServerHost (Deprecated Facade)

**Purpose**: Backward compatibility facade for existing ServerHost users

**Namespace**: `PulseRPC.Server.Core`

```csharp
[Obsolete(
    "ServerHost has been unified into UnifiedPulseServer. " +
    "Use UnifiedPulseServer instead. This class will be removed in v3.0.",
    error: false)]
public sealed class ServerHost : IDisposable
{
    // Delegation target
    private readonly UnifiedPulseServer _implementation;

    // Same public API as before
    public bool IsRunning => _implementation.IsRunning;
    public ConnectionManager ConnectionManager { get; }
    public ServiceRegistry ServiceRegistry { get; }
    // ... all properties/methods delegate to _implementation
}
```

**Mapping**:
- ServerHostOptions → UnifiedServerOptions
- IPulseServerTransport parameter → TransportChannel configuration
- RegisterService() → Delegates to _implementation.RegisterService()

---

## 3. State Models

### 3.1 Server State Machine

```
┌─────────┐
│ Stopped │ (Initial state, post-shutdown)
└────┬────┘
     │ StartAsync()
     ↓
┌──────────┐
│ Starting │ (Initializing transports, starting pipeline)
└────┬─────┘
     │ All transports started successfully
     ↓
┌─────────┐
│ Running │ (Accepting connections, processing messages)
└────┬────┘
     │ StopAsync()
     ↓
┌──────────┐
│ Stopping │ (Gracefully shutting down)
└────┬─────┘
     │ All components stopped
     ↓
┌─────────┐
│ Stopped │
└─────────┘
```

**Concurrent Transition Protection**:
- Uses `volatile ServerState` field + `object _stateLock`
- StartAsync() checks: not Running or Starting
- StopAsync() checks: not Stopped or Stopping

---

### 3.2 Connection Lifecycle

```
Transport Accepts Connection
     ↓
IServerTransport Created
     ↓
ServerChannelManager.AddChannel()
     ↓
ServerTransportChannel Wrapper Created
     ↓
Event Subscriptions:
  - StateChanged
  - MessageParsed
  - Authenticated
     ↓
ChannelConnected Event Raised
     ↓
Active Connection (message processing)
     ↓
Connection Closed / Timeout
     ↓
ChannelDisconnected Event Raised
     ↓
Auto-Cleanup (60s timer)
```

---

### 3.3 Message Processing Pipeline State

```
Transport Receives Bytes
     ↓
ServerChannelManager.OnMessageParsed() Event
     ↓
BackpressurePolicy.ShouldAcceptRequest()
     ├─ Reject → SendErrorResponseAsync("ServiceOverloaded")
     └─ Accept ↓
MessageDispatcher.DispatchMessageAsync()
     ↓
Enqueue to Priority Channel (Critical/High/Normal/Low)
     ↓
Worker Thread Dequeues
     ↓
ServiceInvoker.InvokeAsync()
     ├─ Success → InvocationResult
     ├─ Timeout → InvocationResult.Failure("TimeoutException")
     └─ Exception → InvocationResult.Failure(exception)
     ↓
ResponseTransmitter.SendResponseAsync()
     ↓
Serialize + Frame Response
     ↓
Transport.SendAsync()
```

---

## 4. Configuration Mapping

### 4.1 PulseServer → UnifiedPulseServer

| PulseServer API | UnifiedPulseServer API |
|-----------------|------------------------|
| `new PulseServer(loggerFactory, options, channelManager, transportManager)` | `new UnifiedPulseServer(unifiedOptions)` with DI injection |
| `server.AddTransport(config)` | `options.Transports.Add(config)` before construction |
| `ServerOptions.MaxConnections` | `UnifiedServerOptions.ConnectionManager.MaxConnections` |

**Facade Mapping Logic**:
```csharp
public PulseServer(ILoggerFactory? loggerFactory = null,
    IOptions<ServerOptions>? serverOptions = null,
    IServerChannelManager? channelManager = null,
    ITransportIntegrationManager? transportIntegrationManager = null)
{
    var unifiedOptions = new UnifiedServerOptions
    {
        // Map server options
        DefaultOperationTimeout = serverOptions?.Value.DefaultTimeout,
        ConnectionManager = new ConnectionManagerOptions
        {
            MaxConnections = serverOptions?.Value.MaxConnections ?? 1000
        }
    };

    _implementation = new UnifiedPulseServer(
        loggerFactory,
        Options.Create(unifiedOptions),
        channelManager,
        transportIntegrationManager);
}
```

---

### 4.2 ServerHost → UnifiedPulseServer

| ServerHost API | UnifiedPulseServer API |
|----------------|------------------------|
| `new ServerHost(transport, options)` | `new UnifiedPulseServer(options)` with transport in options |
| `ServerHostOptions.MessageReceiverOptions` | `UnifiedServerOptions.MessageReceiver` (direct) |
| `ServerHostOptions.MessageDispatcherOptions` | `UnifiedServerOptions.MessageDispatcher` (direct) |
| `host.RegisterService<T>(name, instance)` | `server.RegisterService<T>(name, instance)` (same API) |

**Facade Mapping Logic**:
```csharp
public ServerHost(IPulseServerTransport transport, ServerHostOptions? options = null)
{
    var unifiedOptions = new UnifiedServerOptions
    {
        // Create transport configuration from IPulseServerTransport
        Transports = new List<TransportChannelConfiguration>
        {
            TransportChannelConfiguration.FromTransport(transport)
        },

        // Direct mapping of pipeline options
        MessageReceiver = options?.MessageReceiverOptions ?? new(),
        MessageDispatcher = options?.MessageDispatcherOptions ?? new(),
        ResponseTransmitter = options?.ResponseTransmitterOptions ?? new(),
        ConnectionManager = options?.ConnectionManagerOptions ?? new(),
        ServiceRegistry = options?.ServiceRegistryOptions ?? new(),
        BackpressurePolicy = options?.BackpressurePolicyOptions ?? new()
    };

    _implementation = new UnifiedPulseServer(Options.Create(unifiedOptions));
}
```

---

## 5. Event Models

### 5.1 ServerStateChangedEventArgs

```csharp
public sealed class ServerStateChangedEventArgs : EventArgs
{
    public ServerState OldState { get; init; }
    public ServerState NewState { get; init; }
    public DateTime Timestamp { get; init; }
}
```

---

### 5.2 ClientConnectedEventArgs

```csharp
public sealed class ClientConnectedEventArgs : EventArgs
{
    public string ConnectionId { get; init; }
    public EndPoint? RemoteEndPoint { get; init; }
    public TransportType TransportType { get; init; }
    public DateTime ConnectedTime { get; init; }
}
```

---

### 5.3 ClientDisconnectedEventArgs

```csharp
public sealed class ClientDisconnectedEventArgs : EventArgs
{
    public string ConnectionId { get; init; }
    public DisconnectReason Reason { get; init; }
    public TimeSpan ConnectionDuration { get; init; }
    public DateTime DisconnectedTime { get; init; }
}
```

---

## 6. Performance Metrics Model

### 6.1 ServerPerformanceMetrics

```csharp
public sealed class ServerPerformanceMetrics
{
    public int ActiveConnections { get; init; }
    public long TotalConnectionsAccepted { get; init; }
    public long TotalMessagesProcessed { get; init; }
    public long TotalMessagesDropped { get; init; }
    public double AverageLatencyMs { get; init; }
    public double ThroughputMsgsPerSec { get; init; }
    public double MemoryUsageMB { get; init; }
    public double CpuUsagePercent { get; init; }
    public DateTime LastResetTime { get; init; }
}
```

**Collection Strategy**:
- Aggregated from all pipeline components
- Updated on-demand (GetPerformanceMetrics() call)
- Can be reset via ResetPerformanceMetrics()

---

## 7. Dependency Injection Model

### 7.1 Service Registration

```csharp
// Extension method
public static class UnifiedServerServiceCollectionExtensions
{
    public static IServiceCollection AddUnifiedPulseServer(
        this IServiceCollection services,
        Action<UnifiedServerOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddSingleton<ITransportIntegrationManager, TransportIntegrationManager>();
        services.AddSingleton<IServerChannelManager, ServerChannelManager>();
        services.AddSingleton<IPulseServer, UnifiedPulseServer>();
        services.AddHostedService<UnifiedPulseServerHostedService>();
        return services;
    }
}
```

---

### 7.2 Hosted Service Wrapper

```csharp
public sealed class UnifiedPulseServerHostedService : IHostedService
{
    private readonly UnifiedPulseServer _server;

    public UnifiedPulseServerHostedService(UnifiedPulseServer server)
    {
        _server = server;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _server.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _server.StopAsync(cancellationToken);
    }
}
```

---

## 8. Validation Rules Summary

### 8.1 Configuration Validation

| Rule | Validation | Error Message |
|------|------------|---------------|
| At least one transport | `Transports.Count > 0` | "At least one transport must be configured" |
| One default transport | `Transports.Count(t => t.IsDefault) == 1` | "Exactly one transport must be marked as default" |
| Unique transport names | `Transports.Select(t => t.Name).Distinct().Count() == Transports.Count` | "Transport names must be unique" |
| Valid port range | `Port >= 1 && Port <= 65535` | "Port must be between 1 and 65535" |
| Positive timeout | `DefaultOperationTimeout > TimeSpan.Zero` | "Timeout must be positive" |

---

### 8.2 Runtime Validation

| Rule | Validation | Action |
|------|------------|--------|
| Cannot start when running | `State != Running && State != Starting` | Throw InvalidOperationException |
| Cannot stop when stopped | `State != Stopped && State != Stopping` | Return immediately (no-op) |
| Transport supported | `TransportIntegrationManager.IsSupported(type)` | Throw NotSupportedException |
| Duplicate service name | `ServiceRegistry.Contains(name)` | Throw ArgumentException |

---

## 9. Key Relationships Diagram

```
┌─────────────────────────┐
│  UnifiedPulseServer     │
│  (Main Orchestrator)    │
└───────────┬─────────────┘
            │
    ┌───────┼───────┬──────────┬──────────────┐
    │       │       │          │              │
    ↓       ↓       ↓          ↓              ↓
┌──────┐ ┌─────┐ ┌────┐  ┌──────────┐  ┌──────────┐
│Trans-│ │Chan-│ │Msg │  │Service   │  │Response  │
│port  │ │nel  │ │Disp│  │Registry  │  │Transmit  │
│Integ.│ │Mgr  │ │atch│  │          │  │          │
└──┬───┘ └──┬──┘ └─┬──┘  └────┬─────┘  └────┬─────┘
   │        │      │          │             │
   │        │      │          │             │
   │  ┌─────┴──────┴──────────┴─────────────┘
   │  │ Event-Driven Coordination
   │  └────────────────────────────────────┐
   │                                       │
   ↓                                       ↓
┌─────────────┐                    ┌──────────────┐
│IServerListen│ (multiple)         │Backpressure  │
│             │                    │Policy        │
└─────────────┘                    └──────────────┘
```

---

## 10. Migration Data Flow

### 10.1 PulseServer Facade Data Flow

```
User Code → PulseServer (Facade)
                ↓ [AggressiveInlining]
           UnifiedPulseServer
                ↓
           Actual Implementation
```

**Data Transformation**:
- Configuration: ServerOptions → UnifiedServerOptions
- Method calls: Direct delegation (no data transformation)
- Events: Re-raised from implementation

---

### 10.2 ServerHost Facade Data Flow

```
User Code → ServerHost (Facade)
                ↓ [AggressiveInlining]
           UnifiedPulseServer
                ↓
           Actual Implementation
```

**Data Transformation**:
- Configuration: ServerHostOptions → UnifiedServerOptions
- Transport: IPulseServerTransport → TransportChannelConfiguration
- Method calls: Direct delegation with parameter mapping

---

## Conclusion

This data model provides:

1. **Clear Entity Hierarchy**: UnifiedPulseServer as the central orchestrator with well-defined dependencies
2. **Configuration Consolidation**: UnifiedServerOptions merges PulseServer and ServerHost configurations
3. **Facade Compatibility**: PulseServer and ServerHost facades map to unified model with minimal overhead
4. **State Management**: Explicit state machines for server lifecycle and message processing
5. **Validation Rules**: Comprehensive validation at configuration and runtime levels
6. **Event-Driven Integration**: Clear event models for component coordination

This model supports all functional requirements (FR-001 through FR-019) and enables the implementation of the unified server architecture.
