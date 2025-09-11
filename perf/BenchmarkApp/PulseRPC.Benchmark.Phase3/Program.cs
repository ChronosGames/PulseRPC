using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.IO;
using PulseRPC.Server.Scheduling;
using PulseRPC.Server.Serialization;

namespace PulseRPC.Benchmark.Phase3;

/// <summary>
/// PulseRPC第三阶段性能优化基准测试程序
/// </summary>
public class Program
{
    private static readonly ILogger Logger = CreateLogger();

    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== PulseRPC 第三阶段性能优化基准测试 ===");
        Console.WriteLine($"开始时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();

        try
        {
            // 解析命令行参数
            var config = ParseArguments(args);

            // 预热JIT
            Console.WriteLine("正在预热JIT编译器...");
            await WarmupJit();

            // 运行基准测试
            await RunBenchmarks(config);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"测试过程中发生异常: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine();
        Console.WriteLine("按任意键退出...");
        Console.ReadKey();
    }

    /// <summary>
    /// 基准测试配置
    /// </summary>
    private class BenchmarkConfig
    {
        public int MessageCount { get; set; } = 500;
        public int ConcurrentSessions { get; set; } = 100;
        public int MessageSizeMin { get; set; } = 128;
        public int MessageSizeMax { get; set; } = 2048;
        public bool RunDetailedTests { get; set; } = true;
        public bool RunComparisonTests { get; set; } = true;
    }

    /// <summary>
    /// 解析命令行参数
    /// </summary>
    private static BenchmarkConfig ParseArguments(string[] args)
    {
        var config = new BenchmarkConfig();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--messages":
                case "-m":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var messageCount))
                    {
                        config.MessageCount = messageCount;
                        i++;
                    }
                    break;

                case "--sessions":
                case "-s":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var sessions))
                    {
                        config.ConcurrentSessions = sessions;
                        i++;
                    }
                    break;

                case "--size-min":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var sizeMin))
                    {
                        config.MessageSizeMin = sizeMin;
                        i++;
                    }
                    break;

                case "--size-max":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var sizeMax))
                    {
                        config.MessageSizeMax = sizeMax;
                        i++;
                    }
                    break;

                case "--quick":
                case "-q":
                    config.RunDetailedTests = false;
                    config.RunComparisonTests = false;
                    config.MessageCount = Math.Min(config.MessageCount, 10000);
                    break;
            }
        }

        return config;
    }

    /// <summary>
    /// JIT预热
    /// </summary>
    private static async Task WarmupJit()
    {
        const int warmupMessages = 1000;

        // 预热BatchedNetworkWriter
        using var memoryStream = new MemoryStream();
        await using var batchedWriter = new BatchedNetworkWriter(memoryStream);

        var data = new byte[256];
        for (int i = 0; i < warmupMessages; i++)
        {
            await batchedWriter.WriteAsync(data);
        }
        await batchedWriter.FlushAsync();

        // 预热序列化管道
        var pipe = new Pipe();
        await using var serializationPipeline = new ZeroCopySerializationPipeline(pipe.Writer);

        for (int i = 0; i < 100; i++)
        {
            await serializationPipeline.SerializeAsync(new TestMessage { Id = i, Content = "warmup" });
        }
        await serializationPipeline.FlushAsync();

        pipe.Writer.Complete();
        pipe.Reader.Complete();

        // 预热调度器
        await using var scheduler = new AffinityAwareScheduler();

        var warmupTasks = Enumerable.Range(0, 100).Select(i =>
            scheduler.ScheduleAsync($"warmup-session", async ct =>
            {
                await Task.Delay(1, ct);
                return i;
            }));

        await Task.WhenAll(warmupTasks);

        Console.WriteLine("JIT预热完成");

        // 强制垃圾收集
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// 运行所有基准测试
    /// </summary>
    private static async Task RunBenchmarks(BenchmarkConfig config)
    {
        Console.WriteLine($"基准测试配置:");
        Console.WriteLine($"  消息数量: {config.MessageCount:N0}");
        Console.WriteLine($"  并发会话: {config.ConcurrentSessions}");
        Console.WriteLine($"  消息大小: {config.MessageSizeMin}-{config.MessageSizeMax} bytes");
        Console.WriteLine();

        var results = new Dictionary<string, BenchmarkResult>();

        // 1. BatchedNetworkWriter基准测试
        Console.WriteLine("=== 1. BatchedNetworkWriter 基准测试 ===");
        results["BatchedNetworkWriter"] = await BenchmarkBatchedNetworkWriter(config);
        Console.WriteLine();

        // 2. ZeroCopySerializationPipeline基准测试
        Console.WriteLine("=== 2. ZeroCopySerializationPipeline 基准测试 ===");
        results["ZeroCopySerializationPipeline"] = await BenchmarkSerializationPipeline(config);
        Console.WriteLine();

        // 3. ReactiveBackpressureController基准测试
        Console.WriteLine("=== 3. ReactiveBackpressureController 基准测试 ===");
        results["ReactiveBackpressureController"] = await BenchmarkBackpressureController(config);
        Console.WriteLine();

        // 4. AffinityAwareScheduler基准测试
        Console.WriteLine("=== 4. AffinityAwareScheduler 基准测试 ===");
        results["AffinityAwareScheduler"] = await BenchmarkAffinityScheduler(config);
        Console.WriteLine();

        // 5. 集成基准测试
        Console.WriteLine("=== 5. 集成基准测试 ===");
        results["IntegratedPipeline"] = await BenchmarkIntegratedPipeline(config);
        Console.WriteLine();

        // 输出汇总结果
        OutputSummaryResults(results);

        // 生成报告
        await GenerateReport(results, config);
    }

    /// <summary>
    /// BatchedNetworkWriter基准测试
    /// </summary>
    private static async Task<BenchmarkResult> BenchmarkBatchedNetworkWriter(BenchmarkConfig config)
    {
        var testData = GenerateTestData(config.MessageCount, config.MessageSizeMin, config.MessageSizeMax);

        using var memoryStream = new MemoryStream();
        var options = new BatchedNetworkWriter.BatchedWriterOptions
        {
            BatchThreshold = 32,
            BatchSizeThreshold = 64 * 1024,
            FlushInterval = TimeSpan.FromMilliseconds(2),
            EnableAdaptiveBatching = true
        };

        await using var batchedWriter = new BatchedNetworkWriter(memoryStream, options, Logger);

        var sw = Stopwatch.StartNew();
        var tasks = testData.Select<ReadOnlyMemory<byte>, Task<bool>>(data => batchedWriter.WriteAsync(data.ToArray()).AsTask());
        var results = await Task.WhenAll(tasks);
        await batchedWriter.FlushAsync();
        sw.Stop();

        var successCount = results.Count(r => r);
        var stats = batchedWriter.Statistics;
        var throughput = successCount / sw.Elapsed.TotalSeconds;

        Console.WriteLine($"  消息处理数: {successCount:N0}/{config.MessageCount:N0}");
        Console.WriteLine($"  总字节数: {stats.TotalBytesWritten:N0}");
        Console.WriteLine($"  批次数: {stats.TotalBatchesFlushed:N0}");
        Console.WriteLine($"  平均批次大小: {stats.AverageBatchSize:F1}");
        Console.WriteLine($"  吞吐量: {throughput:F0} messages/sec");
        Console.WriteLine($"  延迟: {sw.ElapsedMilliseconds} ms");

        return new BenchmarkResult
        {
            ComponentName = "BatchedNetworkWriter",
            MessageCount = successCount,
            Throughput = throughput,
            ElapsedMilliseconds = sw.ElapsedMilliseconds,
            BytesProcessed = stats.TotalBytesWritten,
            AdditionalMetrics = new Dictionary<string, object>
            {
                ["BatchCount"] = stats.TotalBatchesFlushed,
                ["AverageBatchSize"] = stats.AverageBatchSize
            }
        };
    }

    /// <summary>
    /// ZeroCopySerializationPipeline基准测试
    /// </summary>
    private static async Task<BenchmarkResult> BenchmarkSerializationPipeline(BenchmarkConfig config)
    {
        var testMessages = GenerateTestMessages(config.MessageCount);

        var pipe = new Pipe();
        var options = new ZeroCopySerializationPipeline.ZeroCopySerializationOptions
        {
            SmallMessageThreshold = 1024,
            EnableTypeSpecificOptimization = true,
            EnablePreallocatedBuffers = true
        };

        await using var pipeline = new ZeroCopySerializationPipeline(pipe.Writer, options, null, Logger);

        var sw = Stopwatch.StartNew();
        var tasks = testMessages.Select(msg => pipeline.SerializeAsync(msg).AsTask());

        var results = await Task.WhenAll(tasks);
        await pipeline.FlushAsync();
        sw.Stop();

        pipe.Writer.Complete();
        var readResult = await pipe.Reader.ReadAsync();
        var totalBytes = readResult.Buffer.Length;
        pipe.Reader.AdvanceTo(readResult.Buffer.End);
        pipe.Reader.Complete();

        var successCount = results.Count(r => r);
        var stats = pipeline.Statistics;
        var throughput = successCount / sw.Elapsed.TotalSeconds;

        Console.WriteLine($"  消息序列化数: {successCount:N0}/{config.MessageCount:N0}");
        Console.WriteLine($"  序列化字节数: {stats.TotalBytesWritten:N0}");
        Console.WriteLine($"  快速路径: {stats.FastPathCount:N0} ({stats.FastPathRatio:P1})");
        Console.WriteLine($"  慢速路径: {stats.SlowPathCount:N0}");
        Console.WriteLine($"  大对象: {stats.LargeObjectCount:N0}");
        Console.WriteLine($"  吞吐量: {throughput:F0} messages/sec");
        Console.WriteLine($"  延迟: {sw.ElapsedMilliseconds} ms");

        return new BenchmarkResult
        {
            ComponentName = "ZeroCopySerializationPipeline",
            MessageCount = successCount,
            Throughput = throughput,
            ElapsedMilliseconds = sw.ElapsedMilliseconds,
            BytesProcessed = stats.TotalBytesWritten,
            AdditionalMetrics = new Dictionary<string, object>
            {
                ["FastPathCount"] = stats.FastPathCount,
                ["FastPathRatio"] = stats.FastPathRatio,
                ["LargeObjectCount"] = stats.LargeObjectCount
            }
        };
    }

    /// <summary>
    /// ReactiveBackpressureController基准测试
    /// </summary>
    private static async Task<BenchmarkResult> BenchmarkBackpressureController(BenchmarkConfig config)
    {
        const int concurrentRequests = 500; // 高并发测试

        var options = new ReactiveBackpressureController.BackpressureOptions
        {
            BaseConcurrencyLimits = new[] { 100, 80, 60, 40, 20 },
            TokenRefillRates = new[] { 1000.0, 800.0, 600.0, 400.0, 200.0 },
            EnableAdaptiveControl = true
        };

        await using var controller = new ReactiveBackpressureController(options, Logger);

        var successCount = 0;
        var sw = Stopwatch.StartNew();

        var semaphore = new SemaphoreSlim(concurrentRequests);
        var tasks = Enumerable.Range(0, config.MessageCount).Select(async i =>
        {
            await semaphore.WaitAsync();
            try
            {
                var priority = (MessagePriority)(i % 5);
                await using var permit = await controller.TryAcquireAsync(priority);

                if (permit != null)
                {
                    // 模拟处理时间
                    await Task.Delay(Random.Shared.Next(1, 5));
                    Interlocked.Increment(ref successCount);
                    return true;
                }
                return false;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        sw.Stop();

        var stats = controller.GetStatistics();
        var throughput = successCount / sw.Elapsed.TotalSeconds;

        Console.WriteLine($"  请求处理数: {successCount:N0}/{config.MessageCount:N0}");
        Console.WriteLine($"  拒绝数: {stats.TotalRejections:N0}");
        Console.WriteLine($"  拒绝率: {stats.OverallRejectionRate:P2}");
        Console.WriteLine($"  超时数: {stats.TotalTimeouts:N0}");
        Console.WriteLine($"  吞吐量: {throughput:F0} requests/sec");
        Console.WriteLine($"  延迟: {sw.ElapsedMilliseconds} ms");

        return new BenchmarkResult
        {
            ComponentName = "ReactiveBackpressureController",
            MessageCount = successCount,
            Throughput = throughput,
            ElapsedMilliseconds = sw.ElapsedMilliseconds,
            AdditionalMetrics = new Dictionary<string, object>
            {
                ["RejectionCount"] = stats.TotalRejections,
                ["RejectionRate"] = stats.OverallRejectionRate,
                ["TimeoutCount"] = stats.TotalTimeouts
            }
        };
    }

    /// <summary>
    /// AffinityAwareScheduler基准测试
    /// </summary>
    private static async Task<BenchmarkResult> BenchmarkAffinityScheduler(BenchmarkConfig config)
    {
        var options = new AffinityAwareScheduler.AffinitySchedulerOptions
        {
            WorkerThreadCount = Environment.ProcessorCount,
            WorkerQueueCapacity = 1000,
            EnableWorkStealing = true,
            AffinityStrength = 0.8
        };

        await using var scheduler = new AffinityAwareScheduler(options, Logger);

        var sw = Stopwatch.StartNew();
        var tasks = new List<Task<int>>();

        for (int i = 0; i < config.MessageCount; i++)
        {
            var sessionId = $"session-{i % config.ConcurrentSessions}";
            var messageId = i;

            tasks.Add(scheduler.ScheduleAsync(sessionId, async ct =>
            {
                // 模拟消息处理
                await Task.Delay(Random.Shared.Next(1, 3), ct);
                return messageId;
            }));
        }

        var results = await Task.WhenAll(tasks);
        sw.Stop();

        var stats = scheduler.GetStatistics();
        var throughput = stats.TotalCompleted / sw.Elapsed.TotalSeconds;

        Console.WriteLine($"  任务调度数: {stats.TotalScheduled:N0}");
        Console.WriteLine($"  任务完成数: {stats.TotalCompleted:N0}");
        Console.WriteLine($"  任务失败数: {stats.TotalFailed:N0}");
        Console.WriteLine($"  完成率: {stats.CompletionRate:P2}");
        Console.WriteLine($"  亲和性命中率: {stats.AffinityHitRate:P2}");
        Console.WriteLine($"  活跃会话数: {stats.ActiveSessions}");
        Console.WriteLine($"  吞吐量: {throughput:F0} tasks/sec");
        Console.WriteLine($"  延迟: {sw.ElapsedMilliseconds} ms");

        return new BenchmarkResult
        {
            ComponentName = "AffinityAwareScheduler",
            MessageCount = (int)stats.TotalCompleted,
            Throughput = throughput,
            ElapsedMilliseconds = sw.ElapsedMilliseconds,
            AdditionalMetrics = new Dictionary<string, object>
            {
                ["CompletionRate"] = stats.CompletionRate,
                ["AffinityHitRate"] = stats.AffinityHitRate,
                ["ActiveSessions"] = stats.ActiveSessions
            }
        };
    }

    /// <summary>
    /// 集成基准测试
    /// </summary>
    private static async Task<BenchmarkResult> BenchmarkIntegratedPipeline(BenchmarkConfig config)
    {
        const int messagesPerSession = 100;
        var sessionCount = config.MessageCount / messagesPerSession;

        // 创建组件
        using var memoryStream = new MemoryStream();
        await using var batchedWriter = new BatchedNetworkWriter(memoryStream);

        var pipe = new Pipe();
        await using var serializationPipeline = new ZeroCopySerializationPipeline(pipe.Writer);

        await using var backpressureController = new ReactiveBackpressureController();
        await using var affinityScheduler = new AffinityAwareScheduler();

        var testMessages = GenerateTestMessages(config.MessageCount);
        var completedCount = 0;

        var sw = Stopwatch.StartNew();
        var tasks = new List<Task>();

        for (int sessionIndex = 0; sessionIndex < sessionCount; sessionIndex++)
        {
            var sessionId = $"session-{sessionIndex}";
            var sessionMessages = testMessages
                .Skip(sessionIndex * messagesPerSession)
                .Take(messagesPerSession)
                .ToArray();

            tasks.Add(ProcessSessionMessagesIntegrated(sessionId, sessionMessages,
                serializationPipeline, batchedWriter, backpressureController, affinityScheduler,
                () => Interlocked.Increment(ref completedCount)));
        }

        await Task.WhenAll(tasks);
        await serializationPipeline.FlushAsync();
        await batchedWriter.FlushAsync();
        sw.Stop();

        pipe.Writer.Complete();
        pipe.Reader.Complete();

        var throughput = completedCount / sw.Elapsed.TotalSeconds;

        Console.WriteLine($"  会话数: {sessionCount}");
        Console.WriteLine($"  每会话消息数: {messagesPerSession}");
        Console.WriteLine($"  完成消息数: {completedCount:N0}/{config.MessageCount:N0}");
        Console.WriteLine($"  吞吐量: {throughput:F0} messages/sec");
        Console.WriteLine($"  延迟: {sw.ElapsedMilliseconds} ms");

        return new BenchmarkResult
        {
            ComponentName = "IntegratedPipeline",
            MessageCount = completedCount,
            Throughput = throughput,
            ElapsedMilliseconds = sw.ElapsedMilliseconds
        };
    }

    /// <summary>
    /// 处理会话消息（集成模式）
    /// </summary>
    private static async Task ProcessSessionMessagesIntegrated(string sessionId, TestMessage[] messages,
        ZeroCopySerializationPipeline serializationPipeline,
        BatchedNetworkWriter batchedWriter,
        ReactiveBackpressureController backpressureController,
        AffinityAwareScheduler affinityScheduler,
        Action onCompleted)
    {
        foreach (var message in messages)
        {
            await affinityScheduler.ScheduleAsync(sessionId, async ct =>
            {
                await using var permit = await backpressureController.TryAcquireAsync(MessagePriority.Normal, ct, TimeSpan.FromSeconds(1));
                if (permit != null)
                {
                    var serialized = await serializationPipeline.SerializeAsync(message, ct);
                    if (serialized)
                    {
                        var data = System.Text.Encoding.UTF8.GetBytes($"Processed: {message.Content}");
                        var written = await batchedWriter.WriteAsync(data, ct);
                        if (written)
                        {
                            onCompleted();
                        }
                    }
                }
                return true;
            });
        }
    }

    /// <summary>
    /// 生成测试数据
    /// </summary>
    private static List<ReadOnlyMemory<byte>> GenerateTestData(int count, int minSize, int maxSize)
    {
        var random = new Random(42); // 固定种子确保可重现
        var data = new List<ReadOnlyMemory<byte>>();

        for (int i = 0; i < count; i++)
        {
            var size = random.Next(minSize, maxSize + 1);
            var bytes = new byte[size];
            random.NextBytes(bytes);
            data.Add(bytes);
        }

        return data;
    }

    /// <summary>
    /// 生成测试消息
    /// </summary>
    private static TestMessage[] GenerateTestMessages(int count)
    {
        var random = new Random(42);
        var messages = new TestMessage[count];

        for (int i = 0; i < count; i++)
        {
            var dataSize = random.Next(50, 500);
            var data = new byte[dataSize];
            random.NextBytes(data);

            messages[i] = new TestMessage
            {
                Id = i,
                Content = $"Benchmark message {i}",
                Timestamp = DateTime.UtcNow.AddMilliseconds(i),
                Data = data
            };
        }

        return messages;
    }

    /// <summary>
    /// 输出汇总结果
    /// </summary>
    private static void OutputSummaryResults(Dictionary<string, BenchmarkResult> results)
    {
        Console.WriteLine("=== 基准测试汇总结果 ===");
        Console.WriteLine();

        foreach (var kvp in results)
        {
            var result = kvp.Value;
            Console.WriteLine($"{result.ComponentName}:");
            Console.WriteLine($"  处理消息数: {result.MessageCount:N0}");
            Console.WriteLine($"  吞吐量: {result.Throughput:F0} messages/sec");
            Console.WriteLine($"  总耗时: {result.ElapsedMilliseconds} ms");
            if (result.BytesProcessed > 0)
                Console.WriteLine($"  处理字节数: {result.BytesProcessed:N0}");
            Console.WriteLine();
        }

        var totalThroughput = results.Values.Sum(r => r.Throughput);
        Console.WriteLine($"整体性能指标:");
        Console.WriteLine($"  综合吞吐量: {totalThroughput:F0} operations/sec");
        Console.WriteLine($"  平均延迟: {results.Values.Average(r => r.ElapsedMilliseconds):F1} ms");
    }

    /// <summary>
    /// 生成报告
    /// </summary>
    private static async Task GenerateReport(Dictionary<string, BenchmarkResult> results, BenchmarkConfig config)
    {
        var reportPath = Path.Combine(Directory.GetCurrentDirectory(), $"phase3_benchmark_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        await using var writer = new StreamWriter(reportPath);

        await writer.WriteLineAsync("PulseRPC 第三阶段性能优化基准测试报告");
        await writer.WriteLineAsync($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        await writer.WriteLineAsync($"测试配置: 消息数={config.MessageCount:N0}, 会话数={config.ConcurrentSessions}");
        await writer.WriteLineAsync();

        foreach (var kvp in results)
        {
            var result = kvp.Value;
            await writer.WriteLineAsync($"=== {result.ComponentName} ===");
            await writer.WriteLineAsync($"处理消息数: {result.MessageCount:N0}");
            await writer.WriteLineAsync($"吞吐量: {result.Throughput:F0} messages/sec");
            await writer.WriteLineAsync($"总耗时: {result.ElapsedMilliseconds} ms");

            if (result.BytesProcessed > 0)
                await writer.WriteLineAsync($"处理字节数: {result.BytesProcessed:N0}");

            if (result.AdditionalMetrics.Count > 0)
            {
                await writer.WriteLineAsync("附加指标:");
                foreach (var metric in result.AdditionalMetrics)
                {
                    await writer.WriteLineAsync($"  {metric.Key}: {metric.Value}");
                }
            }

            await writer.WriteLineAsync();
        }

        Console.WriteLine($"基准测试报告已生成: {reportPath}");
    }

    /// <summary>
    /// 创建日志器
    /// </summary>
    private static ILogger CreateLogger()
    {
        using var factory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });
        return factory.CreateLogger<Program>();
    }
}

/// <summary>
/// 基准测试结果
/// </summary>
public class BenchmarkResult
{
    public string ComponentName { get; set; } = "";
    public int MessageCount { get; set; }
    public double Throughput { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public long BytesProcessed { get; set; }
    public Dictionary<string, object> AdditionalMetrics { get; set; } = new();
}

/// <summary>
/// 测试消息
/// </summary>
[MemoryPackable]
public partial class TestMessage
{
    public int Id { get; set; }
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
