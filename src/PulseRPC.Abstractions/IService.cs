namespace PulseRPC;

public interface IServiceMarker
{

}

public interface IService<TSelf> : IServiceMarker
{
    TSelf WithDeadline(DateTime deadline);
    TSelf WithCancellationToken(CancellationToken cancellationToken);
    TSelf WithHost(string host);
}
