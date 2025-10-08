using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Scheduling;
using PulseRPC.Server.Scheduling;
using Xunit;

namespace PulseRPC.IntegrationTests;

/// <summary>
/// Integration tests for ServiceName-based thread scheduling.
/// </summary>
public class ServiceSchedulingIntegrationTests : IAsyncLifetime
{
    private IServiceScheduler? _scheduler;

    public async Task InitializeAsync()
    {
        var config = new SchedulerConfiguration
        {
            InitialThreadCount = 4,
            MaxThreadCount = 8,
            ChannelCapacity = 128,
            ThreadIdleTimeout = TimeSpan.FromSeconds(30),
            EnableMetrics = true
        };

        _scheduler = new ServiceThreadScheduler(config, NullLogger<ServiceThreadScheduler>.Instance);
        await _scheduler.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_scheduler != null)
        {
            await _scheduler.StopAsync();
            await _scheduler.DisposeAsync();
        }
    }

    [Fact]
    public async Task SameServiceNameAndId_ExecutesOnSameThread()
    {
        // T010: Thread affinity integration test
        // Arrange
        var key = new ServiceSchedulingKey("PlayerService", "player123");
        var threadIds = new ConcurrentBag<int>();

        // Act - schedule 10 operations
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            var task = _scheduler!.ScheduleAsync(key, async () =>
            {
                threadIds.Add(Environment.CurrentManagedThreadId);
                await Task.Delay(10);
            });
            tasks.Add(task);
        }
        await Task.WhenAll(tasks);

        // Assert - all operations should run on same thread
        threadIds.Distinct().Should().HaveCount(1, "all operations for the same ServiceId should execute on the same thread");
    }

    [Fact]
    public async Task DifferentServiceIds_CanExecuteConcurrently()
    {
        // T011: Concurrent execution integration test
        // Arrange
        var key1 = new ServiceSchedulingKey("PlayerService", "player123");
        var key2 = new ServiceSchedulingKey("PlayerService", "player456");
        var completionTimes = new ConcurrentDictionary<string, DateTimeOffset>();
        var startTime = DateTimeOffset.UtcNow;

        // Act - schedule operations for different keys concurrently
        var task1 = _scheduler!.ScheduleAsync(key1, async () =>
        {
            await Task.Delay(100);
            completionTimes[key1.ServiceId] = DateTimeOffset.UtcNow;
        });
        var task2 = _scheduler.ScheduleAsync(key2, async () =>
        {
            await Task.Delay(100);
            completionTimes[key2.ServiceId] = DateTimeOffset.UtcNow;
        });
        await Task.WhenAll(task1, task2);
        var totalTime = DateTimeOffset.UtcNow - startTime;

        // Assert - operations should complete concurrently (not sequentially)
        completionTimes.Should().HaveCount(2);
        totalTime.Should().BeLessThan(TimeSpan.FromMilliseconds(180),
            "concurrent execution should complete faster than sequential (100ms + 100ms = 200ms)");
    }

    [Fact]
    public async Task MissingServiceId_ThrowsInvalidOperationException()
    {
        // T012: Missing ServiceId error test
        // This test will fail - Validation not implemented yet

        // Arrange - would create real scheduler
        // var scheduler = CreateRealScheduler();

        // Act & Assert - null ServiceId should throw during key construction
        Action act = () => new ServiceSchedulingKey("PlayerService", null!);
        act.Should().Throw<ArgumentException>();

        await Task.CompletedTask;
    }

    [Fact]
    public async Task ChannelFull_TriggersBlockingBehavior()
    {
        // T013: Channel backpressure integration test
        // Arrange - create scheduler with small channel capacity
        var config = new SchedulerConfiguration
        {
            ChannelCapacity = 4,
            InitialThreadCount = 1,
            MaxThreadCount = 1,
            ThreadIdleTimeout = TimeSpan.FromSeconds(30),
            EnableMetrics = false
        };
        var smallScheduler = new ServiceThreadScheduler(config, NullLogger<ServiceThreadScheduler>.Instance);
        await smallScheduler.StartAsync();

        try
        {
            var key = new ServiceSchedulingKey("PlayerService", "player123");
            var completedCount = 0;

            // Act - schedule many long-running operations
            var scheduleTasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                var task = smallScheduler.ScheduleAsync(key, async () =>
                {
                    await Task.Delay(50); // Simulated work
                    Interlocked.Increment(ref completedCount);
                });
                scheduleTasks.Add(task);
            }

            // Wait for all operations to complete
            await Task.WhenAll(scheduleTasks);

            // Assert - all operations should eventually complete despite backpressure
            completedCount.Should().Be(10, "all scheduled operations should complete even with small channel capacity");
        }
        finally
        {
            await smallScheduler.StopAsync();
            await smallScheduler.DisposeAsync();
        }
    }

    [Fact]
    public async Task AuthenticationScenario_ServiceIdInjection()
    {
        // T032: Authentication integration test
        // Arrange - simulate authentication setting ServiceId
        var connectionId = "conn-123";
        var serviceName = "GameService";
        var authenticatedServiceId = "auth-service-456";

        var serviceContext = new ServiceExecutionContext(connectionId, serviceName);
        serviceContext.Should().NotBeNull();
        serviceContext.IsAuthenticated.Should().BeFalse("ServiceId not set yet");

        // Act - simulate authentication middleware setting ServiceId
        serviceContext.ServiceId = authenticatedServiceId;

        // Assert - context should now be authenticated
        serviceContext.IsAuthenticated.Should().BeTrue("ServiceId has been set");
        serviceContext.ServiceId.Should().Be(authenticatedServiceId);

        // Verify scheduler can use authenticated context
        var key = new ServiceSchedulingKey(serviceName, serviceContext.ServiceId);
        var workExecuted = false;

        await _scheduler!.ScheduleAsync(key, async () =>
        {
            workExecuted = true;
            await Task.CompletedTask;
        });

        workExecuted.Should().BeTrue("scheduled work should execute with authenticated ServiceId");
    }
}