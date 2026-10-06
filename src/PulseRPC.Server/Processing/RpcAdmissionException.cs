namespace PulseRPC.Server.Processing;

/// <summary>An admission failure before business execution; serialized as SERVER_BUSY.</summary>
internal sealed class RpcAdmissionException : InvalidOperationException
{
    internal RpcAdmissionException(string message) : base(message) { }
}
