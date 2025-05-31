using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Benchmark.Core.Models;

namespace PulseRPC.Benchmark.Core.Interfaces;

/// <summary>
/// 基准测试运行器接口，负责运行完整的基准测试流程
/// </summary>
public interface IBenchmarkRunner
{
    /// <summary>
    /// 运行器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 是否正在运行
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 注册的场景列表
    /// </summary>
    IReadOnlyList<IBenchmarkScenario> RegisteredScenarios { get; }

    /// <summary>
    /// 注册基准测试场景
    /// </summary>
    /// <param name="scenario">要注册的场景</param>
    void RegisterScenario(IBenchmarkScenario scenario);

    /// <summary>
    /// 批量注册基准测试场景
    /// </summary>
    /// <param name="scenarios">要注册的场景集合</param>
    void RegisterScenarios(IEnumerable<IBenchmarkScenario> scenarios);

    /// <summary>
    /// 注销基准测试场景
    /// </summary>
    /// <param name="scenarioName">场景名称</param>
    /// <returns>是否成功注销</returns>
    bool UnregisterScenario(string scenarioName);

    /// <summary>
    /// 运行所有注册的场景
    /// </summary>
    /// <param name="configuration">运行配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>运行结果</returns>
    Task<BenchmarkRunResult> RunAllAsync(BenchmarkConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// 运行指定的场景
    /// </summary>
    /// <param name="scenarioNames">要运行的场景名称</param>
    /// <param name="configuration">运行配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>运行结果</returns>
    Task<BenchmarkRunResult> RunScenariosAsync(IEnumerable<string> scenarioNames, BenchmarkConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// 运行单个场景
    /// </summary>
    /// <param name="scenarioName">场景名称</param>
    /// <param name="configuration">运行配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>运行结果</returns>
    Task<BenchmarkResult> RunScenarioAsync(string scenarioName, BenchmarkConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止当前运行
    /// </summary>
    /// <returns>停止任务</returns>
    Task StopAsync();

    /// <summary>
    /// 获取运行进度
    /// </summary>
    /// <returns>进度信息</returns>
    RunnerProgress GetProgress();

    /// <summary>
    /// 运行进度更新事件
    /// </summary>
    event Action<RunnerProgress>? ProgressUpdated;

    /// <summary>
    /// 场景开始事件
    /// </summary>
    event Action<ScenarioStartedEventArgs>? ScenarioStarted;

    /// <summary>
    /// 场景完成事件
    /// </summary>
    event Action<ScenarioCompletedEventArgs>? ScenarioCompleted;

    /// <summary>
    /// 运行完成事件
    /// </summary>
    event Action<BenchmarkRunResult>? RunCompleted;

    /// <summary>
    /// 错误发生事件
    /// </summary>
    event Action<Exception>? ErrorOccurred;
}

/// <summary>
/// 运行器进度信息
/// </summary>
public class RunnerProgress
{
    /// <summary>
    /// 总场景数
    /// </summary>
    public int TotalScenarios { get; set; }

    /// <summary>
    /// 已完成场景数
    /// </summary>
    public int CompletedScenarios { get; set; }

    /// <summary>
    /// 当前运行的场景名称
    /// </summary>
    public string CurrentScenario { get; set; } = string.Empty;

    /// <summary>
    /// 整体进度百分比
    /// </summary>
    public double OverallProgress => TotalScenarios > 0 ? (double)CompletedScenarios / TotalScenarios * 100 : 0;

    /// <summary>
    /// 当前场景进度
    /// </summary>
    public ExecutionProgress? CurrentScenarioProgress { get; set; }

    /// <summary>
    /// 运行开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 预计总剩余时间
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }
}

/// <summary>
/// 场景开始事件参数
/// </summary>
public class ScenarioStartedEventArgs : EventArgs
{
    /// <summary>
    /// 场景名称
    /// </summary>
    public string ScenarioName { get; }

    /// <summary>
    /// 场景索引
    /// </summary>
    public int ScenarioIndex { get; }

    /// <summary>
    /// 总场景数
    /// </summary>
    public int TotalScenarios { get; }

    public ScenarioStartedEventArgs(string scenarioName, int scenarioIndex, int totalScenarios)
    {
        ScenarioName = scenarioName;
        ScenarioIndex = scenarioIndex;
        TotalScenarios = totalScenarios;
    }
}

/// <summary>
/// 场景完成事件参数
/// </summary>
public class ScenarioCompletedEventArgs : EventArgs
{
    /// <summary>
    /// 场景名称
    /// </summary>
    public string ScenarioName { get; }

    /// <summary>
    /// 测试结果
    /// </summary>
    public BenchmarkResult Result { get; }

    /// <summary>
    /// 执行耗时
    /// </summary>
    public TimeSpan Duration { get; }

    public ScenarioCompletedEventArgs(string scenarioName, BenchmarkResult result, TimeSpan duration)
    {
        ScenarioName = scenarioName;
        Result = result;
        Duration = duration;
    }
}

/// <summary>
/// 基准测试运行结果
/// </summary>
public class BenchmarkRunResult
{
    /// <summary>
    /// 运行开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 运行结束时间
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 总运行时间
    /// </summary>
    public TimeSpan TotalDuration => EndTime - StartTime;

    /// <summary>
    /// 场景结果列表
    /// </summary>
    public List<BenchmarkResult> ScenarioResults { get; set; } = new();

    /// <summary>
    /// 是否所有场景都成功
    /// </summary>
    public bool AllScenariosSuccessful => ScenarioResults.All(r => r.IsSuccessful);

    /// <summary>
    /// 成功的场景数量
    /// </summary>
    public int SuccessfulScenariosCount => ScenarioResults.Count(r => r.IsSuccessful);

    /// <summary>
    /// 失败的场景数量
    /// </summary>
    public int FailedScenariosCount => ScenarioResults.Count(r => !r.IsSuccessful);

    /// <summary>
    /// 聚合结果
    /// </summary>
    public AggregatedBenchmarkResult? AggregatedResult { get; set; }

    /// <summary>
    /// 运行配置
    /// </summary>
    public BenchmarkConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// 运行期间发生的错误
    /// </summary>
    public List<Exception> Errors { get; set; } = new();
}
