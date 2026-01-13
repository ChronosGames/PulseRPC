using PulseRPC.Scheduling;

namespace PulseRPC.Server.Services.Scheduling;

/// <summary>
/// Encapsulates a unit of work to be executed on a worker thread.
/// </summary>
public readonly struct WorkItem
{
    /// <summary>
    /// Gets the service scheduling key identifying which service instance this work belongs to.
    /// </summary>
    public ServiceSchedulingKey Key { get; }

    /// <summary>
    /// Gets the async work to execute.
    /// </summary>
    public Func<Task> Work { get; }

    /// <summary>
    /// Gets the time when this work was enqueued (for latency metrics).
    /// </summary>
    public DateTimeOffset EnqueuedTime { get; }

    /// <summary>
    /// Gets the priority level of this work item (for L3 degradation).
    /// </summary>
    public MessagePriority Priority { get; }

    /// <summary>
    /// Initializes a new WorkItem.
    /// </summary>
    /// <param name="key">The service scheduling key.</param>
    /// <param name="work">The async work to execute.</param>
    /// <param name="priority">The message priority (default: Normal).</param>
    /// <exception cref="ArgumentNullException">If work is null.</exception>
    public WorkItem(ServiceSchedulingKey key, Func<Task> work, MessagePriority priority = MessagePriority.Normal)
    {
        Key = key;
        Work = work ?? throw new ArgumentNullException(nameof(work));
        EnqueuedTime = DateTimeOffset.UtcNow;
        Priority = priority;
    }
}