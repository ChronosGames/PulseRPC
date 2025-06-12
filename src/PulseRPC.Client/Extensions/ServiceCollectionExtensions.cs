using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PulseRPC.LoadBalancing;
using PulseRPC.ServiceDiscovery;
using PulseRPC.Client.LoadBalancing;
using PulseRPC.Monitoring;
using PulseRPC.Tracing;

namespace PulseRPC.Client.Extensions
{
    /// <summary>
    /// 客户端依赖注入扩展方法
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 配置连接池选项
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configureOptions">配置回调</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection ConfigurePulseRpcConnectionPool(
            this IServiceCollection services,
            Action<ConnectionPoolOptions> configureOptions)
        {
            services.Configure<ClientOptions>(options => configureOptions(options.ConnectionPoolOptions));
            return services;
        }

        /// <summary>
        /// 配置重试策略
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configureOptions">配置回调</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection ConfigurePulseRpcRetryPolicy(
            this IServiceCollection services,
            Action<RetryPolicyOptions> configureOptions)
        {
            services.Configure(configureOptions);
            return services;
        }

        /// <summary>
        /// 配置超时策略
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configureOptions">配置回调</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection ConfigurePulseRpcTimeoutPolicy(
            this IServiceCollection services,
            Action<TimeoutPolicyOptions> configureOptions)
        {
            services.Configure(configureOptions);
            return services;
        }

        /// <summary>
        /// 添加服务注册
        /// </summary>
        /// <typeparam name="TImplementation">实现类型</typeparam>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddServiceRegistry<TImplementation>(this IServiceCollection services)
            where TImplementation : class, IServiceRegistry
        {
            services.TryAddSingleton<IServiceRegistry, TImplementation>();

            return services;
        }

        /// <summary>
        /// 添加自定义服务注册实现
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="implementationFactory">实现工厂</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddServiceRegistry(this IServiceCollection services,
            Func<IServiceProvider, IServiceRegistry> implementationFactory)
        {
            services.RemoveAll<IServiceRegistry>();
            services.AddSingleton(implementationFactory);

            return services;
        }

        /// <summary>
        /// 添加负载均衡
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="strategy">负载均衡策略</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddLoadBalancing(this IServiceCollection services,
            LoadBalancingStrategy strategy = LoadBalancingStrategy.RoundRobin)
        {
            return strategy switch
            {
                LoadBalancingStrategy.RoundRobin => services.AddLoadBalancing<RoundRobinLoadBalancer>(),
                _ => throw new NotSupportedException($"负载均衡策略 {strategy} 尚未实现")
            };
        }

        /// <summary>
        /// 添加自定义负载均衡实现
        /// </summary>
        /// <typeparam name="TImplementation">实现类型</typeparam>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddLoadBalancing<TImplementation>(this IServiceCollection services)
            where TImplementation : class, ILoadBalancer
        {
            services.TryAddSingleton<ILoadBalancer, TImplementation>();

            return services;
        }

        /// <summary>
        /// 添加自定义负载均衡实现
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="implementationFactory">实现工厂</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddLoadBalancing(this IServiceCollection services,
            Func<IServiceProvider, ILoadBalancer> implementationFactory)
        {
            services.RemoveAll<ILoadBalancer>();
            services.AddSingleton(implementationFactory);

            return services;
        }

        /// <summary>
        /// 添加链路追踪（可选）
        /// </summary>
        /// <typeparam name="TImplementation">实现类型</typeparam>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddTracing<TImplementation>(this IServiceCollection services)
            where TImplementation : class, ITracer
        {
            services.TryAddSingleton<ITracer, TImplementation>();

            return services;
        }

        /// <summary>
        /// 添加自定义链路追踪实现
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="implementationFactory">实现工厂</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddTracing(this IServiceCollection services,
            Func<IServiceProvider, ITracer> implementationFactory)
        {
            services.RemoveAll<ITracer>();
            services.AddSingleton(implementationFactory);

            return services;
        }

        /// <summary>
        /// 添加监控指标（可选）
        /// </summary>
        /// <typeparam name="TImplementation">实现类型</typeparam>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddMonitoring<TImplementation>(this IServiceCollection services)
            where TImplementation : class, IMetricsCollector
        {
            services.TryAddSingleton<IMetricsCollector, TImplementation>();

            return services;
        }

        /// <summary>
        /// 添加自定义监控指标实现
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="implementationFactory">实现工厂</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddMonitoring(this IServiceCollection services,
            Func<IServiceProvider, IMetricsCollector> implementationFactory)
        {
            services.RemoveAll<IMetricsCollector>();
            services.AddSingleton(implementationFactory);

            return services;
        }

        #region Private Methods

        /// <summary>
        /// 根据服务发现类型添加相应的服务
        /// </summary>
        private static void AddServiceDiscoveryByType(IServiceCollection services, ServiceDiscoveryType type, IConfiguration configuration)
        {
            switch (type)
            {
                // case ServiceDiscoveryType.Consul:
                //     services.AddConsulServiceDiscovery(configuration);
                //     break;
                // case ServiceDiscoveryType.Etcd:
                //     services.AddEtcdServiceDiscovery(configuration);
                //     break;
                // case ServiceDiscoveryType.Dns:
                //     services.AddDnsServiceDiscovery(configuration);
                //     break;
                case ServiceDiscoveryType.Static:
                    // 静态配置不需要额外的服务
                    break;
                case ServiceDiscoveryType.Custom:
                    // 自定义实现由用户手动注册
                    break;
            }
        }

        /// <summary>
        /// 根据负载均衡策略添加相应的服务
        /// </summary>
        private static void AddLoadBalancingByStrategy(IServiceCollection services, LoadBalancingStrategy strategy)
        {
            switch (strategy)
            {
                // case LoadBalancingStrategy.RoundRobin:
                //     services.AddRoundRobinLoadBalancing();
                //     break;
                // case LoadBalancingStrategy.Random:
                //     services.AddRandomLoadBalancing();
                //     break;
                // case LoadBalancingStrategy.WeightedRoundRobin:
                //     services.AddWeightedRoundRobinLoadBalancing();
                //     break;
                // case LoadBalancingStrategy.LeastConnections:
                //     services.AddLeastConnectionsLoadBalancing();
                //     break;
                // case LoadBalancingStrategy.ConsistentHash:
                //     services.AddConsistentHashLoadBalancing();
                //     break;
                default:
                    throw new NotSupportedException($"<UNK> {strategy} <UNK>");
            }
        }

        #endregion
    }

    /// <summary>
    /// PulseRPC 客户端接口
    /// </summary>
    public interface IPulseRpcClient
    {
        /// <summary>
        /// 连接到服务器
        /// </summary>
        /// <param name="endpoint">服务端点</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>连接任务</returns>
        Task ConnectAsync(string endpoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// 断开连接
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>断开连接任务</returns>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 发送请求
        /// </summary>
        /// <typeparam name="TRequest">请求类型</typeparam>
        /// <typeparam name="TResponse">响应类型</typeparam>
        /// <param name="request">请求数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应数据</returns>
        Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// PulseRPC 服务客户端接口
    /// </summary>
    public interface IPulseRpcServiceClient
    {
        /// <summary>
        /// 调用服务方法
        /// </summary>
        /// <typeparam name="TRequest">请求类型</typeparam>
        /// <typeparam name="TResponse">响应类型</typeparam>
        /// <param name="methodName">方法名称</param>
        /// <param name="request">请求数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应数据</returns>
        Task<TResponse> InvokeAsync<TRequest, TResponse>(string methodName, TRequest request, CancellationToken cancellationToken = default);
    }

    // 占位符实现
    internal class PulseRpcClientPlaceholder : IPulseRpcClient
    {
        private readonly ClientOptions _options;

        public PulseRpcClientPlaceholder(ClientOptions options)
        {
            _options = options;
        }

        public Task ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("PulseRPC客户端实现尚未完成");
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("PulseRPC客户端实现尚未完成");
        }

        public Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("PulseRPC客户端实现尚未完成");
        }
    }

    /// <summary>
    /// 连接池配置选项
    /// </summary>
    // public class ConnectionPoolOptions
    // {
    //     /// <summary>
    //     /// 最大连接数
    //     /// </summary>
    //     public int MaxConnections { get; set; } = 100;
    //
    //     /// <summary>
    //     /// 连接空闲超时时间
    //     /// </summary>
    //     public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);
    //
    //     /// <summary>
    //     /// 连接获取超时时间
    //     /// </summary>
    //     public TimeSpan AcquireTimeout { get; set; } = TimeSpan.FromSeconds(30);
    // }

    /// <summary>
    /// 重试策略配置选项
    /// </summary>
    public class RetryPolicyOptions
    {
        /// <summary>
        /// 最大重试次数
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// 基础重试延迟
        /// </summary>
        public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// 最大重试延迟
        /// </summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 是否使用指数退避
        /// </summary>
        public bool UseExponentialBackoff { get; set; } = true;

        /// <summary>
        /// 是否添加随机抖动
        /// </summary>
        public bool UseJitter { get; set; } = true;
    }

    /// <summary>
    /// 超时策略配置选项
    /// </summary>
    public class TimeoutPolicyOptions
    {
        /// <summary>
        /// 默认超时时间
        /// </summary>
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 方法级超时配置
        /// </summary>
        public Dictionary<string, TimeSpan> MethodTimeouts { get; set; } = new();

        /// <summary>
        /// 是否启用超时警告
        /// </summary>
        public bool EnableTimeoutWarning { get; set; } = true;

        /// <summary>
        /// 超时警告阈值
        /// </summary>
        public double TimeoutWarningThreshold { get; set; } = 0.8;
    }
}
