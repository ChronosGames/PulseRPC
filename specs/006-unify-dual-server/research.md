# Unified Server Implementation Research

**Date**: 2025-10-13
**Feature**: `006-unify-dual-server`
**Objective**: Analyze PulseServer and ServerHost implementations to design unified server architecture

---

## Executive Summary

This research analyzes two existing server implementations in PulseRPC that exhibit complementary architectural patterns:

- **PulseServer** (transport-focused): Manages multiple transport listeners (TCP/KCP), connection lifecycle, and client events
- **ServerHost** (pipeline-focused): Orchestrates message processing pipeline (MessageReceiver → MessageDispatcher → ServiceInvoker → ResponseTransmitter)

**Key Finding**: Both implementations are **complementary, not competing**. The unified design should adopt PulseServer's transport orchestration as the primary pattern while integrating ServerHost's proven pipeline components as managed subsystems.

**Critical Preservation Requirements**:
- ServerHost's service registration API (functional, unlike PulseServer's incomplete stub)
- ServerHost's pipeline component access (ConnectionManager, ServiceRegistry, BackpressurePolicy)
- ServerHost's comprehensive health monitoring (queue depth, backpressure level)
- PulseServer's multi-transport support and transport-level events

---

## Architecture Analysis

### PulseServer: Transport-Focused Orchestration

**File**: `src/PulseRPC.Server/PulseServer.cs` (458 lines)

**Core Philosophy**: Network connectivity is the first-class concern. The server orchestrates multiple transport listeners (TCP, KCP) and routes accepted connections to a channel manager.

**Architecture**:
```
┌─────────────────────────────────────────────────────────────┐
│                        PulseServer                          │
├─────────────────────────────────────────────────────────────┤
│  State: ServerState (Stopped/Starting/Running/Stopping)     │
│  Transports: ConcurrentDictionary<name, config>            │
│  Listeners: ConcurrentDictionary<name, IServerListener>    │
└─────────────────────────────────────────────────────────────┘
         │                        │                        │
         ▼                        ▼                        ▼
┌──────────────────┐   ┌──────────────────┐   ┌──────────────────┐
│ TCP Listener     │   │ KCP Listener     │   │ [Future] WS      │
│ (port 8080)      │   │ (port 9090)      │   │ Listener         │
└──────────────────┘   └──────────────────┘   └──────────────────┘
         │                        │                        │
         └────────────┬───────────┘                        │
                      ▼                                    │
         ┌────────────────────────────────┐               │
         │  IServerChannelManager         │ ◄─────────────┘
         │  - Manages active connections  │
         │  - Message processing (opaque) │
         └────────────────────────────────┘
```

**Key Components**:
1. **ITransportIntegrationManager**: Factory for creating IServerListener instances
2. **TransportChannelConfiguration**: Declarative transport config (name, type, port, options)
3. **IServerChannelManager**: Connection tracking and message processing (delegated)
4. **ServerLifecycle**: Lock-based state machine with parallel transport start/stop

**Lifecycle**:
```csharp
StartAsync():
├─ Lock-based state validation (prevent double-start)
├─ Parallel transport startup (Task.WhenAll)
│  ├─ CreateListener (ITransportIntegrationManager)
│  ├─ Register ConnectionAccepted event
│  └─ listener.StartAsync()
├─ Store in ConcurrentDictionary<string, IServerListener>
└─ Set state to Running

StopAsync():
├─ Lock-based state validation
├─ Cancel internal CancellationTokenSource
├─ Parallel transport shutdown (Task.WhenAll)
│  ├─ Unregister events
│  ├─ listener.StopAsync()
│  └─ listener.Dispose()
└─ Set state to Stopped
```

**Strengths**:
- ✅ Multi-transport support (TCP + KCP + future WebSocket/QUIC)
- ✅ Clear separation: transport concerns vs message processing
- ✅ Parallel transport operations (fast startup/shutdown)
- ✅ Rich event model (StateChanged, ClientConnected, ClientDisconnected)
- ✅ Full DI integration (all components injectable)
- ✅ Fluent builder API (PulseServerBuilder)

**Weaknesses**:
- ❌ Service registration incomplete (GetRegisteredServices returns empty list)
- ❌ No pipeline component visibility (opaque IServerChannelManager)
- ❌ No health monitoring (performance metrics incomplete)
- ❌ No graceful shutdown timeout enforcement

---

### ServerHost: Pipeline-Focused Orchestration

**File**: `src/PulseRPC.Server/Core/ServerHost.cs` (330 lines)

**Core Philosophy**: Message flow through the system is the first-class concern. The server orchestrates discrete pipeline stages (receive → parse → dispatch → invoke → respond).

**Architecture**:
```
┌─────────────────────────────────────────────────────────────┐
│                         ServerHost                          │
├─────────────────────────────────────────────────────────────┤
│  IPulseServerTransport (single transport)                   │
│  IsRunning: int flag (Interlocked)                          │
└─────────────────────────────────────────────────────────────┘
         │
         ├─────────────┬─────────────┬─────────────┬──────────────────┐
         ▼             ▼             ▼             ▼                  ▼
┌─────────────────┐ ┌───────────┐ ┌─────────────┐ ┌──────────────┐ ┌─────────────────┐
│ MessageReceiver │→│ MessageD  │→│ ServiceReg  │→│ ServiceInv   │→│ ResponseTrans   │
│ - Framing       │ │ ispatcher │ │ istry       │ │ oker         │ │ mitter          │
│ - Buffering     │ │ - Priority│ │ - Service   │ │ - Method     │ │ - Batching      │
│ - Parsing       │ │ - Channels│ │   Tracking  │ │   Dispatch   │ │ - Framing       │
└─────────────────┘ └───────────┘ └─────────────┘ └──────────────┘ └─────────────────┘
         │                  │              │                               │
         ▼                  ▼              ▼                               ▼
    (Pooled Buffers) (Worker Threads) (Reflection/Codegen)      (Connection-level Send)
```

**Key Components**:
1. **MessageReceiver**: Connection-level buffering, message framing (4-byte length header), parsing
2. **MessageDispatcher**: Priority-based queuing (Critical/High/Normal/Low), System.Threading.Channels, worker pool
3. **ServiceRegistry**: Service instance management, method introspection, state tracking
4. **ResponseTransmitter**: Response batching, channel-based queue, framing
5. **ConnectionManager**: Connection lifecycle tracking, statistics
6. **BackpressurePolicy**: Queue depth monitoring, admission control

**Lifecycle**:
```csharp
StartAsync():
├─ Atomic flag check (Interlocked.CompareExchange)
├─ Sequential pipeline startup
│  ├─ MessageReceiver.StartAsync() (start buffering)
│  ├─ MessageDispatcher.StartAsync() (spawn workers)
│  └─ ResponseTransmitter.StartAsync() (spawn sender)
└─ Set IsRunning flag

StopAsync():
├─ Atomic flag check
├─ Reverse-order pipeline shutdown
│  ├─ ResponseTransmitter.StopAsync()
│  ├─ MessageDispatcher.StopAsync()
│  └─ MessageReceiver.StopAsync()
└─ ConnectionManager.CloseAllConnectionsAsync()
```

**Strengths**:
- ✅ Proven message processing pipeline (production-tested)
- ✅ Functional service registration (RegisterService/UnregisterService)
- ✅ Comprehensive health monitoring (queue depth, backpressure level, message counts)
- ✅ Pipeline component access (ConnectionManager, ServiceRegistry, BackpressurePolicy properties)
- ✅ Built-in backpressure and priority scheduling
- ✅ Clear testing boundaries (each component independently testable)

**Weaknesses**:
- ❌ Single transport only (no multi-transport support)
- ❌ No state machine (boolean flag lacks Starting/Stopping states)
- ❌ No external events (no StateChanged, ClientConnected)
- ❌ Limited DI integration (creates components internally)
- ❌ No graceful shutdown timeout enforcement

---

## API Compatibility Matrix

### Common Methods (Identical Signature)

| Method | PulseServer | ServerHost | Behavioral Difference |
|--------|-------------|------------|----------------------|
| `StartAsync(CancellationToken)` | ✅ | ✅ | PulseServer: starts transports; ServerHost: starts pipeline |
| `StopAsync(CancellationToken)` | ✅ | ✅ | PulseServer: stops transports; ServerHost: stops pipeline |
| `IsRunning` | ✅ | ✅ | PulseServer: enum-backed; ServerHost: int flag |
| `Dispose()` | ✅ | ✅ | Both implement IDisposable |

### PulseServer-Exclusive Features

**Transport Management** (no ServerHost equivalent):
```csharp
void AddTransport(TransportChannelConfiguration config)
IReadOnlyDictionary<string, TransportInfo> GetTransports()
TransportInfo? GetDefaultTransport()
```

**Connection Management**:
```csharp
int ActiveConnectionCount { get; }
IReadOnlyList<ConnectionInfo> GetActiveConnections()
Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, ...)
Task<bool> SendAsync(string connectionId, ReadOnlyMemory<byte> data, ...)
```

**State & Events**:
```csharp
ServerState State { get; } // Enum: Stopped, Starting, Running, Stopping
event EventHandler<ServerStateChangedEventArgs>? StateChanged
event EventHandler<ClientConnectedEventArgs>? ClientConnected
event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected
```

**Performance Metrics**:
```csharp
ServerPerformanceMetrics GetPerformanceMetrics()
void ResetPerformanceMetrics()
IReadOnlyList<ServiceInfo> GetRegisteredServices() // ⚠️ INCOMPLETE - returns empty list
```

### ServerHost-Exclusive Features (CRITICAL - MUST PRESERVE)

**Pipeline Component Access**:
```csharp
ConnectionManager ConnectionManager { get; }
ServiceRegistry ServiceRegistry { get; }
BackpressurePolicy BackpressurePolicy { get; }
```
**Why Critical**: Enables fine-grained control, testing, production monitoring

**Service Registration** (functional, unlike PulseServer stub):
```csharp
void RegisterService<TService>(string serviceName, TService instance, ServiceOptions? options)
bool UnregisterService(string serviceName)
```

**Health Status** (comprehensive pipeline visibility):
```csharp
ServerHealthStatus GetHealthStatus()
// Returns: IsRunning, ActiveConnections,
//          TotalMessagesReceived/Dispatched/Sent,
//          RegisteredServiceCount, BackpressureLevel, QueueDepth
```

---

## Integration Patterns

### Dependency Injection

**PulseServer: Full DI Integration**
```csharp
// Registration (via PulseServerBuilder)
services.AddPulseServer(builder => builder
    .AddTcpTransport(8080)
    .AddKcpTransport(9090)
    .AddService<MyService>());

// Constructor injection
public PulseServer(
    ILoggerFactory loggerFactory,
    IOptions<ServerOptions> serverOptions,
    IServerChannelManager channelManager,
    ITransportIntegrationManager transportIntegrationManager)
```

**ServerHost: Self-Contained**
```csharp
// Direct construction
var transport = new TcpServerTransport("localhost", 5000);
var options = new ServerHostOptions { /* ... */ };
var serverHost = new ServerHost(transport, options);

// Manual service registration
serverHost.RegisterService("MyService", new MyServiceImpl());
```

**Key Difference**: PulseServer is DI-native (all components injectable), ServerHost creates components internally.

### Builder Patterns

**PulseServer** uses `PulseServerBuilder`:
```csharp
var server = new PulseServerBuilder()
    .AddTcpTransport(8080, options => { /* ... */ })
    .AddKcpTransport(9090, options => { /* ... */ })
    .WithServiceDiscovery(...)
    .Build();
```

**ServerHost** uses direct instantiation:
```csharp
var options = new ServerHostOptions
{
    MessageReceiverOptions = new MessageReceiverOptions { MaxBufferSize = 16MB },
    MessageDispatcherOptions = new MessageDispatcherOptions { WorkerThreadCount = 8 },
    // ...
};
var serverHost = new ServerHost(transport, options);
```

---

## Test Compatibility Plan

### Existing Test Inventory

**PulseRPC.Server.Tests** (tests/PulseRPC.Server.Tests/):
```
Integration/
├── [Multiple tests targeting PulseServer lifecycle]
├── [Transport integration tests]
└── [Multi-transport scenarios]

Unit/
├── [Component-level tests for channel managers]
└── [Builder API tests]

Contract/
├── [Service registration contract tests]
└── [Message serialization tests]
```

### Compatibility Validation Strategy

**Phase 1: Test Inventory**
1. Identify tests explicitly constructing `PulseServer` or `ServerHost`
2. Categorize by test type: unit, integration, performance
3. Mark tests that MUST pass unchanged for binary compatibility validation

**Phase 2: Compatibility Tests**
```csharp
// BinaryCompatibilityTests.cs - NEW
[Fact]
public void UnifiedServer_ImplementsIPulseServerInterface()
{
    var server = new PulseServer(/* ... */);
    Assert.IsAssignableFrom<IPulseServer>(server);
}

[Fact]
public async Task UnifiedServer_ExistingIntegrationTests_PassUnchanged()
{
    // Run all existing integration tests against unified implementation
    // Expect 100% pass rate
}
```

**Phase 3: Facade Tests**
```csharp
// ServerHostFacadeTests.cs - NEW
[Fact]
public void ServerHostFacade_DelegatesToUnifiedPulseServer()
{
    var facade = new ServerHost(transport, options);
    // Verify all method calls delegate to internal unified server
}

[Fact]
public void ServerHostFacade_PreservesServiceRegistrationAPI()
{
    var facade = new ServerHost(transport, options);
    facade.RegisterService("MyService", new MyServiceImpl());

    var healthStatus = facade.GetHealthStatus();
    Assert.Equal(1, healthStatus.RegisteredServiceCount);
}
```

**Expected Pass Rates**:
- Unit tests: 100% (no changes to component logic)
- Integration tests: 95%+ (minor adjustments for unified startup)
- Performance tests: 100% (facade overhead <5% per spec)

---

## Performance Baseline

### Current Metrics (from BenchmarkApp results)

**PulseServer** (transport-focused, current implementation):
- Average latency: 19.5ms (local network)
- P95 latency: ~45ms
- P99 latency: ~85ms
- Peak QPS: 46-68 requests/second
- Network throughput: Send 86.7MB/s, Receive 80.8MB/s
- Success rate: 99.8%
- Memory usage: 160-492MB
- CPU usage: 50-55%

**ServerHost** (pipeline-focused, no standalone benchmarks available):
- Performance characterized through pipeline component benchmarks
- MessageDispatcher: High throughput via System.Threading.Channels
- MessageReceiver: Pooled buffers reduce allocations
- Expected similar or better performance due to optimized pipeline

### Facade Overhead Budget

**Spec Requirement**: ServerHost facade delegation overhead <5% measured by message throughput

**Measurement Methodology**:
```csharp
// FacadeDelegationBenchmark.cs - NEW
[Benchmark(Baseline = true)]
public async Task DirectUnifiedServer()
{
    var server = new PulseServer(/* ... */);
    await server.StartAsync();
    // Send 10,000 messages, measure throughput
    await server.StopAsync();
}

[Benchmark]
public async Task ServerHostFacade()
{
    var facade = new ServerHost(transport, options);
    await facade.StartAsync();
    // Send 10,000 messages, measure throughput
    await facade.StopAsync();
}

// Expected result: Facade throughput >= 95% of direct (overhead <5%)
```

**Mitigation Strategies**:
- Use direct method delegation (no virtual dispatch)
- Avoid allocations in facade methods
- Inline-candidate small methods
- Profile with BenchmarkDotNet to identify bottlenecks

---

## Design Decisions

### Decision 1: Transport-Focused Orchestration

**Selected Approach**: Adopt PulseServer's transport-focused model as primary architecture

**Rationale**:
- Multi-transport support is core requirement (TCP + KCP + future WebSocket/QUIC)
- Clearer separation of concerns: transport lifecycle vs message processing
- Better extensibility for new transport types
- Aligns with spec clarification decision

**Implementation Approach**:
1. Keep `ITransportIntegrationManager` for transport provider abstraction
2. Maintain `TransportChannelConfiguration` for declarative transport setup
3. Preserve parallel transport start/stop for fast initialization
4. Integrate ServerHost's pipeline components as managed subsystems per transport

**Pipeline Integration**:
```
UnifiedPulseServer (Transport Orchestrator)
├── TransportOrchestrator
│   ├── TCP Listener (port 8080)
│   │   └── PipelineCoordinator
│   │       ├── MessageReceiver
│   │       ├── MessageDispatcher
│   │       └── ResponseTransmitter
│   └── KCP Listener (port 9090)
│       └── PipelineCoordinator
│           ├── MessageReceiver
│           ├── MessageDispatcher
│           └── ResponseTransmitter
└── ServerLifecycleCoordinator (state machine)
```

**Alternatives Considered**:
- ❌ **Pipeline-Focused Primary**: Would make multi-transport support awkward (one pipeline for all transports?)
- ❌ **Peer Architecture**: Treating transport and pipeline as equals adds complexity without benefit

---

### Decision 2: Facade Delegation Pattern

**Selected Approach**: Thin wrapper with direct delegation (zero-overhead design)

**Rationale**:
- Zero breaking changes required for existing ServerHost users
- Gradual migration path (deprecation warnings guide users)
- Performance-critical: delegation overhead must be <5% per spec
- Simple implementation: each ServerHost method delegates to unified PulseServer

**Implementation Pattern**:
```csharp
[Obsolete("ServerHost is deprecated. Use PulseServer instead. See quickstart.md for migration guide.", false)]
public sealed class ServerHost : IDisposable
{
    private readonly PulseServer _unifiedServer;
    private readonly string _transportName; // For pipeline access

    public ServerHost(IPulseServerTransport transport, ServerHostOptions? options = null)
    {
        // Convert ServerHostOptions → ServerConfiguration
        var config = ConvertToUnifiedConfig(transport, options);
        _unifiedServer = new PulseServer(/* config */);
        _transportName = config.Transports.First().Name;
    }

    // Direct delegation (zero-overhead)
    public async Task StartAsync(CancellationToken ct = default)
        => await _unifiedServer.StartAsync(ct);

    public async Task StopAsync(CancellationToken ct = default)
        => await _unifiedServer.StopAsync(ct);

    // Expose pipeline components (ServerHost-exclusive API)
    public ConnectionManager ConnectionManager
        => _unifiedServer.GetPipeline(_transportName).ConnectionManager;

    public void RegisterService<T>(string name, T instance, ServiceOptions? options = null)
        => _unifiedServer.RegisterService(name, instance, options);
}
```

**Performance Considerations**:
- **Inline candidate methods**: Small methods (property getters, simple delegates) will be inlined by JIT
- **No allocations**: Delegation methods create no heap objects
- **Direct calls**: No virtual dispatch (sealed class, direct field access)

**Alternatives Considered**:
- ❌ **Adapter Pattern**: More complex, unnecessary indirection
- ❌ **Breaking Changes**: Would violate 100% binary compatibility requirement

---

### Decision 3: Graceful Shutdown Strategy

**Selected Approach**: Timeout-based coordinated shutdown (30s default, configurable)

**Rationale**:
- Kubernetes-aligned (default pod termination grace period is 30s)
- Prevents hung shutdowns in production
- Allows in-flight requests to complete before force shutdown
- Configurable for different deployment scenarios

**Implementation**:
```csharp
public async Task StopAsync(CancellationToken cancellationToken = default)
{
    // Create linked token with configured timeout
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    cts.CancelAfter(_configuration.ShutdownTimeout); // Default 30s

    try
    {
        _logger.LogInformation("Initiating graceful shutdown (timeout: {Timeout})", _configuration.ShutdownTimeout);

        // Phase 1: Stop accepting new connections
        await _transportOrchestrator.StopAcceptingAsync(cts.Token);

        // Phase 2: Wait for in-flight messages to drain
        await _pipelineCoordinator.DrainPipelinesAsync(cts.Token);

        // Phase 3: Close all connections
        await _channelManager.CloseAllChannelsAsync(cts.Token);

        // Phase 4: Stop transports
        await _transportOrchestrator.StopAllTransportsAsync(cts.Token);

        _logger.LogInformation("Graceful shutdown completed");
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        // Timeout occurred (not user cancellation)
        _logger.LogWarning("Graceful shutdown timed out after {Timeout}, forcing shutdown", _configuration.ShutdownTimeout);

        // Force shutdown (best-effort)
        await ForceShutdownAsync();
    }
}
```

**Timeout Behavior**:
- Default: 30 seconds (configurable via `ShutdownOptions.Timeout`)
- User cancellation: Immediate force shutdown (respects external cancellation token)
- Timeout cancellation: Log warning, attempt force shutdown, then exit
- Force shutdown: Close transports immediately, dispose all resources

**Alternatives Considered**:
- ❌ **No Timeout**: Risk of hung servers in production
- ❌ **Fixed Timeout**: Less flexible for different deployment scenarios
- ❌ **Polling-Based**: More complex, less efficient than CancellationToken approach

---

## Migration Patterns

### Pattern 1: PulseServer User (Minimal Changes)

**Before** (current PulseServer user):
```csharp
var server = new PulseServerBuilder()
    .AddTcpTransport(8080)
    .AddKcpTransport(9090)
    .Build();

await server.StartAsync();
// ... use server
await server.StopAsync();
```

**After** (unified PulseServer):
```csharp
// NO CHANGES REQUIRED - Binary compatible!
var server = new PulseServerBuilder()
    .AddTcpTransport(8080)
    .AddKcpTransport(9090)
    .Build();

await server.StartAsync();
// ... use server (same API)
await server.StopAsync();
```

**Changes Required**: **NONE** - Existing code continues to work

**Benefits**:
- ✅ Service registration now functional (was incomplete before)
- ✅ Health monitoring API available (was partial before)
- ✅ Graceful shutdown timeout enforced (was missing before)

---

### Pattern 2: ServerHost User (Update to PulseServer)

**Before** (current ServerHost user):
```csharp
var transport = new TcpServerTransport("localhost", 5000);
var options = new ServerHostOptions
{
    MessageReceiverOptions = new MessageReceiverOptions { /* ... */ },
    MessageDispatcherOptions = new MessageDispatcherOptions { /* ... */ }
};

var serverHost = new ServerHost(transport, options);
serverHost.RegisterService("MyService", new MyServiceImpl());

await serverHost.StartAsync();
var healthStatus = serverHost.GetHealthStatus();
```

**After** (unified PulseServer - recommended):
```csharp
var server = new PulseServerBuilder()
    .AddTcpTransport(5000, tcpOptions =>
    {
        tcpOptions.ReceiverOptions = new MessageReceiverOptions { /* ... */ };
        tcpOptions.DispatcherOptions = new MessageDispatcherOptions { /* ... */ };
    })
    .Build();

server.RegisterService("MyService", new MyServiceImpl());

await server.StartAsync();
var healthStatus = server.GetHealthStatus(); // Unified API
```

**Alternative** (facade - zero code changes):
```csharp
// NO CHANGES REQUIRED - ServerHost facade maintains compatibility
var transport = new TcpServerTransport("localhost", 5000);
var options = new ServerHostOptions { /* ... */ };

var serverHost = new ServerHost(transport, options); // [Obsolete] warning
serverHost.RegisterService("MyService", new MyServiceImpl());

await serverHost.StartAsync();
var healthStatus = serverHost.GetHealthStatus();
// Works identically, but receives deprecation warning
```

**Migration Timeline**:
- **Immediate**: Existing code continues to work via ServerHost facade
- **Warning**: Compiler shows `[Obsolete]` deprecation message
- **Recommended**: Migrate to unified PulseServer within next major version
- **No Force**: Facade retained indefinitely for maximum compatibility

---

### Pattern 3: Custom Extension Method Users (Must Rewrite)

**Before** (extension methods targeting ServerHost):
```csharp
public static class ServerHostExtensions
{
    public static void AddMyCustomMiddleware(this ServerHost server)
    {
        // Directly accesses ServerHost-specific internals
        server.BackpressurePolicy.UpdateThreshold(1000);
        server.ConnectionManager.SetMaxConnections(500);
    }
}
```

**After** (update to target unified PulseServer):
```csharp
public static class PulseServerExtensions
{
    public static void AddMyCustomMiddleware(this PulseServer server, string? transportName = null)
    {
        // Option 1: Target specific transport pipeline
        if (transportName != null)
        {
            var pipeline = server.GetPipeline(transportName);
            pipeline.BackpressurePolicy.UpdateThreshold(1000);
            pipeline.ConnectionManager.SetMaxConnections(500);
        }

        // Option 2: Apply to all transports
        else
        {
            foreach (var transport in server.GetTransports().Keys)
            {
                var pipeline = server.GetPipeline(transport);
                pipeline.BackpressurePolicy.UpdateThreshold(1000);
                pipeline.ConnectionManager.SetMaxConnections(500);
            }
        }
    }
}
```

**Why Required**: Clarification decision established that custom extensions will break (accepted trade-off for cleaner unified API)

**Migration Guide**: Provide clear documentation in `quickstart.md` with before/after patterns for common extension scenarios

---

### Pattern 4: DI Registration (Verify Works)

**Before** (PulseServer DI registration):
```csharp
services.AddPulseServer(builder => builder
    .AddTcpTransport(8080)
    .AddService<MyService>());

// Resolve
var server = serviceProvider.GetRequiredService<IPulseServer>();
```

**After** (unified PulseServer DI registration):
```csharp
// NO CHANGES REQUIRED - ServiceCollectionExtensions updated internally
services.AddPulseServer(builder => builder
    .AddTcpTransport(8080)
    .AddService<MyService>());

// Resolve (same interface)
var server = serviceProvider.GetRequiredService<IPulseServer>();
```

**Internal Change** (transparent to users):
```csharp
// ServiceCollectionExtensions.cs - updated to construct unified server
services.TryAddSingleton<IPulseServer>(sp =>
{
    var config = /* build from builder state */;
    return new PulseServer(config); // Now unified implementation
});
```

---

## Alternatives Considered

### Alternative 1: Keep Both Implementations Separate

**Approach**: Maintain PulseServer and ServerHost as distinct implementations

**Pros**:
- No breaking changes
- Each implementation optimized for its use case
- Lower implementation risk

**Cons**:
- ❌ Continued API confusion ("which server class to use?")
- ❌ Duplicate maintenance burden
- ❌ Feature parity drift over time
- ❌ PulseServer's incomplete service registration remains unfixed

**Rejection Reason**: Does not address the core problem (dual implementation confusion and maintenance burden)

---

### Alternative 2: Delete PulseServer, Promote ServerHost

**Approach**: Make ServerHost the only implementation, deprecate PulseServer

**Pros**:
- Single implementation
- ServerHost has functional service registration
- Proven pipeline architecture

**Cons**:
- ❌ Breaks multi-transport support (core PulseServer feature)
- ❌ No transport-level abstraction (ITransportIntegrationManager)
- ❌ Limited DI integration
- ❌ No fluent builder API

**Rejection Reason**: Loses valuable PulseServer features (multi-transport, DI, builder pattern)

---

### Alternative 3: Compositional Unification (Multiple ServerHost Instances)

**Approach**: PulseServer manages multiple ServerHost instances (one per transport)

**Pros**:
- Preserves both architectures fully
- Clear ownership boundaries

**Cons**:
- ❌ Complex coordination logic
- ❌ Unclear service registration semantics (register per-transport or globally?)
- ❌ Duplicated pipeline components per transport (memory overhead)
- ❌ Difficult to reason about cross-transport scenarios

**Rejection Reason**: Adds complexity without clear benefit; service registration ambiguity is problematic

---

### Alternative 4: Full Rewrite (Greenfield)

**Approach**: Delete both implementations, rewrite from scratch

**Pros**:
- Perfect architecture (no legacy constraints)
- Opportunity to fix all known issues

**Cons**:
- ❌ High risk (new bugs, performance regressions)
- ❌ Long development timeline
- ❌ Requires extensive testing to reach current stability
- ❌ Breaks binary compatibility (major version required)

**Rejection Reason**: Violates 100% binary compatibility requirement; too risky for production framework

---

## Recommendation: Unified Rewrite with Facade

**Selected Strategy**: Rewrite PulseServer as unified implementation (transport-focused + integrated pipeline), ServerHost becomes thin facade

**Key Elements**:
1. **Transport-Focused Core**: Adopt PulseServer's multi-transport orchestration model
2. **Integrated Pipeline**: Incorporate ServerHost's MessageReceiver/Dispatcher/Transmitter via new PipelineCoordinator
3. **Unified Configuration**: Merge ServerOptions + ServerHostOptions into ServerConfiguration
4. **Full API Preservation**: Implement complete IPulseServer + expose ServerHost's pipeline components
5. **Graceful Shutdown**: Add 30s timeout-based coordinated shutdown
6. **Binary Compatibility**: ServerHost facade delegates to unified PulseServer (zero breaking changes)

**Implementation Phases**:
1. **Phase 0** (Research): ✅ Complete (this document)
2. **Phase 1** (Design): Generate data-model.md, contracts/, quickstart.md
3. **Phase 2** (Tasks): Generate implementation task breakdown via `/speckit.tasks`
4. **Phase 3** (Implementation): TDD approach (write tests → implement → validate)
5. **Phase 4** (Validation): BenchmarkDotNet verification of <5% facade overhead

**Success Criteria**:
- ✅ 100% existing integration tests pass unchanged
- ✅ Facade delegation overhead <5% (measured by message throughput)
- ✅ All ServerHost functionality preserved (service registration, health monitoring, pipeline access)
- ✅ All PulseServer functionality preserved (multi-transport, events, state machine)
- ✅ Graceful shutdown completes within 30s default timeout
- ✅ Migration guide validates via external developer review (<30 min migration time)

---

**Next Step**: Proceed to Phase 1 - Design & Contracts (`/speckit.plan` will now generate `data-model.md`, `contracts/`, `quickstart.md`)
