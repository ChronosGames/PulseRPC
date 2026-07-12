using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Services.Management;
using Xunit;

namespace PulseRPC.Server.Tests.Services;

public sealed class ServiceInstanceEvictorLifecycleTests
{
    [Fact]
    public async Task StopAsync_WaitsForCleanupLoopAndPreventsFurtherRuns()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await using var manager = new PulseServiceManager(
            provider,
            NullLogger<PulseServiceManager>.Instance);
        using var evictor = new ServiceInstanceEvictor(
            manager,
            Options.Create(new PulseServiceManagerOptions
            {
                CleanupInterval = TimeSpan.FromMilliseconds(10)
            }),
            NullLogger<ServiceInstanceEvictor>.Instance);

        await evictor.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => evictor.TotalCleanupRuns > 0, TimeSpan.FromSeconds(5));

        await evictor.StopAsync(CancellationToken.None);
        var cleanupRunsAfterStop = evictor.TotalCleanupRuns;
        await Task.Delay(50);

        evictor.TotalCleanupRuns.Should().Be(cleanupRunsAfterStop);
        evictor.GetStatistics().IsRunning.Should().BeFalse();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!condition())
        {
            await Task.Delay(5, cancellation.Token);
        }
    }
}
