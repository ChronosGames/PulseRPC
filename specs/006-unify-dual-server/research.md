# Research: Unified Server Implementation

**Date**: 2025-10-16
**Feature**: 006-unify-dual-server
**Purpose**: Technical research to inform the design of a unified server implementation that consolidates PulseServer and ServerHost

---

## Executive Summary

This research analyzes the existing dual server architectures (PulseServer and ServerHost) to inform the design of a unified implementation. Key findings:

1. **PulseServer** uses a transport-focused architecture managing listeners, transports, and channels
2. **ServerHost** uses a pipeline-focused architecture coordinating message processing components
3. The unified server should adopt PulseServer's transport-focused model while integrating ServerHost's pipeline capabilities
4. Binary compatibility can be maintained through facade pattern with near-zero overhead using AggressiveInlining
5. Existing test infrastructure can validate behavior parity between facades and unified implementation

---

## 1. PulseServer Architecture Analysis

### 1.1 Core Design: Transport-Focused Orchestration

**Location**: `src/PulseRPC.Server/PulseServer.cs`

**Primary Responsibilities**:
- Manages multiple transport configurations (TCP, KCP, custom)
- Creates and lifecycle-manages transport listeners
- Coordinates connection acceptance and channel registration
- Provides server state management (Stopped, Starting, Running, Stopping)

### 1.2 Key Components

#### TransportIntegrationManager
**Location**: `src/PulseRPC.Server/Integration/TransportIntegrationManager.cs`

- **Plugin architecture** for transport providers (ITransportProvider)
- Factory pattern for creating transport-specific listeners
- Validates transport support before listener creation
- Thread-safe provider registry using ConcurrentDictionary

#### ServerChannelManager
**Location**: `src/PulseRPC.Server/Processing/ServerChannelManager.cs`

- Tracks all active connections via IServerChannel abstraction
- Routes messages to processing engines (tiered message engine integration)
- Manages authentication state
- Automatic cleanup of stale connections (60s timer, 5min timeout)

#### IServerListener
**Location**: `src/PulseRPC.Abstractions/Transport/ITransport.cs`

**Implementations**:
- **TcpServerListener**: Uses .NET TcpListener, async accept loop
- **KcpServerListener**: Single UDP socket with handshake protocol

**Responsibilities**:
- Binds to network endpoint
- Accepts new connections
- Raises ConnectionAccepted events with IServerTransport instances

### 1.3 Lifecycle Flow

```
StartAsync():
  1. Validate transports configured
  2. Parallel start all transports (Task.WhenAll)
     → TransportIntegrationManager.CreateListener()
     → listener.ConnectionAccepted += OnConnectionAccepted
     → listener.StartAsync()
     → Store in _listeners dictionary
  3. Transition to Running state

OnConnectionAccepted():
  1. Non-blocking Task.Run for connection processing
  2. ServerChannelManager.AddChannel(transport)
  3. Wrap transport in ServerTransportChannel
  4. Subscribe to channel events (StateChanged, MessageParsed)
  5. Raise ChannelConnected event

StopAsync():
  1. Transition to Stopping state
  2. Parallel stop all listeners (Task.WhenAll)
     → Unsubscribe from events
     → listener.StopAsync() + Dispose()
  3. Transition to Stopped state
```

### 1.4 Architectural Strengths

- ✅ **Parallel Operations**: Listeners start/stop concurrently for performance
- ✅ **Non-blocking Acceptance**: Connection processing doesn't block listener threads
- ✅ **Extensibility**: New transports via ITransportProvider plugin
- ✅ **State Safety**: Volatile state + lock-based transitions prevent race conditions
- ✅ **Event-Driven**: Loose coupling between transport and processing layers

### 1.5 Current Limitations

- ⚠️ **Incomplete Message Pipeline**: Channel events not fully integrated with message processing
- ⚠️ **Limited Pipeline Components**: No MessageDispatcher, ServiceRegistry, or BackpressurePolicy integration

---

## 2. ServerHost Architecture Analysis

### 2.1 Core Design: Pipeline-Focused Orchestration

**Location**: `src/PulseRPC.Server/Core/ServerHost.cs`

**Primary Responsibilities**:
- Coordinates message processing pipeline components
- Enforces backpressure policy
- Handles service registration/unregistration
- Generates error responses for pipeline failures

### 2.2 Pipeline Components

#### MessageReceiver
**Location**: `src/PulseRPC.Server/Pipeline/MessageReceiver.cs`

- Receives raw bytes from transport
- Per-connection buffering using ArrayPool<byte>
- Length-prefixed framing (4-byte header)
- Emits MessageReceived and ParseError events

#### MessageDispatcher
**Location**: `src/PulseRPC.Server/Pipeline/MessageDispatcher.cs`

- Priority-based message queuing (Critical, High, Normal, Low)
- Worker thread pool for parallel processing
- Uses System.Threading.Channels for lock-free queuing
- Routes messages to registered service handlers
- Exposes QueueDepth for backpressure monitoring

**Priority Scheduling**:
```
Worker Loop:
  1. Fast path: TryRead from priority channels (Critical → Low)
  2. Async path: WhenAny on all channels if no immediate work
  3. Process item via ServiceInvoker
```

#### ServiceRegistry
**Location**: `src/PulseRPC.Server/Core/ServiceRegistry.cs`

- Thread-safe service lifecycle management
- Creates ServiceInvoker wrappers with timeout enforcement
- Service state transitions (Active, Paused, Unregistered)
- Maintains separate registry from MessageDispatcher routing table

#### ServiceInvoker
**Location**: `src/PulseRPC.Server/Pipeline/ServiceInvoker.cs`

- Wraps CompiledServiceInvoker with cross-cutting concerns
- Timeout enforcement via linked cancellation tokens
- Exception isolation and context analysis
- Distinguishes timeout vs. cancellation vs. exceptions

#### ResponseTransmitter
**Location**: `src/PulseRPC.Server/Pipeline/ResponseTransmitter.cs`

- Asynchronous response transmission
- Response batching for throughput optimization
- Worker thread pool for parallel I/O
- Decouples serialization (CPU) from transmission (I/O)

#### BackpressurePolicy
**Location**: `src/PulseRPC.Server/Core/BackpressurePolicy.cs`

**Three-level strategy**:
- **None** (0-70% queue utilization): Accept all
- **Throttle** (70-90%): Probabilistic 50% rejection
- **Reject** (90%+): Reject all new requests

**Hysteresis**: 10% band prevents oscillation (e.g., Throttle→None at 60%, not 70%)

#### ConnectionManager
**Location**: `src/PulseRPC.Server/Core/ConnectionManager.cs`

- Thread-safe connection tracking
- State machine validation (Connecting → Active → Closing → Closed)
- Timer-based cleanup (30s interval)
- Connection limits enforcement

### 2.3 Event Flow

```
Transport.DataReceived
  → MessageReceiver.OnDataReceived()
    → Parse complete message
    → Emit MessageReceived event

ServerHost.OnMessageReceived():
  1. Update BackpressurePolicy.UpdateQueueDepth()
  2. Check ShouldAcceptRequest()
  3. If rejected → SendErrorResponseAsync("ServiceOverloaded")
  4. If accepted → MessageDispatcher.DispatchMessageAsync()
     → Enqueue to priority channel
     → Worker dequeues
     → ServiceInvoker.InvokeAsync()
       → Timeout enforcement
       → CompiledServiceInvoker.InvokeAsync()
```

### 2.4 Service Registration Integration

```
ServerHost.RegisterService<TService>(name, instance, options):
  1. ServiceRegistry.RegisterService()
     → Create ServiceInvoker(instance, timeout)
     → Store ServiceRegistration

  2. Get IServiceHandler from registration

  3. MessageDispatcher.RegisterServiceHandler(name, handler)
     → Store in routing dictionary
```

**Design Rationale**: Separate registries allow pausing services (remove from dispatcher) without destroying lifecycle state (ServiceRegistry retains).

### 2.5 Architectural Strengths

- ✅ **Separation of Concerns**: Each component has single responsibility
- ✅ **Explicit Error Handling**: Errors caught at each pipeline stage with appropriate responses
- ✅ **Backpressure Integration**: Queue depth monitored before dispatch prevents saturation
- ✅ **Priority Scheduling**: Critical operations never starved under load
- ✅ **Async All the Way**: No blocking I/O, proper cancellation propagation
- ✅ **Memory Efficiency**: ArrayPool usage, ReadOnlyMemory avoids copying

### 2.6 Current Limitations

- ⚠️ **Incomplete Response Pipeline**: InvocationResult not automatically converted to responses
- ⚠️ **No Request Context**: Service methods can't access metadata, cancellation tokens
- ⚠️ **Statistics Isolation**: Each component tracks independently, no centralized aggregation

---

## 3. Architectural Comparison

| Aspect | PulseServer | ServerHost |
|--------|-------------|------------|
| **Primary Focus** | Transport management | Pipeline orchestration |
| **Entry Point** | Connection acceptance | Message reception |
| **Coordination** | Event-driven (ConnectionAccepted) | Event-driven (MessageReceived) |
| **State Management** | Server-level (Running/Stopped) | Component-level (per pipeline stage) |
| **Lifecycle** | Parallel listener startup/shutdown | Sequential component startup/shutdown |
| **Extensibility** | Transport plugins (ITransportProvider) | Service handlers (IServiceHandler) |
| **Performance** | Optimized for connection throughput | Optimized for message throughput |
| **Backpressure** | Not implemented | Integrated (BackpressurePolicy) |
| **Message Processing** | Delegates to ChannelManager | Full pipeline (Receiver→Dispatcher→Invoker→Transmitter) |

### 3.1 Complementary Strengths

- **PulseServer**: Strong transport abstraction, parallel operations, state safety
- **ServerHost**: Comprehensive message pipeline, backpressure, priority scheduling

### 3.2 Integration Opportunity

The unified server can adopt:
- **PulseServer's transport management** as the foundation
- **ServerHost's pipeline components** as integrated services
- **Event-driven coordination** to connect the two layers

---

## 4. Facade Pattern for Binary Compatibility

### 4.1 Implementation Pattern

```csharp
[Obsolete("Use UnifiedPulseServer instead. This will be removed in v3.0.")]
public class PulseServer : IDisposable
{
    private readonly UnifiedPulseServer _implementation;

    public PulseServer(/* old parameters */)
    {
        _implementation = new UnifiedPulseServer(/* mapped parameters */);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async Task StartAsync(CancellationToken ct = default)
    {
        return await _implementation.StartAsync(ct);
    }

    // ... delegate all methods
}
```

### 4.2 ObsoleteAttribute Strategy

**Three-Phase Deprecation**:

1. **Phase 1 (v2.x)**: Warning mode
   ```csharp
   [Obsolete("Use UnifiedPulseServer. Removed in v3.0.", error: false)]
   ```

2. **Phase 2 (v3.0)**: Error mode
   ```csharp
   [Obsolete("Use UnifiedPulseServer. Removed in v3.0.", error: true)]
   ```

3. **Phase 3 (v4.0)**: Complete removal

**Message Guidelines**:
- State reason: "Unified for better performance and maintainability"
- Provide alternative: "Use UnifiedPulseServer instead"
- Include timeline: "This will be removed in v3.0"
- Link to guide: "See migration guide at https://..."

### 4.3 Performance Optimization

**AggressiveInlining**:
- Overhead: <1ns per call (vs 7ns without inlining)
- Speedup: 7-25x for small delegation methods
- JIT limitations: 32-byte IL limit, no virtual calls, no complex control flow

**Expected facade overhead**: <5% (meets spec requirement SC-009)

### 4.4 .NET Ecosystem Examples

#### TypeForwardedToAttribute Pattern
Used by Microsoft for moving types between assemblies:
```csharp
// In old assembly
[assembly: TypeForwardedTo(typeof(MovedType))]
```
- Maintains binary compatibility without recompilation
- Used extensively in .NET Framework → .NET Core migration

#### ASP.NET Core WebApiCompatShim
- Compatibility layer for Web API 2 → ASP.NET Core MVC
- Temporary facade eventually deprecated and removed
- Clear communication about migration timeline

---

## 5. Unified Server Design Decisions

### 5.1 Architecture Choice: Transport-Focused with Pipeline Integration

**Decision**: Adopt PulseServer's transport-focused model as foundation, integrate ServerHost pipeline components as managed dependencies.

**Rationale**:
1. **Transport abstraction** is more fundamental than pipeline stages
2. **Multiple transports** (TCP + KCP + WebSocket) better managed with transport-first approach
3. **Pipeline components** can be composed into transport event handlers
4. **Simpler for users**: Single server manages transports, pipeline is internal detail

**Integration Pattern**:
```
UnifiedPulseServer
  ├─ TransportIntegrationManager (manages listeners)
  ├─ ServerChannelManager (manages connections)
  ├─ MessageReceiver (pipeline stage 1) ─┐
  ├─ MessageDispatcher (pipeline stage 2) │ Integrated
  ├─ ServiceRegistry (service lifecycle)   │ as internal
  └─ ResponseTransmitter (pipeline stage 3)┘ components
```

### 5.2 Component Reuse Strategy

**Reuse without modification**:
- ✅ TransportIntegrationManager
- ✅ IServerListener implementations (TcpServerListener, KcpServerListener)
- ✅ MessageReceiver
- ✅ MessageDispatcher
- ✅ ServiceRegistry
- ✅ ServiceInvoker
- ✅ ResponseTransmitter
- ✅ BackpressurePolicy
- ✅ ConnectionManager

**Modify for integration**:
- ⚠️ ServerChannelManager: Wire MessageParsed events to MessageReceiver
- ⚠️ PulseServer: Refactor to facade delegating to UnifiedPulseServer
- ⚠️ ServerHost: Refactor to facade delegating to UnifiedPulseServer

### 5.3 Event Wiring Pattern

```csharp
// In UnifiedPulseServer

// Transport layer events
listener.ConnectionAccepted += OnConnectionAccepted;

OnConnectionAccepted(transport):
  channel = _channelManager.AddChannel(transport);
  channel.MessageParsed += OnMessageParsed;

// Pipeline integration
OnMessageParsed(messageParsedEventArgs):
  rpcMessage = messageParsedEventArgs.Message;

  // Backpressure check
  var decision = _backpressurePolicy.ShouldAcceptRequest();
  if (!decision.Accept) {
    await SendErrorAsync("ServiceOverloaded");
    return;
  }

  // Dispatch to pipeline
  var result = await _messageDispatcher.DispatchMessageAsync(rpcMessage);

  // Send response (if pipeline returns result)
  if (result.IsSuccess) {
    await _responseTransmitter.SendResponseAsync(connectionId, result.Response);
  }
```

### 5.4 Configuration Consolidation

**Unified Configuration Object**:
```csharp
public class UnifiedServerOptions
{
    // From PulseServer
    public List<TransportChannelConfiguration> Transports { get; set; }

    // From ServerHost
    public MessageReceiverOptions MessageReceiver { get; set; }
    public MessageDispatcherOptions MessageDispatcher { get; set; }
    public ResponseTransmitterOptions ResponseTransmitter { get; set; }
    public ConnectionManagerOptions ConnectionManager { get; set; }
    public ServiceRegistryOptions ServiceRegistry { get; set; }
    public BackpressurePolicyOptions BackpressurePolicy { get; set; }
}
```

**Migration Mapping**:
- PulseServer.ServerOptions → UnifiedServerOptions (focus on Transports)
- ServerHost.ServerHostOptions → UnifiedServerOptions (focus on pipeline options)

---

## 6. Testing Strategy

### 6.1 Facade Testing Approach

**Option**: Reuse existing integration tests against both implementations

```csharp
[Theory]
[InlineData(typeof(UnifiedPulseServer))]
[InlineData(typeof(PulseServer))] // Facade
[InlineData(typeof(ServerHost))] // Facade
public async Task Server_ProcessesRequests_Correctly(Type serverType)
{
    var server = CreateServer(serverType);

    // Same tests for all implementations
    await TestRequestProcessing(server);
    await TestConnectionManagement(server);
    await TestGracefulShutdown(server);
}
```

**Benefits**:
- Ensures facades maintain identical behavior
- Catches configuration mapping errors
- Validates delegation correctness
- Minimal test code duplication

### 6.2 Performance Validation

**BenchmarkDotNet Tests**:
```csharp
[MemoryDiagnoser]
public class FacadeOverheadBenchmark
{
    [Benchmark(Baseline = true)]
    public async Task UnifiedServer_ProcessMessage()

    [Benchmark]
    public async Task PulseServerFacade_ProcessMessage()

    [Benchmark]
    public async Task ServerHostFacade_ProcessMessage()
}
```

**Expected Results**:
- Overhead: <5% (spec requirement SC-009)
- Memory: Zero additional allocations per call
- Throughput: Identical QPS

### 6.3 Integration Test Infrastructure

**Leverage existing**:
- `tests/PulseRPC.Server.Tests/` (unit + integration tests)
- `perf/BenchmarkApp/` (performance benchmarks)
- `examples/BasicServerDI/` (real-world usage validation)

---

## 7. Migration Path

### 7.1 Developer Experience

**Before (PulseServer)**:
```csharp
var server = new PulseServer(loggerFactory, options, channelManager, transportManager);
server.AddTransport(TransportChannelConfiguration.Tcp("tcp", 8080));
await server.StartAsync();
```

**After (UnifiedPulseServer)**:
```csharp
var server = new UnifiedPulseServer(options);
// Transports configured in options
await server.StartAsync();
```

**Before (ServerHost)**:
```csharp
var host = new ServerHost(transport, options);
host.RegisterService("myService", serviceInstance);
await host.StartAsync();
```

**After (UnifiedPulseServer)**:
```csharp
var server = new UnifiedPulseServer(options);
server.RegisterService("myService", serviceInstance);
await server.StartAsync();
```

### 7.2 Configuration Migration

**PulseServer → UnifiedPulseServer**:
- ServerOptions.Transports → UnifiedServerOptions.Transports (no change)
- AddTransport() calls → Configure in options (API shift)

**ServerHost → UnifiedPulseServer**:
- ServerHostOptions → UnifiedServerOptions (direct mapping)
- IPulseServerTransport parameter → Configured via TransportIntegrationManager

### 7.3 Timeline (from spec)

- **v2.x**: Facades with deprecation warnings
- **v3.0**: Compilation errors for deprecated APIs
- **v4.0**: Complete removal

---

## 8. Open Questions & Decisions Needed

### 8.1 Resolved by Research

✅ **Q**: How to maintain binary compatibility?
**A**: Facade pattern with AggressiveInlining for <5% overhead

✅ **Q**: Which architecture should be the base?
**A**: Transport-focused (PulseServer model) with pipeline integration

✅ **Q**: Can we reuse existing components?
**A**: Yes, all pipeline components can be reused without modification

✅ **Q**: How to test facades?
**A**: Run existing integration tests against all implementations

### 8.2 Remaining Decisions

⚠️ **Interface Design**: Should UnifiedPulseServer implement both IPulseServer and new interfaces?

⚠️ **DI Registration**: How to register UnifiedPulseServer in IServiceCollection while maintaining compatibility?

⚠️ **Builder API**: Should builder pattern change, or maintain backward compatibility?

⚠️ **Response Pipeline**: How to automatically wire InvocationResult → ResponseTransmitter?

---

## 9. Key Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Facade delegation overhead >5% | Performance regression | AggressiveInlining + benchmarking before release |
| Configuration mapping errors | Runtime failures | Comprehensive unit tests for mapping logic |
| Behavioral differences in facades | Breaking changes | Integration test parity validation |
| Incomplete pipeline integration | Missing functionality | Phase-by-phase implementation with tests |
| Documentation gaps | Poor migration experience | Migration guide + examples before release |

---

## 10. References

### Codebase Files Analyzed
- `src/PulseRPC.Server/PulseServer.cs`
- `src/PulseRPC.Server/Core/ServerHost.cs`
- `src/PulseRPC.Server/Pipeline/MessageReceiver.cs`
- `src/PulseRPC.Server/Pipeline/MessageDispatcher.cs`
- `src/PulseRPC.Server/Pipeline/ServiceInvoker.cs`
- `src/PulseRPC.Server/Pipeline/ResponseTransmitter.cs`
- `src/PulseRPC.Server/Core/ServiceRegistry.cs`
- `src/PulseRPC.Server/Core/BackpressurePolicy.cs`
- `src/PulseRPC.Server/Core/ConnectionManager.cs`
- `src/PulseRPC.Server/Integration/TransportIntegrationManager.cs`
- `src/PulseRPC.Server/Processing/ServerChannelManager.cs`

### External Resources
- Microsoft Docs: Type Forwarding in CLR
- Microsoft Docs: Breaking Changes in .NET Libraries
- .NET Blog: Understanding the Cost of C# Delegates
- Code Maze: How to Mark Methods as Deprecated in C#
- Mark Seemann's Blog: Facade Test Pattern

---

## Conclusion

The research validates the unified server approach with:

1. **Technical Feasibility**: Both architectures are complementary and can be integrated
2. **Performance**: Facade pattern with AggressiveInlining meets <5% overhead requirement
3. **Compatibility**: Binary compatibility maintained through well-established facade pattern
4. **Testing**: Existing test infrastructure can validate behavior parity
5. **Migration**: Clear path with three-phase deprecation strategy

**Recommendation**: Proceed to Phase 1 (Design) with transport-focused unified architecture integrating pipeline components as internal managed dependencies.
