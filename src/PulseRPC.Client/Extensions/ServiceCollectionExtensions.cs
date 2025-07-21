using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PulseRPC.Transport;

namespace PulseRPC.Client
{
    /// <summary>
    /// 客户端依赖注入扩展方法
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加PulseRPC客户端服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddPulseRpcClient(this IServiceCollection services)
        {
            // 注册通道管理器
            services.TryAddSingleton<IChannelManager, ChannelManager>();

            // 注册默认客户端实例（无传输配置）
            services.TryAddSingleton<IPulseRpcClient>(sp =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var channelManager = sp.GetRequiredService<IChannelManager>();

                return new PulseRpcClientManager(channelManager, loggerFactory);
            });

            return services;
        }

        /// <summary>
        /// 添加PulseRPC客户端 (使用配置回调)
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configureOptions">配置回调</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddPulseRpcClient(
            this IServiceCollection services,
            Action<ClientOptions> configureOptions)
        {
            services.Configure(configureOptions);

            // 注册基础组件
            services.AddPulseRpcClient();

            return services;
        }

        /// <summary>
        /// 添加PulseRPC客户端 (使用客户端配置构建器)
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="configure">客户端配置构建器</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddPulseRpcClient(
            this IServiceCollection services,
            Action<ClientConfigurationBuilder> configure)
        {
            var builder = new ClientConfigurationBuilder();
            configure(builder);
            var (transports, clientConfig) = builder.Build();

            // 配置客户端选项
            if (clientConfig != null)
            {
                services.Configure(clientConfig);
            }

            // 注册基础组件
            services.AddPulseRpcClient();

            // 注册带传输配置的客户端管理器
            services.AddSingleton<IPulseRpcClient>(sp =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var channelManager = sp.GetRequiredService<IChannelManager>();

                var clientManager = new PulseRpcClientManager(channelManager, loggerFactory);

                // 自动添加配置的传输通道
                clientManager.AddTransports(transports);

                return clientManager;
            });

            return services;
        }

        /// <summary>
        /// 添加PulseRPC客户端 (使用传输配置列表)
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="transports">传输配置列表</param>
        /// <param name="configureClient">客户端配置回调</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddPulseRpcClient(
            this IServiceCollection services,
            IEnumerable<ClientTransportConfiguration> transports,
            Action<ClientOptions>? configureClient = null)
        {
            // 配置客户端选项
            if (configureClient != null)
            {
                services.Configure(configureClient);
            }

            // 注册基础组件
            services.AddPulseRpcClient();

            var transportList = transports.ToList();

            // 注册带传输配置的客户端管理器
            services.AddSingleton<IPulseRpcClient>(sp =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var channelManager = sp.GetRequiredService<IChannelManager>();

                var clientManager = new PulseRpcClientManager(channelManager, loggerFactory);

                // 自动添加传输通道
                clientManager.AddTransports(transportList);

                return clientManager;
            });

            return services;
        }

        /// <summary>
        /// 快速配置TCP客户端
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="host">服务器主机</param>
        /// <param name="port">服务器端口</param>
        /// <param name="configureClient">客户端配置回调</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddPulseRpcTcpClient(
            this IServiceCollection services,
            string host,
            int port,
            Action<ClientOptions>? configureClient = null)
        {
            return services.AddPulseRpcClient(builder =>
            {
                builder.AddTcp("Default", host, port, isDefault: true);
                if (configureClient != null)
                {
                    builder.ConfigureClient(configureClient);
                }
            });
        }

        /// <summary>
        /// 快速配置KCP客户端
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="host">服务器主机</param>
        /// <param name="port">服务器端口</param>
        /// <param name="configureClient">客户端配置回调</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddPulseRpcKcpClient(
            this IServiceCollection services,
            string host,
            int port,
            Action<ClientOptions>? configureClient = null)
        {
            return services.AddPulseRpcClient(builder =>
            {
                builder.AddKcp("Default", host, port, isDefault: true);
                if (configureClient != null)
                {
                    builder.ConfigureClient(configureClient);
                }
            });
        }

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
