using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace PulseRPC.Routing;

/// <summary>
/// 多实例服务管理器
/// </summary>
public interface IMultiInstanceServiceManager<T> : IDisposable where T : class, IPulseService
{
    /// <summary>
    /// 获取所有可用实例
    /// </summary>
    Task<IReadOnlyList<ServiceInstanceInfo>> GetAvailableInstancesAsync();

    /// <summary>
    /// 根据路由上下文获取服务代理
    /// </summary>
    Task<T> GetServiceAsync2(IRoutingContext routingContext);

    /// <summary>
    /// 获取特定实例的服务代理
    /// </summary>
    Task<T> GetServiceAsync2(string instanceId);

    /// <summary>
    /// 执行广播调用（所有实例）
    /// </summary>
    Task<BroadcastResult<TResult>> BroadcastAsync<TResult>(Func<T, Task<TResult>> operation);

    /// <summary>
    /// 执行聚合调用（多实例结果聚合）
    /// </summary>
    Task<TAggregated> AggregateAsync<TResult, TAggregated>(
        Func<T, Task<TResult>> operation,
        Func<IEnumerable<TResult>, TAggregated> aggregator);

    /// <summary>
    /// 执行并行调用（指定实例列表）
    /// </summary>
    Task<ParallelResult<TResult>> ParallelAsync<TResult>(
        Func<T, Task<TResult>> operation,
        IEnumerable<string> instanceIds);

    /// <summary>
    /// 获取实例健康状态
    /// </summary>
    Task<Dictionary<string, bool>> GetInstanceHealthAsync();

    /// <summary>
    /// 实例状态变化事件
    /// </summary>
    event EventHandler<ServiceInstanceEventArgs> InstanceStateChanged;

    /// <summary>
    /// 负载均衡状态变化事件
    /// </summary>
    event EventHandler<LoadBalancingEventArgs> LoadBalancingChanged;
}

/// <summary>
/// 广播结果
/// </summary>
public class BroadcastResult<T>
{
    /// <summary>
    /// 所有结果
    /// </summary>
    public IReadOnlyList<BroadcastResultItem<T>> Results { get; set; } = new List<BroadcastResultItem<T>>();

    /// <summary>
    /// 成功数量
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 失败数量
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// 总数量
    /// </summary>
    public int TotalCount => Results.Count;

    /// <summary>
    /// 成功率
    /// </summary>
    public double SuccessRate => TotalCount > 0 ? (double)SuccessCount / TotalCount : 0;

    /// <summary>
    /// 获取成功的结果
    /// </summary>
    public IEnumerable<T> GetSuccessResults()
    {
        return Results.Where(r => r.IsSuccess && r.Result != null).Select(r => r.Result!);
    }

    /// <summary>
    /// 获取失败的异常
    /// </summary>
    public IEnumerable<Exception> GetFailureExceptions()
    {
        return Results.Where(r => !r.IsSuccess && r.Error != null).Select(r => r.Error!);
    }
}

/// <summary>
/// 广播结果项
/// </summary>
public class BroadcastResultItem<T>
{
    /// <summary>
    /// 实例ID
    /// </summary>
    public string InstanceId { get; set; } = "";

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 结果数据
    /// </summary>
    public T? Result { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public Exception? Error { get; set; }

    /// <summary>
    /// 执行时长
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 并行执行结果
/// </summary>
public class ParallelResult<T> : BroadcastResult<T>
{
    /// <summary>
    /// 指定的实例ID列表
    /// </summary>
    public IReadOnlyList<string> TargetInstanceIds { get; set; } = new List<string>();

    /// <summary>
    /// 未找到的实例ID列表
    /// </summary>
    public IReadOnlyList<string> NotFoundInstanceIds { get; set; } = new List<string>();
}

/// <summary>
/// 服务实例事件参数
/// </summary>
public class ServiceInstanceEventArgs : EventArgs
{
    /// <summary>
    /// 实例信息
    /// </summary>
    public ServiceInstanceInfo Instance { get; set; } = new();

    /// <summary>
    /// 事件类型
    /// </summary>
    public ServiceInstanceEventType EventType { get; set; }

    /// <summary>
    /// 事件时间
    /// </summary>
    public DateTime EventTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 附加信息
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// 服务实例事件类型
/// </summary>
public enum ServiceInstanceEventType
{
    /// <summary>
    /// 实例添加
    /// </summary>
    Added,

    /// <summary>
    /// 实例移除
    /// </summary>
    Removed,

    /// <summary>
    /// 实例变为健康
    /// </summary>
    HealthyStateChanged,

    /// <summary>
    /// 实例变为不健康
    /// </summary>
    UnhealthyStateChanged,

    /// <summary>
    /// 连接数变化
    /// </summary>
    ConnectionCountChanged,

    /// <summary>
    /// 元数据更新
    /// </summary>
    MetadataUpdated
}

/// <summary>
/// 负载均衡事件参数
/// </summary>
public class LoadBalancingEventArgs : EventArgs
{
    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = "";

    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public ServiceRoutingStrategy Strategy { get; set; }

    /// <summary>
    /// 可用实例数量
    /// </summary>
    public int AvailableInstanceCount { get; set; }

    /// <summary>
    /// 选中的实例ID
    /// </summary>
    public string? SelectedInstanceId { get; set; }

    /// <summary>
    /// 路由上下文
    /// </summary>
    public IRoutingContext? RoutingContext { get; set; }

    /// <summary>
    /// 事件时间
    /// </summary>
    public DateTime EventTime { get; set; } = DateTime.UtcNow;
}
