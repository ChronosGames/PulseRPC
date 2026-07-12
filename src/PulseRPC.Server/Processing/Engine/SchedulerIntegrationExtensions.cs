using PulseRPC.Scheduling;
using PulseRPC.Server.Services.Scheduling;

namespace PulseRPC.Server.Processing.Engine;

/// <summary>
/// Legacy helper retained for compatibility. The fixed-shard MessageEngine does not
/// resolve or invoke IServiceScheduler.
/// </summary>
[Obsolete("IServiceScheduler is not connected to the fixed-shard server runtime. Configure MessageWorkerShardCount and MessageQueueCapacityPerShard on PulseServerOptions.", false)]
public static class SchedulerIntegrationExtensions
{
    /// <summary>
    /// Invokes a delegate through a manually supplied standalone scheduler.
    /// This helper is not part of the MessageEngine pipeline.
    /// </summary>
    /// <param name="scheduler">The service scheduler (optional, null-safe).</param>
    /// <param name="serviceContext">The service context containing ServiceId.</param>
    /// <param name="serviceName">The service name from ChannelAttribute or interface name.</param>
    /// <param name="serviceInvocation">The actual service method invocation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the scheduled or direct invocation.</returns>
    [Obsolete("This helper is not invoked by MessageEngine. Use the fixed-shard runtime configuration for RPC dispatch.", false)]
    public static async Task InvokeWithSchedulerAsync(
        this IServiceScheduler? scheduler,
        IServiceContext? serviceContext,
        string serviceName,
        Func<Task> serviceInvocation,
        CancellationToken cancellationToken = default)
    {
        // If scheduler is null or context is null, invoke directly (backward compatibility)
        if (scheduler == null || serviceContext == null)
        {
            await serviceInvocation();
            return;
        }

        // If ServiceId is not set, invoke directly
        // This handles unauthenticated calls or services that don't require scheduling
        if (!serviceContext.IsAuthenticated)
        {
            await serviceInvocation();
            return;
        }

        // Use scheduler for authenticated services
        var key = new ServiceSchedulingKey(serviceName, serviceContext.ServiceId!);
        await scheduler.ScheduleAsync(key, serviceInvocation, cancellationToken);
    }
}

/// <summary>
/// Historical placeholder retained for binary compatibility. It does not describe
/// the current fixed-shard engine and must not be used as integration guidance.
/// </summary>
[Obsolete("Historical integration notes only; IServiceScheduler is not connected to MessageEngine.", false)]
public static class EngineIntegrationNotes
{
    // Documentation class - see XML comments above for integration instructions
}
