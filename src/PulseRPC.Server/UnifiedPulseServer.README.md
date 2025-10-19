# UnifiedPulseServer Usage Guide

## Overview

`UnifiedPulseServer` is the unified server implementation that consolidates the functionality of both `PulseServer` and `ServerHost`. It provides a single, clear API for managing RPC servers with multiple transport support.

## Features

- **Unified API**: Single entry point for server management
- **Multi-Transport Support**: TCP, KCP, and custom transports
- **Pipeline Integration**: Built-in message processing pipeline with backpressure
- **Service Registry**: Dynamic service registration and lifecycle management
- **Performance Monitoring**: Built-in metrics and monitoring
- **Lifecycle Management**: Thread-safe start/stop with graceful shutdown
- **Event-Driven**: Connection and state change events

## Quick Start

### Basic TCP Server

```csharp
using PulseRPC.Server;
using PulseRPC.Server.Configuration;
using Microsoft.Extensions.Logging;

// Create options
var options = new UnifiedServerOptions
{
    Transports = new List<TransportChannelConfiguration>
    {
        TransportChannelConfiguration.Tcp("tcp", 8080, isDefault: true)
    }
};

// Create server
var server = new UnifiedPulseServer(
    loggerFactory,
    Options.Create(options),
    channelManager,
    transportManager);

// Start server
await server.StartAsync();

Console.WriteLine("Server running on port 8080");

// Stop server
await server.StopAsync();
```

### Multi-Transport Server

```csharp
var options = new UnifiedServerOptions
{
    Transports = new List<TransportChannelConfiguration>
    {
        TransportChannelConfiguration.Tcp("tcp", 8080, isDefault: true),
        TransportChannelConfiguration.Kcp("kcp", 9090)
    },
    DefaultOperationTimeout = TimeSpan.FromSeconds(60),
    MaxConcurrentOperations = 5000
};

var server = new UnifiedPulseServer(
    loggerFactory,
    Options.Create(options),
    channelManager,
    transportManager);

await server.StartAsync();
// Server now listens on TCP:8080 and KCP:9090
```

### Service Registration

```csharp
public class MyService
{
    public string SayHello(string name) => $"Hello, {name}!";
}

// Register service
server.RegisterService("MyService", new MyService());

// Unregister service
server.UnregisterService("MyService");
```

### Monitoring and Metrics

```csharp
// Get performance metrics
var metrics = server.GetPerformanceMetrics();
Console.WriteLine($"Active Connections: {metrics.ActiveConnections}");
Console.WriteLine($"Total Messages: {metrics.TotalMessagesProcessed}");
Console.WriteLine($"Memory Usage: {metrics.MemoryUsageMB:F2} MB");

// Get transport information
var transports = server.GetTransports();
foreach (var (name, info) in transports)
{
    Console.WriteLine($"{name}: {info.Type}:{info.Port} (Listening: {info.IsListening})");
}

// Get active connections
var connections = server.GetActiveConnections();
foreach (var conn in connections)
{
    Console.WriteLine($"Connection {conn.ConnectionId} from {conn.RemoteEndPoint}");
}
```

### Broadcasting Messages

```csharp
// Broadcast to all connections
var message = Encoding.UTF8.GetBytes("Server announcement");
int sentCount = await server.BroadcastAsync(message);
Console.WriteLine($"Broadcast sent to {sentCount} clients");

// Send to specific connection
bool sent = await server.SendAsync("connection-id-123", message);
```

### Event Handling

```csharp
// Subscribe to events
server.StateChanged += (sender, e) =>
{
    Console.WriteLine($"Server state: {e.OldState} -> {e.NewState}");
};

server.ClientConnected += (sender, e) =>
{
    Console.WriteLine($"Client connected: {e.Channel.Id}");
};

server.ClientDisconnected += (sender, e) =>
{
    Console.WriteLine($"Client disconnected: {e.Channel.Id}");
};
```

### Graceful Shutdown

```csharp
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await server.StartAsync(cts.Token);
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Shutting down...");
}
finally
{
    await server.StopAsync();
    Console.WriteLine("Server stopped");
}
```

## Configuration Options

### UnifiedServerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Transports` | `List<TransportChannelConfiguration>` | Required | Transport configurations (at least one required) |
| `MessageReceiver` | `MessageReceiverOptions` | `new()` | Message receiver component options |
| `MessageDispatcher` | `MessageDispatcherOptions` | `new()` | Message dispatcher component options |
| `ResponseTransmitter` | `ResponseTransmitterOptions` | `new()` | Response transmitter component options |
| `ConnectionManager` | `ConnectionManagerOptions` | `new()` | Connection manager component options |
| `ServiceRegistry` | `ServiceRegistryOptions` | `new()` | Service registry component options |
| `BackpressurePolicy` | `BackpressurePolicyOptions` | `new()` | Backpressure policy component options |
| `DefaultOperationTimeout` | `TimeSpan` | 30 seconds | Default timeout for operations |
| `MaxConcurrentOperations` | `int` | 1000 | Maximum concurrent operations |
| `EnableDetailedLogging` | `bool` | false | Enable detailed debug logging |

### Validation Rules

- At least one transport must be configured
- Exactly one transport must be marked as default (`IsDefault = true`)
- Transport names must be unique
- Transport ports must be in range 1-65535
- DefaultOperationTimeout must be positive
- MaxConcurrentOperations must be greater than zero

## Server States

| State | Description |
|-------|-------------|
| `Stopped` | Server is not running |
| `Starting` | Server is initializing transports |
| `Running` | Server is active and accepting connections |
| `Stopping` | Server is shutting down gracefully |

State transitions:
```
Stopped → Starting → Running → Stopping → Stopped
```

## Thread Safety

UnifiedPulseServer is fully thread-safe:
- Multiple concurrent `StartAsync()` calls are safe (idempotent)
- Multiple concurrent `StopAsync()` calls are safe (idempotent)
- State transitions use lock-based synchronization
- Service registration is thread-safe
- Connection tracking is thread-safe

## Performance Tips

1. **Worker Count**: Match to CPU cores for CPU-bound services
   ```csharp
   options.MessageDispatcher = new MessageDispatcherOptions
   {
       WorkerCount = Environment.ProcessorCount
   };
   ```

2. **Backpressure**: Adjust thresholds based on expected load
   ```csharp
   options.BackpressurePolicy = new BackpressurePolicyOptions
   {
       ThrottleThreshold = 0.7,  // 70% queue utilization
       RejectThreshold = 0.9     // 90% queue utilization
   };
   ```

3. **Connection Limits**: Configure based on expected concurrency
   ```csharp
   options.ConnectionManager = new ConnectionManagerOptions
   {
       MaxConnections = 10000
   };
   ```

## Migration from PulseServer/ServerHost

See migration guides:
- [Migrating from PulseServer](docs/migration-pulseserver.md)
- [Migrating from ServerHost](docs/migration-serverhost.md)

## Architecture

UnifiedPulseServer adopts a transport-focused architecture:

```
UnifiedPulseServer
├── TransportIntegrationManager (manages listeners)
├── ServerChannelManager (manages connections)
├── MessageDispatcher (pipeline stage 1)
├── ServiceRegistry (service lifecycle)
└── ResponseTransmitter (pipeline stage 2)
```

Event flow:
```
Transport accepts connection
  → OnConnectionAccepted (non-blocking)
    → ChannelManager.AddChannel
      → ClientConnected event raised
```

## Disposal

UnifiedPulseServer implements both `IDisposable` and `IAsyncDisposable`:

```csharp
// Synchronous disposal
using (var server = new UnifiedPulseServer(...))
{
    await server.StartAsync();
    // ... use server
} // Automatically stops and disposes

// Asynchronous disposal
await using (var server = new UnifiedPulseServer(...))
{
    await server.StartAsync();
    // ... use server
} // Automatically stops and disposes
```

## Troubleshooting

### Server won't start

**Problem**: `InvalidOperationException: No transports configured`

**Solution**: Ensure at least one transport is configured in options:
```csharp
options.Transports.Add(
    TransportChannelConfiguration.Tcp("tcp", 8080, isDefault: true));
```

### Multiple default transports error

**Problem**: `InvalidOperationException: Exactly one transport must be marked as default`

**Solution**: Mark only one transport as default:
```csharp
options.Transports = new List<TransportChannelConfiguration>
{
    TransportChannelConfiguration.Tcp("tcp", 8080, isDefault: true),
    TransportChannelConfiguration.Kcp("kcp", 9090, isDefault: false)
};
```

### Port already in use

**Problem**: `SocketException: Address already in use`

**Solution**: Change port or use port 0 for random assignment:
```csharp
TransportChannelConfiguration.Tcp("tcp", 0, isDefault: true) // Random port
```

## See Also

- [Specification](../../specs/006-unify-dual-server/spec.md)
- [Architecture Design](../../specs/006-unify-dual-server/research.md)
- [Data Model](../../specs/006-unify-dual-server/data-model.md)
- [API Contracts](../../specs/006-unify-dual-server/contracts/)
