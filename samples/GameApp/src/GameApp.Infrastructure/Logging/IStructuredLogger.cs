using System;
using System.Collections.Generic;

namespace GameApp.Infrastructure.Logging;

/// <summary>
/// 结构化日志接口
/// </summary>
public interface IStructuredLogger
{
    /// <summary>
    /// 记录信息日志
    /// </summary>
    void LogInfo(string message, object? data = null, Dictionary<string, object>? properties = null);

    /// <summary>
    /// 记录警告日志
    /// </summary>
    void LogWarning(string message, object? data = null, Dictionary<string, object>? properties = null);

    /// <summary>
    /// 记录错误日志
    /// </summary>
    void LogError(string message, Exception? exception = null, object? data = null, Dictionary<string, object>? properties = null);

    /// <summary>
    /// 记录业务事件
    /// </summary>
    void LogBusinessEvent(string eventType, string eventName, object data, Dictionary<string, object>? properties = null);

    /// <summary>
    /// 记录性能事件
    /// </summary>
    void LogPerformance(string operation, TimeSpan duration, bool success, Dictionary<string, object>? properties = null);

    /// <summary>
    /// 记录安全事件
    /// </summary>
    void LogSecurity(SecurityEventType eventType, string message, object? data = null, Dictionary<string, object>? properties = null);

    /// <summary>
    /// 开始性能追踪
    /// </summary>
    IDisposable BeginScope(string operation, Dictionary<string, object>? properties = null);
}

/// <summary>
/// 安全事件类型
/// </summary>
public enum SecurityEventType
{
    LoginAttempt,
    FailedLogin,
    SuspiciousActivity,
    AccessDenied,
    TokenValidation,
    PasswordChange,
    AccountLocked,
    UnauthorizedAccess
}
