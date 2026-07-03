using PulseRPC.Scheduling;
using PulseRPC.Server.Services.Scheduling;

namespace PulseRPC.Server.Processing.Engine;

/// <summary>
/// Extension methods for integrating ServiceThreadScheduler into MessageEngine.
/// </summary>
public static class SchedulerIntegrationExtensions
{
    /// <summary>
    /// Wrap a service invocation with scheduler-based execution if scheduler is available.
    /// This should be called after L2 batch processing, before actual service invocation.
    /// </summary>
    /// <param name="scheduler">The service scheduler (optional, null-safe).</param>
    /// <param name="serviceContext">The service context containing ServiceId.</param>
    /// <param name="serviceName">The service name from ChannelAttribute or interface name.</param>
    /// <param name="serviceInvocation">The actual service method invocation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the scheduled or direct invocation.</returns>
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
/// Integration notes for MessageEngine modification (T028):
///
/// In the message processing pipeline (after L2 batch processing), add:
///
/// <code>
/// // In CreateMessageHandler() or message processing method:
/// private async Task ProcessMessageAsync(Message message)
/// {
///     // ... existing L1, L2, L3 processing ...
///
///     // Extract service metadata (from source generator)
///     var serviceName = GetServiceNameForMessage(message); // From generated metadata
///     var serviceContext = GetServiceContextForConnection(message.ConnectionId);
///
///     // Get scheduler from DI (injected in constructor)
///     var scheduler = _serviceProvider.GetService<IServiceScheduler>();
///
///     // Wrap service invocation with scheduler
///     await scheduler.InvokeWithSchedulerAsync(
///         serviceContext,
///         serviceName,
///         async () =>
///         {
///             // Actual service method dispatch (existing logic)
///             await _messageDispatcher.DispatchAsync(message);
///         });
/// }
/// </code>
///
/// Key integration points:
/// 1. Inject IServiceScheduler? (nullable) in MessageEngine constructor
/// 2. Store IServiceScheduler field: private readonly IServiceScheduler? _scheduler;
/// 3. Call InvokeWithSchedulerAsync() before service dispatch
/// 4. Maintain backward compatibility (null scheduler = direct invocation)
/// </summary>
public static class EngineIntegrationNotes
{
    // Documentation class - see XML comments above for integration instructions
}