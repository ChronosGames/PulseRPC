using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Benchmark.Core.Abstract;
using PulseRPC.Benchmark.Core.Interfaces;
using PulseRPC.Benchmark.Core.Models;

namespace PulseRPC.Benchmark.Core.Extensions
{
    /// <summary>
    /// ServiceCollection 扩展方法
    /// </summary>
    public static class ServiceCollectionExtensions
    {

        /// <summary>
        /// 添加基准测试场景
        /// </summary>
        /// <typeparam name="TScenario">场景类型</typeparam>
        /// <param name="services">服务集合</param>
        /// <param name="lifetime">服务生命周期</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddBenchmarkScenario<TScenario>(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Transient)
            where TScenario : class, IBenchmarkScenario
        {
            services.Add(new ServiceDescriptor(typeof(IBenchmarkScenario), typeof(TScenario), lifetime));
            services.Add(new ServiceDescriptor(typeof(TScenario), typeof(TScenario), lifetime));

            return services;
        }

        /// <summary>
        /// 添加自定义传输类型
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="transportType">传输类型名称</param>
        /// <param name="transportFactory">传输工厂函数</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddCustomTransport(this IServiceCollection services, string transportType, Func<IServiceProvider, TransportOptions?, IBenchmarkTransport> transportFactory)
        {
            if (string.IsNullOrEmpty(transportType))
                throw new ArgumentException("传输类型不能为空", nameof(transportType));

            if (transportFactory == null)
                throw new ArgumentNullException(nameof(transportFactory));

            services.Configure<BenchmarkCoreOptions>(options =>
            {
                options.CustomTransportFactories[transportType] = (provider, opts) => transportFactory(provider, opts);
            });

            return services;
        }

        /// <summary>
        /// 添加结果聚合器
        /// </summary>
        /// <typeparam name="TAggregator">聚合器类型</typeparam>
        /// <param name="services">服务集合</param>
        /// <param name="lifetime">服务生命周期</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddResultAggregator<TAggregator>(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Singleton)
            where TAggregator : class, IResultAggregator
        {
            services.Replace(new ServiceDescriptor(typeof(IResultAggregator), typeof(TAggregator), lifetime));
            return services;
        }

        /// <summary>
        /// 添加基准测试执行器
        /// </summary>
        /// <typeparam name="TExecutor">执行器类型</typeparam>
        /// <param name="services">服务集合</param>
        /// <param name="lifetime">服务生命周期</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddBenchmarkExecutor<TExecutor>(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Transient)
            where TExecutor : class, IBenchmarkExecutor
        {
            services.Replace(new ServiceDescriptor(typeof(IBenchmarkExecutor), typeof(TExecutor), lifetime));
            return services;
        }

        /// <summary>
        /// 添加基准测试运行器
        /// </summary>
        /// <typeparam name="TRunner">运行器类型</typeparam>
        /// <param name="services">服务集合</param>
        /// <param name="lifetime">服务生命周期</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddBenchmarkRunner<TRunner>(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Transient)
            where TRunner : class, IBenchmarkRunner
        {
            services.Replace(new ServiceDescriptor(typeof(IBenchmarkRunner), typeof(TRunner), lifetime));
            return services;
        }
    }

    /// <summary>
    /// 基准测试核心选项
    /// </summary>
    public class BenchmarkCoreOptions
    {
        /// <summary>
        /// 默认传输类型
        /// </summary>
        public string DefaultTransportType { get; set; } = TransportTypes.Tcp;

        /// <summary>
        /// 默认并发连接数
        /// </summary>
        public int DefaultConcurrentConnections { get; set; } = 1;

        /// <summary>
        /// 默认测试持续时间（秒）
        /// </summary>
        public int DefaultDurationSeconds { get; set; } = 30;

        /// <summary>
        /// 默认预热时间（秒）
        /// </summary>
        public int DefaultWarmupSeconds { get; set; } = 5;

        /// <summary>
        /// 默认消息大小（字节）
        /// </summary>
        public int DefaultMessageSizeBytes { get; set; } = 1024;

        /// <summary>
        /// 是否启用详细日志
        /// </summary>
        public bool EnableVerboseLogging { get; set; } = false;

        /// <summary>
        /// 是否收集资源指标
        /// </summary>
        public bool CollectResourceMetrics { get; set; } = true;

        /// <summary>
        /// 自定义传输工厂
        /// </summary>
        public Dictionary<string, Func<IServiceProvider, TransportOptions?, IBenchmarkTransport>> CustomTransportFactories { get; set; } = new();

        /// <summary>
        /// 最大重试次数
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// 重试间隔（毫秒）
        /// </summary>
        public int RetryIntervalMs { get; set; } = 1000;
    }

    /// <summary>
    /// 基准测试服务配置选项
    /// </summary>
    public class BenchmarkServiceOptions
    {
        /// <summary>
        /// 服务端点地址
        /// </summary>
        public string ServiceEndpoint { get; set; } = "localhost:5000";

        /// <summary>
        /// 连接超时时间（毫秒）
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 请求超时时间（毫秒）
        /// </summary>
        public int RequestTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// 最大重试次数
        /// </summary>
        public int MaxRetryCount { get; set; } = 3;

        /// <summary>
        /// 是否启用压缩
        /// </summary>
        public bool EnableCompression { get; set; } = true;

        /// <summary>
        /// 缓冲区大小
        /// </summary>
        public int BufferSize { get; set; } = 64 * 1024; // 64KB

        /// <summary>
        /// 日志级别
        /// </summary>
        public string LogLevel { get; set; } = "Information";
    }

    internal class MemoryBenchmarkTransport : BaseBenchmarkTransport
    {
        public MemoryBenchmarkTransport(ILogger logger, TransportOptions? options) : base("Memory", logger) { }
        public override Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task<bool> SendAsync(byte[] data, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public override Task<byte[]?> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(new byte[0]);
    }

    // 占位符接口和实现，用于编译通过
    public interface IMessageSerializer { }
    public interface IBenchmarkServiceClientFactory { }
    public interface IMessageHandler { }
    public class MemoryPackMessageSerializer : IMessageSerializer { }
    public class DefaultBenchmarkServiceClientFactory : IBenchmarkServiceClientFactory { }
    public class DefaultMessageHandler : IMessageHandler { }
}
