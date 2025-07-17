using Microsoft.Extensions.DependencyInjection;

namespace PulseRPC.Client
{
    /// <summary>
    /// 客户端依赖注入扩展方法
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPulseRpcClient(
            this IServiceCollection services,
            Action<ClientOptions> configureOptions)
        {
            // 配置客户端选项
            services.Configure(configureOptions);

            // 注册 PulseRPC 客户端
            // services.AddSingleton<IPulseRpcClient, PulseRpcClient>();
            // services.AddSingleton<IPulseRpcServiceClient, PulseRpcServiceClient>();

            return services;
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
