# Quickstart: Unified Server Implementation

**Date**: 2025-10-16
**Feature**: 006-unify-dual-server
**Purpose**: Quick reference guide for using the unified server implementation

---

## Overview

The UnifiedPulseServer consolidates the functionality of both PulseServer and ServerHost into a single, cohesive implementation. This guide shows how to get started with the new unified API and migrate from existing implementations.

---

## Basic Usage

### 1. Simple TCP Server

```csharp
using PulseRPC.Server;
using Microsoft.Extensions.Logging;

// Create logger factory
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// Configure server options
var options = new UnifiedServerOptions
{
    Transports = new List<TransportChannelConfiguration>
    {
        TransportChannelConfiguration.Tcp("tcp", 8080, isDefault: true)
    }
};

// Create and start server
var server = new UnifiedPulseServer(
    loggerFactory,
    Options.Create(options));

// Register a service
server.RegisterService("MyService", new MyServiceImpl());

// Start server
await server.StartAsync();

Console.WriteLine("Server running on port 8080. Press Enter to stop...");
Console.ReadLine();

// Stop server gracefully
await server.StopAsync();
```

---

### 2. Multi-Transport Server (TCP + KCP)

```csharp
var options = new UnifiedServerOptions
{
    Transports = new List<TransportChannelConfiguration>
    {
        TransportChannelConfiguration.Tcp("tcp", 8080, isDefault: true),
        TransportChannelConfiguration.Kcp("kcp", 9090)
    }
};

var server = new UnifiedPulseServer(
    loggerFactory,
    Options.Create(options));

server.RegisterService("GameService", new GameServiceImpl());
server.RegisterService("ChatService", new ChatServiceImpl());

await server.StartAsync();

// Server now listens on:
// - TCP port 8080 (default)
// - KCP port 9090
```

---

### 3. ASP.NET Core Integration

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Configure unified server options
builder.Services.Configure<UnifiedServerOptions>(options =>
{
    options.Transports = new List<TransportChannelConfiguration>
    {
        TransportChannelConfiguration.Tcp("tcp", 8080, isDefault: true)
    };
    options.DefaultOperationTimeout = TimeSpan.FromSeconds(60);
});

// Register unified server as hosted service
builder.Services.AddUnifiedPulseServer(options =>
{
    options.Transports.Add(
        TransportChannelConfiguration.Tcp("tcp", 8080, isDefault: true));
});

var app = builder.Build();

// Server starts automatically with ASP.NET Core host
await app.RunAsync();
```

---

### 4. Advanced Configuration

```csharp
var options = new UnifiedServerOptions
{
    // Transport configuration
    Transports = new List<TransportChannelConfiguration>
    {
        TransportChannelConfiguration.Tcp("tcp", 8080, tcpOptions =>
        {
            tcpOptions.KeepAlive = true;
            tcpOptions.NoDelay = true;
        }, isDefault: true)
    },

    // Pipeline configuration
    MessageDispatcher = new MessageDispatcherOptions
    {
        WorkerCount = 4,
        MaxQueueDepthPerPriority = 2000
    },

    BackpressurePolicy = new BackpressurePolicyOptions
    {
        ThrottleThreshold = 0.7,  // 70% queue utilization
        RejectThreshold = 0.9      // 90% queue utilization
    },

    ConnectionManager = new ConnectionManagerOptions
    {
        MaxConnections = 5000,
        ConnectionTimeout = TimeSpan.FromMinutes(5)
    },

    // General options
    DefaultOperationTimeout = TimeSpan.FromSeconds(30),
    MaxConcurrentOperations = 10000,
    EnableDetailedLogging = true
};

var server = new UnifiedPulseServer(
    loggerFactory,
    Options.Create(options));
```

---

## Migration Guide

### Migrating from PulseServer

**Before (PulseServer)**:
```csharp
var server = new PulseServer(
    loggerFactory,
    serverOptions,
    channelManager,
    transportManager);

server.AddTransport(TransportChannelConfiguration.Tcp("tcp", 8080));
server.AddTransport(TransportChannelConfiguration.Kcp("kcp", 9090));

await server.StartAsync();
```

**After (UnifiedPulseServer)**:
```csharp
var options = new UnifiedServerOptions
{
    Transports = new List<TransportChannelConfiguration>
    {
        TransportChannelConfiguration.Tcp("tcp", 8080, isDefault: true),
        TransportChannelConfiguration.Kcp("kcp", 9090)
    }
};

var server = new UnifiedPulseServer(
    loggerFactory,
    Options.Create(options));

await server.StartAsync();
```

**Key Changes**:
- Transports configured in options instead of AddTransport() calls
- Dependency injection of channelManager and transportManager now automatic
- Options use Options pattern (IOptions<T>)

---

### Migrating from ServerHost

**Before (ServerHost)**:
```csharp
var transport = new TcpServerTransport(/* ... */);

var options = new ServerHostOptions
{
    MessageDispatcherOptions = new() { WorkerCount = 4 },
    // ... other pipeline options
};

var host = new ServerHost(transport, options);
host.RegisterService("MyService", serviceInstance);
await host.StartAsync();
```

**After (UnifiedPulseServer)**:
```csharp
var options = new UnifiedServerOptions
{
    Transports = new List<TransportChannelConfiguration>
    {
        TransportChannelConfiguration.Tcp("tcp", 8080, isDefault: true)
    },
    MessageDispatcher = new MessageDispatcherOptions { WorkerCount = 4 },
    // ... other pipeline options (same as before)
};

var server = new UnifiedPulseServer(
    loggerFactory,
    Options.Create(options));

server.RegisterService("MyService", serviceInstance);
await server.StartAsync();
```

**Key Changes**:
- Transport no longer passed as constructor parameter
- Transport configured in options.Transports
- Pipeline options remain the same
- RegisterService() API unchanged

---

## Deprecation Timeline

| Version | PulseServer/ServerHost Status | Action Required |
|---------|-------------------------------|-----------------|
| **v2.x** (Current) | Deprecated with warnings | Optional - migrate when convenient |
| **v3.0** (Next major) | Compilation errors | **Required** - must migrate before upgrading |
| **v4.0** (Future) | Removed | N/A |

---

## Common Patterns

### 1. Service Registration with Options

```csharp
var serviceOptions = new ServiceOptions
{
    DefaultTimeout = TimeSpan.FromSeconds(60),
    Priority = MessagePriority.High
};

server.RegisterService("CriticalService", serviceInstance, serviceOptions);
```

---

### 2. Monitoring Server Health

```csharp
// Get performance metrics
var metrics = server.GetPerformanceMetrics();
Console.WriteLine($"Active Connections: {metrics.ActiveConnections}");
Console.WriteLine($"Messages Processed: {metrics.TotalMessagesProcessed}");
Console.WriteLine($"Average Latency: {metrics.AverageLatencyMs}ms");

// Get transport information
var transports = server.GetTransports();
foreach (var (name, info) in transports)
{
    Console.WriteLine($"Transport: {name} ({info.Type}:{info.Port}) - Listening: {info.IsListening}");
}

// Get active connections
var connections = server.GetActiveConnections();
foreach (var conn in connections)
{
    Console.WriteLine($"Connection: {conn.ConnectionId} from {conn.RemoteEndPoint}");
}
```

---

### 3. Broadcasting Messages

```csharp
// Broadcast to all connections
var message = Encoding.UTF8.GetBytes("Hello, everyone!");
var sentCount = await server.BroadcastAsync(message);
Console.WriteLine($"Message sent to {sentCount} clients");

// Broadcast with filter (e.g., only authenticated clients)
sentCount = await server.BroadcastAsync(
    message,
    filter: ctx => ctx.IsAuthenticated);
```

---

### 4. Graceful Shutdown

```csharp
// Handle Ctrl+C for graceful shutdown
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await server.StartAsync(cts.Token);

    Console.WriteLine("Server started. Press Ctrl+C to stop...");

    // Wait for cancellation
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Shutdown requested, stopping server...");
}
finally
{
    await server.StopAsync();
    Console.WriteLine("Server stopped gracefully");
}
```

---

## Performance Tips

### 1. Optimize Worker Count

```csharp
// Match worker count to CPU cores for CPU-bound services
var workerCount = Environment.ProcessorCount;

options.MessageDispatcher = new MessageDispatcherOptions
{
    WorkerCount = workerCount
};
```

---

### 2. Configure Backpressure

```csharp
// Adjust thresholds based on expected load
options.BackpressurePolicy = new BackpressurePolicyOptions
{
    ThrottleThreshold = 0.7,  // Start rejecting some requests at 70%
    RejectThreshold = 0.9,    // Reject all new requests at 90%
    HysteresisBand = 0.1      // 10% buffer to prevent oscillation
};
```

---

### 3. Connection Pooling

```csharp
options.ConnectionManager = new ConnectionManagerOptions
{
    MaxConnections = 10000,            // Increase for high-traffic scenarios
    ConnectionTimeout = TimeSpan.FromMinutes(5),
    EnableAutoCleanup = true,
    CleanupInterval = TimeSpan.FromSeconds(30)
};
```

---

## Troubleshooting

### Server Won't Start

**Problem**: `InvalidOperationException: No transports configured`

**Solution**: Ensure at least one transport is configured in options:
```csharp
options.Transports.Add(
    TransportChannelConfiguration.Tcp("tcp", 8080, isDefault: true));
```

---

### Multiple Default Transports Error

**Problem**: `InvalidOperationException: Exactly one transport must be marked as default`

**Solution**: Mark only one transport as default:
```csharp
options.Transports = new List<TransportChannelConfiguration>
{
    TransportChannelConfiguration.Tcp("tcp", 8080, isDefault: true),   // Default
    TransportChannelConfiguration.Kcp("kcp", 9090, isDefault: false)  // Not default
};
```

---

### Port Already in Use

**Problem**: `SocketException: Address already in use`

**Solution**: Change the port number or stop the process using the port:
```bash
# Windows
netstat -ano | findstr :8080
taskkill /PID <process_id> /F

# Linux
lsof -i :8080
kill -9 <process_id>
```

---

## Next Steps

1. **Read the Full Spec**: See [spec.md](./spec.md) for complete requirements and design decisions
2. **Review Architecture**: See [research.md](./research.md) for detailed architecture analysis
3. **Understand Data Model**: See [data-model.md](./data-model.md) for entity relationships
4. **Explore API Contracts**: See [contracts/](./contracts/) for interface definitions
5. **Check Examples**: See `examples/BasicServerDI/` for a working example

---

## Support

- **Migration Issues**: See migration guides in `docs/` directory
- **API Questions**: Check API contracts in `contracts/` directory
- **Bug Reports**: File issues on GitHub
- **Performance Questions**: Review `research.md` for optimization patterns

---

## Summary

The UnifiedPulseServer provides:
- ✅ Single, clear API entry point
- ✅ Consolidated configuration
- ✅ Transport-focused architecture
- ✅ Full pipeline integration
- ✅ Backward compatibility via facades
- ✅ ASP.NET Core integration
- ✅ Comprehensive monitoring

Start with the basic examples and gradually adopt advanced features as needed. The unified server maintains all functionality from both PulseServer and ServerHost while providing a cleaner, more maintainable API.
