// Contract: IServiceScheduler Interface
// Purpose: Core scheduler abstraction for ServiceName-based thread scheduling
// Location: PulseRPC.Abstractions/Scheduling/IServiceScheduler.cs

namespace PulseRPC.Scheduling;

/// <summary>
/// Scheduler for routing service invocations to dedicated threads based on ServiceName+ServiceId.
/// </summary>
public interface IServiceScheduler
{
    /// <summary>
    /// Schedule work for execution on the thread assigned to the given key.
    /// </summary>
    /// <param name="key">The ServiceName+ServiceId composite key identifying the service instance.</param>
    /// <param name="work">The async work to execute.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task that completes when the work has been queued (not necessarily executed).</returns>
    /// <exception cref="ArgumentNullException">If key or work is null.</exception>
    /// <exception cref="InvalidOperationException">If ServiceId is not set in the key.</exception>
    /// <exception cref="ObjectDisposedException">If the scheduler has been disposed.</exception>
    Task ScheduleAsync(ServiceSchedulingKey key, Func<Task> work, CancellationToken cancellationToken = default);

    /// <summary>
    /// Start the scheduler and its worker threads.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the start operation.</param>
    /// <returns>A task that completes when the scheduler is fully started.</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the scheduler gracefully, completing in-flight work.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the stop operation.</param>
    /// <returns>A task that completes when the scheduler has stopped.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the scheduler is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the current metrics snapshot for monitoring.
    /// </summary>
    SchedulerMetrics GetMetrics();
}