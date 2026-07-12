using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// Diagnostic HTTP endpoints for server health, metrics, and debugging.
/// Endpoints: /diagnostics/health, /diagnostics/metrics, /diagnostics/connections,
/// /diagnostics/queue-stats, /diagnostics/thread-dump
/// </summary>
public sealed class DiagnosticEndpoints
{
    private readonly PipelineMetricsCollector _metricsCollector;
    private readonly ILogger<DiagnosticEndpoints> _logger;
    private readonly Func<object>? _getConnectionList;
    private readonly Func<object>? _getQueueStats;

    public DiagnosticEndpoints(
        PipelineMetricsCollector metricsCollector,
        ILogger<DiagnosticEndpoints>? logger = null,
        Func<object>? getConnectionList = null,
        Func<object>? getQueueStats = null)
    {
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DiagnosticEndpoints>.Instance;
        _getConnectionList = getConnectionList;
        _getQueueStats = getQueueStats;
    }

    /// <summary>
    /// GET /diagnostics/health
    /// Returns server health status.
    /// </summary>
    public string GetHealth()
    {
        var metrics = _metricsCollector.GetSnapshot();

        var health = new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            checks = new
            {
                requests = new
                {
                    status = metrics.ActiveRequests < 10000 ? "healthy" : "degraded",
                    active = metrics.ActiveRequests,
                    total = metrics.TotalRequests
                },
                errors = new
                {
                    status = metrics.ErrorRate < 0.05 ? "healthy" : "degraded",
                    rate = metrics.ErrorRate,
                    total = metrics.TotalErrors
                },
                memory = new
                {
                    status = metrics.MemoryUsageMB < 2048 ? "healthy" : "warning",
                    usage_mb = metrics.MemoryUsageMB
                },
                cpu = new
                {
                    status = metrics.CpuUsagePercent < 90 ? "healthy" : "warning",
                    usage_percent = metrics.CpuUsagePercent
                }
            }
        };

        return JsonSerializer.Serialize(health, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// GET /diagnostics/metrics
    /// Returns Prometheus-compatible metrics.
    /// </summary>
    public string GetMetrics()
    {
        return _metricsCollector.ExportPrometheus();
    }

    /// <summary>
    /// GET /diagnostics/metrics/json
    /// Returns metrics in JSON format.
    /// </summary>
    public string GetMetricsJson()
    {
        var metrics = _metricsCollector.GetSnapshot();
        return JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// GET /diagnostics/connections
    /// Returns list of active connections.
    /// </summary>
    public string GetConnections()
    {
        if (_getConnectionList == null)
        {
            return JsonSerializer.Serialize(new { error = "Connection list provider not configured" });
        }

        try
        {
            var connections = _getConnectionList();
            return JsonSerializer.Serialize(connections, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get connection list");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    /// <summary>
    /// GET /diagnostics/queue-stats
    /// Returns queue depth and saturation statistics.
    /// </summary>
    public string GetQueueStats()
    {
        var metrics = _metricsCollector.GetSnapshot();

        var queueStats = new
        {
            timestamp = DateTime.UtcNow,
            l1_queue = new
            {
                depth = metrics.L1QueueDepth,
                capacity = (long?)null,
                saturation = (double?)null
            },
            l2_queue = new
            {
                depth = metrics.L2QueueDepth,
                capacity = (long?)null,
                saturation = (double?)null
            },
            l3_queue = new
            {
                depth = metrics.L3QueueDepth,
                capacity = (long?)null,
                saturation = (double?)null
            }
        };

        if (_getQueueStats != null)
        {
            try
            {
                var customStats = _getQueueStats();
                return JsonSerializer.Serialize(new { standard = queueStats, custom = customStats }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get custom queue stats");
            }
        }

        return JsonSerializer.Serialize(queueStats, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// GET /diagnostics/thread-dump
    /// Returns thread pool state (debug only).
    /// </summary>
    public string GetThreadDump()
    {
        ThreadPool.GetAvailableThreads(out int availableWorkerThreads, out int availableCompletionPortThreads);
        ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxCompletionPortThreads);
        ThreadPool.GetMinThreads(out int minWorkerThreads, out int minCompletionPortThreads);

        var threadDump = new
        {
            timestamp = DateTime.UtcNow,
            thread_pool = new
            {
                worker_threads = new
                {
                    available = availableWorkerThreads,
                    max = maxWorkerThreads,
                    min = minWorkerThreads,
                    in_use = maxWorkerThreads - availableWorkerThreads
                },
                completion_port_threads = new
                {
                    available = availableCompletionPortThreads,
                    max = maxCompletionPortThreads,
                    min = minCompletionPortThreads,
                    in_use = maxCompletionPortThreads - availableCompletionPortThreads
                }
            },
            process = new
            {
                thread_count = System.Diagnostics.Process.GetCurrentProcess().Threads.Count,
                processor_count = Environment.ProcessorCount
            }
        };

        return JsonSerializer.Serialize(threadDump, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// GET /diagnostics/gc
    /// Returns garbage collection statistics.
    /// </summary>
    public string GetGcStats()
    {
        var gcStats = new
        {
            timestamp = DateTime.UtcNow,
            memory = new
            {
                total_memory_mb = GC.GetTotalMemory(false) / (1024 * 1024),
                total_allocated_mb = GC.GetTotalAllocatedBytes() / (1024 * 1024)
            },
            collections = new
            {
                gen0 = GC.CollectionCount(0),
                gen1 = GC.CollectionCount(1),
                gen2 = GC.CollectionCount(2)
            },
            pause_time = new
            {
                total_ms = GC.GetTotalPauseDuration().TotalMilliseconds
            }
        };

        return JsonSerializer.Serialize(gcStats, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Handles diagnostic endpoint routing.
    /// </summary>
    public string? HandleRequest(string path)
    {
        return path.ToLowerInvariant() switch
        {
            "/diagnostics/health" => GetHealth(),
            "/diagnostics/metrics" => GetMetrics(),
            "/diagnostics/metrics/json" => GetMetricsJson(),
            "/diagnostics/connections" => GetConnections(),
            "/diagnostics/queue-stats" => GetQueueStats(),
            "/diagnostics/thread-dump" => GetThreadDump(),
            "/diagnostics/gc" => GetGcStats(),
            _ => null
        };
    }
}
