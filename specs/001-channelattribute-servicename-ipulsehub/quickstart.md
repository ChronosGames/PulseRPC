# Quickstart: ServiceName-Based Thread Scheduling

**Feature**: 001-channelattribute-servicename-ipulsehub
**Audience**: Developers implementing or testing the feature

## Overview

This guide demonstrates how to use ServiceName-based thread scheduling in PulseRPC to ensure thread-affinity for stateful services.

## Prerequisites

- .NET 9.0+ SDK installed
- PulseRPC.Server and PulseRPC.Abstractions packages referenced
- Understanding of IPulseHub and ChannelAttribute

## Step 1: Define Your Service with ServiceName

Use the `ChannelAttribute` to specify a `ServiceName` for your hub interface:

```csharp
using PulseRPC;

[Channel("player-channel", ServiceName = "PlayerService")]
public interface IPlayerHub : IPulseHub
{
    Task HandlePlayerLogin(string playerId);
    Task ProcessPlayerAction(PlayerAction action);
    Task HandlePlayerLogout();
}
```

**Key Points**:
- `ServiceName` is the logical identifier for thread-affinity scheduling
- All service instances with the same `ServiceName` share a dedicated thread pool
- The source generator extracts `ServiceName` at compile-time

## Step 2: Configure the Scheduler

Configure the scheduler in your server setup:

```csharp
using PulseRPC.Server;
using PulseRPC.Server.Scheduling;

var builder = PulseServer.CreateBuilder();

// Configure the thread scheduler
builder.ConfigureScheduler(options =>
{
    options.InitialThreadCount = Environment.ProcessorCount;
    options.MaxThreadCount = Environment.ProcessorCount * 2;
    options.ChannelCapacity = 1024;
    options.EnableMetrics = true;
});

var server = builder.Build();
await server.StartAsync();
```

**Configuration Options**:
- `InitialThreadCount`: Starting thread pool size
- `MaxThreadCount`: Maximum threads (for scaling)
- `ChannelCapacity`: Bounded channel size per thread
- `EnableMetrics`: Enable performance monitoring

## Step 3: Set ServiceId During Authentication

Implement authentication to set the `ServiceId` in the service context:

```csharp
using PulseRPC.Scheduling;

public class PlayerAuthenticationHandler : IAuthenticationHandler
{
    public async Task<bool> AuthenticateAsync(IServiceContext context, AuthRequest request)
    {
        // Validate credentials
        var playerId = ValidatePlayerToken(request.Token);

        if (playerId != null)
        {
            // Set ServiceId for scheduling
            context.ServiceId = playerId;
            return true;
        }

        return false;
    }
}
```

**Key Points**:
- `ServiceId` uniquely identifies the service instance (e.g., player ID, session ID)
- Combined with `ServiceName`, it forms the `ServiceSchedulingKey`
- Must be set before service methods are invoked

## Step 4: Implement Your Service

Implement your hub service with stateful operations:

```csharp
public class PlayerHub : IPlayerHub
{
    private readonly IServiceContext _context;
    private readonly ILogger<PlayerHub> _logger;

    // Service-local state - safe because of thread-affinity
    private PlayerState? _playerState;

    public PlayerHub(IServiceContext context, ILogger<PlayerHub> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task HandlePlayerLogin(string playerId)
    {
        // This runs on a dedicated thread for this ServiceName+ServiceId
        _playerState = await LoadPlayerState(playerId);
        _logger.LogInformation("Player {PlayerId} logged in on thread {ThreadId}",
            playerId, Environment.CurrentManagedThreadId);
    }

    public async Task ProcessPlayerAction(PlayerAction action)
    {
        // Sequential execution guaranteed - no race conditions
        _playerState!.ApplyAction(action);
        await SavePlayerState(_playerState);
    }

    public async Task HandlePlayerLogout()
    {
        await SavePlayerState(_playerState);
        _playerState = null;
    }
}
```

**Key Points**:
- Service-local state (`_playerState`) is safe due to sequential execution
- No locks or synchronization needed within the same `ServiceSchedulingKey`
- All methods for the same player execute on the same thread

## Step 5: Verify Thread Affinity (Testing)

Write tests to verify correct thread scheduling:

```csharp
using Xunit;
using FluentAssertions;
using PulseRPC.Scheduling;

public class ServiceSchedulingTests
{
    [Fact]
    public async Task SameServiceNameAndId_ExecutesOnSameThread()
    {
        // Arrange
        var scheduler = CreateScheduler();
        var key = new ServiceSchedulingKey("PlayerService", "player123");
        var threadIds = new ConcurrentBag<int>();

        // Act: Schedule 10 operations for the same key
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => scheduler.ScheduleAsync(key, async () =>
            {
                threadIds.Add(Environment.CurrentManagedThreadId);
                await Task.Delay(10);
            }));

        await Task.WhenAll(tasks);

        // Assert: All operations ran on the same thread
        threadIds.Distinct().Should().HaveCount(1);
    }

    [Fact]
    public async Task DifferentServiceIds_ExecuteOnDifferentThreads()
    {
        // Arrange
        var scheduler = CreateScheduler();
        var key1 = new ServiceSchedulingKey("PlayerService", "player123");
        var key2 = new ServiceSchedulingKey("PlayerService", "player456");
        var threadIds = new ConcurrentDictionary<string, int>();

        // Act
        await Task.WhenAll(
            scheduler.ScheduleAsync(key1, async () =>
            {
                threadIds[key1.ServiceId] = Environment.CurrentManagedThreadId;
                await Task.Delay(10);
            }),
            scheduler.ScheduleAsync(key2, async () =>
            {
                threadIds[key2.ServiceId] = Environment.CurrentManagedThreadId;
                await Task.Delay(10);
            })
        );

        // Assert: Different ServiceIds may use different threads
        threadIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task MissingServiceId_ThrowsException()
    {
        // Arrange
        var scheduler = CreateScheduler();
        var invalidKey = new ServiceSchedulingKey("PlayerService", null!);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            scheduler.ScheduleAsync(invalidKey, async () => await Task.CompletedTask)
        );
    }
}
```

## Step 6: Monitor Performance

Access scheduler metrics for monitoring:

```csharp
using PulseRPC.Scheduling;

public class SchedulerMonitor
{
    private readonly IServiceScheduler _scheduler;
    private readonly ILogger _logger;

    public async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var metrics = _scheduler.GetMetrics();

            _logger.LogInformation(
                "Scheduler Metrics: ActiveThreads={ActiveThreads}, " +
                "QueuedMessages={QueuedMessages}, " +
                "P95Latency={P95Latency}ms, " +
                "DroppedMessages={DroppedMessages}",
                metrics.ActiveThreadCount,
                metrics.TotalQueuedMessages,
                metrics.P95LatencyMs,
                metrics.DroppedMessageCount
            );

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }
}
```

## Expected Behavior

### ✅ Correct Usage

1. **Same ServiceName+ServiceId**: All operations execute sequentially on the same thread
2. **Different ServiceIds**: Operations can execute concurrently on different threads
3. **ServiceId Set**: Scheduler routes to correct thread
4. **Metrics Enabled**: Performance data available via `GetMetrics()`

### ❌ Error Scenarios

1. **Missing ServiceId**: Throws `InvalidOperationException` with clear message
2. **Channel Full**: Blocks/waits (with optional L3 degradation)
3. **Scheduler Not Started**: Throws `InvalidOperationException`

## Performance Validation

Run benchmarks to validate constitutional requirements:

```bash
cd perf/BenchmarkApp
dotnet run -c Release -- --benchmark SchedulingBenchmarks

# Expected Results (per constitution):
# - P95 Latency: < 50ms
# - Throughput: > 100 QPS
# - Success Rate: > 99.5%
```

## Troubleshooting

### ServiceId Not Set

**Error**: `InvalidOperationException: ServiceId not set for ServiceName 'PlayerService'`

**Solution**: Ensure authentication handler sets `context.ServiceId` before service invocation.

### Thread Contention

**Symptom**: High P95 latency, dropped messages

**Solution**: Increase `MaxThreadCount` or `ChannelCapacity` in configuration.

### Memory Pressure

**Symptom**: High GC pressure, OutOfMemoryException

**Solution**: Reduce `ChannelCapacity` or `MaxThreadCount` to limit memory usage.

## Next Steps

- Read the [Data Model](data-model.md) for detailed entity descriptions
- Review [Research Report](research.md) for architectural decisions
- Implement following the [Implementation Plan](plan.md)
- Run the full test suite to validate behavior

## Summary

This quickstart demonstrates:
- ✅ Defining services with `ServiceName` via `ChannelAttribute`
- ✅ Configuring the scheduler with appropriate thread pool settings
- ✅ Setting `ServiceId` during authentication for routing
- ✅ Implementing stateful services with thread-affinity guarantees
- ✅ Testing thread scheduling behavior
- ✅ Monitoring performance metrics

**Key Takeaway**: ServiceName-based thread scheduling enables safe, stateful service implementations without manual synchronization, while maintaining high performance through dedicated thread pools and System.Threading.Channels.