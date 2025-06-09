using System.Diagnostics;

namespace PulseRPC.Tracing;

/// <summary>
/// 链路追踪器接口
/// </summary>
public interface ITracer
{
    /// <summary>
    /// 开始新的跨度
    /// </summary>
    /// <param name="operationName">操作名称</param>
    /// <param name="parentSpan">父跨度</param>
    /// <param name="tags">标签</param>
    /// <returns>跨度</returns>
    ISpan StartSpan(string operationName, ISpan? parentSpan = null, IDictionary<string, object>? tags = null);

    /// <summary>
    /// 从活动开始跨度
    /// </summary>
    /// <param name="activity">活动</param>
    /// <returns>跨度</returns>
    ISpan StartSpan(Activity activity);

    /// <summary>
    /// 注入跨度上下文到载体
    /// </summary>
    /// <param name="span">跨度</param>
    /// <param name="carrier">载体</param>
    void Inject(ISpan span, IDictionary<string, string> carrier);

    /// <summary>
    /// 从载体提取跨度上下文
    /// </summary>
    /// <param name="carrier">载体</param>
    /// <returns>跨度上下文</returns>
    ISpanContext? Extract(IDictionary<string, string> carrier);
}

/// <summary>
/// 跨度接口
/// </summary>
public interface ISpan : IDisposable
{
    /// <summary>
    /// 跨度上下文
    /// </summary>
    ISpanContext Context { get; }

    /// <summary>
    /// 操作名称
    /// </summary>
    string OperationName { get; set; }

    /// <summary>
    /// 设置标签
    /// </summary>
    /// <param name="key">标签键</param>
    /// <param name="value">标签值</param>
    /// <returns>跨度</returns>
    ISpan SetTag(string key, object value);

    /// <summary>
    /// 设置状态
    /// </summary>
    /// <param name="status">状态</param>
    /// <returns>跨度</returns>
    ISpan SetStatus(SpanStatus status);

    /// <summary>
    /// 记录事件
    /// </summary>
    /// <param name="eventName">事件名称</param>
    /// <param name="timestamp">时间戳</param>
    /// <param name="tags">标签</param>
    /// <returns>跨度</returns>
    ISpan LogEvent(string eventName, DateTime? timestamp = null, IDictionary<string, object>? tags = null);

    /// <summary>
    /// 完成跨度
    /// </summary>
    /// <param name="finishTime">完成时间</param>
    void Finish(DateTime? finishTime = null);
}

/// <summary>
/// 跨度上下文接口
/// </summary>
public interface ISpanContext
{
    /// <summary>
    /// 跟踪ID
    /// </summary>
    string TraceId { get; }

    /// <summary>
    /// 跨度ID
    /// </summary>
    string SpanId { get; }

    /// <summary>
    /// 是否采样
    /// </summary>
    bool IsSampled { get; }

    /// <summary>
    /// 附加数据
    /// </summary>
    IDictionary<string, string> Baggage { get; }
}

/// <summary>
/// 跨度状态
/// </summary>
public enum SpanStatus
{
    /// <summary>
    /// 未设置
    /// </summary>
    Unset = 0,

    /// <summary>
    /// 成功
    /// </summary>
    Ok = 1,

    /// <summary>
    /// 错误
    /// </summary>
    Error = 2
}
