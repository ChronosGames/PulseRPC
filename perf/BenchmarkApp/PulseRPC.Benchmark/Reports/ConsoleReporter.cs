using PulseRPC.Benchmark.Models;

namespace PulseRPC.Benchmark.Reports;

/// <summary>
/// 控制台报告器
/// </summary>
public static class ConsoleReporter
{
    public static void Print(BenchmarkResult result)
    {
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($"  基准测试结果: {result.ScenarioName}");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // 基本信息
        Console.WriteLine($"  状态: {(result.IsSuccessful ? "成功" : "失败")}");
        Console.WriteLine($"  开始时间: {result.StartTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"  结束时间: {result.EndTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"  总时长: {result.Duration.TotalSeconds:F2} 秒");

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            Console.WriteLine($"  错误: {result.ErrorMessage}");
        }

        // 延迟指标
        if (result.Latency.SampleCount > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  ─── 延迟指标 ───────────────────────────────────────────────");
            Console.WriteLine($"  样本数:     {result.Latency.SampleCount:N0}");
            Console.WriteLine($"  平均延迟:   {result.Latency.AverageMs:F3} ms");
            Console.WriteLine($"  最小延迟:   {result.Latency.MinMs:F3} ms");
            Console.WriteLine($"  最大延迟:   {result.Latency.MaxMs:F3} ms");
            Console.WriteLine($"  P50:        {result.Latency.P50Ms:F3} ms");
            Console.WriteLine($"  P90:        {result.Latency.P90Ms:F3} ms");
            Console.WriteLine($"  P95:        {result.Latency.P95Ms:F3} ms");
            Console.WriteLine($"  P99:        {result.Latency.P99Ms:F3} ms");
            Console.WriteLine($"  标准差:     {result.Latency.StandardDeviationMs:F3} ms");
        }

        // 吞吐量指标
        if (result.Throughput.TotalOperations > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  ─── 吞吐量指标 ─────────────────────────────────────────────");
            Console.WriteLine($"  总操作数:   {result.Throughput.TotalOperations:N0}");
            Console.WriteLine($"  成功:       {result.Throughput.SuccessfulOperations:N0}");
            Console.WriteLine($"  失败:       {result.Throughput.FailedOperations:N0}");
            Console.WriteLine($"  成功率:     {result.Throughput.SuccessRatePercentage:F2}%");
            Console.WriteLine($"  OPS:        {result.Throughput.OperationsPerSecond:F2} ops/s");

            if (result.Throughput.TotalBytesTransferred > 0)
            {
                var mbTransferred = result.Throughput.TotalBytesTransferred / (1024.0 * 1024.0);
                var mbps = result.Throughput.AverageBandwidthBps / (1024.0 * 1024.0);
                Console.WriteLine($"  数据传输:   {mbTransferred:F2} MB");
                Console.WriteLine($"  带宽:       {mbps:F2} MB/s");
            }
        }

        // 资源指标
        if (result.Resources.PeakMemoryUsageBytes > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  ─── 资源指标 ───────────────────────────────────────────────");
            Console.WriteLine($"  平均内存:   {result.Resources.AverageMemoryUsageBytes / (1024.0 * 1024.0):F2} MB");
            Console.WriteLine($"  峰值内存:   {result.Resources.PeakMemoryUsageBytes / (1024.0 * 1024.0):F2} MB");
            Console.WriteLine($"  GC Gen0:    {result.Resources.GcGen0Collections}");
            Console.WriteLine($"  GC Gen1:    {result.Resources.GcGen1Collections}");
            Console.WriteLine($"  GC Gen2:    {result.Resources.GcGen2Collections}");
        }

        // 稳定性指标
        if (result.Stability.MemorySampleCount > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  ─── 稳定性指标 ─────────────────────────────────────────────");
            Console.WriteLine($"  内存泄漏:   {(result.Stability.MemoryLeakDetected ? "检测到" : "未检测到")}");
            Console.WriteLine($"  内存增长率: {result.Stability.MemoryGrowthRate / 1024.0:F2} KB/sample");
            Console.WriteLine($"  连接失败:   {result.Stability.ConnectionFailures}");
            Console.WriteLine($"  内存样本:   {result.Stability.MemorySampleCount}");
        }

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();
    }
}
