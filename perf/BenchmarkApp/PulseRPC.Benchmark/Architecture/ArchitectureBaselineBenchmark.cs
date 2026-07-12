using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Abstractions.Transport.Batching;
using PulseRPC.Server.Services;
using PulseRPC.Server.Services.Management;
using PulseRPC.Shared;

namespace PulseRPC.Benchmark.Architecture;

internal static class ArchitectureBaselineBenchmark
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task RunAsync(
        int operations,
        int concurrency,
        int repetitions,
        bool smoke,
        string? outputPath,
        string? comparisonPath,
        double maxRegressionPercent,
        CancellationToken cancellationToken)
    {
        if (maxRegressionPercent < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRegressionPercent),
                "Regression percentage cannot be negative.");
        }

        if (repetitions is < 1 or > 9)
        {
            throw new ArgumentOutOfRangeException(
                nameof(repetitions),
                "Repetitions must be between 1 and 9.");
        }

        if (smoke)
        {
            operations = Math.Min(operations, 250);
            concurrency = Math.Min(concurrency, 8);
            repetitions = 1;
        }

        operations = Math.Max(100, operations);
        concurrency = Math.Clamp(concurrency, 1, operations);
        var hotLookupOperations = smoke ? operations : Math.Max(200_000, checked(operations * 100));
        var lifecycleOperations = smoke
            ? Math.Max(100, operations / 4)
            : Math.Max(5_000, checked(operations * 2));

        var scenarioSamples = new List<ArchitectureScenarioResult>[4];
        for (var index = 0; index < scenarioSamples.Length; index++)
        {
            scenarioSamples[index] = new List<ArchitectureScenarioResult>(repetitions);
        }

        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            scenarioSamples[0].Add(await RunTransportBackpressureAsync(operations, concurrency, cancellationToken));
            scenarioSamples[1].Add(await RunActorHotLookupAsync(hotLookupOperations, concurrency, cancellationToken));
            scenarioSamples[2].Add(await RunActorMailboxBackpressureAsync(operations, concurrency, cancellationToken));
            scenarioSamples[3].Add(await RunActorLifecycleAsync(lifecycleOperations, concurrency, cancellationToken));
        }

        var scenarios = scenarioSamples.Select(AggregateMedian).ToList();

        var report = new ArchitectureBenchmarkReport
        {
            SchemaVersion = SchemaVersion,
            CreatedUtc = DateTimeOffset.UtcNow,
            Runtime = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
                ?? RuntimeInformation.ProcessArchitecture.ToString(),
            ServerGc = GCSettings.IsServerGC,
            Configuration = new ArchitectureBenchmarkConfiguration
            {
                Operations = operations,
                Concurrency = concurrency,
                Repetitions = repetitions,
                Smoke = smoke
            },
            Scenarios = scenarios
        };

        PrintReport(report);

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(
                fullPath,
                JsonSerializer.Serialize(report, JsonOptions),
                cancellationToken);
            Console.WriteLine($"baseline-json: {fullPath}");
        }

        if (!string.IsNullOrWhiteSpace(comparisonPath))
        {
            var baseline = JsonSerializer.Deserialize<ArchitectureBenchmarkReport>(
                await File.ReadAllTextAsync(comparisonPath, cancellationToken),
                JsonOptions) ?? throw new InvalidOperationException("Unable to deserialize architecture baseline.");
            ValidateComparison(report, baseline, maxRegressionPercent);
            var regressions = Compare(report, baseline);
            PrintComparison(report, baseline, regressions);

            if (maxRegressionPercent > 0
                && regressions.Any(item => item.RegressionPercent > maxRegressionPercent))
            {
                throw new InvalidOperationException(
                    $"Architecture benchmark exceeded the allowed {maxRegressionPercent:F1}% regression threshold.");
            }
        }
    }

    private static async Task<ArchitectureScenarioResult> RunTransportBackpressureAsync(
        int operations,
        int concurrency,
        CancellationToken cancellationToken)
    {
        var waitSamples = new double[operations];
        var payloads = Enumerable.Range(0, operations)
            .Select(_ => new byte[64])
            .ToArray();
        using var inner = new ConstrainedTransport(waitSamples, TimeSpan.FromTicks(2_000));
        await using var transport = new BatchedTransport(
            inner,
            new BatchedTransportOptions
            {
                BatchThreshold = 8,
                BatchSizeThreshold = 1024,
                FlushInterval = TimeSpan.FromMilliseconds(1),
                QueueCapacity = Math.Max(10, Math.Min(32, concurrency)),
                BackpressureStrategy = TransportBackpressureStrategy.Block,
                EnableMetrics = false,
                TransportId = "architecture-baseline"
            });

        await WarmupAsync(
            Math.Min(32, operations),
            Math.Min(4, concurrency),
            async (index, started) =>
            {
                BinaryPrimitives.WriteInt64LittleEndian(payloads[index], started);
                BinaryPrimitives.WriteInt32LittleEndian(payloads[index].AsSpan(sizeof(long)), index);
                if (!await transport.SendAsync(payloads[index], cancellationToken))
                {
                    throw new IOException("Transport warmup send failed.");
                }
            },
            cancellationToken);
        Array.Clear(waitSamples);

        return await MeasureAsync(
            "transport-batched-block",
            operations,
            concurrency,
            async (index, started) =>
            {
                BinaryPrimitives.WriteInt64LittleEndian(payloads[index], started);
                BinaryPrimitives.WriteInt32LittleEndian(payloads[index].AsSpan(sizeof(long)), index);
                if (!await transport.SendAsync(payloads[index], cancellationToken).ConfigureAwait(false))
                {
                    throw new IOException("Transport benchmark send failed.");
                }
            },
            () => waitSamples,
            cancellationToken);
    }

    private static async Task<ArchitectureScenarioResult> RunActorHotLookupAsync(
        int operations,
        int concurrency,
        CancellationToken cancellationToken)
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await using var manager = CreateActorManager(provider);
        await manager.GetOrCreateServiceAsync(nameof(BenchmarkActor), "hot", cancellationToken);

        await WarmupAsync(
            Math.Min(32, operations),
            Math.Min(4, concurrency),
            async (unusedIndex, unusedStarted) =>
            {
                _ = await manager.GetOrCreateServiceAsync(
                    nameof(BenchmarkActor),
                    "hot",
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

        return await MeasureAsync(
            "actor-hot-lookup",
            operations,
            concurrency,
            async (index, started) =>
            {
                _ = await manager.GetOrCreateServiceAsync(
                    nameof(BenchmarkActor),
                    "hot",
                    cancellationToken).ConfigureAwait(false);
            },
            waitSamples: null,
            cancellationToken);
    }

    private static async Task<ArchitectureScenarioResult> RunActorMailboxBackpressureAsync(
        int operations,
        int concurrency,
        CancellationToken cancellationToken)
    {
        var waitSamples = new double[operations];
        await using var actor = new BenchmarkActor("mailbox", queueCapacity: 16);
        await actor.StartAsync(cancellationToken);

        await WarmupAsync(
            Math.Min(32, operations),
            Math.Min(4, concurrency),
            async (index, started) =>
            {
                await actor.EnqueueAsync(async () =>
                {
                    waitSamples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    await Task.Yield();
                    BusyWait(TimeSpan.FromTicks(2_000));
                }, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
        Array.Clear(waitSamples);

        return await MeasureAsync(
            "actor-mailbox-block",
            operations,
            concurrency,
            async (index, started) =>
            {
                await actor.EnqueueAsync(async () =>
                {
                    waitSamples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    await Task.Yield();
                    BusyWait(TimeSpan.FromTicks(2_000));
                }, cancellationToken).ConfigureAwait(false);
            },
            () => waitSamples,
            cancellationToken);
    }

    private static async Task<ArchitectureScenarioResult> RunActorLifecycleAsync(
        int operations,
        int concurrency,
        CancellationToken cancellationToken)
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        await using var manager = CreateActorManager(provider);

        await WarmupAsync(
            Math.Min(16, operations),
            Math.Min(4, concurrency),
            async (index, unusedStarted) =>
            {
                var id = $"warmup-{index}";
                _ = await manager.GetOrCreateServiceAsync(
                    nameof(BenchmarkActor),
                    id,
                    cancellationToken).ConfigureAwait(false);
                if (!await manager.RemoveServiceAsync(
                        nameof(BenchmarkActor),
                        id,
                        cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException($"Warmup actor '{id}' was not removed.");
                }
            },
            cancellationToken);

        return await MeasureAsync(
            "actor-create-remove",
            operations,
            concurrency,
            async (index, started) =>
            {
                var id = $"actor-{index}";
                _ = await manager.GetOrCreateServiceAsync(
                    nameof(BenchmarkActor),
                    id,
                    cancellationToken).ConfigureAwait(false);
                if (!await manager.RemoveServiceAsync(
                        nameof(BenchmarkActor),
                        id,
                        cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException($"Actor '{id}' was not removed.");
                }
            },
            waitSamples: null,
            cancellationToken);
    }

    private static PulseServiceManager CreateActorManager(IServiceProvider provider)
    {
        var manager = new PulseServiceManager(
            provider,
            NullLogger<PulseServiceManager>.Instance,
            new PulseServiceManagerOptions { MaxCachedInstances = 100_000 });
        manager.Register<BenchmarkActor>((_, id) => new BenchmarkActor(id));
        return manager;
    }

    private static async Task WarmupAsync(
        int operations,
        int concurrency,
        Func<int, long, ValueTask> operation,
        CancellationToken cancellationToken)
    {
        await RunConcurrentAsync(operations, concurrency, operation, latencies: null, cancellationToken);
    }

    private static async Task<ArchitectureScenarioResult> MeasureAsync(
        string name,
        int operations,
        int concurrency,
        Func<int, long, ValueTask> operation,
        Func<double[]>? waitSamples,
        CancellationToken cancellationToken)
    {
        var latencies = new double[operations];
        ForceGc();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var elapsed = Stopwatch.StartNew();

        await RunConcurrentAsync(operations, concurrency, operation, latencies, cancellationToken);

        elapsed.Stop();
        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        return new ArchitectureScenarioResult
        {
            Name = name,
            Operations = operations,
            Concurrency = concurrency,
            DurationMs = elapsed.Elapsed.TotalMilliseconds,
            OperationsPerSecond = operations / elapsed.Elapsed.TotalSeconds,
            AllocatedBytesPerOperation = Math.Max(0, allocatedBytes) / (double)operations,
            Latency = Distribution.From(latencies),
            BackpressureWait = waitSamples is null
                ? Distribution.Empty
                : Distribution.From(waitSamples())
        };
    }

    private static async Task RunConcurrentAsync(
        int operations,
        int concurrency,
        Func<int, long, ValueTask> operation,
        double[]? latencies,
        CancellationToken cancellationToken)
    {
        var next = -1;
        var workers = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = Interlocked.Increment(ref next);
                if (index >= operations)
                {
                    return;
                }

                var started = Stopwatch.GetTimestamp();
                await operation(index, started).ConfigureAwait(false);
                if (latencies is not null)
                {
                    latencies[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                }
            }
        });

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private static void ForceGc()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static void BusyWait(TimeSpan duration)
    {
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < duration)
        {
            Thread.SpinWait(32);
        }
    }

    private static void PrintReport(ArchitectureBenchmarkReport report)
    {
        Console.WriteLine("PulseRPC architecture concurrency baseline");
        Console.WriteLine(
            $"runtime={report.Runtime}, os={report.OperatingSystem}, " +
            $"cpu={report.ProcessorCount} ({report.Processor}), server-gc={report.ServerGc}");
        Console.WriteLine($"repetitions={report.Configuration.Repetitions} (reported metrics are medians)");
        Console.WriteLine("scenario                         ops/s     p50 ms     p95 ms     p99 ms    alloc B/op   wait p95 ms");
        foreach (var scenario in report.Scenarios)
        {
            Console.WriteLine(
                $"{scenario.Name,-30} {scenario.OperationsPerSecond,8:F0} " +
                $"{scenario.Latency.P50Ms,10:F4} {scenario.Latency.P95Ms,10:F4} {scenario.Latency.P99Ms,10:F4} " +
                $"{scenario.AllocatedBytesPerOperation,13:F1} {scenario.BackpressureWait.P95Ms,13:F4}");
        }
    }

    private static IReadOnlyList<MetricRegression> Compare(
        ArchitectureBenchmarkReport current,
        ArchitectureBenchmarkReport baseline)
    {
        var regressions = new List<MetricRegression>();
        foreach (var currentScenario in current.Scenarios)
        {
            var baselineScenario = baseline.Scenarios.FirstOrDefault(item => item.Name == currentScenario.Name);
            if (baselineScenario is null)
            {
                continue;
            }

            AddLowerIsBetter("p95-ms", currentScenario.Latency.P95Ms, baselineScenario.Latency.P95Ms);
            AddLowerIsBetter("allocated-b/op", currentScenario.AllocatedBytesPerOperation, baselineScenario.AllocatedBytesPerOperation);
            if (baselineScenario.BackpressureWait.P95Ms > 0)
            {
                AddLowerIsBetter("wait-p95-ms", currentScenario.BackpressureWait.P95Ms, baselineScenario.BackpressureWait.P95Ms);
            }

            if (baselineScenario.OperationsPerSecond > 0)
            {
                regressions.Add(new MetricRegression
                {
                    Scenario = currentScenario.Name,
                    Metric = "ops/s",
                    Current = currentScenario.OperationsPerSecond,
                    Baseline = baselineScenario.OperationsPerSecond,
                    RegressionPercent =
                        (baselineScenario.OperationsPerSecond - currentScenario.OperationsPerSecond)
                        / baselineScenario.OperationsPerSecond * 100
                });
            }

            void AddLowerIsBetter(string metric, double currentValue, double baselineValue)
            {
                if (baselineValue <= 0)
                {
                    return;
                }

                regressions.Add(new MetricRegression
                {
                    Scenario = currentScenario.Name,
                    Metric = metric,
                    Current = currentValue,
                    Baseline = baselineValue,
                    RegressionPercent = (currentValue - baselineValue) / baselineValue * 100
                });
            }
        }

        return regressions;
    }

    private static ArchitectureScenarioResult AggregateMedian(
        IReadOnlyList<ArchitectureScenarioResult> samples)
    {
        var first = samples[0];
        return new ArchitectureScenarioResult
        {
            Name = first.Name,
            Operations = first.Operations,
            Concurrency = first.Concurrency,
            DurationMs = Median(samples.Select(item => item.DurationMs)),
            OperationsPerSecond = Median(samples.Select(item => item.OperationsPerSecond)),
            AllocatedBytesPerOperation = Median(samples.Select(item => item.AllocatedBytesPerOperation)),
            Latency = AggregateMedian(samples.Select(item => item.Latency)),
            BackpressureWait = AggregateMedian(samples.Select(item => item.BackpressureWait))
        };
    }

    private static Distribution AggregateMedian(IEnumerable<Distribution> samples)
    {
        var values = samples.ToArray();
        return new Distribution
        {
            Count = values[0].Count,
            AverageMs = Median(values.Select(item => item.AverageMs)),
            P50Ms = Median(values.Select(item => item.P50Ms)),
            P95Ms = Median(values.Select(item => item.P95Ms)),
            P99Ms = Median(values.Select(item => item.P99Ms)),
            MaxMs = Median(values.Select(item => item.MaxMs))
        };
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        var midpoint = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[midpoint - 1] + sorted[midpoint]) / 2
            : sorted[midpoint];
    }

    private static void ValidateComparison(
        ArchitectureBenchmarkReport current,
        ArchitectureBenchmarkReport baseline,
        double maxRegressionPercent)
    {
        if (baseline.SchemaVersion != SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Architecture baseline schema v{baseline.SchemaVersion} is incompatible with v{SchemaVersion}.");
        }

        if (current.Configuration.Operations != baseline.Configuration.Operations
            || current.Configuration.Concurrency != baseline.Configuration.Concurrency
            || current.Configuration.Repetitions != baseline.Configuration.Repetitions
            || current.Configuration.Smoke != baseline.Configuration.Smoke)
        {
            throw new InvalidOperationException(
                "Architecture baseline configuration differs; operations, concurrency, repetitions, and smoke must match.");
        }

        var baselineScenarios = baseline.Scenarios.ToDictionary(item => item.Name, StringComparer.Ordinal);
        foreach (var scenario in current.Scenarios)
        {
            if (!baselineScenarios.TryGetValue(scenario.Name, out var baselineScenario)
                || scenario.Operations != baselineScenario.Operations
                || scenario.Concurrency != baselineScenario.Concurrency)
            {
                throw new InvalidOperationException(
                    $"Architecture baseline scenario '{scenario.Name}' is missing or has a different workload.");
            }
        }

        var environmentDiffers = current.ProcessorCount != baseline.ProcessorCount
            || current.Processor != baseline.Processor
            || current.Runtime != baseline.Runtime
            || current.OperatingSystem != baseline.OperatingSystem
            || current.Architecture != baseline.Architecture
            || current.ServerGc != baseline.ServerGc;
        if (environmentDiffers && maxRegressionPercent > 0)
        {
            throw new InvalidOperationException(
                "Regression gating requires the same runtime, OS, architecture, and processor count as the baseline.");
        }
    }

    private static void PrintComparison(
        ArchitectureBenchmarkReport current,
        ArchitectureBenchmarkReport baseline,
        IReadOnlyList<MetricRegression> regressions)
    {
        Console.WriteLine();
        Console.WriteLine($"comparison baseline: {baseline.CreatedUtc:O}");
        if (current.ProcessorCount != baseline.ProcessorCount
            || current.Processor != baseline.Processor
            || current.Runtime != baseline.Runtime
            || current.OperatingSystem != baseline.OperatingSystem
            || current.Architecture != baseline.Architecture
            || current.ServerGc != baseline.ServerGc)
        {
            Console.WriteLine("warning: runtime/OS/CPU differs; compare trends only, not absolute pass/fail values.");
        }

        foreach (var item in regressions)
        {
            Console.WriteLine(
                $"{item.Scenario}/{item.Metric}: {item.Baseline:F4} -> {item.Current:F4} " +
                $"({item.RegressionPercent:+0.0;-0.0;0.0}% regression delta)");
        }
    }

    private sealed class BenchmarkActor : PulseServiceBase
    {
        public BenchmarkActor(string id, int queueCapacity = 10_000)
            : base(
                nameof(BenchmarkActor),
                id,
                NullLogger.Instance,
                new ServiceExecutionOptions
                {
                    SchedulingMode = ServiceSchedulingMode.DedicatedQueue,
                    MaxConcurrency = 1,
                    QueueCapacity = queueCapacity,
                    BackpressureMode = ServiceBackpressureMode.Block
                })
        {
        }
    }

    private sealed class ConstrainedTransport : ITransport
    {
        private static readonly EndPoint Loopback = new IPEndPoint(IPAddress.Loopback, 0);
        private readonly double[] _waitSamples;
        private readonly TimeSpan _sendDelay;

        public ConstrainedTransport(double[] waitSamples, TimeSpan sendDelay)
        {
            _waitSamples = waitSamples;
            _sendDelay = sendDelay;
        }

        public string Id => "architecture-baseline";
        public TransportType Type => TransportType.TCP;
        public bool IsConnected => true;
        public ConnectionState State => ConnectionState.Connected;
        public EndPoint LocalEndPoint => Loopback;
        public EndPoint RemoteEndPoint => Loopback;
        public event EventHandler<TransportStateEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<TransportDataEventArgs>? DataReceived
        {
            add { }
            remove { }
        }

        public async Task<bool> SendAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
        {
            var started = BinaryPrimitives.ReadInt64LittleEndian(data.Span);
            var index = BinaryPrimitives.ReadInt32LittleEndian(data.Span[sizeof(long)..]);
            _waitSamples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            BusyWait(_sendDelay);
            return true;
        }

        public void Dispose()
        {
        }
    }
}

internal sealed class ArchitectureBenchmarkReport
{
    public int SchemaVersion { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public string Runtime { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public string Processor { get; set; } = string.Empty;
    public bool ServerGc { get; set; }
    public ArchitectureBenchmarkConfiguration Configuration { get; set; } = new();
    public List<ArchitectureScenarioResult> Scenarios { get; set; } = new();
}

internal sealed class ArchitectureBenchmarkConfiguration
{
    public int Operations { get; set; }
    public int Concurrency { get; set; }
    public int Repetitions { get; set; }
    public bool Smoke { get; set; }
}

internal sealed class ArchitectureScenarioResult
{
    public string Name { get; set; } = string.Empty;
    public int Operations { get; set; }
    public int Concurrency { get; set; }
    public double DurationMs { get; set; }
    public double OperationsPerSecond { get; set; }
    public double AllocatedBytesPerOperation { get; set; }
    public Distribution Latency { get; set; } = Distribution.Empty;
    public Distribution BackpressureWait { get; set; } = Distribution.Empty;
}

internal sealed class Distribution
{
    public static Distribution Empty { get; } = new();

    public int Count { get; set; }
    public double AverageMs { get; set; }
    public double P50Ms { get; set; }
    public double P95Ms { get; set; }
    public double P99Ms { get; set; }
    public double MaxMs { get; set; }

    public static Distribution From(double[] samples)
    {
        if (samples.Length == 0)
        {
            return new Distribution();
        }

        var sorted = (double[])samples.Clone();
        Array.Sort(sorted);
        return new Distribution
        {
            Count = sorted.Length,
            AverageMs = sorted.Average(),
            P50Ms = Percentile(sorted, 0.50),
            P95Ms = Percentile(sorted, 0.95),
            P99Ms = Percentile(sorted, 0.99),
            MaxMs = sorted[^1]
        };
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        var position = (sorted.Length - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }

        var fraction = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }
}

internal sealed class MetricRegression
{
    public string Scenario { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public double Current { get; set; }
    public double Baseline { get; set; }
    public double RegressionPercent { get; set; }
}
