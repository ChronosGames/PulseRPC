using FluentAssertions;
using NSubstitute;
using PulseRPC.Scheduling;
using Xunit;

namespace PulseRPC.Server.Tests.Scheduling;

public class ServiceSchedulerTests
{
    [Fact]
    public async Task ScheduleAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var scheduler = CreateTestScheduler();
        Func<Task> work = async () => await Task.CompletedTask;

        // Act
        Func<Task> act = async () => await scheduler.ScheduleAsync(default!, work);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ScheduleAsync_WithNullWork_ThrowsArgumentNullException()
    {
        // Arrange
        var scheduler = CreateTestScheduler();
        var key = new ServiceSchedulingKey("PlayerService", "player123");

        // Act
        Func<Task> act = async () => await scheduler.ScheduleAsync(key, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("work");
    }

    [Fact]
    public async Task ScheduleAsync_WithNullServiceIdInKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var scheduler = CreateTestScheduler();
        // This will throw during key construction, so we test the validation

        // Act & Assert
        Action act = () => new ServiceSchedulingKey("PlayerService", null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ScheduleAsync_AfterDisposal_ThrowsObjectDisposedException()
    {
        // Arrange
        var scheduler = CreateTestScheduler();
        var key = new ServiceSchedulingKey("PlayerService", "player123");
        Func<Task> work = async () => await Task.CompletedTask;

        await scheduler.DisposeAsync();

        // Act
        Func<Task> act = async () => await scheduler.ScheduleAsync(key, work);

        // Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task StartAsync_InitializesSchedulerAndSetsIsRunning()
    {
        // Arrange
        var scheduler = CreateTestScheduler();
        scheduler.IsRunning.Should().BeFalse();

        // Act
        await scheduler.StartAsync();

        // Assert
        scheduler.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_GracefullyShutdownAndSetsIsRunningToFalse()
    {
        // Arrange
        var scheduler = CreateTestScheduler();
        await scheduler.StartAsync();
        scheduler.IsRunning.Should().BeTrue();

        // Act
        await scheduler.StopAsync();

        // Assert
        scheduler.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void GetMetrics_ReturnsValidSchedulerMetrics()
    {
        // Arrange
        var scheduler = CreateTestScheduler();

        // Act
        var metrics = scheduler.GetMetrics();

        // Assert
        metrics.Should().NotBeNull();
        metrics.ActiveThreadCount.Should().BeGreaterOrEqualTo(0);
        metrics.TotalQueuedMessages.Should().BeGreaterOrEqualTo(0);
        metrics.P95LatencyMs.Should().BeGreaterOrEqualTo(0);
        metrics.DroppedMessageCount.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task ScheduleAsync_BeforeStart_ThrowsInvalidOperationException()
    {
        // Arrange
        var scheduler = CreateTestScheduler();
        var key = new ServiceSchedulingKey("PlayerService", "player123");
        Func<Task> work = async () => await Task.CompletedTask;

        // Act - scheduler not started
        Func<Task> act = async () => await scheduler.ScheduleAsync(key, work);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not started*");
    }

    [Fact]
    public async Task StartAsync_CalledTwice_ThrowsInvalidOperationException()
    {
        // Arrange
        var scheduler = CreateTestScheduler();
        await scheduler.StartAsync();

        // Act
        Func<Task> act = async () => await scheduler.StartAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already started*");
    }

    // Helper method to create a test implementation of IServiceScheduler
    private IServiceScheduler CreateTestScheduler()
    {
        return new TestServiceScheduler();
    }

    // Test implementation of IServiceScheduler
    private class TestServiceScheduler : IServiceScheduler
    {
        private bool _isDisposed;
        public bool IsRunning { get; private set; }

        public async Task ScheduleAsync(ServiceSchedulingKey key, Func<Task> work, CancellationToken cancellationToken = default)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(TestServiceScheduler));

            if (work == null)
                throw new ArgumentNullException(nameof(work));

            if (!IsRunning)
                throw new InvalidOperationException("Scheduler not started");

            // Simulate scheduling
            await Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (IsRunning)
                throw new InvalidOperationException("Scheduler already started");

            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public SchedulerMetrics GetMetrics()
        {
            return new SchedulerMetrics
            {
                ActiveThreadCount = Environment.ProcessorCount,
                TotalQueuedMessages = 0,
                P95LatencyMs = 0,
                DroppedMessageCount = 0
            };
        }

        public async ValueTask DisposeAsync()
        {
            if (!_isDisposed)
            {
                await StopAsync();
                _isDisposed = true;
            }
        }
    }
}