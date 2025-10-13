# Unified Server Migration Guide

**Feature**: `006-unify-dual-server`
**Date**: 2025-10-13
**Estimated Migration Time**: <30 minutes for typical applications

---

## Quick Start: What Changed?

### For PulseServer Users

**Good News**: Your code continues to work without changes! The unified PulseServer is binary compatible.

**New Features Available**:
- ✅ Functional service registration (was incomplete before)
- ✅ Comprehensive health monitoring (`GetHealthStatus()`)
- ✅ Graceful shutdown timeout enforcement (30s default)

### For ServerHost Users

**Options**:
1. **No Code Changes** (use facade): Existing code works, receives deprecation warning
2. **Migrate to PulseServer** (recommended): Update to unified API, remove warning

---

## Migration Scenarios

### Scenario 1: PulseServer User (No Changes Required)

**Before**:
```csharp
var server = new PulseServerBuilder()
    .AddTcpTransport(8080)
    .AddKcpTransport(9090)
    .Build();

await server.StartAsync();
var connections = server.GetActiveConnections();
await server.StopAsync();
```

**After**:
```csharp
// NO CHANGES REQUIRED - Binary compatible!
var server = new PulseServerBuilder()
    .AddTcpTransport(8080)
    .AddKcpTransport(9090)
    .Build();

await server.StartAsync();
var connections = server.GetActiveConnections();
await server.StopAsync();

// NEW: Now functional (was incomplete before)
server.RegisterService("MyService", new MyServiceImpl());
var health = server.GetHealthStatus(); // NEW API
```

---

### Scenario 2: ServerHost User (Zero Code Changes via Facade)

**Before**:
```csharp
var transport = new TcpServerTransport("localhost", 5000);
var options = new ServerHostOptions
{
    MessageReceiverOptions = new MessageReceiverOptions { MaxBufferSize = 16 * 1024 * 1024 },
    MessageDispatcherOptions = new MessageDispatcherOptions { WorkerThreadCount = 8 }
};

var serverHost = new ServerHost(transport, options);
serverHost.RegisterService("MyService", new MyServiceImpl());

await serverHost.StartAsync();
var healthStatus = serverHost.GetHealthStatus();
await serverHost.StopAsync();
```

**After (no changes)**:
```csharp
// NO CHANGES - ServerHost facade works identically
var transport = new TcpServerTransport("localhost", 5000);
var options = new ServerHostOptions { /* ... */ };

var serverHost = new ServerHost(transport, options); // [Obsolete] warning
serverHost.RegisterService("MyService", new MyServiceImpl());

await serverHost.StartAsync();
var healthStatus = serverHost.GetHealthStatus();
await serverHost.StopAsync();
```

**Compiler Output**:
```
warning CS0618: 'ServerHost' is obsolete: 'ServerHost is deprecated. Use PulseServer instead.
See migration guide at https://github.com/your-org/PulseRPC/blob/main/specs/006-unify-dual-server/quickstart.md'
```

---

### Scenario 3: ServerHost User (Migrate to Unified PulseServer)

**Recommended Approach** - eliminates deprecation warning:

**Before**:
```csharp
var transport = new TcpServerTransport("localhost", 5000);
var options = new ServerHostOptions
{
    MessageReceiverOptions = new MessageReceiverOptions { MaxBufferSize = 16 * 1024 * 1024 },
    MessageDispatcherOptions = new MessageDispatcherOptions { WorkerThreadCount = 8 },
    ConnectionManagerOptions = new ConnectionManagerOptions { MaxConnections = 1000 }
};

var serverHost = new ServerHost(transport, options);
serverHost.RegisterService("UserService", new UserServiceImpl());
serverHost.RegisterService("AuthService", new AuthServiceImpl());

await serverHost.StartAsync();
```

**After (migrated)**:
```csharp
var server = new PulseServerBuilder()
    .AddTcpTransport(5000, tcpOptions =>
    {
        tcpOptions.ReceiverOptions = new MessageReceiverOptions { MaxBufferSize = 16 * 1024 * 1024 };
        tcpOptions.DispatcherOptions = new MessageDispatcherOptions { WorkerThreadCount = 8 };
        tcpOptions.ConnectionOptions = new ConnectionManagerOptions { MaxConnections = 1000 };
    })
    .Build();

server.RegisterService("UserService", new UserServiceImpl());
server.RegisterService("AuthService", new AuthServiceImpl());

await server.StartAsync();
```

**Migration Steps**:
1. Replace `new ServerHost(transport, options)` with `new PulseServerBuilder().AddTcpTransport(...).Build()`
2. Move `ServerHostOptions` properties into transport configuration lambda
3. Keep service registration calls unchanged (`RegisterService` works identically)
4. Update method calls if using ServerHost-specific APIs (see below)

---

### Scenario 4: Multi-Transport Migration (ServerHost → PulseServer)

**Before** (ServerHost - single transport):
```csharp
var tcpTransport = new TcpServerTransport("localhost", 8080);
var serverHost = new ServerHost(tcpTransport, options);
```

**After** (unified PulseServer - multi-transport):
```csharp
var server = new PulseServerBuilder()
    .AddTcpTransport(8080, tcp =>
    {
        tcp.ReceiverOptions = new MessageReceiverOptions { /* ... */ };
        tcp.DispatcherOptions = new MessageDispatcherOptions { /* ... */ };
    })
    .AddKcpTransport(9090, kcp =>
    {
        kcp.EnableLowLatencyMode = true;
    })
    .Build();
```

**Benefit**: Now supports multiple transports simultaneously (TCP for reliability + KCP for low latency).

---

### Scenario 5: DI Registration Migration

**Before** (PulseServer DI):
```csharp
services.AddPulseServer(builder => builder
    .AddTcpTransport(8080)
    .AddService<MyService>());

// Resolve
var server = serviceProvider.GetRequiredService<IPulseServer>();
```

**After** (unified PulseServer DI):
```csharp
// NO CHANGES REQUIRED - ServiceCollectionExtensions updated internally
services.AddPulseServer(builder => builder
    .AddTcpTransport(8080)
    .AddService<MyService>());

// Resolve (same interface)
var server = serviceProvider.GetRequiredService<IPulseServer>();
```

---

### Scenario 6: Pipeline Component Access (ServerHost-Exclusive API)

**Before** (ServerHost):
```csharp
var serverHost = new ServerHost(transport, options);

// Direct access to pipeline components
var connectionMgr = serverHost.ConnectionManager;
var serviceRegistry = serverHost.ServiceRegistry;
var backpressure = serverHost.BackpressurePolicy;

backpressure.UpdateMaxQueueDepth(5000);
```

**After** (unified PulseServer):
```csharp
var server = new PulseServerBuilder()
    .AddTcpTransport(5000)
    .Build();

// Access pipeline coordinator for component access
var pipeline = server.GetPipelineCoordinator();

var connectionMgr = pipeline.ConnectionManager;
var serviceRegistry = pipeline.ServiceRegistry;
var backpressure = pipeline.BackpressurePolicy;

backpressure.UpdateMaxQueueDepth(5000);
```

**Change**: Use `GetPipelineCoordinator()` to access pipeline components (ServerHost exposed them directly as properties).

---

### Scenario 7: Custom Extension Methods (Requires Rewrite)

**Important**: Custom extension methods targeting ServerHost types must be rewritten (clarification decision).

**Before** (extension method on ServerHost):
```csharp
public static class ServerHostExtensions
{
    public static void ConfigureForGameServer(this ServerHost server)
    {
        server.BackpressurePolicy.UpdateMaxQueueDepth(10000);
        server.ConnectionManager.SetMaxConnections(5000);
    }
}

// Usage
var serverHost = new ServerHost(transport, options);
serverHost.ConfigureForGameServer();
```

**After** (extension method on PulseServer):
```csharp
public static class PulseServerExtensions
{
    public static void ConfigureForGameServer(this PulseServer server, string? transportName = null)
    {
        var pipeline = server.GetPipelineCoordinator(transportName);
        pipeline.BackpressurePolicy.UpdateMaxQueueDepth(10000);
        pipeline.ConnectionManager.SetMaxConnections(5000);
    }
}

// Usage
var server = new PulseServerBuilder().AddTcpTransport(5000).Build();
server.ConfigureForGameServer();
```

**Changes**:
1. Change extension target from `ServerHost` to `PulseServer`
2. Access pipeline components via `GetPipelineCoordinator()`
3. Optionally support transport-specific configuration (multi-transport scenarios)

---

## API Mapping Reference

### Lifecycle Methods (Identical)

| ServerHost | Unified PulseServer | Notes |
|------------|---------------------|-------|
| `StartAsync(ct)` | `StartAsync(ct)` | Identical signature |
| `StopAsync(ct)` | `StopAsync(ct)` | Now includes graceful shutdown timeout (30s default) |
| `IsRunning` | `IsRunning` | Identical |
| `Dispose()` | `Dispose()` | Identical |

### Service Management

| ServerHost | Unified PulseServer | Notes |
|------------|---------------------|-------|
| `RegisterService<T>(name, instance, options)` | `RegisterService<T>(name, instance, options)` | Identical - now works in PulseServer (was incomplete before) |
| `UnregisterService(name)` | `UnregisterService(name)` | Identical |

### Health Monitoring

| ServerHost | Unified PulseServer | Notes |
|------------|---------------------|-------|
| `GetHealthStatus()` | `GetHealthStatus()` | Identical - returns `ServerHealthStatus` |
| N/A | `GetPerformanceMetrics()` | NEW in unified - returns `ServerPerformanceMetrics` |

### Pipeline Component Access

| ServerHost | Unified PulseServer | Notes |
|------------|---------------------|-------|
| `ConnectionManager` | `GetPipelineCoordinator().ConnectionManager` | Access via coordinator |
| `ServiceRegistry` | `GetPipelineCoordinator().ServiceRegistry` | Access via coordinator |
| `BackpressurePolicy` | `GetPipelineCoordinator().BackpressurePolicy` | Access via coordinator |

### Transport Management (New in Unified)

| ServerHost | Unified PulseServer | Notes |
|------------|---------------------|-------|
| N/A (single transport via constructor) | `AddTransport(config)` | NEW - multi-transport support |
| N/A | `GetTransports()` | NEW - query all transports |
| N/A | `GetDefaultTransport()` | NEW - get primary transport |

---

## Common Pitfalls

### Pitfall 1: Forgetting to Migrate Extension Methods

**Problem**:
```csharp
// Extension method still targets ServerHost
public static class MyExtensions
{
    public static void Setup(this ServerHost server) { /* ... */ }
}

// Usage with facade (works but generates warning)
var facade = new ServerHost(transport, options);
facade.Setup(); // Works but extension is deprecated
```

**Solution**: Rewrite extension to target `PulseServer` (see Scenario 7).

---

### Pitfall 2: Assuming Zero Pipeline Overhead

**Problem**: ServerHost facade delegates to unified PulseServer, which may add minimal overhead.

**Reality**: Facade overhead <5% per spec (measured by message throughput). For most applications, this is negligible.

**Validation**: Run BenchmarkDotNet suite to measure impact in your scenario.

---

### Pitfall 3: Direct Access to Internal Components

**Problem**:
```csharp
// Before (ServerHost - direct access)
var dispatcher = serverHost._messageDispatcher; // BREAKS - private field
```

**Solution**: Use public APIs (`GetPipelineCoordinator()`) instead of relying on internal implementation details.

---

## Testing Your Migration

### Step 1: Verify Compilation
```bash
dotnet build
# Expect: Success (may have [Obsolete] warnings)
```

### Step 2: Run Integration Tests
```bash
dotnet test
# Expect: 100% pass rate (binary compatibility)
```

### Step 3: Performance Validation
```bash
cd perf/BenchmarkApp
dotnet run --project PulseRPC.Benchmark.Client -- run --scenario throughput
# Compare before/after metrics (expect <5% difference)
```

### Step 4: Production Smoke Test
- Deploy to staging environment
- Monitor health metrics (`GetHealthStatus()`)
- Validate connection counts, message throughput
- Check logs for unexpected errors

---

## Getting Help

- **Documentation**: [Implementation Plan](./plan.md)
- **Architecture**: [Data Model](./data-model.md)
- **Issues**: [GitHub Issues](https://github.com/your-org/PulseRPC/issues)
- **Discussions**: [GitHub Discussions](https://github.com/your-org/PulseRPC/discussions)

---

**Estimated Migration Time**: <30 minutes for typical applications (spec requirement validated)
