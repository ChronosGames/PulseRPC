using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PulseRPC.Client.Configuration;
using Xunit;

namespace PulseRPC.Client.Tests;

public class PulseClientLifecycleTests
{
    [Fact]
    public async Task StopAsync_RaisesStoppingEvent_WithRunningAsPreviousState()
    {
        var client = new PulseClient(Array.Empty<ConnectionDescriptor>());
        var transitions = new List<(ClientState Previous, ClientState Current)>();
        client.StateChanged += (_, e) => transitions.Add((e.PreviousState, e.CurrentState));

        await client.InitializeAsync();
        await client.StopAsync();

        Assert.Contains(transitions, x => x is { Previous: ClientState.Running, Current: ClientState.Stopping });
        Assert.Equal(ClientState.Stopped, client.State);
    }

    [Fact]
    public async Task StopAsync_WithAbortiveMode_MustFailExplicitly()
    {
        var client = new PulseClient(Array.Empty<ConnectionDescriptor>());

        await client.InitializeAsync();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => client.StopAsync(graceful: false));
        Assert.Contains("abortive", ex.Message);
        Assert.Equal(ClientState.Running, client.State);

        await client.StopAsync();
    }

    [Fact]
    public async Task DisconnectAsync_WithAbortiveMode_MustFailExplicitly()
    {
        var client = new PulseClient(Array.Empty<ConnectionDescriptor>());

        var byId = await Assert.ThrowsAsync<NotSupportedException>(
            () => client.DisconnectAsync("conn-1", graceful: false));
        Assert.Contains("abortive", byId.Message);

        var byPredicate = await Assert.ThrowsAsync<NotSupportedException>(
            () => client.DisconnectAsync(_ => true, graceful: false));
        Assert.Contains("abortive", byPredicate.Message);

        await Task.CompletedTask;
    }
}
