using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Memory;
using PulseRPC.Server.Memory;
using PulseRPC.Server.Processing;

namespace PulseRPC.Phase1.Benchmarks;

/// <summary>
/// 第一阶段性能基准测试
/// 验证零拷贝循环缓冲区、自适应批处理调度器和分层内存池的性能
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== PulseRPC 第一阶段性能基准测试 ===\n");
        
        // 1. ZeroCopyCircularBuffer 性能测试
        await BenchmarkZeroCopyCircularBuffer();
        
        // 2. TieredMemoryPool 性能测试
        BenchmarkTieredMemoryPool();
        
        // 3. AdaptiveBatchScheduler 性能测试
        await BenchmarkAdaptiveBatchScheduler();
        
        Console.WriteLine("\n=== 所有测试完成 ===");
    }
    
    static async Task BenchmarkZeroCopyCircularBuffer()
    {
        Console.WriteLine("1. ZeroCopyCircularBuffer 性能基准测试");
        Console.WriteLine("----------------------------------------");
        
        const int iterations = 10_000_000;
        const int bufferSize = 1024;
        
        using var buffer = new ZeroCopyCircularBuffer<int>(bufferSize);
        
        // 单线程吞吐量测试
        var sw = Stopwatch.StartNew();
        
        for (int i = 0; i < iterations / 2; i++)
        {
            buffer.TryEnqueue(i);
            buffer.TryDequeue(out _);
        }
        
        sw.Stop();
        var opsPerSecond = iterations / sw.Elapsed.TotalSeconds;
        
        Console.WriteLine($"单线程操作数：{iterations:N0}");
        Console.WriteLine($"执行时间：{sw.ElapsedMilliseconds:N0} ms");
        Console.WriteLine($"吞吐量：{opsPerSecond:N0} ops/sec");
        
        // 并发测试
        buffer.Clear();
        
        const int producers = 4;
        const int consumers = 4;
        const int itemsPerProducer = 100_000;
        
        sw.Restart();
        
        var producerTasks = new Task[producers];
        var consumerTasks = new Task[consumers];
        var totalConsumed = 0;
        
        // 启动生产者
        for (int p = 0; p < producers; p++)
        {
            int producerId = p;
            producerTasks[p] = Task.Run(async () =>
            {
                for (int i = 0; i < itemsPerProducer; i++)
                {
                    while (!buffer.TryEnqueue(producerId * itemsPerProducer + i))
                    {
                        await Task.Yield();
                    }
                }
            });
        }
        
        // 启动消费者
        for (int c = 0; c < consumers; c++)
        {
            consumerTasks[c] = Task.Run(async () =>
            {
                int localCount = 0;
                int targetCount = producers * itemsPerProducer / consumers;
                
                while (localCount < targetCount)
                {
                    if (buffer.TryDequeue(out _))
                    {
                        localCount++;
                        Interlocked.Increment(ref totalConsumed);
                    }
                    else
                    {
                        await Task.Yield();
                    }
                }
            });
        }
        
        await Task.WhenAll(producerTasks);
        await Task.WhenAll(consumerTasks);
        
        sw.Stop();
        
        var concurrentOpsPerSecond = (producers * itemsPerProducer * 2) / sw.Elapsed.TotalSeconds;
        
        Console.WriteLine($"并发测试 - 生产者：{producers}, 消费者：{consumers}");
        Console.WriteLine($"总操作数：{producers * itemsPerProducer * 2:N0}");
        Console.WriteLine($"执行时间：{sw.ElapsedMilliseconds:N0} ms");
        Console.WriteLine($"并发吞吐量：{concurrentOpsPerSecond:N0} ops/sec");
        Console.WriteLine($"处理的消息数：{totalConsumed:N0}");
        
        var stats = buffer.GetStatistics();
        Console.WriteLine($"缓冲区统计 - 容量：{stats.Capacity}, 利用率：{stats.Utilization:P2}");
        Console.WriteLine();
    }
    
    static void BenchmarkTieredMemoryPool()
    {
        Console.WriteLine("2. TieredMemoryPool 性能基准测试");
        Console.WriteLine("----------------------------------");
        
        var pool = TieredMemoryPool.Instance;
        const int iterations = 1_000_000;
        
        // 热身
        for (int i = 0; i < 1000; i++)
        {
            var buffer = pool.Rent(1024);
            pool.Return(buffer);
        }
        
        var sw = Stopwatch.StartNew();
        
        // 单线程租用/归还测试
        for (int i = 0; i < iterations; i++)
        {
            var buffer = pool.Rent(1024);
            pool.Return(buffer);
        }
        
        sw.Stop();
        var rentReturnOps = (iterations * 2) / sw.Elapsed.TotalSeconds;
        
        Console.WriteLine($"单线程租用/归还操作数：{iterations * 2:N0}");
        Console.WriteLine($"执行时间：{sw.ElapsedMilliseconds:N0} ms");
        Console.WriteLine($"吞吐量：{rentReturnOps:N0} ops/sec");
        
        // 多种大小的测试
        var sizes = new[] { 128, 512, 1024, 4096, 8192 };
        sw.Restart();
        
        const int sizeTestIterations = 200_000;
        for (int i = 0; i < sizeTestIterations; i++)
        {
            foreach (var size in sizes)
            {
                var buffer = pool.Rent(size);
                pool.Return(buffer);
            }
        }
        
        sw.Stop();
        var mixedSizeOps = (sizeTestIterations * sizes.Length * 2) / sw.Elapsed.TotalSeconds;
        
        Console.WriteLine($"多大小测试操作数：{sizeTestIterations * sizes.Length * 2:N0}");
        Console.WriteLine($"执行时间：{sw.ElapsedMilliseconds:N0} ms");
        Console.WriteLine($"吞吐量：{mixedSizeOps:N0} ops/sec");
        
        var stats = pool.GetStatistics();
        Console.WriteLine($"内存池统计 - 总租用：{stats.TotalRents:N0}, 缓存命中率：{stats.CacheHitRatio:P2}");
        Console.WriteLine();
    }
    
    static async Task BenchmarkAdaptiveBatchScheduler()
    {
        Console.WriteLine("3. AdaptiveBatchScheduler 性能基准测试");
        Console.WriteLine("---------------------------------------");
        
        var scheduler = new AdaptiveBatchScheduler(NullLogger<AdaptiveBatchScheduler>.Instance);
        
        // 注册一个模拟的批处理器
        var processor = new MockBatchProcessor();
        scheduler.RegisterProcessor(processor);
        
        scheduler.Start();
        
        const int recordingIterations = 100_000;
        
        var sw = Stopwatch.StartNew();
        
        // 模拟批处理操作记录
        for (int i = 0; i < recordingIterations; i++)
        {
            var batchSize = 16 + (i % 32); // 16-48之间变化
            var processingTime = TimeSpan.FromMicroseconds(100 + (i % 50)); // 100-150微秒
            var queueDepth = 10 + (i % 80); // 10-90之间变化
            
            scheduler.RecordBatchOperation(batchSize, processingTime, queueDepth);
        }
        
        sw.Stop();
        
        var recordingOps = recordingIterations / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"性能记录操作数：{recordingIterations:N0}");
        Console.WriteLine($"执行时间：{sw.ElapsedMilliseconds:N0} ms");
        Console.WriteLine($"吞吐量：{recordingOps:N0} ops/sec");
        
        // 获取自适应指标
        var metrics = scheduler.GetMetrics();
        Console.WriteLine($"当前批处理间隔：{metrics.CurrentBatchInterval} ms");
        Console.WriteLine($"当前批处理大小：{metrics.CurrentBatchSize}");
        Console.WriteLine($"平均吞吐量：{metrics.AverageThroughput:F2}");
        Console.WriteLine($"平均延迟：{metrics.AverageLatency.TotalMicroseconds:F2} μs");
        Console.WriteLine($"当前负载：{metrics.CurrentLoad:F2}");
        Console.WriteLine($"总批次数：{metrics.TotalBatches:N0}");
        Console.WriteLine($"适应次数：{metrics.AdaptationCount:N0}");
        
        await scheduler.StopAsync();
        await scheduler.DisposeAsync();
        
        Console.WriteLine();
    }
    
    /// <summary>
    /// 模拟批处理器用于测试
    /// </summary>
    class MockBatchProcessor : IBatchProcessor
    {
        public void OnParametersUpdated(int newBatchInterval, int newBatchSize)
        {
            // 模拟参数更新
        }
    }
}