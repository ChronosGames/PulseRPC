using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PulseRPC.Diagnostics;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Processing.Serialization;
using PulseRPC.Server.Services.Management;
using StackExchange.Redis;

namespace GameServer.Host;

internal sealed class GameAdmission
{
    private readonly object _sync = new();
    private readonly TaskCompletionSource _empty = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private bool _draining;
    internal bool IsDraining { get { lock (_sync) return _draining; } }
    internal int Active { get { lock (_sync) return _active; } }
    internal long Rejected;

    internal IDisposable Enter()
    {
        lock (_sync)
        {
            if (_draining)
            {
                Interlocked.Increment(ref Rejected);
                throw new RpcAdmissionException("Node is draining; retry a retryable operation on an available node.");
            }
            _active++;
            return new Admission(this);
        }
    }

    internal Task BeginDrain()
    {
        lock (_sync)
        {
            _draining = true;
            if (_active == 0) _empty.TrySetResult();
            return _empty.Task;
        }
    }

    private sealed class Admission(GameAdmission owner) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (owner._sync)
                if (--owner._active == 0 && owner._draining) owner._empty.TrySetResult();
        }
    }
}

internal sealed class GameDispatcher(IMessageDispatcher inner, GameAdmission admission) : IMessageDispatcher
{
    internal long Completed, Failed;
    public event EventHandler<MessageProcessedEventArgs> MessageProcessed
    { add => inner.MessageProcessed += value; remove => inner.MessageProcessed -= value; }
    public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken = default) => inner.StopAsync(cancellationToken);
    public async ValueTask<object?> DispatchAsync(MessageEnvelope message, IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        using var lease = admission.Enter();
        try { return await inner.DispatchAsync(message, serviceProvider, cancellationToken); }
        catch { Interlocked.Increment(ref Failed); throw; }
        finally { Interlocked.Increment(ref Completed); }
    }
    public void Dispose() => inner.Dispose();
}

internal sealed class DrainDirectory(IConnectionMultiplexer redis, GameAdmission admission, string node,
    ILogger<DrainDirectory> logger) : BackgroundService
{
    private static readonly string[] Members = ["gateway", "game-a", "game-b"];
    private string[] _excluded = [];
    private static RedisKey Key(string member) => "game-acceptance:draining:" + member;
    internal bool Excludes(string member) => Volatile.Read(ref _excluded).Contains(member, StringComparer.Ordinal);

    internal Task PublishDrainAsync(CancellationToken ct)
        => redis.GetDatabase().StringSetAsync(Key(node), "1", TimeSpan.FromSeconds(60)).WaitAsync(ct);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await redis.GetDatabase().KeyDeleteAsync(Key(node)).WaitAsync(cancellationToken);
        await RefreshAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var states = await redis.GetDatabase().StringGetAsync(Members.Select(Key).ToArray()).WaitAsync(ct);
        Volatile.Write(ref _excluded, Members.Where((_, index) => states[index] == "1").ToArray());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                if (admission.IsDraining) await PublishDrainAsync(timeout.Token);
                await RefreshAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning(error, "Could not refresh draining-node placement view"); }
            try { await Task.Delay(500, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

// The admin listener is strictly loopback. Put it behind an authenticated control
// plane for remote operations; it is never part of the public player listener.
internal sealed class GameOperations(int port, string node, GameAdmission admission, DrainDirectory directory,
    PulseServiceManager services, ITieredMessageEngine engine, GameDispatcher dispatcher,
    NpgsqlDataSource source, IConnectionMultiplexer redis, ILogger<GameOperations> logger) : BackgroundService
{
    private readonly HttpListener _listener = new();
    private Task? _drain;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var stopped = stoppingToken.Register(_listener.Stop);
        while (!stoppingToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().WaitAsync(stoppingToken); }
            catch (Exception error) when (stoppingToken.IsCancellationRequested
                && error is OperationCanceledException or HttpListenerException or ObjectDisposedException) { break; }
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var status = 200;
                var body = "ok\n";
                switch ((context.Request.HttpMethod, context.Request.Url?.AbsolutePath))
                {
                    case ("GET", "/live"): break;
                    case ("GET", "/ready"):
                        if (admission.IsDraining) { status = 503; body = "draining\n"; break; }
                        // A database cancellation may wait for its own network ACK.
                        // Keep command ownership inside the probe task, but bound the
                        // HTTP response independently of that asynchronous cleanup.
                        await CheckDependenciesAsync(timeout.Token).WaitAsync(timeout.Token);
                        break;
                    case ("GET", "/metrics"):
                        body = await MetricsAsync(timeout.Token).WaitAsync(timeout.Token);
                        break;
                    case ("POST", "/drain"):
                        _drain ??= DrainAsync(stoppingToken);
                        status = _drain.IsCompletedSuccessfully ? 200 : 202;
                        if (_drain.IsFaulted) { status = 500; body = "drain failed; inspect logs\n"; }
                        else body = status == 200 ? "drained\n" : "draining\n";
                        break;
                    default: status = 404; body = "not found\n"; break;
                }
                context.Response.StatusCode = status;
                context.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, stoppingToken);
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Admin request failed on {Node}", node);
                try { context.Response.StatusCode = 503; }
                catch (ObjectDisposedException) { }
            }
            finally { context.Response.Close(); }
        }
        if (_drain is not null)
            try { await _drain; }
            catch (Exception error) { logger.LogWarning(error, "Drain did not complete before shutdown"); }
    }

    private async Task DrainAsync(CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        var empty = admission.BeginDrain();
        await directory.PublishDrainAsync(timeout.Token);
        await empty.WaitAsync(timeout.Token);
        foreach (var service in services.GetAllServices().ToArray())
            await services.RemoveServiceIfSameAsync(service).AsTask().WaitAsync(timeout.Token);
        logger.LogWarning("DRAINED {Node}; active requests={Active}", node, admission.Active);
    }

    private async Task CheckDependenciesAsync(CancellationToken ct)
    {
        await redis.GetDatabase().PingAsync().WaitAsync(ct);
        await using var query = source.CreateCommand("SELECT 1");
        await query.ExecuteScalarAsync(ct);
    }

    private async Task<string> MetricsAsync(CancellationToken ct)
    {
        var metrics = new StringBuilder();
        void Add(string name, double value, string extra = "")
            => metrics.Append("game_").Append(name).Append("{node=\"").Append(node).Append('"')
                .Append(extra).Append("} ").Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        var engineStats = engine.GetStatistics();
        using var process = Process.GetCurrentProcess();
        Add("working_set_bytes", process.WorkingSet64);
        Add("cpu_seconds_total", process.TotalProcessorTime.TotalSeconds);
        Add("managed_heap_bytes", GC.GetTotalMemory(false));
        Add("connections", engineStats.ActiveConnections);
        Add("actors", services.GetStatistics().ActiveInstances);
        Add("requests_active", admission.Active);
        Add("requests_total", Interlocked.Read(ref dispatcher.Completed));
        Add("requests_failed_total", Interlocked.Read(ref dispatcher.Failed));
        Add("draining", admission.IsDraining ? 1 : 0);
        Add("drain_rejections_total", Interlocked.Read(ref admission.Rejected));
        Add("rpc_p99_milliseconds", engineStats.P99LatencyMs);
        for (var generation = 0; generation <= 2; generation++) Add("gc_collections_total", GC.CollectionCount(generation), $",generation=\"{generation}\"");
        foreach (var group in RuntimeQueueMetrics.GetSnapshots().GroupBy(snapshot => snapshot.QueueName))
        {
            var tag = ",queue=\"" + group.Key.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
            Add("queue_depth", group.Sum(snapshot => (long)snapshot.Depth), tag);
            Add("queue_capacity", group.Sum(snapshot => (long)snapshot.Capacity), tag);
            Add("queue_rejected_total", group.Sum(snapshot => snapshot.RejectedEnqueues), tag);
        }
        try
        {
            await using var query = source.CreateCommand("""
                SELECT count(*),COALESCE(extract(epoch FROM clock_timestamp()-min(created_at)),0)
                FROM game_outbox WHERE delivered_at IS NULL
                """);
            await using var reader = await query.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            Add("outbox_pending", reader.GetInt64(0));
            Add("outbox_oldest_seconds", (double)reader.GetDecimal(1));
            Add("storage_up", 1);
        }
        catch (Exception error) when (error is NpgsqlException or OperationCanceledException)
        { Add("storage_up", 0); }
        return metrics.ToString();
    }

    public override void Dispose() { _listener.Close(); base.Dispose(); }
}
