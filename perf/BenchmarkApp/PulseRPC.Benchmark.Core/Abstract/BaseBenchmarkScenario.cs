using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Core.Interfaces;
using PulseRPC.Benchmark.Core.Models;

namespace PulseRPC.Benchmark.Core.Abstract
{
    /// <summary>
    /// 基准测试场景基础抽象类
    /// </summary>
    public abstract class BaseBenchmarkScenario : IBenchmarkScenario
    {
        private readonly ILogger _logger;
        private bool _isInitialized = false;
        private IBenchmarkTransport? _transport;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        protected BaseBenchmarkScenario(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public abstract string Name { get; }

        /// <inheritdoc />
        public abstract string Description { get; }

        /// <inheritdoc />
        public abstract string Version { get; }

        /// <inheritdoc />
        public abstract string Category { get; }

        /// <inheritdoc />
        public virtual bool SupportsTransport(string transportType)
        {
            var requirements = GetRequirements();
            return requirements.SupportedTransports.Contains(transportType);
        }

        /// <inheritdoc />
        public abstract ScenarioRequirements GetRequirements();

        /// <inheritdoc />
        public virtual async Task InitializeAsync(IBenchmarkTransport transport, BenchmarkConfiguration configuration, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
        {
            if (_isInitialized)
            {
                _logger.LogWarning("场景 {ScenarioName} 已经初始化", Name);
                return;
            }

            _transport = transport ?? throw new ArgumentNullException(nameof(transport));

            try
            {
                _logger.LogInformation("初始化场景: {ScenarioName}", Name);

                // 配置服务依赖
                if (serviceProvider != null && RequiresServiceDependencies)
                {
                    ConfigureServices(serviceProvider);
                }

                await DoInitializeAsync(configuration, cancellationToken);

                _isInitialized = true;
                _logger.LogInformation("场景初始化完成: {ScenarioName}", Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "场景初始化失败: {ScenarioName}", Name);
                throw;
            }
        }

        /// <inheritdoc />
        public virtual async Task WarmupAsync(BenchmarkConfiguration warmupConfiguration, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
                throw new InvalidOperationException($"场景 {Name} 尚未初始化");

            try
            {
                _logger.LogInformation("开始预热场景: {ScenarioName}", Name);

                await DoWarmupAsync(warmupConfiguration, cancellationToken);

                _logger.LogInformation("场景预热完成: {ScenarioName}", Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "场景预热失败: {ScenarioName}", Name);
                throw;
            }
        }

        /// <inheritdoc />
        public virtual async Task<BenchmarkResult> ExecuteAsync(BenchmarkConfiguration configuration, Action<ExecutionProgress>? progressCallback = null, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
                throw new InvalidOperationException($"场景 {Name} 尚未初始化");

            try
            {
                _logger.LogInformation("开始执行场景: {ScenarioName}", Name);

                var result = await DoExecuteAsync(configuration, progressCallback, cancellationToken);

                _logger.LogInformation("场景执行完成: {ScenarioName}, 耗时: {Duration}ms",
                    Name, result.Duration.TotalMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "场景执行失败: {ScenarioName}", Name);
                throw;
            }
        }

        /// <inheritdoc />
        public virtual async Task CleanupAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("清理场景资源: {ScenarioName}", Name);

                await DoCleanupAsync(cancellationToken);

                _isInitialized = false;
                _transport = null;

                _logger.LogInformation("场景资源清理完成: {ScenarioName}", Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "场景资源清理失败: {ScenarioName}", Name);
                throw;
            }
        }

        /// <inheritdoc />
        public virtual BenchmarkConfiguration GetDefaultConfiguration()
        {
            return new BenchmarkConfiguration
            {
                Host = "localhost",
                Port = 12345,
                TransportType = TransportTypes.Tcp,
                ConcurrentConnections = 1,
                DurationSeconds = 30,
                WarmupSeconds = 5,
                MessageSizeBytes = 1024,
                RequestIntervalMs = 0,
                EnableVerboseLogging = false,
                CollectResourceMetrics = true
            };
        }

        /// <inheritdoc />
        public virtual BenchmarkConfiguration[] GetRecommendedConfigurations()
        {
            var baseConfig = GetDefaultConfiguration();

            return new[]
            {
                // 低并发，小消息
                new BenchmarkConfiguration
                {
                    Host = baseConfig.Host,
                    Port = baseConfig.Port,
                    TransportType = baseConfig.TransportType,
                    ConcurrentConnections = 1,
                    DurationSeconds = 30,
                    MessageSizeBytes = 100,
                    WarmupSeconds = 5
                },
                // 中等并发，中等消息
                new BenchmarkConfiguration
                {
                    Host = baseConfig.Host,
                    Port = baseConfig.Port,
                    TransportType = baseConfig.TransportType,
                    ConcurrentConnections = 10,
                    DurationSeconds = 60,
                    MessageSizeBytes = 1024,
                    WarmupSeconds = 10
                },
                // 高并发，大消息
                new BenchmarkConfiguration
                {
                    Host = baseConfig.Host,
                    Port = baseConfig.Port,
                    TransportType = baseConfig.TransportType,
                    ConcurrentConnections = 100,
                    DurationSeconds = 120,
                    MessageSizeBytes = 4096,
                    WarmupSeconds = 15
                }
            };
        }

        /// <inheritdoc />
        public virtual byte[] GenerateTestData(int size)
        {
            if (size <= 0)
                throw new ArgumentException("数据大小必须大于0", nameof(size));

            var data = new byte[size];
            RandomNumberGenerator.Fill(data);
            return data;
        }

        /// <summary>
        /// 执行具体的初始化操作（由子类实现）
        /// </summary>
        /// <param name="configuration">配置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>初始化任务</returns>
        protected abstract Task DoInitializeAsync(BenchmarkConfiguration configuration, CancellationToken cancellationToken);

        /// <summary>
        /// 执行具体的预热操作（由子类实现）
        /// </summary>
        /// <param name="configuration">配置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>预热任务</returns>
        protected abstract Task DoWarmupAsync(BenchmarkConfiguration configuration, CancellationToken cancellationToken);

        /// <summary>
        /// 执行具体的基准测试（由子类实现）
        /// </summary>
        /// <param name="configuration">配置</param>
        /// <param name="progressCallback">进度回调</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>测试结果</returns>
        protected abstract Task<BenchmarkResult> DoExecuteAsync(BenchmarkConfiguration configuration, Action<ExecutionProgress>? progressCallback, CancellationToken cancellationToken);

        /// <summary>
        /// 执行具体的清理操作（由子类实现）
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>清理任务</returns>
        protected abstract Task DoCleanupAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 创建基准测试结果
        /// </summary>
        /// <param name="configuration">配置</param>
        /// <param name="latencyMetrics">延迟指标</param>
        /// <param name="throughputMetrics">吞吐量指标</param>
        /// <param name="resourceMetrics">资源指标</param>
        /// <returns>基准测试结果</returns>
        protected BenchmarkResult CreateResult(
            BenchmarkConfiguration configuration,
            LatencyMetrics latencyMetrics,
            ThroughputMetrics throughputMetrics,
            ResourceMetrics resourceMetrics)
        {
            return new BenchmarkResult
            {
                ScenarioName = Name,
                Configuration = configuration,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow,
                Latency = latencyMetrics,
                Throughput = throughputMetrics,
                Resources = resourceMetrics,
                IsSuccessful = true,
                ErrorMessage = null
            };
        }

        /// <summary>
        /// 获取传输层实例
        /// </summary>
        protected IBenchmarkTransport Transport
        {
            get
            {
                if (_transport == null)
                    throw new InvalidOperationException("传输层未初始化");
                return _transport;
            }
        }

        /// <summary>
        /// 获取日志记录器
        /// </summary>
        protected ILogger Logger => _logger;

        /// <summary>
        /// 检查是否已初始化
        /// </summary>
        protected bool IsInitialized => _isInitialized;

        /// <summary>
        /// 生成随机字节数组
        /// </summary>
        /// <param name="size">大小</param>
        /// <returns>随机字节数组</returns>
        protected byte[] GenerateRandomBytes(int size)
        {
            var buffer = new byte[size];
            RandomNumberGenerator.Fill(buffer);
            return buffer;
        }

        /// <inheritdoc />
        public virtual bool RequiresServiceDependencies => false;

        /// <inheritdoc />
        public virtual IEnumerable<Type> GetRequiredServiceTypes()
        {
            return Enumerable.Empty<Type>();
        }

        /// <inheritdoc />
        public virtual void ConfigureServices(IServiceProvider serviceProvider)
        {
            // 默认不需要配置服务
        }
    }
}
