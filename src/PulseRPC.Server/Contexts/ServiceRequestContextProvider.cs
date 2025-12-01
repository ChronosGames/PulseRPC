using System;
using System.Threading;
using PulseRPC.Server.Contexts;

namespace PulseRPC.Server;

/// <summary>
/// 服务请求上下文提供者（兼容层 - 转发到 UnifiedRequestContext）
/// </summary>
/// <remarks>
/// <para>
/// <strong>⚠️ 已废弃</strong>：请使用 <see cref="UnifiedRequestContext"/> 替代。
/// </para>
/// <para>
/// 此类保留用于向后兼容，内部转发到统一上下文系统。
/// </para>
/// </remarks>
[Obsolete("使用 UnifiedRequestContext 替代。此类将在未来版本中移除。")]
public static class ServiceRequestContextProvider
{
    /// <summary>
    /// 当前服务请求上下文（从 UnifiedRequestContext 获取）
    /// </summary>
    public static IServiceRequestContext? Current
    {
        get => UnifiedRequestContext.Current;
        set
        {
            if (value == null)
            {
                UnifiedRequestContext.Clear();
            }
            else if (value is UnifiedContextData unified)
            {
                UnifiedRequestContext.Current = unified;
            }
            else
            {
                UnifiedRequestContext.Current = UnifiedContextData.FromServiceRequestContext(value);
            }
        }
    }

    /// <summary>
    /// 确保有请求上下文，否则抛出异常
    /// </summary>
    public static IServiceRequestContext RequireCurrent()
    {
        return UnifiedRequestContext.RequireCurrent();
    }

    /// <summary>
    /// 设置上下文并返回 Disposable 用于自动清理
    /// </summary>
    [Obsolete("使用 UnifiedRequestContext.SetContext 替代。")]
    public static IDisposable SetContext(IServiceRequestContext context)
    {
        if (context is UnifiedContextData unified)
        {
            return UnifiedRequestContext.SetContext(unified);
        }
        else
        {
            return UnifiedRequestContext.SetContext(UnifiedContextData.FromServiceRequestContext(context));
        }
    }
}
