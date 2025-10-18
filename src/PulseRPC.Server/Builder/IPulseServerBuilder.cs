using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Server.Scheduling;
using PulseRPC.Transport;

namespace PulseRPC.Server.Builder;

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
/// PulseRPC 中间件接口
/// </summary>
public interface IPulseMiddleware
{
    /// <summary>
    /// 执行中间件逻辑
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <param name="next">下一个中间件</param>
    /// <returns>处理任务</returns>
    Task InvokeAsync(IPulseContext context, Func<Task> next);
}

/// <summary>
/// PulseRPC 拦截器接口
/// </summary>
public interface IPulseInterceptor
{
    /// <summary>
    /// 请求前拦截
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <returns>拦截任务</returns>
    Task OnRequestAsync(IPulseContext context);

    /// <summary>
    /// 响应后拦截
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <returns>拦截任务</returns>
    Task OnResponseAsync(IPulseContext context);

    /// <summary>
    /// 异常拦截
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <param name="exception">异常信息</param>
    /// <returns>拦截任务</returns>
    Task OnExceptionAsync(IPulseContext context, Exception exception);
}

/// <summary>
/// PulseRPC 请求上下文接口
/// </summary>
public interface IPulseContext
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
