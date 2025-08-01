using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GameApp.Infrastructure.Performance
{
    /// <summary>
    /// 性能监控和优化服务实现
    /// </summary>
    public class PerformanceService : IPerformanceService, IDisposable
    {
        private readonly ILogger<PerformanceService> _logger;
        private readonly IDatabase? _redis;
        private readonly Timer _cleanupTimer;

        // 内存中的性能数据存储（滑动窗口）
        private readonly ConcurrentDictionary<string, ConcurrentQueue<PerformanceMetric>> _metricsBuffer;
        private readonly ConcurrentDictionary<string, ResponseTimeMetric> _responseTimeBuffer;
        private readonly ConcurrentQueue<ErrorMetric> _errorBuffer;

        private static readonly TimeSpan MetricsRetentionPeriod = TimeSpan.FromHours(1);
        private static readonly int MaxBufferSize = 10000;

        public PerformanceService(ILogger<PerformanceService> logger, IDatabase? redis = null)
        {
            _logger = logger;
            _redis = redis;
            _metricsBuffer = new ConcurrentDictionary<string, ConcurrentQueue<PerformanceMetric>>();
            _responseTimeBuffer = new ConcurrentDictionary<string, ResponseTimeMetric>();
            _errorBuffer = new ConcurrentQueue<ErrorMetric>();

            // 每5分钟清理一次过期数据
            _cleanupTimer = new Timer(CleanupExpiredMetrics, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        public async Task RecordMetricAsync(string name, double value, IDictionary<string, string>? tags = null)
        {
            try
            {
                var metric = new PerformanceMetric
                {
                    Name = name,
                    Value = value,
                    Tags = tags ?? new Dictionary<string, string>(),
                    Timestamp = DateTime.UtcNow
                };

                // 存储到内存缓冲区
                var queue = _metricsBuffer.GetOrAdd(name, _ => new ConcurrentQueue<PerformanceMetric>());
                queue.Enqueue(metric);

                // 限制缓冲区大小
                while (queue.Count > MaxBufferSize)
                {
                    queue.TryDequeue(out _);
                }

                // 异步存储到Redis（如果可用）
                if (_redis != null)
                {
                    var key = $"performance:metric:{name}:{DateTime.UtcNow:yyyy-MM-dd-HH}";
                    var data = JsonSerializer.Serialize(metric);
                    await _redis.ListLeftPushAsync(key, data);
                    await _redis.KeyExpireAsync(key, MetricsRetentionPeriod);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录性能指标失败: {MetricName}", name);
            }
        }

        public async Task RecordResponseTimeAsync(string operation, TimeSpan duration, bool success = true)
        {
            try
            {
                var metric = _responseTimeBuffer.AddOrUpdate(operation,
                    new ResponseTimeMetric
                    {
                        Operation = operation,
                        TotalRequests = 1,
                        SuccessfulRequests = success ? 1 : 0,
                        FailedRequests = success ? 0 : 1,
                        TotalResponseTime = duration.TotalMilliseconds,
                        MinResponseTime = duration.TotalMilliseconds,
                        MaxResponseTime = duration.TotalMilliseconds,
                        ResponseTimes = new List<double> { duration.TotalMilliseconds }
                    },
                    (key, existing) =>
                    {
                        existing.TotalRequests++;
                        if (success)
                            existing.SuccessfulRequests++;
                        else
                            existing.FailedRequests++;

                        existing.TotalResponseTime += duration.TotalMilliseconds;
                        existing.MinResponseTime = Math.Min(existing.MinResponseTime, duration.TotalMilliseconds);
                        existing.MaxResponseTime = Math.Max(existing.MaxResponseTime, duration.TotalMilliseconds);

                        existing.ResponseTimes.Add(duration.TotalMilliseconds);
                        if (existing.ResponseTimes.Count > 1000) // 限制响应时间样本数量
                        {
                            existing.ResponseTimes.RemoveAt(0);
                        }

                        return existing;
                    });

                // 记录到通用指标
                await RecordMetricAsync($"response_time.{operation}", duration.TotalMilliseconds,
                    new Dictionary<string, string> { { "success", success.ToString() } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录响应时间失败: {Operation}", operation);
            }
        }

        public async Task RecordErrorAsync(string operation, Exception exception)
        {
            try
            {
                var errorMetric = new ErrorMetric
                {
                    Operation = operation,
                    ExceptionType = exception.GetType().Name,
                    Message = exception.Message,
                    Timestamp = DateTime.UtcNow
                };

                _errorBuffer.Enqueue(errorMetric);

                // 限制错误缓冲区大小
                while (_errorBuffer.Count > MaxBufferSize)
                {
                    _errorBuffer.TryDequeue(out _);
                }

                // 记录错误指标
                await RecordMetricAsync($"error.{operation}", 1,
                    new Dictionary<string, string>
                    {
                        { "exception_type", exception.GetType().Name },
                        { "message", exception.Message }
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录错误指标失败: {Operation}", operation);
            }
        }

        public async Task<PerformanceStats> GetStatsAsync(TimeSpan period)
        {
            try
            {
                var stats = new PerformanceStats
                {
                    Period = DateTime.UtcNow,
                    ResourceUsage = GetSystemResourceUsage()
                };

                var cutoffTime = DateTime.UtcNow - period;

                // 计算操作统计
                foreach (var kvp in _responseTimeBuffer)
                {
                    var metric = kvp.Value;
                    var operationStats = new OperationStats
                    {
                        Name = kvp.Key,
                        TotalRequests = metric.TotalRequests,
                        SuccessfulRequests = metric.SuccessfulRequests,
                        FailedRequests = metric.FailedRequests,
                        AverageResponseTime = metric.TotalRequests > 0 ? metric.TotalResponseTime / metric.TotalRequests : 0,
                        MinResponseTime = metric.MinResponseTime,
                        MaxResponseTime = metric.MaxResponseTime
                    };

                    // 计算P95响应时间
                    if (metric.ResponseTimes.Any())
                    {
                        var sortedTimes = metric.ResponseTimes.OrderBy(t => t).ToList();
                        var p95Index = (int)Math.Ceiling(sortedTimes.Count * 0.95) - 1;
                        operationStats.P95ResponseTime = sortedTimes[Math.Max(0, p95Index)];
                    }

                    stats.Operations[kvp.Key] = operationStats;
                }

                // 计算总体统计
                var totalRequests = stats.Operations.Values.Sum(o => o.TotalRequests);
                var totalErrors = stats.Operations.Values.Sum(o => o.FailedRequests);
                var totalResponseTime = stats.Operations.Values.Sum(o => o.AverageResponseTime * o.TotalRequests);

                stats.RequestsPerSecond = totalRequests / period.TotalSeconds;
                stats.ErrorRate = totalRequests > 0 ? (double)totalErrors / totalRequests : 0;
                stats.AverageResponseTime = totalRequests > 0 ? totalResponseTime / totalRequests : 0;

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取性能统计失败");
                return new PerformanceStats { ResourceUsage = GetSystemResourceUsage() };
            }
        }

        public SystemResourceUsage GetSystemResourceUsage()
        {
            try
            {
                using var process = Process.GetCurrentProcess();

                return new SystemResourceUsage
                {
                    CpuUsagePercent = GetCpuUsage(),
                    MemoryUsageBytes = process.WorkingSet64,
                    MemoryUsagePercent = GetMemoryUsagePercent(),
                    AvailableMemoryBytes = GC.GetTotalMemory(false),
                    ThreadCount = process.Threads.Count,
                    GcCollectionCount = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2),
                    Uptime = DateTime.UtcNow - process.StartTime,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取系统资源使用情况失败");
                return new SystemResourceUsage { Timestamp = DateTime.UtcNow };
            }
        }

        private double GetCpuUsage()
        {
            // 简化的CPU使用率计算，实际生产环境可能需要更精确的实现
            try
            {
                using var process = Process.GetCurrentProcess();
                return process.TotalProcessorTime.TotalMilliseconds / Environment.ProcessorCount / Environment.TickCount * 100;
            }
            catch
            {
                return 0;
            }
        }

        private double GetMemoryUsagePercent()
        {
            try
            {
                var totalMemory = GC.GetTotalMemory(false);
                var maxMemory = 1024 * 1024 * 1024; // 假设1GB最大内存，实际应该获取系统内存
                return (double)totalMemory / maxMemory * 100;
            }
            catch
            {
                return 0;
            }
        }

        private void CleanupExpiredMetrics(object? state)
        {
            try
            {
                var cutoffTime = DateTime.UtcNow - MetricsRetentionPeriod;

                // 清理指标缓冲区
                foreach (var kvp in _metricsBuffer)
                {
                    var queue = kvp.Value;
                    var tempList = new List<PerformanceMetric>();

                    while (queue.TryDequeue(out var metric))
                    {
                        if (metric.Timestamp > cutoffTime)
                        {
                            tempList.Add(metric);
                        }
                    }

                    foreach (var metric in tempList)
                    {
                        queue.Enqueue(metric);
                    }
                }

                // 清理错误缓冲区
                var errorList = new List<ErrorMetric>();
                while (_errorBuffer.TryDequeue(out var error))
                {
                    if (error.Timestamp > cutoffTime)
                    {
                        errorList.Add(error);
                    }
                }

                foreach (var error in errorList)
                {
                    _errorBuffer.Enqueue(error);
                }

                _logger.LogDebug("性能指标清理完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期性能指标失败");
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }

        // 内部数据模型
        private class PerformanceMetric
        {
            public string Name { get; set; } = string.Empty;
            public double Value { get; set; }
            public IDictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
            public DateTime Timestamp { get; set; }
        }

        private class ResponseTimeMetric
        {
            public string Operation { get; set; } = string.Empty;
            public long TotalRequests { get; set; }
            public long SuccessfulRequests { get; set; }
            public long FailedRequests { get; set; }
            public double TotalResponseTime { get; set; }
            public double MinResponseTime { get; set; }
            public double MaxResponseTime { get; set; }
            public List<double> ResponseTimes { get; set; } = new();
        }

        private class ErrorMetric
        {
            public string Operation { get; set; } = string.Empty;
            public string ExceptionType { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
        }
    }
}
