using System.Diagnostics;

namespace PulseRPC.Tracing
{
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

        /// <summary>
        /// 获取当前活跃跨度
        /// </summary>
        ISpan? ActiveSpan { get; }

        /// <summary>
        /// 设置当前活跃跨度
        /// </summary>
        /// <param name="span">跨度</param>
        /// <returns>作用域</returns>
        IScope SetActiveSpan(ISpan span);

        /// <summary>
        /// 刷新追踪数据
        /// </summary>
        Task FlushAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 关闭追踪器
        /// </summary>
        Task CloseAsync(CancellationToken cancellationToken = default);
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
        string OperationName { get; }

        /// <summary>
        /// 开始时间
        /// </summary>
        DateTimeOffset StartTime { get; }

        /// <summary>
        /// 结束时间
        /// </summary>
        DateTimeOffset? FinishTime { get; }

        /// <summary>
        /// 是否已完成
        /// </summary>
        bool IsFinished { get; }

        /// <summary>
        /// 设置标签
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        /// <returns>跨度</returns>
        ISpan SetTag(string key, object value);

        /// <summary>
        /// 设置多个标签
        /// </summary>
        /// <param name="tags">标签</param>
        /// <returns>跨度</returns>
        ISpan SetTags(IDictionary<string, object> tags);

        /// <summary>
        /// 记录日志
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="timestamp">时间戳</param>
        /// <param name="fields">字段</param>
        /// <returns>跨度</returns>
        ISpan Log(string message, DateTimeOffset? timestamp = null, IDictionary<string, object>? fields = null);

        /// <summary>
        /// 设置状态
        /// </summary>
        /// <param name="status">状态</param>
        /// <param name="description">描述</param>
        /// <returns>跨度</returns>
        ISpan SetStatus(SpanStatus status, string? description = null);

        /// <summary>
        /// 记录异常
        /// </summary>
        /// <param name="exception">异常</param>
        /// <returns>跨度</returns>
        ISpan RecordException(Exception exception);

        /// <summary>
        /// 完成跨度
        /// </summary>
        /// <param name="finishTime">完成时间</param>
        void Finish(DateTimeOffset? finishTime = null);

        /// <summary>
        /// 获取标签值
        /// </summary>
        /// <param name="key">键</param>
        /// <returns>值</returns>
        object? GetTag(string key);

        /// <summary>
        /// 获取所有标签
        /// </summary>
        /// <returns>标签字典</returns>
        IReadOnlyDictionary<string, object> GetTags();

        /// <summary>
        /// 获取所有日志
        /// </summary>
        /// <returns>日志列表</returns>
        IReadOnlyList<SpanLog> GetLogs();
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
        /// 父跨度ID
        /// </summary>
        string? ParentSpanId { get; }

        /// <summary>
        /// 采样标志
        /// </summary>
        bool IsSampled { get; }

        /// <summary>
        /// 行李
        /// </summary>
        IReadOnlyDictionary<string, string> Baggage { get; }

        /// <summary>
        /// 设置行李
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        /// <returns>新的上下文</returns>
        ISpanContext SetBaggage(string key, string value);

        /// <summary>
        /// 获取行李
        /// </summary>
        /// <param name="key">键</param>
        /// <returns>值</returns>
        string? GetBaggage(string key);
    }

    /// <summary>
    /// 作用域接口
    /// </summary>
    public interface IScope : IDisposable
    {
        /// <summary>
        /// 跨度
        /// </summary>
        ISpan Span { get; }
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
        /// 正常
        /// </summary>
        Ok = 1,

        /// <summary>
        /// 错误
        /// </summary>
        Error = 2,

        /// <summary>
        /// 取消
        /// </summary>
        Cancelled = 3,

        /// <summary>
        /// 超时
        /// </summary>
        DeadlineExceeded = 4,

        /// <summary>
        /// 参数无效
        /// </summary>
        InvalidArgument = 5,

        /// <summary>
        /// 未找到
        /// </summary>
        NotFound = 6,

        /// <summary>
        /// 已存在
        /// </summary>
        AlreadyExists = 7,

        /// <summary>
        /// 权限不足
        /// </summary>
        PermissionDenied = 8,

        /// <summary>
        /// 资源耗尽
        /// </summary>
        ResourceExhausted = 9,

        /// <summary>
        /// 前置条件失败
        /// </summary>
        FailedPrecondition = 10,

        /// <summary>
        /// 已中止
        /// </summary>
        Aborted = 11,

        /// <summary>
        /// 超出范围
        /// </summary>
        OutOfRange = 12,

        /// <summary>
        /// 未实现
        /// </summary>
        Unimplemented = 13,

        /// <summary>
        /// 内部错误
        /// </summary>
        Internal = 14,

        /// <summary>
        /// 不可用
        /// </summary>
        Unavailable = 15,

        /// <summary>
        /// 数据丢失
        /// </summary>
        DataLoss = 16,

        /// <summary>
        /// 未认证
        /// </summary>
        Unauthenticated = 17
    }

    /// <summary>
    /// 跨度日志
    /// </summary>
    public class SpanLog
    {
        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 字段
        /// </summary>
        public Dictionary<string, object> Fields { get; set; } = new();
    }
} 