using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Core.Abstract;
using PulseRPC.Benchmark.Core.Interfaces;
using PulseRPC.Benchmark.Core.Models;

namespace PulseRPC.Benchmark.Scenarios.Basic
{
    /// <summary>
    /// Ping-Pong 基准测试场景
    /// 测试基本的请求-响应延迟
    /// </summary>
    public class PingPongScenario : BaseBenchmarkScenario
    {
        private readonly byte[] _pingData;
        private readonly Stopwatch _stopwatch = new();

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        public PingPongScenario(ILogger<PingPongScenario> logger) : base(logger)
        {
            _pingData = GenerateTestData(64); // 默认64字节的ping数据
        }

        /// <inheritdoc />
        public override string Name => "PingPong";

        /// <inheritdoc />
        public override string Description => "基础的Ping-Pong延迟测试，测量单次请求-响应的往返时间";

        /// <inheritdoc />
        public override string Version => "1.0.0";

        /// <inheritdoc />
        public override string Category => ScenarioCategories.Latency;

        /// <inheritdoc />
        public override ScenarioRequirements GetRequirements()
        {
            return new ScenarioRequirements
            {
                SupportedTransports = new[] { TransportTypes.Tcp, TransportTypes.Kcp, TransportTypes.Memory },
                MinClients = 1,
                MaxClients = 1,
                RequiresNetwork = true,
                MinTestDuration = TimeSpan.FromSeconds(10),
                MaxTestDuration = TimeSpan.FromMinutes(5)
            };
        }

        /// <inheritdoc />
        protected override async Task DoInitializeAsync(BenchmarkConfiguration configuration, CancellationToken cancellationToken)
        {
            Logger.LogInformation("初始化PingPong场景，消息大小: {MessageSize} 字节", configuration.MessageSizeBytes);

            // 验证配置
            if (configuration.ConcurrentConnections > 1)
            {
                Logger.LogWarning("PingPong场景建议使用单连接，当前配置: {Connections} 个连接", configuration.ConcurrentConnections);
            }

            // 等待传输层准备就绪
            await Task.Delay(100, cancellationToken);
        }

        /// <inheritdoc />
        protected override async Task DoWarmupAsync(BenchmarkConfiguration configuration, CancellationToken cancellationToken)
        {
            Logger.LogInformation("开始PingPong场景预热，预热时间: {WarmupSeconds} 秒", configuration.WarmupSeconds);

            var warmupEnd = DateTime.UtcNow.AddSeconds(configuration.WarmupSeconds);
            var warmupCount = 0;

            while (DateTime.UtcNow < warmupEnd && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 发送预热ping
                    var success = await Transport.SendAsync(_pingData, cancellationToken);
                    if (success)
                    {
                        // 等待响应
                        var response = await Transport.ReceiveAsync(TimeSpan.FromSeconds(5), cancellationToken);
                        if (response != null)
                        {
                            warmupCount++;
                        }
                    }

                    // 预热间隔
                    await Task.Delay(100, cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "预热过程中发生错误");
                }
            }

            Logger.LogInformation("PingPong场景预热完成，预热请求数: {WarmupCount}", warmupCount);
        }

        /// <inheritdoc />
        protected override async Task<BenchmarkResult> DoExecuteAsync(
            BenchmarkConfiguration configuration,
            IProgress<ExecutionProgress>? progress,
            CancellationToken cancellationToken)
        {
            Logger.LogInformation("开始执行PingPong基准测试，持续时间: {Duration} 秒", configuration.DurationSeconds);

            var latencyMetrics = new LatencyMetrics();
            var throughputMetrics = new ThroughputMetrics();
            var resourceMetrics = new ResourceMetrics();

            var testData = GenerateTestData(configuration.MessageSizeBytes);
            var latencies = new List<double>();

            var startTime = DateTime.UtcNow;
            var endTime = startTime.AddSeconds(configuration.DurationSeconds);
            var lastProgressReport = startTime;

            long totalOperations = 0;
            long successfulOperations = 0;
            long failedOperations = 0;

            while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _stopwatch.Restart();

                    // 发送ping
                    var sendSuccess = await Transport.SendAsync(testData, cancellationToken);
                    if (!sendSuccess)
                    {
                        failedOperations++;
                        totalOperations++;
                        continue;
                    }

                    // 接收pong
                    var response = await Transport.ReceiveAsync(TimeSpan.FromSeconds(10), cancellationToken);

                    _stopwatch.Stop();
                    var latencyMs = _stopwatch.Elapsed.TotalMilliseconds;

                    if (response != null)
                    {
                        latencies.Add(latencyMs);
                        successfulOperations++;
                    }
                    else
                    {
                        failedOperations++;
                    }

                    totalOperations++;

                    // 报告进度
                    var now = DateTime.UtcNow;
                    if (now - lastProgressReport >= TimeSpan.FromSeconds(1))
                    {
                        var elapsed = now - startTime;
                        var progressPercent = (elapsed.TotalSeconds / configuration.DurationSeconds) * 100;

                        progress?.Report(new ExecutionProgress
                        {
                            CompletedSteps = (int)totalOperations,
                            TotalSteps = configuration.OperationsPerConnection ?? (int)(configuration.DurationSeconds * 100),
                            CurrentOperation = "执行Ping-Pong测试",
                            StartTime = startTime,
                            EstimatedTimeRemaining = TimeSpan.FromSeconds(configuration.DurationSeconds - elapsed.TotalSeconds)
                        });

                        lastProgressReport = now;
                    }

                    // 请求间隔
                    if (configuration.RequestIntervalMs > 0)
                    {
                        await Task.Delay(configuration.RequestIntervalMs, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "执行ping-pong操作时发生错误");
                    failedOperations++;
                    totalOperations++;
                }
            }

            var actualDuration = DateTime.UtcNow - startTime;

            // 计算延迟指标
            if (latencies.Count > 0)
            {
                latencies.Sort();
                latencyMetrics.AverageMs = latencies.Average();
                latencyMetrics.MinMs = latencies.Min();
                latencyMetrics.MaxMs = latencies.Max();
                latencyMetrics.MedianMs = GetPercentile(latencies, 50);
                latencyMetrics.P50Ms = GetPercentile(latencies, 50);
                latencyMetrics.P90Ms = GetPercentile(latencies, 90);
                latencyMetrics.P95Ms = GetPercentile(latencies, 95);
                latencyMetrics.P99Ms = GetPercentile(latencies, 99);
                latencyMetrics.P999Ms = GetPercentile(latencies, 99.9);
                latencyMetrics.StandardDeviationMs = CalculateStandardDeviation(latencies);
                latencyMetrics.SampleCount = latencies.Count;
            }

            // 计算吞吐量指标
            throughputMetrics.TotalOperations = totalOperations;
            throughputMetrics.SuccessfulOperations = successfulOperations;
            throughputMetrics.FailedOperations = failedOperations;
            throughputMetrics.OperationsPerSecond = successfulOperations / actualDuration.TotalSeconds;

            // 基础资源指标
            // 这里可以添加具体的资源监控，当前仅作为占位符

            Logger.LogInformation("PingPong测试完成 - 总操作: {Total}, 成功: {Success}, 失败: {Failed}, 平均延迟: {AvgLatency:F2}ms",
                totalOperations, successfulOperations, failedOperations, latencyMetrics.AverageMs);

            return CreateResult(configuration, latencyMetrics, throughputMetrics, resourceMetrics);
        }

        /// <inheritdoc />
        protected override async Task DoCleanupAsync(CancellationToken cancellationToken)
        {
            Logger.LogInformation("清理PingPong场景资源");
            await Task.Delay(50, cancellationToken); // 简单的清理延迟
        }

        /// <summary>
        /// 计算百分位数
        /// </summary>
        private static double GetPercentile(List<double> sortedValues, double percentile)
        {
            if (sortedValues.Count == 0) return 0;

            var index = (percentile / 100.0) * (sortedValues.Count - 1);
            var lower = (int)Math.Floor(index);
            var upper = (int)Math.Ceiling(index);

            if (lower == upper)
                return sortedValues[lower];

            var weight = index - lower;
            return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
        }

        /// <summary>
        /// 计算标准差
        /// </summary>
        private static double CalculateStandardDeviation(List<double> values)
        {
            if (values.Count <= 1) return 0;

            var mean = values.Average();
            var sumOfSquares = values.Sum(x => Math.Pow(x - mean, 2));
            return Math.Sqrt(sumOfSquares / (values.Count - 1));
        }
    }
}
