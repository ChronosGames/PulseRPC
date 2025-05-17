public interface IScenario
{
    ValueTask PrepareAsync(GrpcChannel channel);
    ValueTask RunAsync(int connectionId, PerformanceTestRunningContext ctx, CancellationToken cancellationToken);
    Task CompleteAsync();
}
