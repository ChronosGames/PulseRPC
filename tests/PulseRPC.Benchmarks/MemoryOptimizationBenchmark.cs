using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using PulseRPC.Transport;
using PulseRPC.Transport.Tcp;

namespace PulseRPC.Benchmarks;

/// <summary>
/// 内存优化效果验证基准测试
/// </summary>
[MemoryDiagnoser]
public class MemoryOptimizationBenchmark
{
    private const int LargePacketSize = 10 * 1024 * 1024; // 10MB
    private const int ChunkSize = 64 * 1024; // 64KB
    private const int TestIterations = 100;

    [Benchmark(Baseline = true)]
    public async Task BenchmarkOriginalImplementationAsync()
    {
        var result = await BenchmarkOriginalImplementation();
        return;
    }

    [Benchmark]
    public async Task BenchmarkOptimizedImplementationAsync()
    {
        var result = await BenchmarkOptimizedImplementation();
        return;
    }

    /// <summary>
    /// 对比原始实现和优化实现的内存使用
    /// </summary>
    public static async Task<BenchmarkResult> RunMemoryComparisonAsync()
    {
        var result = new BenchmarkResult();

        Console.WriteLine("开始内存优化基准测试...");
        Console.WriteLine($"测试包大小: {LargePacketSize / 1024 / 1024}MB");
        Console.WriteLine($"分片大小: {ChunkSize / 1024}KB");
        Console.WriteLine($"测试迭代: {TestIterations}次");
        Console.WriteLine();

        // 强制GC以获得准确的基准
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var baselineMemory = GC.GetTotalMemory(false);
        Console.WriteLine($"基准内存使用: {baselineMemory / 1024 / 1024}MB");

        // 测试原始实现
        Console.WriteLine("测试原始大包分片实现...");
        var originalResult = await BenchmarkOriginalImplementation();
        result.OriginalMemoryPeak = originalResult.MemoryPeak;
        result.OriginalProcessingTime = originalResult.ProcessingTime;

        // 清理内存
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // 测试优化实现
        Console.WriteLine("测试优化大包分片实现...");
        var optimizedResult = await BenchmarkOptimizedImplementation();
        result.OptimizedMemoryPeak = optimizedResult.MemoryPeak;
        result.OptimizedProcessingTime = optimizedResult.ProcessingTime;

        // 计算改进效果
        result.MemoryReduction = (double)(originalResult.MemoryPeak - optimizedResult.MemoryPeak) / originalResult.MemoryPeak;
        result.PerformanceImprovement = (double)(originalResult.ProcessingTime - optimizedResult.ProcessingTime) / originalResult.ProcessingTime;

        return result;
    }

    /// <summary>
    /// 测试原始实现
    /// </summary>
    private static async Task<TestResult> BenchmarkOriginalImplementation()
    {
        var stopwatch = Stopwatch.StartNew();
        var memoryPeak = 0L;

        for (int iteration = 0; iteration < TestIterations; iteration++)
        {
            // 模拟原始实现的内存分配模式
            var chunks = await SimulateOriginalLargePacketProcessing();

            // 记录内存峰值
            var currentMemory = GC.GetTotalMemory(false);
            memoryPeak = Math.Max(memoryPeak, currentMemory);

            // 清理（模拟原始实现的清理）
            foreach (var chunk in chunks)
            {
                // 原始实现中每个分片都是独立的byte[]
            }
        }

        stopwatch.Stop();

        return new TestResult
        {
            MemoryPeak = memoryPeak,
            ProcessingTime = stopwatch.ElapsedMilliseconds
        };
    }

    /// <summary>
    /// 测试优化实现
    /// </summary>
    private static async Task<TestResult> BenchmarkOptimizedImplementation()
    {
        var stopwatch = Stopwatch.StartNew();
        var memoryPeak = 0L;
        var packetHandler = new LargePacketHandler();

        try
        {
            for (int iteration = 0; iteration < TestIterations; iteration++)
            {
                // 使用优化的分片处理器
                await SimulateOptimizedLargePacketProcessing(packetHandler);

                // 记录内存峰值
                var currentMemory = GC.GetTotalMemory(false);
                memoryPeak = Math.Max(memoryPeak, currentMemory);
            }
        }
        finally
        {
            packetHandler.Dispose();
        }

        stopwatch.Stop();

        return new TestResult
        {
            MemoryPeak = memoryPeak,
            ProcessingTime = stopwatch.ElapsedMilliseconds
        };
    }

    /// <summary>
    /// 模拟原始大包处理 - 多次内存拷贝
    /// </summary>
    private static async Task<byte[][]> SimulateOriginalLargePacketProcessing()
    {
        var totalChunks = (LargePacketSize + ChunkSize - 1) / ChunkSize;
        var chunks = new byte[totalChunks][];

        // 模拟接收分片（每个分片独立存储）
        for (int i = 0; i < totalChunks; i++)
        {
            var chunkSize = Math.Min(ChunkSize, LargePacketSize - i * ChunkSize);
            chunks[i] = new byte[chunkSize]; // 每个分片单独分配

            // 模拟填充数据
            for (int j = 0; j < chunkSize; j++)
            {
                chunks[i][j] = (byte)(i + j);
            }
        }

        // 模拟原始实现的重组过程
        var totalSize = 0;
        foreach (var chunk in chunks)
        {
            totalSize += chunk.Length;
        }

        var result = new byte[totalSize]; // 额外分配完整大小的数组
        var offset = 0;

        // 逐一拷贝分片到结果数组
        foreach (var chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }

        await Task.Delay(1); // 模拟异步处理
        return chunks;
    }

    /// <summary>
    /// 模拟优化大包处理 - 减少内存拷贝
    /// </summary>
    private static async Task SimulateOptimizedLargePacketProcessing(LargePacketHandler packetHandler)
    {
        var totalChunks = (LargePacketSize + ChunkSize - 1) / ChunkSize;
        var chunkId = 1;

        // 模拟接收分片
        for (int i = 0; i < totalChunks; i++)
        {
            var chunkSize = Math.Min(ChunkSize, LargePacketSize - i * ChunkSize);
            var chunkData = new byte[chunkSize];

            // 模拟填充数据
            for (int j = 0; j < chunkSize; j++)
            {
                chunkData[j] = (byte)(i + j);
            }

            var chunkHeader = new ChunkHeader(chunkId, i, totalChunks, chunkSize);

            // 使用优化的处理器处理分片
            var isComplete = packetHandler.ProcessChunk(chunkHeader, chunkData, out var completeData);

            if (isComplete)
            {
                // 完整数据已重组，可以直接使用
                // completeData 指向优化后的内存布局
                break;
            }
        }

        await Task.Delay(1); // 模拟异步处理
    }

    /// <summary>
    /// 打印基准测试结果
    /// </summary>
    public static void PrintResults(BenchmarkResult result)
    {
        Console.WriteLine();
        Console.WriteLine("=== 内存优化基准测试结果 ===");
        Console.WriteLine();

        Console.WriteLine("原始实现:");
        Console.WriteLine($"  内存峰值: {result.OriginalMemoryPeak / 1024 / 1024:F2} MB");
        Console.WriteLine($"  处理时间: {result.OriginalProcessingTime} ms");
        Console.WriteLine();

        Console.WriteLine("优化实现:");
        Console.WriteLine($"  内存峰值: {result.OptimizedMemoryPeak / 1024 / 1024:F2} MB");
        Console.WriteLine($"  处理时间: {result.OptimizedProcessingTime} ms");
        Console.WriteLine();

        Console.WriteLine("改进效果:");
        Console.WriteLine($"  内存使用减少: {result.MemoryReduction:P2}");
        Console.WriteLine($"  性能提升: {result.PerformanceImprovement:P2}");
        Console.WriteLine();

        if (result.MemoryReduction > 0)
        {
            Console.WriteLine($"✅ 内存优化成功！节省了 {result.MemoryReduction:P2} 的内存使用");
        }
        else
        {
            Console.WriteLine($"❌ 内存优化未达到预期效果");
        }

        if (result.PerformanceImprovement > 0)
        {
            Console.WriteLine($"✅ 性能优化成功！提升了 {result.PerformanceImprovement:P2} 的处理速度");
        }
        else
        {
            Console.WriteLine($"❌ 性能优化未达到预期效果");
        }
    }
}

/// <summary>
/// 基准测试结果
/// </summary>
public struct BenchmarkResult
{
    public long OriginalMemoryPeak;
    public long OptimizedMemoryPeak;
    public long OriginalProcessingTime;
    public long OptimizedProcessingTime;
    public double MemoryReduction;
    public double PerformanceImprovement;
}

/// <summary>
/// 单个测试结果
/// </summary>
internal struct TestResult
{
    public long MemoryPeak;
    public long ProcessingTime;
}
