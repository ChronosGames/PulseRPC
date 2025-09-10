using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Running;

namespace PulseRPC.Phase2.Benchmarks;

/// <summary>
/// 第二阶段性能基准测试 - Source Generator优化验证
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== PulseRPC 第二阶段性能基准测试 ===");
        Console.WriteLine("验证Source Generator优化效果\n");

        if (args.Length > 0 && args[0] == "--benchmark")
        {
            // 运行BenchmarkDotNet基准测试
            BenchmarkRunner.Run<SourceGeneratorBenchmarks>();
        }
        else
        {
            // 运行快速验证测试
            await RunQuickValidation();
        }

        Console.WriteLine("\n=== 第二阶段测试完成 ===");
    }

    static async Task RunQuickValidation()
    {
        Console.WriteLine("运行快速验证测试...\n");

        var validator = new SourceGeneratorValidator();
        await validator.ValidateGeneratedCodeAsync();

        Console.WriteLine("快速验证完成！");
        Console.WriteLine("要运行详细的BenchmarkDotNet测试，请使用参数 --benchmark");
    }
}