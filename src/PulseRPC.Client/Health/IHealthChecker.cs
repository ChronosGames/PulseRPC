using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Health;

/// <summary>
/// 健康检查状态
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// 健康
    /// </summary>
    Healthy,

    /// <summary>
    /// 不健康
    /// </summary>
    Unhealthy,

    /// <summary>
    /// 降级
    /// </summary>
    Degraded,

    /// <summary>
    /// 未知
    /// </summary>
    Unknown
}

/// <summary>
/// 健康检查结果
/// </summary>
public sealed class HealthCheckResult
{
    /// <summary>
    /// 健康状态
    /// </summary>
    public HealthStatus Status { get; }

    /// <summary>
    /// 描述信息
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 检查时间
    /// </summary>
    public DateTime CheckTime { get; }

    /// <summary>
    /// 响应时间
    /// </summary>
    public TimeSpan ResponseTime { get; }

    /// <summary>
    /// 异常信息
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// 扩展数据
    /// </summary>
    public IReadOnlyDictionary<string, object> Data { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public HealthCheckResult(
        HealthStatus status,
        string description,
        TimeSpan responseTime,
        Exception? exception = null,
        IReadOnlyDictionary<string, object>? data = null)
    {
        Status = status;
        Description = description;
        CheckTime = DateTime.UtcNow;
        ResponseTime = responseTime;
        Exception = exception;
        Data = data ?? new Dictionary<string, object>();
    }

    /// <summary>
    /// 创建健康结果
    /// </summary>
    public static HealthCheckResult Healthy(string description, TimeSpan responseTime, IReadOnlyDictionary<string, object>? data = null)
        => new(HealthStatus.Healthy, description, responseTime, null, data);

    /// <summary>
    /// 创建不健康结果
    /// </summary>
    public static HealthCheckResult Unhealthy(string description, TimeSpan responseTime, Exception? exception = null, IReadOnlyDictionary<string, object>? data = null)
        => new(HealthStatus.Unhealthy, description, responseTime, exception, data);

    /// <summary>
    /// 创建降级结果
    /// </summary>
    public static HealthCheckResult Degraded(string description, TimeSpan responseTime, Exception? exception = null, IReadOnlyDictionary<string, object>? data = null)
        => new(HealthStatus.Degraded, description, responseTime, exception, data);

    /// <summary>
    /// 创建未知结果
    /// </summary>
    public static HealthCheckResult Unknown(string description, TimeSpan responseTime, Exception? exception = null, IReadOnlyDictionary<string, object>? data = null)
        => new(HealthStatus.Unknown, description, responseTime, exception, data);
}

/// <summary>
/// 健康检查上下文
/// </summary>
public sealed class HealthCheckContext
{
    /// <summary>
    /// 检查器名称
    /// </summary>
    public string CheckerName { get; }

    /// <summary>
    /// 目标资源
    /// </summary>
    public object Target { get; }

    /// <summary>
    /// 检查参数
    /// </summary>
    public IReadOnlyDictionary<string, object> Parameters { get; }

    /// <summary>
    /// 取消令牌
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public HealthCheckContext(
        string checkerName,
        object target,
        IReadOnlyDictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        CheckerName = checkerName;
        Target = target;
        Parameters = parameters ?? new Dictionary<string, object>();
        CancellationToken = cancellationToken;
    }
}

/// <summary>
/// 健康检查器接口
/// </summary>
public interface IHealthChecker
{
    /// <summary>
    /// 检查器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 支持的目标类型
    /// </summary>
    Type[] SupportedTargetTypes { get; }

    /// <summary>
    /// 是否支持指定的目标类型
    /// </summary>
    bool SupportsTarget(Type targetType);

    /// <summary>
    /// 执行健康检查
    /// </summary>
    Task<HealthCheckResult> CheckAsync(HealthCheckContext context);
}

/// <summary>
/// 通用健康检查器接口
/// </summary>
public interface IHealthChecker<in T> : IHealthChecker
{
    /// <summary>
    /// 执行健康检查
    /// </summary>
    Task<HealthCheckResult> CheckAsync(T target, CancellationToken cancellationToken = default);
}

/// <summary>
/// 健康检查器基类
/// </summary>
public abstract class HealthCheckerBase : IHealthChecker
{
    /// <summary>
    /// 检查器名称
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// 支持的目标类型
    /// </summary>
    public abstract Type[] SupportedTargetTypes { get; }

    /// <summary>
    /// 是否支持指定的目标类型
    /// </summary>
    public virtual bool SupportsTarget(Type targetType)
    {
        foreach (var supportedType in SupportedTargetTypes)
        {
            if (supportedType.IsAssignableFrom(targetType))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 执行健康检查
    /// </summary>
    public async Task<HealthCheckResult> CheckAsync(HealthCheckContext context)
    {
        if (!SupportsTarget(context.Target.GetType()))
        {
            return HealthCheckResult.Unknown(
                $"不支持的目标类型: {context.Target.GetType().Name}",
                TimeSpan.Zero);
        }

        var startTime = DateTime.UtcNow;
        try
        {
            var result = await CheckInternalAsync(context);
            return result;
        }
        catch (Exception ex)
        {
            var responseTime = DateTime.UtcNow - startTime;
            return HealthCheckResult.Unhealthy(
                $"健康检查异常: {ex.Message}",
                responseTime,
                ex);
        }
    }

    /// <summary>
    /// 内部健康检查实现
    /// </summary>
    protected abstract Task<HealthCheckResult> CheckInternalAsync(HealthCheckContext context);
}

/// <summary>
/// 泛型健康检查器基类
/// </summary>
public abstract class HealthCheckerBase<T> : HealthCheckerBase, IHealthChecker<T>
{
    /// <summary>
    /// 支持的目标类型
    /// </summary>
    public override Type[] SupportedTargetTypes => new[] { typeof(T) };

    /// <summary>
    /// 执行健康检查
    /// </summary>
    public async Task<HealthCheckResult> CheckAsync(T target, CancellationToken cancellationToken = default)
    {
        var context = new HealthCheckContext(Name, target!, cancellationToken: cancellationToken);
        return await CheckAsync(context);
    }

    /// <summary>
    /// 内部健康检查实现
    /// </summary>
    protected override async Task<HealthCheckResult> CheckInternalAsync(HealthCheckContext context)
    {
        if (context.Target is not T target)
        {
            return HealthCheckResult.Unknown(
                $"无效的目标类型: {context.Target.GetType().Name}",
                TimeSpan.Zero);
        }

        return await CheckTargetAsync(target, context);
    }

    /// <summary>
    /// 检查特定目标
    /// </summary>
    protected abstract Task<HealthCheckResult> CheckTargetAsync(T target, HealthCheckContext context);
}

/// <summary>
/// 健康检查事件参数
/// </summary>
public sealed class HealthCheckEventArgs : EventArgs
{
    /// <summary>
    /// 检查器名称
    /// </summary>
    public string CheckerName { get; }

    /// <summary>
    /// 目标对象
    /// </summary>
    public object Target { get; }

    /// <summary>
    /// 检查结果
    /// </summary>
    public HealthCheckResult Result { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public HealthCheckEventArgs(string checkerName, object target, HealthCheckResult result)
    {
        CheckerName = checkerName;
        Target = target;
        Result = result;
    }
}

/// <summary>
/// 健康检查管理器接口
/// </summary>
public interface IHealthCheckManager
{
    /// <summary>
    /// 健康检查完成事件
    /// </summary>
    event EventHandler<HealthCheckEventArgs>? HealthCheckCompleted;

    /// <summary>
    /// 注册健康检查器
    /// </summary>
    void RegisterChecker(IHealthChecker checker);

    /// <summary>
    /// 取消注册健康检查器
    /// </summary>
    bool UnregisterChecker(string checkerName);

    /// <summary>
    /// 获取健康检查器
    /// </summary>
    IHealthChecker? GetChecker(string checkerName);

    /// <summary>
    /// 获取所有检查器
    /// </summary>
    IReadOnlyList<IHealthChecker> GetAllCheckers();

    /// <summary>
    /// 执行健康检查
    /// </summary>
    Task<HealthCheckResult> CheckAsync(string checkerName, object target, CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行批量健康检查
    /// </summary>
    Task<IReadOnlyDictionary<string, HealthCheckResult>> CheckAllAsync(object target, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取健康检查历史
    /// </summary>
    IReadOnlyList<HealthCheckEventArgs> GetHistory(string? checkerName = null, int maxCount = 100);

    /// <summary>
    /// 清理历史记录
    /// </summary>
    void ClearHistory();
}
