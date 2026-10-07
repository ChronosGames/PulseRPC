namespace PulseRPC.Server.Processing;

/// <summary>An admission failure before business execution; serialized as SERVER_BUSY.</summary>
public sealed class RpcAdmissionException : InvalidOperationException
{
    /// <summary>Creates a rejection before business execution. Clients receive SERVER_BUSY.</summary>
    /// <param name="message">A description of the admission failure.</param>
    public RpcAdmissionException(string message) : base(message) { }
}
