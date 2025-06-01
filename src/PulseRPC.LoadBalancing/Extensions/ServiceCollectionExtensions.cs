using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PulseRPC.Client.LoadBalancing;
using PulseRPC.LoadBalancing.Strategies;

namespace PulseRPC.LoadBalancing.Extensions
{
    /// <summary>
    /// 负载均衡依赖注入扩展方法
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加负载均衡支持
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="strategy">负载均衡策略</param>
        /// <param name="configuration">配置</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddPulseRpcLoadBalancing(
            this IServiceCollection services,
            LoadBalancingStrategy strategy = LoadBalancingStrategy.RoundRobin,
            IConfiguration? configuration = null)
        {
            return strategy switch
            {
                LoadBalancingStrategy.RoundRobin => services.AddRoundRobinLoadBalancing(),
                LoadBalancingStrategy.Failover => services.AddFailoverLoadBalancing(configuration),
                _ => throw new ArgumentException($"不支持的负载均衡策略: {strategy}")
            };
        }

        /// <summary>
        /// 添加轮询负载均衡
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddRoundRobinLoadBalancing(this IServiceCollection services)
        {
            services.TryAddSingleton<ILoadBalancer>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<RoundRobinLoadBalancer>>();
                return new RoundRobinLoadBalancer(logger);
            });

            return services;
        }

        /// <summary>
        /// 添加故障转移负载均衡
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configuration">配置</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddFailoverLoadBalancing(
            this IServiceCollection services,
            IConfiguration? configuration = null)
        {
            if (configuration != null)
            {
                services.Configure<FailoverOptions>(configuration.GetSection(FailoverOptions.SectionName));
            }
            else
            {
                services.Configure<FailoverOptions>(options => { });
            }

            services.TryAddSingleton<ILoadBalancer, FailoverLoadBalancer>();

            return services;
        }

        /// <summary>
        /// 添加故障转移负载均衡 (使用配置回调)
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configureOptions">配置回调</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddFailoverLoadBalancing(
            this IServiceCollection services,
            Action<FailoverOptions> configureOptions)
        {
            services.Configure(configureOptions);
            services.TryAddSingleton<ILoadBalancer, FailoverLoadBalancer>();

            return services;
        }

        /// <summary>
        /// 添加加权轮询负载均衡
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddWeightedRoundRobinLoadBalancing(this IServiceCollection services)
        {
            services.TryAddSingleton<ILoadBalancer>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<WeightedRoundRobinLoadBalancer>>();
                return new WeightedRoundRobinLoadBalancer(logger);
            });

            return services;
        }

        /// <summary>
        /// 添加最少连接负载均衡
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddLeastConnectionsLoadBalancing(this IServiceCollection services)
        {
            services.TryAddSingleton<ILoadBalancer>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<LeastConnectionsLoadBalancer>>();
                return new LeastConnectionsLoadBalancer(logger);
            });

            return services;
        }

        /// <summary>
        /// 添加随机负载均衡
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddRandomLoadBalancing(this IServiceCollection services)
        {
            services.TryAddSingleton<ILoadBalancer>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<RandomLoadBalancer>>();
                return new RandomLoadBalancer(logger);
            });

            return services;
        }

        /// <summary>
        /// 添加一致性哈希负载均衡
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="virtualNodes">虚拟节点数量</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddConsistentHashLoadBalancing(
            this IServiceCollection services,
            int virtualNodes = 150)
        {
            services.TryAddSingleton<ILoadBalancer>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<ConsistentHashLoadBalancer>>();
                return new ConsistentHashLoadBalancer(virtualNodes, logger);
            });

            return services;
        }

        /// <summary>
        /// 添加最快响应负载均衡
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddFastestResponseLoadBalancing(this IServiceCollection services)
        {
            services.TryAddSingleton<ILoadBalancer>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<FastestResponseLoadBalancer>>();
                return new FastestResponseLoadBalancer(logger);
            });

            return services;
        }

        /// <summary>
        /// 添加自定义负载均衡实现
        /// </summary>
        /// <typeparam name="TLoadBalancer">负载均衡器实现类型</typeparam>
        /// <param name="services">服务集合</param>
        /// <param name="lifetime">服务生命周期</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddCustomLoadBalancer<TLoadBalancer>(
            this IServiceCollection services,
            ServiceLifetime lifetime = ServiceLifetime.Singleton)
            where TLoadBalancer : class, ILoadBalancer
        {
            services.Add(new ServiceDescriptor(typeof(ILoadBalancer), typeof(TLoadBalancer), lifetime));

            return services;
        }

        /// <summary>
        /// 添加自定义负载均衡实现 (使用工厂方法)
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="factory">负载均衡器工厂方法</param>
        /// <param name="lifetime">服务生命周期</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddCustomLoadBalancer(
            this IServiceCollection services,
            Func<IServiceProvider, ILoadBalancer> factory,
            ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            services.Add(new ServiceDescriptor(typeof(ILoadBalancer), factory, lifetime));

            return services;
        }

        /// <summary>
        /// 添加负载均衡策略工厂
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configuration">配置</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddLoadBalancerFactory(
            this IServiceCollection services,
            IConfiguration? configuration = null)
        {
            services.TryAddSingleton<ILoadBalancerFactory>(provider =>
            {
                return new LoadBalancerFactory(provider, configuration);
            });

            return services;
        }
    }

    /// <summary>
    /// 负载均衡器工厂接口
    /// </summary>
    public interface ILoadBalancerFactory
    {
        /// <summary>
        /// 创建负载均衡器
        /// </summary>
        /// <param name="strategy">负载均衡策略</param>
        /// <returns>负载均衡器实例</returns>
        ILoadBalancer Create(LoadBalancingStrategy strategy);
    }

    /// <summary>
    /// 负载均衡器工厂实现
    /// </summary>
    internal class LoadBalancerFactory : ILoadBalancerFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration? _configuration;

        public LoadBalancerFactory(IServiceProvider serviceProvider, IConfiguration? configuration)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
        }

        public ILoadBalancer Create(LoadBalancingStrategy strategy)
        {
            return strategy switch
            {
                LoadBalancingStrategy.RoundRobin => CreateRoundRobinLoadBalancer(),
                LoadBalancingStrategy.WeightedRoundRobin => CreateWeightedRoundRobinLoadBalancer(),
                LoadBalancingStrategy.LeastConnections => CreateLeastConnectionsLoadBalancer(),
                LoadBalancingStrategy.Random => CreateRandomLoadBalancer(),
                LoadBalancingStrategy.ConsistentHash => CreateConsistentHashLoadBalancer(),
                LoadBalancingStrategy.FastestResponse => CreateFastestResponseLoadBalancer(),
                LoadBalancingStrategy.Failover => CreateFailoverLoadBalancer(),
                _ => throw new ArgumentException($"不支持的负载均衡策略: {strategy}")
            };
        }

        private ILoadBalancer CreateRoundRobinLoadBalancer()
        {
            var logger = _serviceProvider.GetRequiredService<ILogger<RoundRobinLoadBalancer>>();
            return new RoundRobinLoadBalancer(logger);
        }

        private ILoadBalancer CreateWeightedRoundRobinLoadBalancer()
        {
            var logger = _serviceProvider.GetRequiredService<ILogger<WeightedRoundRobinLoadBalancer>>();
            return new WeightedRoundRobinLoadBalancer(logger);
        }

        private ILoadBalancer CreateLeastConnectionsLoadBalancer()
        {
            var logger = _serviceProvider.GetRequiredService<ILogger<LeastConnectionsLoadBalancer>>();
            return new LeastConnectionsLoadBalancer(logger);
        }

        private ILoadBalancer CreateRandomLoadBalancer()
        {
            var logger = _serviceProvider.GetRequiredService<ILogger<RandomLoadBalancer>>();
            return new RandomLoadBalancer(logger);
        }

        private ILoadBalancer CreateConsistentHashLoadBalancer()
        {
            var logger = _serviceProvider.GetRequiredService<ILogger<ConsistentHashLoadBalancer>>();
            return new ConsistentHashLoadBalancer(150, logger);
        }

        private ILoadBalancer CreateFastestResponseLoadBalancer()
        {
            var logger = _serviceProvider.GetRequiredService<ILogger<FastestResponseLoadBalancer>>();
            return new FastestResponseLoadBalancer(logger);
        }

        private ILoadBalancer CreateFailoverLoadBalancer()
        {
            var logger = _serviceProvider.GetRequiredService<ILogger<FailoverLoadBalancer>>();
            
            if (_configuration != null)
            {
                var options = Microsoft.Extensions.Options.Options.Create(
                    _configuration.GetSection(FailoverOptions.SectionName).Get<FailoverOptions>() ?? new FailoverOptions());
                return new FailoverLoadBalancer(options, logger);
            }
            else
            {
                var options = Microsoft.Extensions.Options.Options.Create(new FailoverOptions());
                return new FailoverLoadBalancer(options, logger);
            }
        }
    }

    // 占位符类 - 这些负载均衡器将在后续实现
    internal class WeightedRoundRobinLoadBalancer : ILoadBalancer
    {
        private readonly ILogger<WeightedRoundRobinLoadBalancer> _logger;

        public LoadBalancingStrategy Strategy => LoadBalancingStrategy.WeightedRoundRobin;

        public WeightedRoundRobinLoadBalancer(ILogger<WeightedRoundRobinLoadBalancer> logger)
        {
            _logger = logger;
        }

        public Task<PulseRPC.ServiceDiscovery.ServiceEndpoint?> SelectAsync(IReadOnlyList<PulseRPC.ServiceDiscovery.ServiceEndpoint> endpoints, LoadBalancingContext context)
        {
            throw new NotImplementedException("WeightedRoundRobinLoadBalancer 尚未实现");
        }

        public void ReportResult(PulseRPC.ServiceDiscovery.ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime)
        {
            // 暂时不实现
        }

        public void Reset()
        {
            // 暂时不实现
        }

        public Dictionary<string, object> GetStatistics()
        {
            return new Dictionary<string, object> { ["Strategy"] = Strategy.ToString() };
        }
    }

    internal class LeastConnectionsLoadBalancer : ILoadBalancer
    {
        private readonly ILogger<LeastConnectionsLoadBalancer> _logger;

        public LoadBalancingStrategy Strategy => LoadBalancingStrategy.LeastConnections;

        public LeastConnectionsLoadBalancer(ILogger<LeastConnectionsLoadBalancer> logger)
        {
            _logger = logger;
        }

        public Task<PulseRPC.ServiceDiscovery.ServiceEndpoint?> SelectAsync(IReadOnlyList<PulseRPC.ServiceDiscovery.ServiceEndpoint> endpoints, LoadBalancingContext context)
        {
            throw new NotImplementedException("LeastConnectionsLoadBalancer 尚未实现");
        }

        public void ReportResult(PulseRPC.ServiceDiscovery.ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime)
        {
            // 暂时不实现
        }

        public void Reset()
        {
            // 暂时不实现
        }

        public Dictionary<string, object> GetStatistics()
        {
            return new Dictionary<string, object> { ["Strategy"] = Strategy.ToString() };
        }
    }

    internal class RandomLoadBalancer : ILoadBalancer
    {
        private readonly ILogger<RandomLoadBalancer> _logger;

        public LoadBalancingStrategy Strategy => LoadBalancingStrategy.Random;

        public RandomLoadBalancer(ILogger<RandomLoadBalancer> logger)
        {
            _logger = logger;
        }

        public Task<PulseRPC.ServiceDiscovery.ServiceEndpoint?> SelectAsync(IReadOnlyList<PulseRPC.ServiceDiscovery.ServiceEndpoint> endpoints, LoadBalancingContext context)
        {
            throw new NotImplementedException("RandomLoadBalancer 尚未实现");
        }

        public void ReportResult(PulseRPC.ServiceDiscovery.ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime)
        {
            // 暂时不实现
        }

        public void Reset()
        {
            // 暂时不实现
        }

        public Dictionary<string, object> GetStatistics()
        {
            return new Dictionary<string, object> { ["Strategy"] = Strategy.ToString() };
        }
    }

    internal class ConsistentHashLoadBalancer : ILoadBalancer
    {
        private readonly ILogger<ConsistentHashLoadBalancer> _logger;
        private readonly int _virtualNodes;

        public LoadBalancingStrategy Strategy => LoadBalancingStrategy.ConsistentHash;

        public ConsistentHashLoadBalancer(int virtualNodes, ILogger<ConsistentHashLoadBalancer> logger)
        {
            _virtualNodes = virtualNodes;
            _logger = logger;
        }

        public Task<PulseRPC.ServiceDiscovery.ServiceEndpoint?> SelectAsync(IReadOnlyList<PulseRPC.ServiceDiscovery.ServiceEndpoint> endpoints, LoadBalancingContext context)
        {
            throw new NotImplementedException("ConsistentHashLoadBalancer 尚未实现");
        }

        public void ReportResult(PulseRPC.ServiceDiscovery.ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime)
        {
            // 暂时不实现
        }

        public void Reset()
        {
            // 暂时不实现
        }

        public Dictionary<string, object> GetStatistics()
        {
            return new Dictionary<string, object> 
            { 
                ["Strategy"] = Strategy.ToString(),
                ["VirtualNodes"] = _virtualNodes
            };
        }
    }

    internal class FastestResponseLoadBalancer : ILoadBalancer
    {
        private readonly ILogger<FastestResponseLoadBalancer> _logger;

        public LoadBalancingStrategy Strategy => LoadBalancingStrategy.FastestResponse;

        public FastestResponseLoadBalancer(ILogger<FastestResponseLoadBalancer> logger)
        {
            _logger = logger;
        }

        public Task<PulseRPC.ServiceDiscovery.ServiceEndpoint?> SelectAsync(IReadOnlyList<PulseRPC.ServiceDiscovery.ServiceEndpoint> endpoints, LoadBalancingContext context)
        {
            throw new NotImplementedException("FastestResponseLoadBalancer 尚未实现");
        }

        public void ReportResult(PulseRPC.ServiceDiscovery.ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime)
        {
            // 暂时不实现
        }

        public void Reset()
        {
            // 暂时不实现
        }

        public Dictionary<string, object> GetStatistics()
        {
            return new Dictionary<string, object> { ["Strategy"] = Strategy.ToString() };
        }
    }
} 