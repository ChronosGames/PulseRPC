using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using GameServer.Contracts;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Client.Configuration;
using PulseRPC.Shared;

namespace GameServer.Host;

internal sealed class GameLoadProfile
{
    public int DurationSeconds { get; set; } = 20;
    public int Connections { get; set; } = 8;
    public int RequestsPerSecond { get; set; } = 100;
    public int QueuePerConnection { get; set; } = 2;
    public int PayloadBytes { get; set; } = 128;
    public int ReadPercent { get; set; } = 15;
    public int PurchasePercent { get; set; } = 5;
    public int RequestTimeoutSeconds { get; set; } = 5;
    public int SampleSeconds { get; set; } = 5;
    public int MinimumContinuousSeconds { get; set; }
    public string EnvironmentId { get; set; } = "unspecified";
    public GameLoadSlo? Slo { get; set; }

    internal void Validate()
    {
        if (DurationSeconds is < 1 or > 172800 || Connections is < 1 or > 50000
            || RequestsPerSecond is < 1 or > 100000 || QueuePerConnection is < 1 or > 32
            || PayloadBytes is < 1 or > 65536 || RequestTimeoutSeconds is < 1 or > 60
            || SampleSeconds is < 1 or > 300 || ReadPercent < 0 || PurchasePercent < 0
            || ReadPercent + PurchasePercent > 100 || MinimumContinuousSeconds < 0
            || MinimumContinuousSeconds > DurationSeconds || string.IsNullOrWhiteSpace(EnvironmentId))
            throw new ArgumentException("Invalid load profile bounds.");
        if (Slo is { } slo && (!double.IsFinite(slo.P99Ms) || slo.P99Ms <= 0
            || !double.IsFinite(slo.MaxFailureRatio) || slo.MaxFailureRatio < 0 || slo.MaxFailureRatio > 1))
            throw new ArgumentException("An SLO needs positive P99 and a failure ratio between zero and one.");
    }
}

internal sealed class GameLoadSlo
{
    public double P99Ms { get; set; }
    public double MaxFailureRatio { get; set; }
}

internal static class GameLoadClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    private static readonly JsonSerializerOptions CompactJson = new(JsonSerializerDefaults.Web);
    internal const long InitialBalance = 1_000_000_000_000;

    internal static async Task RunAsync(string[] args, AssetStore store)
    {
        if (args.Length != 5) throw new ArgumentException("load <profile.json> <host> <port> <output-directory>");
        var profileBytes = await File.ReadAllBytesAsync(args[1]);
        var profile = JsonSerializer.Deserialize<GameLoadProfile>(profileBytes, Json) ?? throw new ArgumentException("Missing profile.");
        profile.Validate();
        var port = int.Parse(args[3]);
        if (port is < 1 or > 65535) throw new ArgumentException("Invalid endpoint port.");
        var output = Path.GetFullPath(args[4]);
        Directory.CreateDirectory(output);
        var prefix = "load-" + Guid.NewGuid().ToString("N") + "-";
        using var lifetime = new CancellationTokenSource();
        ConsoleCancelEventHandler interrupt = (_, eventArgs) => { eventArgs.Cancel = true; lifetime.Cancel(); };
        Console.CancelKeyPress += interrupt;
        var startedUtc = DateTimeOffset.UtcNow;
        var counters = new LoadCounters();
        var started = Stopwatch.GetTimestamp();
        var elapsedSeconds = 0d;
        var complete = false;
        var integrity = false;
        Exception? failure = null;
        var queues = new List<Channel<Arrival>>();
        var workers = new List<Task>();
        var peers = new List<IClientChannel>();
        using var logging = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Error));
        using var client = new PulseClientBuilder().WithLogging(logging).Build();
        await using var samples = new StreamWriter(Path.Combine(output, "samples.jsonl"), append: false) { AutoFlush = true };
        try
        {
            await client.InitializeAsync();
            await store.PrepareLoadAsync(prefix, profile.Connections, InitialBalance, lifetime.Token);
            using var setup = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            setup.CancelAfter(TimeSpan.FromMinutes(10));
            for (var index = 0; index < profile.Connections; index++)
            {
                var player = prefix + index;
                var descriptor = ConnectionDescriptor.CreateTcp(Guid.NewGuid().ToString("N"), "game-load", args[2], port);
                descriptor.TransportOptions = new TcpTransportOptions { RecvBufferSize = 65536, SendBufferSize = 65536 };
                var peer = await client.ConnectAsync(descriptor, setup.Token);
                peers.Add(peer);
                await peer.GetHub<ISessionHub>().AuthenticateAsync(SessionHub.IssueTestToken(player), setup.Token);
                var actor = peer.ForGatewayActor<IPlayerHub>(player).GetHub<IPlayerHub>();
                await actor.GetStateAsync(setup.Token);
                var queue = Channel.CreateBounded<Arrival>(new BoundedChannelOptions(profile.QueuePerConnection)
                { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
                queues.Add(queue);
                workers.Add(WorkAsync(peer, actor, player, queue.Reader));
            }
            started = Stopwatch.GetTimestamp();
            var nextSample = 0d;
            var total = checked((long)profile.DurationSeconds * profile.RequestsPerSecond);
            for (long index = 0; index < total; index++)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                var due = index / (double)profile.RequestsPerSecond;
                var now = Stopwatch.GetElapsedTime(started).TotalSeconds;
                while (due > now)
                {
                    // Task.Delay may round a sub-millisecond duration down. Never
                    // dispatch before its scheduled arrival or subtract future time.
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, (due - now) * 1000)), lifetime.Token);
                    now = Stopwatch.GetElapsedTime(started).TotalSeconds;
                }
                Interlocked.Increment(ref counters.Offered);
                // Never hide missed arrivals by waiting for a response or queue space.
                if (now - due > profile.RequestTimeoutSeconds
                    || !queues[(int)(index % queues.Count)].Writer.TryWrite(new Arrival(index, due)))
                    Interlocked.Increment(ref counters.GeneratorDropped);
                counters.SchedulingLag.Record((now - due) * 1000);
                if (now >= nextSample)
                {
                    await samples.WriteLineAsync(JsonSerializer.Serialize(counters.Snapshot(now), CompactJson));
                    nextSample = now + profile.SampleSeconds;
                }
            }
            var remaining = profile.DurationSeconds - Stopwatch.GetElapsedTime(started).TotalSeconds;
            if (remaining > 0) await Task.Delay(TimeSpan.FromSeconds(remaining), lifetime.Token);
            elapsedSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
            foreach (var queue in queues) queue.Writer.TryComplete();
            await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(profile.RequestTimeoutSeconds * (profile.QueuePerConnection + 2)));
            await store.VerifyLoadAsync(prefix, profile.Connections, InitialBalance, lifetime.Token);
            integrity = true;
            complete = true;
        }
        catch (Exception error) { failure = error; }
        finally
        {
            foreach (var queue in queues) queue.Writer.TryComplete();
            lifetime.Cancel();
            try { await Task.WhenAll(workers); }
            catch (OperationCanceledException) { }
            catch (Exception error) { failure ??= error; }
            foreach (var peer in peers)
                try { await peer.DisconnectAsync(); }
                catch (Exception error) { failure ??= error; }
            try { await client.StopAsync(); }
            catch (Exception error) { failure ??= error; }
            Console.CancelKeyPress -= interrupt;
            if (!complete) elapsedSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
            var failures = Interlocked.Read(ref counters.Errors) + Interlocked.Read(ref counters.GeneratorDropped);
            var failureRatio = failures / (double)Math.Max(1, Interlocked.Read(ref counters.Offered));
            var continuous = complete && elapsedSeconds >= profile.MinimumContinuousSeconds;
            var sloPassed = profile.Slo is null ? (bool?)null
                : failureRatio <= profile.Slo.MaxFailureRatio && counters.EndToEnd.Percentile(.99) <= profile.Slo.P99Ms;
            var report = new
            {
                SchemaVersion = 1, Profile = profile,
                ProfileSha256 = Convert.ToHexString(SHA256.HashData(profileBytes)).ToLowerInvariant(),
                StartedUtc = startedUtc, FinishedUtc = DateTimeOffset.UtcNow, PlayerPrefix = prefix,
                Environment = new { Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
                    CpuCount = Environment.ProcessorCount, ServerGc = GCSettings.IsServerGC,
                    Commit = Environment.GetEnvironmentVariable("GAME_CANDIDATE_SHA") ?? "unrecorded" },
                Complete = complete && failure is null, AssetIntegrity = integrity, ContinuousRequirementMet = continuous,
                Soak24HoursCompleted = complete && elapsedSeconds >= 86400,
                CapacityCertified = false, SloPassed = sloPassed, FailureRatio = failureRatio,
                Result = counters.Snapshot(elapsedSeconds), ErrorType = failure?.GetType().Name,
                Workload = "open-loop per-connection bounded queues; new purchases, state reads and echo; no automatic RPC retries",
                LatencyDefinition = "end-to-end starts at scheduled arrival; service latency starts at dispatch; 5% upper histogram bounds"
            };
            await samples.WriteLineAsync(JsonSerializer.Serialize(counters.Snapshot(elapsedSeconds), CompactJson));
            await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(report, Json) + "\n");
            Console.WriteLine("RESULT " + JsonSerializer.Serialize(report, CompactJson));
            if (failure is null && (!complete || !integrity || !continuous || counters.Succeeded == 0 || counters.Corruption != 0 || sloPassed == false))
                failure = new InvalidOperationException("Load acceptance failed; inspect results.json.");
        }
        if (failure is not null) throw new InvalidOperationException("Load did not pass; partial evidence was retained.", failure);

        async Task WorkAsync(IClientChannel peer, IPlayerHub actor, string player, ChannelReader<Arrival> reader)
        {
            var payload = new string('x', profile.PayloadBytes);
            var refreshAt = DateTime.UtcNow.AddMinutes(4);
            await foreach (var arrival in reader.ReadAllAsync(lifetime.Token))
            {
                var dispatched = Stopwatch.GetTimestamp();
                Interlocked.Increment(ref counters.Dispatched);
                Interlocked.Increment(ref counters.InFlight);
                using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                request.CancelAfter(TimeSpan.FromSeconds(profile.RequestTimeoutSeconds));
                try
                {
                    if (DateTime.UtcNow >= refreshAt)
                    {
                        await peer.GetHub<ISessionHub>().AuthenticateAsync(SessionHub.IssueTestToken(player), request.Token);
                        refreshAt = DateTime.UtcNow.AddMinutes(4);
                    }
                    // Mix by rounds, not index % 100: otherwise connection counts that
                    // divide 100 permanently assign each player only one operation type.
                    var kind = (arrival.Index / profile.Connections + arrival.Index % profile.Connections * 17) % 100;
                    if (kind < profile.PurchasePercent)
                    {
                        await actor.PurchaseAsync(new PurchaseCommand { OperationId = Guid.NewGuid() }, request.Token);
                        Interlocked.Increment(ref counters.Purchases);
                    }
                    else if (kind < profile.PurchasePercent + profile.ReadPercent)
                    {
                        await actor.GetStateAsync(request.Token);
                        Interlocked.Increment(ref counters.Reads);
                    }
                    else if (await actor.EchoAsync(payload, request.Token) != payload)
                        throw new InvalidDataException("RPC payload corruption.");
                    Interlocked.Increment(ref counters.Succeeded);
                }
                catch (PulseRemoteException error) when (error.ErrorCode == "SERVER_BUSY")
                { Interlocked.Increment(ref counters.Busy); Interlocked.Increment(ref counters.Errors); }
                catch (PulseRemoteException error) when (error.ErrorCode == "UNAUTHORIZED")
                { Interlocked.Increment(ref counters.Unauthorized); Interlocked.Increment(ref counters.Errors); }
                catch (Exception error) when (error is TimeoutException or OperationCanceledException)
                { Interlocked.Increment(ref counters.Timeouts); Interlocked.Increment(ref counters.Errors); }
                catch (InvalidDataException)
                { Interlocked.Increment(ref counters.Corruption); Interlocked.Increment(ref counters.Errors); }
                catch (Exception) { Interlocked.Increment(ref counters.Errors); }
                finally
                {
                    Interlocked.Decrement(ref counters.InFlight);
                    counters.Service.Record(Stopwatch.GetElapsedTime(dispatched).TotalMilliseconds);
                    counters.EndToEnd.Record((Stopwatch.GetElapsedTime(started).TotalSeconds - arrival.DueSeconds) * 1000);
                }
            }
        }
    }

    private readonly record struct Arrival(long Index, double DueSeconds);

    private sealed class LoadCounters
    {
        internal long Offered, Dispatched, Succeeded, Errors, Busy, GeneratorDropped, InFlight, Purchases, Reads;
        internal long Unauthorized, Timeouts, Corruption;
        internal readonly Histogram EndToEnd = new(), Service = new(), SchedulingLag = new();
        internal object Snapshot(double seconds) => new
        {
            Seconds = seconds, Offered = Interlocked.Read(ref Offered), Dispatched = Interlocked.Read(ref Dispatched),
            Succeeded = Interlocked.Read(ref Succeeded), Errors = Interlocked.Read(ref Errors), Busy = Interlocked.Read(ref Busy),
            GeneratorDropped = Interlocked.Read(ref GeneratorDropped), InFlight = Interlocked.Read(ref InFlight),
            Purchases = Interlocked.Read(ref Purchases), Reads = Interlocked.Read(ref Reads),
            Unauthorized = Interlocked.Read(ref Unauthorized), Timeouts = Interlocked.Read(ref Timeouts),
            Corruption = Interlocked.Read(ref Corruption),
            RequestsPerSecond = Interlocked.Read(ref Succeeded) / Math.Max(.001, seconds),
            P50Ms = EndToEnd.Percentile(.50), P95Ms = EndToEnd.Percentile(.95), P99Ms = EndToEnd.Percentile(.99),
            ServiceP99Ms = Service.Percentile(.99), SchedulingLagP99Ms = SchedulingLag.Percentile(.99),
            WorkingSetBytes = Environment.WorkingSet, ManagedHeapBytes = GC.GetTotalMemory(false),
            Gc0 = GC.CollectionCount(0), Gc1 = GC.CollectionCount(1), Gc2 = GC.CollectionCount(2)
        };
    }

    private sealed class Histogram
    {
        private readonly long[] _buckets = new long[512];
        internal void Record(double milliseconds)
        {
            var bucket = Math.Clamp((int)Math.Ceiling(Math.Log(Math.Max(.001, milliseconds) / .001) / Math.Log(1.05)), 0, 511);
            Interlocked.Increment(ref _buckets[bucket]);
        }
        internal double Percentile(double percentile)
        {
            var counts = _buckets.Select(value => value).ToArray();
            var total = counts.Sum();
            if (total == 0) return 0;
            var target = (long)Math.Ceiling(total * percentile);
            long accumulated = 0;
            for (var index = 0; index < counts.Length; index++)
                if ((accumulated += counts[index]) >= target) return .001 * Math.Pow(1.05, index);
            throw new InvalidOperationException("Histogram count changed unexpectedly.");
        }
    }
}
