using System;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Benchmark.Core.Models;

namespace PulseRPC.Benchmark.Core.Interfaces
{
    /// <summary>
    /// 基准测试执行器接口，负责执行具体的基准测试场景
    /// </summary>
    public interface IBenchmarkExecutor
    {
        /// <summary>
        /// 执行器名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 是否正在运行
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// 当前执行的场景
        /// </summary>
        IBenchmarkScenario? CurrentScenario { get; }

        /// <summary>
        /// 执行基准测试场景
        /// </summary>
        /// <param name="scenario">要执行的场景</param>
        /// <param name="configuration">测试配置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>测试结果</returns>
        Task<BenchmarkResult> ExecuteAsync(IBenchmarkScenario scenario, BenchmarkConfiguration configuration, CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止当前执行的测试
        /// </summary>
        /// <returns>停止任务</returns>
        Task StopAsync();

        /// <summary>
        /// 获取执行进度
        /// </summary>
        /// <returns>执行进度信息</returns>
        ExecutionProgress GetProgress();

        /// <summary>
        /// 进度更新事件
        /// </summary>
        event Action<ExecutionProgress>? ProgressUpdated;

        /// <summary>
        /// 测试完成事件
        /// </summary>
        event Action<BenchmarkResult>? TestCompleted;

        /// <summary>
        /// 错误发生事件
        /// </summary>
        event Action<Exception>? ErrorOccurred;
    }

    /// <summary>
    /// 执行进度信息
    /// </summary>
    public class ExecutionProgress
    {
        /// <summary>
        /// 总步骤数
        /// </summary>
        public int TotalSteps { get; set; }

        /// <summary>
        /// 已完成步骤数
        /// </summary>
        public int CompletedSteps { get; set; }

        /// <summary>
        /// 进度百分比 (0-100)
        /// </summary>
        public double PercentageComplete => TotalSteps > 0 ? (double)CompletedSteps / TotalSteps * 100 : 0;

        /// <summary>
        /// 当前操作描述
        /// </summary>
        public string CurrentOperation { get; set; } = string.Empty;

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 预计剩余时间
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }
    }
}
