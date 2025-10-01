using PulseRPC.Scheduling;

namespace PulseRPC.Server.Authentication;

/// <summary>
/// Extensions for injecting ServiceId into IServiceContext during authentication.
/// </summary>
public static class ServiceIdAuthenticationExtensions
{
    /// <summary>
    /// Set the ServiceId in the service context after successful authentication.
    /// This enables the scheduler to route requests based on ServiceName+ServiceId.
    /// </summary>
    /// <param name="context">The service context.</param>
    /// <param name="serviceId">The unique service instance identifier (e.g., user ID, session ID, player ID).</param>
    /// <exception cref="ArgumentNullException">If context or serviceId is null.</exception>
    /// <exception cref="ArgumentException">If serviceId is empty or whitespace.</exception>
    public static void SetServiceId(this IServiceContext context, string serviceId)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("ServiceId must not be null or whitespace.", nameof(serviceId));

        context.ServiceId = serviceId;
    }

    /// <summary>
    /// Validate that ServiceId is set in the context.
    /// Throws if ServiceId is missing for operations that require scheduling.
    /// </summary>
    /// <param name="context">The service context.</param>
    /// <exception cref="InvalidOperationException">If ServiceId is not set.</exception>
    public static void RequireServiceId(this IServiceContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        if (!context.IsAuthenticated)
        {
            throw new InvalidOperationException(
                $"ServiceId not set for ServiceName '{context.ServiceName}'. " +
                "Ensure authentication middleware sets ServiceId using context.SetServiceId(id).");
        }
    }
}

/// <summary>
/// Example authentication handler demonstrating ServiceId injection.
/// </summary>
/// <example>
/// <code>
/// public class PlayerAuthenticationHandler
/// {
///     public async Task&lt;bool&gt; AuthenticateAsync(IServiceContext context, AuthRequest request)
///     {
///         // 1. Validate credentials
///         var playerId = await ValidatePlayerTokenAsync(request.Token);
///
///         if (playerId == null)
///             return false;
///
///         // 2. Set ServiceId for scheduling
///         context.SetServiceId(playerId);
///
///         return true;
///     }
/// }
///
/// // In generated service proxy or middleware:
/// public async Task HandleRequestAsync(IServiceContext context, Request request)
/// {
///     // Ensure ServiceId is set before invoking scheduled services
///     context.RequireServiceId();
///
///     // Now safe to use scheduler
///     var key = new ServiceSchedulingKey(context.ServiceName, context.ServiceId!);
///     await scheduler.ScheduleAsync(key, () => ProcessRequest(request));
/// }
/// </code>
/// </example>
public static class AuthenticationExamples
{
    // Documentation class - see XML documentation above for usage examples
}