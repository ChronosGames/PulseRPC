using System;
using System.Threading;

namespace PulseRPC.Server;

/// <summary>
/// 服务请求上下文提供者 - 使用AsyncLocal保证异步流程安全
/// </summary>
public static class ServiceRequestContextProvider
{
    private static readonly AsyncLocal<IServiceRequestContext?> _current = new();

    /// <summary>
    /// 当前服务请求上下文
    /// </summary>
    public static IServiceRequestContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    /// <summary>
    /// 确保有请求上下文，否则抛出异常
    /// </summary>
    public static IServiceRequestContext RequireCurrent()
    {
        return Current ?? throw new UnauthorizedAccessException("No service request context available");
    }

    /// <summary>
    /// 设置上下文并返回Disposable用于自动清理
    /// </summary>
    public static IDisposable SetContext(IServiceRequestContext context)
    {
        var previous = Current;
        Current = context;
        return new ContextScope(previous);
    }

    private class ContextScope(IServiceRequestContext? previous) : IDisposable
    {
        public void Dispose()
        {
            Current = previous;
        }
    }
}
