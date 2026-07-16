using System;

namespace PulseRPC.Server;

/// <summary>
/// Runtime capability implemented by generated Receiver client selectors that can change delivery error policy.
/// </summary>
/// <typeparam name="TReceiver">The client Receiver contract.</typeparam>
public interface IReceiverDeliveryModeSelector<out TReceiver> where TReceiver : class, IPulseHub
{
    /// <summary>Creates a client selector that uses the requested delivery error policy.</summary>
    IHubClients<TReceiver> WithDeliveryMode(ReceiverDeliveryMode deliveryMode);
}

/// <summary>Runtime delivery-mode extensions for generated Receiver client selectors.</summary>
public static class HubClientsDeliveryModeExtensions
{
    /// <summary>
    /// Selects how subsequent Receiver pushes handle non-cancellation delivery failures.
    /// </summary>
    /// <remarks>
    /// Cancellation is propagated in both modes. This API is defined in the runtime package so consuming class
    /// libraries do not need a compile-time reference to a host-generated Receiver type.
    /// </remarks>
    public static IHubClients<TReceiver> WithDeliveryMode<TReceiver>(
        this IHubClients<TReceiver> clients,
        ReceiverDeliveryMode deliveryMode)
        where TReceiver : class, IPulseHub
    {
        ArgumentNullException.ThrowIfNull(clients);
        if (deliveryMode != ReceiverDeliveryMode.BestEffort && deliveryMode != ReceiverDeliveryMode.Strict)
        {
            throw new ArgumentOutOfRangeException(nameof(deliveryMode));
        }

        if (clients is IReceiverDeliveryModeSelector<TReceiver> selector)
        {
            return selector.WithDeliveryMode(deliveryMode);
        }

        throw new NotSupportedException(
            "The IHubClients implementation does not support Receiver delivery-mode selection.");
    }
}
