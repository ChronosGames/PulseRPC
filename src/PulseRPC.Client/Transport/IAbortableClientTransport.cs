namespace PulseRPC.Client.Transport;

/// <summary>
/// Client-internal transport capability for immediately terminating a connection without
/// sending a graceful disconnect frame or draining pending writes.
/// </summary>
internal interface IAbortableClientTransport
{
    void Abort();
}
