using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Benchmark.Core.Models;

namespace PulseRPC.Benchmark.Core.Interfaces
{
    /// <summary>
    /// 基准测试场景接口，定义测试场景的基本操作
    /// </summary>
    public interface IBenchmarkScenario
    {
        /// <summary>
        /// 场景名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 场景描述
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 场景版本
        /// </summary>
        string Version { get; }

        /// <summary>
        /// 场景类别
        /// </summary>
        string Category { get; }

        /// <summary>
        /// 是否支持指定的传输类型
        /// </summary>
        /// <param name="transportType">传输类型</param>
        /// <returns>是否支持</returns>
        bool SupportsTransport(string transportType);

        /// <summary>
        /// 获取场景的配置要求
        /// </summary>
        /// <returns>配置要求</returns>
        ScenarioRequirements GetRequirements();

        /// <summary>
        /// 初始化场景
        /// </summary>
        /// <param name="transport">传输层实例</param>
        /// <param name="configuration">基准测试配置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>初始化任务</returns>
        Task InitializeAsync(IBenchmarkTransport transport, BenchmarkConfiguration configuration, CancellationToken cancellationToken = default);

        /// <summary>
        /// 执行预热操作
        /// </summary>
        /// <param name="configuration">基准测试配置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>预热任务</returns>
        Task WarmupAsync(BenchmarkConfiguration configuration, CancellationToken cancellationToken = default);

        /// <summary>
        /// 执行基准测试
        /// </summary>
        /// <param name="configuration">基准测试配置</param>
        /// <param name="progress">进度报告器</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>测试结果</returns>
        Task<BenchmarkResult> ExecuteAsync(BenchmarkConfiguration configuration, IProgress<ExecutionProgress>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 清理场景资源
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>清理任务</returns>
        Task CleanupAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取场景的默认配置
        /// </summary>
        /// <returns>默认配置</returns>
        BenchmarkConfiguration GetDefaultConfiguration();

        /// <summary>
        /// 获取场景的推荐配置集合
        /// </summary>
        /// <returns>推荐配置列表</returns>
        BenchmarkConfiguration[] GetRecommendedConfigurations();

        /// <summary>
        /// 生成测试数据
        /// </summary>
        /// <param name="size">数据大小</param>
        /// <returns>测试数据</returns>
        byte[] GenerateTestData(int size);
    }

    /// <summary>
    /// 场景配置要求
    /// </summary>
    public class ScenarioRequirements
    {
        /// <summary>
        /// 支持的传输类型列表
        /// </summary>
        public IReadOnlyList<string> SupportedTransports { get; set; } = new List<string>();

        /// <summary>
        /// 最小客户端连接数
        /// </summary>
        public int MinClients { get; set; } = 1;

        /// <summary>
        /// 最大客户端连接数
        /// </summary>
        public int MaxClients { get; set; } = int.MaxValue;

        /// <summary>
        /// 预期的最小测试时长
        /// </summary>
        public TimeSpan MinTestDuration { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// 预期的最大测试时长
        /// </summary>
        public TimeSpan MaxTestDuration { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// 需要的内存量（字节）
        /// </summary>
        public long RequiredMemoryBytes { get; set; } = 0;

        /// <summary>
        /// 是否需要网络连接
        /// </summary>
        public bool RequiresNetwork { get; set; } = true;

        /// <summary>
        /// 自定义要求
        /// </summary>
        public IDictionary<string, object> CustomRequirements { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// 场景验证结果
    /// </summary>
    public class ScenarioValidationResult
    {
        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 错误消息列表
        /// </summary>
        public List<string> ErrorMessages { get; set; } = new();

        /// <summary>
        /// 警告消息列表
        /// </summary>
        public List<string> WarningMessages { get; set; } = new();

        /// <summary>
        /// 验证的配置项
        /// </summary>
        public Dictionary<string, object> ValidatedProperties { get; set; } = new();

        /// <summary>
        /// 添加错误消息
        /// </summary>
        /// <param name="message">错误消息</param>
        public void AddError(string message)
        {
            ErrorMessages.Add(message);
            IsValid = false;
        }

        /// <summary>
        /// 添加警告消息
        /// </summary>
        /// <param name="message">警告消息</param>
        public void AddWarning(string message)
        {
            WarningMessages.Add(message);
        }

        /// <summary>
        /// 创建成功的验证结果
        /// </summary>
        /// <returns>成功的验证结果</returns>
        public static ScenarioValidationResult Success()
        {
            return new ScenarioValidationResult { IsValid = true };
        }

        /// <summary>
        /// 创建失败的验证结果
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        /// <returns>失败的验证结果</returns>
        public static ScenarioValidationResult Failure(string errorMessage)
        {
            return new ScenarioValidationResult
            {
                IsValid = false,
                ErrorMessages = { errorMessage }
            };
        }
    }

    /// <summary>
    /// 场景类别常量
    /// </summary>
    public static class ScenarioCategories
    {
        /// <summary>
        /// 延迟测试
        /// </summary>
        public const string Latency = "Latency";

        /// <summary>
        /// 吞吐量测试
        /// </summary>
        public const string Throughput = "Throughput";

        /// <summary>
        /// 并发测试
        /// </summary>
        public const string Concurrency = "Concurrency";

        /// <summary>
        /// 压力测试
        /// </summary>
        public const string Stress = "Stress";

        /// <summary>
        /// 负载测试
        /// </summary>
        public const string Load = "Load";

        /// <summary>
        /// 稳定性测试
        /// </summary>
        public const string Stability = "Stability";

        /// <summary>
        /// 功能测试
        /// </summary>
        public const string Functional = "Functional";

        /// <summary>
        /// 性能回归测试
        /// </summary>
        public const string Regression = "Regression";
    }
}
