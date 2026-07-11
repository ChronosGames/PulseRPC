using System.Diagnostics;
using System.Globalization;

namespace PulseRPC.Benchmark.Clustering;

/// <summary>在真实 Gateway A→B→C（含 caller claims）loopback TCP 拓扑上测量端到端吞吐与延迟。</summary>
public static class ThreeHopClusterBenchmark
{
    public static async Task RunAsync(
        int warmup,
        int iterations,
        int concurrency,
        bool smoke,
        CancellationToken cancellationToken = default)
    {
        if (smoke)
        {
            warmup = Math.Min(Math.Max(warmup, 0), 5);
            iterations = Math.Min(Math.Max(iterations, 1), 30);
            concurrency = Math.Min(Math.Max(concurrency, 1), 2);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(warmup);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);

        await using var topology = await ThreeNodeTcpTopology.StartAsync(
            new ThreeNodeTcpTopologyOptions
            {
                ConnectTimeout = TimeSpan.FromSeconds(2),
                RequestTimeout = TimeSpan.FromSeconds(5),
                RecoveryTtl = TimeSpan.FromSeconds(1),
                LeaseDuration = TimeSpan.FromSeconds(5),
            },
            cancellationToken).ConfigureAwait(false);

        await RunOperationsAsync(topology, warmup, concurrency, null, cancellationToken).ConfigureAwait(false);

        var latencies = new double[iterations];
        var elapsed = Stopwatch.StartNew();
        await RunOperationsAsync(topology, iterations, concurrency, latencies, cancellationToken).ConfigureAwait(false);
        elapsed.Stop();

        Array.Sort(latencies);
        var opsPerSecond = iterations / elapsed.Elapsed.TotalSeconds;

        Console.WriteLine("PulseRPC three-node TCP benchmark (Gateway A -> B -> C, caller claims enabled)");
        Console.WriteLine($"warmup={warmup}, iterations={iterations}, concurrency={concurrency}");
        Console.WriteLine($"ops/s: {opsPerSecond.ToString("N2", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"p50:   {Percentile(latencies, 50).ToString("F3", CultureInfo.InvariantCulture)} ms");
        Console.WriteLine($"p95:   {Percentile(latencies, 95).ToString("F3", CultureInfo.InvariantCulture)} ms");
        Console.WriteLine($"p99:   {Percentile(latencies, 99).ToString("F3", CultureInfo.InvariantCulture)} ms");
    }

    private static Task RunOperationsAsync(
        ThreeNodeTcpTopology topology,
        int count,
        int concurrency,
        double[]? latencies,
        CancellationToken cancellationToken)
    {
        if (count == 0)
        {
            return Task.CompletedTask;
        }

        var next = -1;
        var workers = Enumerable.Range(0, Math.Min(count, concurrency))
            .Select(_ => Task.Run(async () =>
            {
                while (true)
                {
                    var index = Interlocked.Increment(ref next);
                    if (index >= count)
                    {
                        return;
                    }

                    var started = Stopwatch.GetTimestamp();
                    var response = await topology.InvokeAsync($"benchmark-{index}", cancellationToken)
                        .ConfigureAwait(false);
                    if (response.Length == 0)
                    {
                        throw new InvalidOperationException("三跳基准收到空响应。");
                    }

                    if (latencies is not null)
                    {
                        latencies[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    }
                }
            }, cancellationToken))
            .ToArray();

        return Task.WhenAll(workers);
    }

    private static double Percentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0)
        {
            return 0;
        }

        var position = percentile / 100 * (sortedValues.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        var weight = position - lower;
        return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
    }
}
