using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace PulseRPC.Diagnostics;

/// <summary>运行时有界队列指标快照。</summary>
public sealed class RuntimeQueueMetricsSnapshot
{
    public string QueueName { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int Depth { get; set; }
    public int HighWatermark { get; set; }
    public double Saturation => Capacity <= 0 ? 0 : Math.Clamp((double)Depth / Capacity, 0, 1);
    public long SaturationEvents { get; set; }
    public long EnqueueWaitCount { get; set; }
    public TimeSpan EnqueueWaitDuration { get; set; }
    public long RejectedEnqueues { get; set; }
}

/// <summary>有界队列注册句柄；队列在入队/出队后调用 <see cref="Observe"/>。</summary>
public interface IRuntimeQueueMetricsRegistration : IDisposable
{
    void Observe();
    void RecordEnqueueWait(TimeSpan duration);
    void RecordRejectedEnqueue();
    RuntimeQueueMetricsSnapshot GetSnapshot();
}

/// <summary>
/// PulseRPC 进程内有界队列注册表，同时通过 <see cref="Meter"/> 发布容量、深度、饱和度和高水位。
/// </summary>
public static class RuntimeQueueMetrics
{
    private static readonly ConcurrentDictionary<long, Registration> s_registrations = new();
    private static readonly Meter s_meter = new("PulseRPC.RuntimeQueues", "1.0.0");
    private static long s_nextId;

    static RuntimeQueueMetrics()
    {
        s_meter.CreateObservableGauge("pulserpc.queue.capacity", ObserveCapacity, unit: "items");
        s_meter.CreateObservableGauge("pulserpc.queue.depth", ObserveDepth, unit: "items");
        s_meter.CreateObservableGauge("pulserpc.queue.saturation", ObserveSaturation, unit: "ratio");
        s_meter.CreateObservableGauge("pulserpc.queue.high_watermark", ObserveHighWatermark, unit: "items");
    }

    public static IRuntimeQueueMetricsRegistration Register(
        string queueName,
        string instanceId,
        int capacity,
        Func<int> getDepth)
    {
        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("Queue name is required.", nameof(queueName));
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("Instance id is required.", nameof(instanceId));
        if (getDepth == null)
            throw new ArgumentNullException(nameof(getDepth));
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        var id = Interlocked.Increment(ref s_nextId);
        var registration = new Registration(id, queueName, instanceId, capacity, getDepth);
        if (!s_registrations.TryAdd(id, registration))
            throw new InvalidOperationException("Unable to register runtime queue metrics.");
        registration.Observe();
        return registration;
    }

    public static IReadOnlyList<RuntimeQueueMetricsSnapshot> GetSnapshots()
        => s_registrations.Values
            .Select(registration => registration.GetSnapshot())
            .OrderBy(snapshot => snapshot.QueueName, StringComparer.Ordinal)
            .ThenBy(snapshot => snapshot.InstanceId, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<Measurement<int>> ObserveCapacity()
        => ObserveInt(snapshot => snapshot.Capacity);

    private static IEnumerable<Measurement<int>> ObserveDepth()
        => ObserveInt(snapshot => snapshot.Depth);

    private static IEnumerable<Measurement<int>> ObserveHighWatermark()
        => ObserveInt(snapshot => snapshot.HighWatermark);

    private static IEnumerable<Measurement<double>> ObserveSaturation()
    {
        foreach (var snapshot in GetSnapshots())
            yield return new Measurement<double>(snapshot.Saturation, Tags(snapshot));
    }

    private static IEnumerable<Measurement<int>> ObserveInt(Func<RuntimeQueueMetricsSnapshot, int> selector)
    {
        foreach (var snapshot in GetSnapshots())
            yield return new Measurement<int>(selector(snapshot), Tags(snapshot));
    }

    private static KeyValuePair<string, object?>[] Tags(RuntimeQueueMetricsSnapshot snapshot)
        => new KeyValuePair<string, object?>[]
        {
            new("queue.name", snapshot.QueueName),
            new("queue.instance", snapshot.InstanceId)
        };

    private sealed class Registration : IRuntimeQueueMetricsRegistration
    {
        private readonly long _id;
        private readonly Func<int> _getDepth;
        private int _highWatermark;
        private int _wasSaturated;
        private long _saturationEvents;
        private long _enqueueWaitCount;
        private long _enqueueWaitTicks;
        private long _rejectedEnqueues;
        private int _disposed;

        public string QueueName { get; }
        public string InstanceId { get; }
        public int Capacity { get; }

        public Registration(long id, string queueName, string instanceId, int capacity, Func<int> getDepth)
        {
            _id = id;
            QueueName = queueName;
            InstanceId = instanceId;
            Capacity = capacity;
            _getDepth = getDepth;
        }

        public void Observe()
        {
            var depth = ReadDepth();
            UpdateHighWatermark(depth);
            var saturated = depth >= Capacity ? 1 : 0;
            if (saturated == 1 && Interlocked.Exchange(ref _wasSaturated, 1) == 0)
                Interlocked.Increment(ref _saturationEvents);
            else if (saturated == 0)
                Interlocked.Exchange(ref _wasSaturated, 0);
        }

        public void RecordEnqueueWait(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(duration));
            Interlocked.Increment(ref _enqueueWaitCount);
            Interlocked.Add(ref _enqueueWaitTicks, duration.Ticks);
            Observe();
        }

        public void RecordRejectedEnqueue()
        {
            Interlocked.Increment(ref _rejectedEnqueues);
            Observe();
        }

        public RuntimeQueueMetricsSnapshot GetSnapshot()
        {
            Observe();
            return new RuntimeQueueMetricsSnapshot
            {
                QueueName = QueueName,
                InstanceId = InstanceId,
                Capacity = Capacity,
                Depth = ReadDepth(),
                HighWatermark = Volatile.Read(ref _highWatermark),
                SaturationEvents = Interlocked.Read(ref _saturationEvents),
                EnqueueWaitCount = Interlocked.Read(ref _enqueueWaitCount),
                EnqueueWaitDuration = TimeSpan.FromTicks(Interlocked.Read(ref _enqueueWaitTicks)),
                RejectedEnqueues = Interlocked.Read(ref _rejectedEnqueues)
            };
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                s_registrations.TryRemove(_id, out _);
        }

        private int ReadDepth()
        {
            try
            {
                return Math.Clamp(_getDepth(), 0, Capacity);
            }
            catch
            {
                return 0;
            }
        }

        private void UpdateHighWatermark(int value)
        {
            var current = Volatile.Read(ref _highWatermark);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(ref _highWatermark, value, current);
                if (observed == current)
                    return;
                current = observed;
            }
        }
    }
}
