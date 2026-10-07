using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using MemoryPack;
using GameServer.Contracts;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Client.Configuration;
using PulseRPC.Shared;
using PulseRPC.Clustering;
using PulseRPC.Server.Clustering;

namespace GameServer.Host;

[PulseClientGeneration(typeof(IPlayerHub))]
[PulseClientGeneration(typeof(ISessionHub))]
internal static class AcceptanceClient
{
    internal static async Task RunAsync(string[] args)
    {
        if (args.Length < 4) throw new ArgumentException("client <port> <player> <state|purchase|security|load> [operation-id|operations] [connections] [payload-bytes]");
        var port = int.Parse(args[1]);
        var player = args[2];
        using var logging = LoggerFactory.Create(options => options.SetMinimumLevel(LogLevel.Error));
        using var client = new PulseClientBuilder().WithLogging(logging).Build();
        await client.InitializeAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        Task<IClientChannel> ConnectAsync()
        {
            var descriptor = ConnectionDescriptor.CreateTcp(Guid.NewGuid().ToString("N"), "acceptance", "127.0.0.1", port);
            descriptor.TransportOptions = new TcpTransportOptions { RecvBufferSize = 64 * 1024, SendBufferSize = 64 * 1024 };
            return client.ConnectAsync(descriptor, deadline.Token);
        }
        var channel = await ConnectAsync();
        var actor = channel.ForGatewayActor<IPlayerHub>(player).GetHub<IPlayerHub>();
        try
        {
            if (args[3] == "security")
                await MustRejectAsync(() => actor.GetStateAsync(deadline.Token));
            await channel.GetHub<ISessionHub>().AuthenticateAsync(SessionHub.IssueTestToken(player), deadline.Token);
            switch (args[3])
            {
                case "state":
                    Print(await actor.GetStateAsync(deadline.Token));
                    break;
                case "purchase":
                    var operation = args.Length > 4 ? Guid.Parse(args[4]) : Guid.NewGuid();
                    var command = new PurchaseCommand { OperationId = operation };
                    var first = await actor.PurchaseAsync(command, deadline.Token);
                    var replay = await actor.PurchaseAsync(command, deadline.Token);
                    if (first.Balance != replay.Balance || first.Inventory != replay.Inventory)
                        throw new InvalidOperationException("Purchase replay changed committed state.");
                    Print(replay);
                    break;
                case "sessions":
                    var sessionCommand = new PurchaseCommand { OperationId = Guid.NewGuid() };
                    var committed = await actor.PurchaseAsync(sessionCommand, deadline.Token);
                    var replacement = await ConnectAsync();
                    await replacement.GetHub<ISessionHub>().AuthenticateAsync(SessionHub.IssueTestToken(player), deadline.Token);
                    var replacementActor = replacement.ForGatewayActor<IPlayerHub>(player).GetHub<IPlayerHub>();
                    await MustRejectAsync(() => actor.GetStateAsync(deadline.Token));
                    await MustRejectAsync(() => actor.PurchaseAsync(new PurchaseCommand { OperationId = Guid.NewGuid() }, deadline.Token));
                    // Late logout from the old connection must not revoke its replacement.
                    await channel.GetHub<ISessionHub>().LogoutAsync(deadline.Token);
                    var restored = await replacementActor.PurchaseAsync(sessionCommand, deadline.Token);
                    if (restored.Balance != committed.Balance || restored.Inventory != committed.Inventory)
                        throw new InvalidOperationException("Reconnect replay duplicated assets.");
                    await replacement.GetHub<ISessionHub>().LogoutAsync(deadline.Token);
                    await MustRejectAsync(() => replacementActor.GetStateAsync(deadline.Token));
                    await replacement.DisconnectAsync();
                    var reconnected = await ConnectAsync();
                    var reconnectedActor = reconnected.ForGatewayActor<IPlayerHub>(player).GetHub<IPlayerHub>();
                    await MustRejectAsync(() => reconnectedActor.GetStateAsync(deadline.Token));
                    await reconnected.GetHub<ISessionHub>().AuthenticateAsync(SessionHub.IssueTestToken(player), deadline.Token);
                    var resynced = await reconnectedActor.GetStateAsync(deadline.Token);
                    if (resynced.Balance != committed.Balance || resynced.Inventory != committed.Inventory)
                        throw new InvalidOperationException("Session resynchronization changed state.");
                    await reconnected.DisconnectAsync();
                    Print(new { Passed = true, OldSessionDenied = true, LateLogoutSafe = true,
                        ExplicitLogoutEnforced = true, ReconnectRequiresAuthentication = true, ReplayMutations = 1 });
                    break;
                case "security":
                    await MustRejectAsync(() => channel.ForGatewayActor<IPlayerHub>(player + "-other").GetHub<IPlayerHub>().GetStateAsync(deadline.Token));
                    await MustRejectAsync(() => channel.GetHub<ISessionHub>().AuthenticateAsync(SessionHub.IssueTestToken(player, DateTime.UtcNow.AddSeconds(-1)), deadline.Token));
                    await channel.GetHub<ISessionHub>().AuthenticateAsync(SessionHub.IssueTestToken(player, DateTime.UtcNow.AddSeconds(5)), deadline.Token);
                    await actor.GetStateAsync(deadline.Token);
                    await Task.Delay(TimeSpan.FromSeconds(6), deadline.Token);
                    await MustRejectAsync(() => actor.GetStateAsync(deadline.Token));
                    // A CA-trusted non-member must not bypass mTLS subject rules by
                    // presenting a signed node credential on the public player endpoint.
                    using (var outsider = X509Certificate2.CreateFromPemFile(
                        Path.Combine(args[4], "outsider.crt"), Path.Combine(args[4], "outsider.key")))
                    {
                        var signer = new CertificateNodeAuthenticator(new CertificateNodeAuthenticatorOptions { LocalCertificate = outsider });
                        var credential = await signer.CreateCredentialAsync("outsider", deadline.Token);
                        var response = await ((IHubAddressedClientChannel)channel).InvokeHubRawAsync(
                            NodeWireProtocol.ClusterInternalHubName, NodeWireProtocol.AuthenticateProtocolId,
                            MemoryPackSerializer.Serialize(("outsider", credential.ToArray())), deadline.Token);
                        if (MemoryPackSerializer.Deserialize<bool>(response.Span))
                            throw new InvalidOperationException("A trusted-CA non-member became a cluster node through the public endpoint.");
                    }
                    Print(new { SecurityChecks = 5, Passed = true });
                    break;
                case "load":
                    var count = args.Length > 4 ? int.Parse(args[4]) : 2000;
                    var connections = args.Length > 5 ? int.Parse(args[5]) : 16;
                    // A player has one current session. Hot-player load multiplexes
                    // one connection instead of creating sessions that revoke each other.
                    if (player == "hot") connections = 1;
                    var bytes = args.Length > 6 ? int.Parse(args[6]) : 128;
                    var actors = new List<IPlayerHub>();
                    for (var i = 0; i < connections; i++)
                    {
                        var peer = i == 0 ? channel : await ConnectAsync();
                        var user = player == "hot" ? "hot" : player + "-" + i;
                        await peer.GetHub<ISessionHub>().AuthenticateAsync(SessionHub.IssueTestToken(user), deadline.Token);
                        var target = peer.ForGatewayActor<IPlayerHub>(user).GetHub<IPlayerHub>();
                        await target.GetStateAsync(deadline.Token); // Exclude initial DB/Actor activation.
                        actors.Add(target);
                    }
                    var payload = new string('x', bytes);
                    var latency = new ConcurrentBag<double>();
                    var elapsed = Stopwatch.StartNew();
                    await Parallel.ForEachAsync(Enumerable.Range(0, connections),
                        new ParallelOptions { MaxDegreeOfParallelism = connections, CancellationToken = deadline.Token },
                        async (index, ct) =>
                        {
                            for (var i = index; i < count; i += connections)
                            {
                                var start = Stopwatch.GetTimestamp();
                                if (await actors[index].EchoAsync(payload, ct) != payload) throw new InvalidOperationException("Echo corruption.");
                                latency.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                            }
                        });
                    elapsed.Stop();
                    var sorted = latency.Order().ToArray();
                    double Percentile(double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * p) - 1)];
                    Print(new
                    {
                        Operations = count, Connections = connections, PayloadBytes = bytes,
                        Seconds = elapsed.Elapsed.TotalSeconds, RequestsPerSecond = count / elapsed.Elapsed.TotalSeconds,
                        P50Ms = Percentile(.5), P95Ms = Percentile(.95), P99Ms = Percentile(.99),
                        ClientWorkingSetBytes = Environment.WorkingSet, Failures = 0,
                        CapacityCertified = false, Workload = "bounded closed-loop generated-client echo"
                    });
                    break;
                case "overload":
                    // The harness holds this player's SQL row; the first request blocks
                    // while later requests fill this physical connection's bounded queue.
                    var blocked = actor.PurchaseAsync(new PurchaseCommand { OperationId = Guid.NewGuid() }, deadline.Token);
                    await Task.Delay(100, deadline.Token);
                    var outcomes = await Task.WhenAll(Enumerable.Range(0, 256).Select(async _ =>
                    {
                        try
                        {
                            if (await actor.EchoAsync("bounded", deadline.Token) != "bounded")
                                throw new InvalidOperationException("Overload response corruption.");
                            return true;
                        }
                        catch (PulseRemoteException error) when (error.ErrorCode == "SERVER_BUSY") { return false; }
                    }));
                    await blocked;
                    var busy = outcomes.Count(success => !success);
                    if (busy == 0 || busy == outcomes.Length) throw new InvalidOperationException("Expected bounded admission and explicit busy responses.");
                    if (await actor.EchoAsync("recovered", deadline.Token) != "recovered")
                        throw new InvalidOperationException("Connection did not recover after overload.");
                    Print(new { Requests = outcomes.Length, Busy = busy, Succeeded = outcomes.Length - busy, Recovered = true });
                    break;
                default: throw new ArgumentException("Unknown client scenario.");
            }
        }
        finally { await channel.DisconnectAsync(); await client.StopAsync(); }
    }

    private static async Task MustRejectAsync(Func<Task> action)
    {
        try { await action(); }
        catch (PulseRemoteException error) when (error.ErrorCode == "UNAUTHORIZED") { return; }
        throw new InvalidOperationException("An unauthorized request was not rejected explicitly.");
    }

    private static void Print<T>(T value) => Console.WriteLine("RESULT " + JsonSerializer.Serialize(value));
}
