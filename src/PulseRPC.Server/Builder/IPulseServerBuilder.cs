namespace PulseRPC.Server.Builder;

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
