using BenchmarkDotNet.Running;
using PulseRPC.Benchmarks;

namespace PulseRPC.Benchmarks;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            Console.WriteLine("PulseRPC 基准测试工具");
            Console.WriteLine("使用方式:");
            Console.WriteLine("  dotnet run -- --benchmark     运行 BenchmarkDotNet 基准测试");
            Console.WriteLine("  dotnet run                    运行简单的内存对比测试");
            Console.WriteLine();

            if (args.Length > 0 && args[0] == "--benchmark")
            {
                Console.WriteLine("启动 BenchmarkDotNet 基准测试...");
                BenchmarkRunner.Run<MemoryOptimizationBenchmark>();
                return 0;
            }
            else
            {
                Console.WriteLine("运行简单的内存对比测试...");
                var result = await MemoryOptimizationBenchmark.RunMemoryComparisonAsync();
                MemoryOptimizationBenchmark.PrintResults(result);

                // 如果内存优化和性能都有提升，返回成功
                return result.MemoryReduction > 0 && result.PerformanceImprovement > 0 ? 0 : 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"基准测试执行失败: {ex.Message}");
            Console.Error.WriteLine($"详细错误: {ex}");
            return 1;
        }
    }
}
