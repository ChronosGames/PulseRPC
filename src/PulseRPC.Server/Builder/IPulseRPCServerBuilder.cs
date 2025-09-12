using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Transport;

namespace PulseRPC.Server.Builder;

/// <summary>
/// PulseRPC 服务器构建器接口 - 支持链式配置
/// </summary>
public interface IPulseRPCServerBuilder
{
    /// <summary>
    /// 服务集合
    /// </summary>
    IServiceCollection Services { get; }

    // === 传输配置 ===
    /// <summary>
    /// 添加 TCP 传输支持
    /// </summary>
    /// <param name="name">传输名称</param>
    /// <param name="port">监听端口</param>
    /// <param name="configure">传输配置回调</param>
    /// <param name="isDefault">是否为默认传输</param>
    IPulseRPCServerBuilder AddTcp(string name, int port,
        Action<TcpTransportOptions>? configure = null, bool isDefault = false);

    /// <summary>
    /// 添加 KCP 传输支持
    /// </summary>
    /// <param name="name">传输名称</param>
    /// <param name="port">监听端口</param>
    /// <param name="configure">传输配置回调</param>
    /// <param name="isDefault">是否为默认传输</param>
    IPulseRPCServerBuilder AddKcp(string name, int port,
        Action<KcpTransportOptions>? configure = null, bool isDefault = false);

    // === 服务注册 ===
    /// <summary>
    /// 注册 RPC 服务
    /// </summary>
    /// <typeparam name="TService">服务接口类型</typeparam>
    /// <typeparam name="TImplementation">服务实现类型</typeparam>
    /// <param name="lifetime">服务生命周期</param>
    IPulseRPCServerBuilder AddService<TService, TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TService : class
        where TImplementation : class, TService;

    /// <summary>
    /// 注册 RPC 服务实例
    /// </summary>
    /// <typeparam name="TService">服务接口类型</typeparam>
    /// <param name="implementationInstance">服务实例</param>
    IPulseRPCServerBuilder AddService<TService>(TService implementationInstance)
        where TService : class;

    // === 性能优化配置 ===
    /// <summary>
    /// 启用高性能消息引擎 (默认启用)
    /// </summary>
    /// <param name="configure">引擎配置回调</param>
    IPulseRPCServerBuilder UseHighPerformanceEngine(Action<MessageEngineOptions>? configure = null);

    /// <summary>
    /// 启用分层消息处理器
    /// </summary>
    /// <param name="configure">处理器配置回调</param>
    IPulseRPCServerBuilder UseTieredMessageProcessor(Action<TieredProcessorOptions>? configure = null);

    /// <summary>
    /// 启用优先级感知调度器
    /// </summary>
    /// <param name="configure">调度器配置回调</param>
    IPulseRPCServerBuilder UsePriorityScheduler(Action<PrioritySchedulerOptions>? configure = null);

    // === 安全和认证 ===
    /// <summary>
    /// 启用身份认证
    /// </summary>
    /// <param name="configure">认证配置回调</param>
    IPulseRPCServerBuilder UseAuthentication(Action<AuthenticationOptions>? configure = null);

    /// <summary>
    /// 启用角色授权
    /// </summary>
    /// <param name="configure">授权配置回调</param>
    IPulseRPCServerBuilder UseAuthorization(Action<AuthorizationOptions>? configure = null);

    // === 中间件和拦截器 ===
    /// <summary>
    /// 添加中间件
    /// </summary>
    /// <typeparam name="TMiddleware">中间件类型</typeparam>
    IPulseRPCServerBuilder UseMiddleware<TMiddleware>() where TMiddleware : class, IPulseRpcMiddleware;

    /// <summary>
    /// 添加拦截器
    /// </summary>
    /// <typeparam name="TInterceptor">拦截器类型</typeparam>
    IPulseRPCServerBuilder UseInterceptor<TInterceptor>() where TInterceptor : class, IPulseRpcInterceptor;

    // === 服务器配置 ===
    /// <summary>
    /// 配置服务器选项
    /// </summary>
    /// <param name="configure">服务器配置回调</param>
    IPulseRPCServerBuilder ConfigureServer(Action<ServerOptions> configure);

    // === 构建 ===
    /// <summary>
    /// 构建服务器实例
    /// </summary>
    /// <returns>配置完成的服务器实例</returns>
    void Build();
}

/// <summary>
/// 消息引擎配置选项
/// </summary>
public class MessageEngineOptions
{
    /// <summary>
    /// 是否启用消息引擎 (默认: true)
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// L1 循环缓冲区大小 (默认: 4096)
    /// </summary>
    public int L1BufferSize { get; set; } = 4096;

    /// <summary>
    /// L2 批处理队列容量 (默认: 256)
    /// </summary>
    public int L2QueueCapacity { get; set; } = 256;

    /// <summary>
    /// L3 响应队列容量 (默认: 128)
    /// </summary>
    public int L3QueueCapacity { get; set; } = 128;
}

/// <summary>
/// 分层处理器配置选项
/// </summary>
public class TieredProcessorOptions
{
    /// <summary>
    /// 是否启用分层处理 (默认: true)
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 快速通道配置 (小消息 < 1KB)
    /// </summary>
    public FastPathOptions FastPath { get; set; } = new();

    /// <summary>
    /// 批量通道配置 (中等消息 1KB-64KB)
    /// </summary>
    public BatchPathOptions BatchPath { get; set; } = new();
}

/// <summary>
/// 快速通道配置
/// </summary>
public class FastPathOptions
{
    /// <summary>
    /// 消息大小阈值 (字节, 默认: 1024)
    /// </summary>
    public int MessageSizeThreshold { get; set; } = 1024;

    /// <summary>
    /// 专用线程数 (默认: 2)
    /// </summary>
    public int DedicatedThreads { get; set; } = 2;
}

/// <summary>
/// 批量通道配置
/// </summary>
public class BatchPathOptions
{
    /// <summary>
    /// 最小消息大小 (字节, 默认: 1024)
    /// </summary>
    public int MinMessageSize { get; set; } = 1024;

    /// <summary>
    /// 最大消息大小 (字节, 默认: 65536)
    /// </summary>
    public int MaxMessageSize { get; set; } = 65536;

    /// <summary>
    /// 批处理大小 (默认: 16)
    /// </summary>
    public int BatchSize { get; set; } = 16;
}

/// <summary>
/// 优先级调度器配置选项
/// </summary>
public class PrioritySchedulerOptions
{
    /// <summary>
    /// 是否启用优先级调度 (默认: true)
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 关键消息权重 (默认: 50%)
    /// </summary>
    public int CriticalWeight { get; set; } = 50;

    /// <summary>
    /// 普通消息权重 (默认: 35%)
    /// </summary>
    public int NormalWeight { get; set; } = 35;

    /// <summary>
    /// 批量消息权重 (默认: 15%)
    /// </summary>
    public int BulkWeight { get; set; } = 15;
}

/// <summary>
/// 认证配置选项
/// </summary>
public class AuthenticationOptions
{
    /// <summary>
    /// 是否启用认证 (默认: false)
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// JWT密钥
    /// </summary>
    public string? JwtSecretKey { get; set; }

    /// <summary>
    /// JWT过期时间
    /// </summary>
    public TimeSpan JwtExpiration { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// 授权配置选项
/// </summary>
public class AuthorizationOptions
{
    /// <summary>
    /// 是否启用授权 (默认: false)
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 支持的角色列表
    /// </summary>
    public List<string> SupportedRoles { get; set; } = new();
}

/// <summary>
/// PulseRPC 中间件接口
/// </summary>
public interface IPulseRpcMiddleware
{
    /// <summary>
    /// 执行中间件逻辑
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <param name="next">下一个中间件</param>
    /// <returns>处理任务</returns>
    Task InvokeAsync(IPulseRpcContext context, Func<Task> next);
}

/// <summary>
/// PulseRPC 拦截器接口
/// </summary>
public interface IPulseRpcInterceptor
{
    /// <summary>
    /// 请求前拦截
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <returns>拦截任务</returns>
    Task OnRequestAsync(IPulseRpcContext context);

    /// <summary>
    /// 响应后拦截
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <returns>拦截任务</returns>
    Task OnResponseAsync(IPulseRpcContext context);

    /// <summary>
    /// 异常拦截
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <param name="exception">异常信息</param>
    /// <returns>拦截任务</returns>
    Task OnExceptionAsync(IPulseRpcContext context, Exception exception);
}

/// <summary>
/// PulseRPC 请求上下文接口
/// </summary>
public interface IPulseRpcContext
{
    /// <summary>
    /// 请求标识
    /// </summary>
    string RequestId { get; }

    /// <summary>
    /// 服务名称
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// 方法名称
    /// </summary>
    string MethodName { get; }

    /// <summary>
    /// 请求数据
    /// </summary>
    object? RequestData { get; }

    /// <summary>
    /// 响应数据
    /// </summary>
    object? ResponseData { get; set; }

    /// <summary>
    /// 上下文数据
    /// </summary>
    Dictionary<string, object> Items { get; }
}
