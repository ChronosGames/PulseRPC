using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace GameApp.Infrastructure.Logging;

/// <summary>
/// 结构化日志服务实现
/// </summary>
public class StructuredLogger : IStructuredLogger
{
    private readonly ILogger<StructuredLogger> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public StructuredLogger(ILogger<StructuredLogger> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public void LogInfo(string message, object? data = null, Dictionary<string, object>? properties = null)
    {
        var logData = CreateLogData("INFO", message, data, properties);
        _logger.LogInformation("【INFO】{Message} | Data: {LogData}", message, JsonSerializer.Serialize(logData, _jsonOptions));
    }

    public void LogWarning(string message, object? data = null, Dictionary<string, object>? properties = null)
    {
        var logData = CreateLogData("WARNING", message, data, properties);
        _logger.LogWarning("【WARNING】{Message} | Data: {LogData}", message, JsonSerializer.Serialize(logData, _jsonOptions));
    }

    public void LogError(string message, Exception? exception = null, object? data = null, Dictionary<string, object>? properties = null)
    {
        var logData = CreateLogData("ERROR", message, data, properties);
        if (exception != null)
        {
            logData["exception"] = new
            {
                type = exception.GetType().Name,
                message = exception.Message,
                stackTrace = exception.StackTrace,
                innerException = exception.InnerException?.Message
            };
        }

        _logger.LogError(exception, "【ERROR】{Message} | Data: {LogData}", message, JsonSerializer.Serialize(logData, _jsonOptions));
    }

    public void LogBusinessEvent(string eventType, string eventName, object data, Dictionary<string, object>? properties = null)
    {
        var logData = CreateLogData("BUSINESS_EVENT", $"{eventType}.{eventName}", data, properties);
        logData["eventType"] = eventType;
        logData["eventName"] = eventName;

        _logger.LogInformation("【BUSINESS】{EventType}.{EventName} | Data: {LogData}",
            eventType, eventName, JsonSerializer.Serialize(logData, _jsonOptions));
    }

    public void LogPerformance(string operation, TimeSpan duration, bool success, Dictionary<string, object>? properties = null)
    {
        var logData = CreateLogData("PERFORMANCE", operation, null, properties);
        logData["operation"] = operation;
        logData["durationMs"] = duration.TotalMilliseconds;
        logData["success"] = success;

        var level = duration.TotalMilliseconds > 1000 ? LogLevel.Warning : LogLevel.Information;
        var status = success ? "SUCCESS" : "FAILED";

        _logger.Log(level, "【PERFORMANCE】{Operation} | {Status} | {Duration}ms | Data: {LogData}",
            operation, status, duration.TotalMilliseconds, JsonSerializer.Serialize(logData, _jsonOptions));
    }

    public void LogSecurity(SecurityEventType eventType, string message, object? data = null, Dictionary<string, object>? properties = null)
    {
        var logData = CreateLogData("SECURITY", message, data, properties);
        logData["securityEventType"] = eventType.ToString();
        logData["severity"] = GetSecuritySeverity(eventType);

        var logLevel = GetSecurityLogLevel(eventType);

        _logger.Log(logLevel, "【SECURITY】{EventType} | {Message} | Data: {LogData}",
            eventType, message, JsonSerializer.Serialize(logData, _jsonOptions));
    }

    public IDisposable BeginScope(string operation, Dictionary<string, object>? properties = null)
    {
        return new PerformanceScope(this, operation, properties);
    }

    private static Dictionary<string, object> CreateLogData(string category, string message, object? data, Dictionary<string, object>? properties)
    {
        var logData = new Dictionary<string, object>
        {
            ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ["category"] = category,
            ["message"] = message,
            ["traceId"] = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString(),
            ["spanId"] = Activity.Current?.SpanId.ToString() ?? Guid.NewGuid().ToString("N")[..16]
        };

        if (data != null)
        {
            logData["data"] = data;
        }

        if (properties != null)
        {
            foreach (var prop in properties)
            {
                logData[prop.Key] = prop.Value;
            }
        }

        return logData;
    }

    private static string GetSecuritySeverity(SecurityEventType eventType)
    {
        return eventType switch
        {
            SecurityEventType.LoginAttempt => "LOW",
            SecurityEventType.TokenValidation => "LOW",
            SecurityEventType.FailedLogin => "MEDIUM",
            SecurityEventType.AccessDenied => "MEDIUM",
            SecurityEventType.PasswordChange => "MEDIUM",
            SecurityEventType.SuspiciousActivity => "HIGH",
            SecurityEventType.AccountLocked => "HIGH",
            SecurityEventType.UnauthorizedAccess => "CRITICAL",
            _ => "MEDIUM"
        };
    }

    private static LogLevel GetSecurityLogLevel(SecurityEventType eventType)
    {
        return eventType switch
        {
            SecurityEventType.LoginAttempt => LogLevel.Information,
            SecurityEventType.TokenValidation => LogLevel.Information,
            SecurityEventType.FailedLogin => LogLevel.Warning,
            SecurityEventType.AccessDenied => LogLevel.Warning,
            SecurityEventType.PasswordChange => LogLevel.Information,
            SecurityEventType.SuspiciousActivity => LogLevel.Warning,
            SecurityEventType.AccountLocked => LogLevel.Warning,
            SecurityEventType.UnauthorizedAccess => LogLevel.Error,
            _ => LogLevel.Information
        };
    }
}

/// <summary>
/// 性能追踪作用域
/// </summary>
internal class PerformanceScope : IDisposable
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private bool _disposed;
    private readonly IStructuredLogger _logger;
    private readonly string _operation;
    private readonly Dictionary<string, object>? _properties;

    /// <summary>
    /// 性能追踪作用域
    /// </summary>
    public PerformanceScope(IStructuredLogger logger, string operation, Dictionary<string, object>? properties)
    {
        _logger = logger;
        _operation = operation;
        _properties = properties;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _stopwatch.Stop();
        _logger.LogPerformance(_operation, _stopwatch.Elapsed, true, _properties);
        _disposed = true;
    }
}
