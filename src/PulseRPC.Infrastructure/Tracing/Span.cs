using System.Collections.Concurrent;

namespace PulseRPC.Tracing;

/// <summary>
/// 跨度实现
/// </summary>
public class Span : ISpan
{
    private readonly Tracer _tracer;
    private readonly ConcurrentDictionary<string, object> _tags = new();
    private readonly List<SpanLog> _logs = new();
    private readonly object _lock = new();
    private bool _disposed;

    public Span(Tracer tracer, ISpanContext context, string operationName, DateTimeOffset startTime, IDictionary<string, object>? initialTags = null)
    {
        _tracer = tracer;
        Context = context;
        OperationName = operationName;
        StartTime = startTime;
        Status = SpanStatus.Unset;

        if (initialTags == null)
        {
            return;
        }

        foreach (var tag in initialTags)
        {
            _tags.TryAdd(tag.Key, tag.Value);
        }
    }

    /// <summary>
    /// 跨度上下文
    /// </summary>
    public ISpanContext Context { get; }

    /// <summary>
    /// 操作名称
    /// </summary>
    public string OperationName { get; set; }

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTimeOffset StartTime { get; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTimeOffset? FinishTime { get; private set; }

    /// <summary>
    /// 是否已完成
    /// </summary>
    public bool IsFinished { get; private set; }

    /// <summary>
    /// 跨度状态
    /// </summary>
    public SpanStatus Status { get; private set; }

    /// <summary>
    /// 状态描述
    /// </summary>
    public string? StatusDescription { get; private set; }

    /// <summary>
    /// 设置标签
    /// </summary>
    public ISpan SetTag(string key, object value)
    {
        if (_disposed || IsFinished) return this;

        _tags.AddOrUpdate(key, value, (_, _) => value);
        return this;
    }

    /// <summary>
    /// 设置多个标签
    /// </summary>
    public ISpan SetTags(IDictionary<string, object> tags)
    {
        if (_disposed || IsFinished) return this;

        foreach (var tag in tags)
        {
            _tags.AddOrUpdate(tag.Key, tag.Value, (_, _) => tag.Value);
        }
        return this;
    }

    /// <summary>
    /// 记录日志
    /// </summary>
    public ISpan Log(string message, DateTimeOffset? timestamp = null, IDictionary<string, object>? fields = null)
    {
        if (_disposed || IsFinished) return this;

        lock (_lock)
        {
            var log = new SpanLog
            {
                Timestamp = timestamp ?? DateTimeOffset.UtcNow,
                Message = message,
                Fields = fields?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, object>()
            };

            _logs.Add(log);
        }

        return this;
    }

    /// <summary>
    /// 设置状态
    /// </summary>
    public ISpan SetStatus(SpanStatus status, string? description = null)
    {
        if (_disposed || IsFinished) return this;

        Status = status;
        StatusDescription = description;

        // 设置相应的标签
        SetTag("status.code", (int)status);
        if (!string.IsNullOrEmpty(description))
        {
            SetTag("status.description", description);
        }

        return this;
    }

    /// <summary>
    /// 记录异常
    /// </summary>
    public ISpan RecordException(Exception exception)
    {
        if (_disposed || IsFinished) return this;

        SetStatus(SpanStatus.Error, exception.Message);

        var fields = new Dictionary<string, object>
        {
            ["exception.type"] = exception.GetType().FullName ?? exception.GetType().Name,
            ["exception.message"] = exception.Message,
            ["exception.stacktrace"] = exception.StackTrace ?? string.Empty
        };

        if (exception.InnerException != null)
        {
            fields["exception.inner_type"] = exception.InnerException.GetType().FullName ?? exception.InnerException.GetType().Name;
            fields["exception.inner_message"] = exception.InnerException.Message;
        }

        Log("Exception occurred", null, fields);

        // 设置错误相关标签
        SetTag("error", true);
        SetTag("error.kind", exception.GetType().Name);
        SetTag("error.object", exception);

        return this;
    }

    /// <summary>
    /// 完成跨度
    /// </summary>
    public void Finish(DateTimeOffset? finishTime = null)
    {
        if (_disposed || IsFinished) return;

        lock (_lock)
        {
            if (IsFinished) return;

            FinishTime = finishTime ?? DateTimeOffset.UtcNow;
            IsFinished = true;

            // 设置持续时间标签
            var duration = FinishTime.Value - StartTime;
            SetTag("duration_ms", duration.TotalMilliseconds);

            // 如果状态未设置，默认为成功
            if (Status == SpanStatus.Unset)
            {
                SetStatus(SpanStatus.Ok);
            }
        }

        // 从追踪器中移除
        _tracer.RemoveActiveSpan(Context.SpanId);
    }

    /// <summary>
    /// 获取标签值
    /// </summary>
    public object? GetTag(string key)
    {
        return _tags.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// 获取所有标签
    /// </summary>
    public IReadOnlyDictionary<string, object> GetTags()
    {
        return _tags.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// 获取所有日志
    /// </summary>
    public IReadOnlyList<SpanLog> GetLogs()
    {
        lock (_lock)
        {
            return _logs.ToList();
        }
    }

    /// <summary>
    /// 获取跨度摘要
    /// </summary>
    public SpanSummary GetSummary()
    {
        return new SpanSummary
        {
            TraceId = Context.TraceId,
            SpanId = Context.SpanId,
            ParentSpanId = Context.ParentSpanId,
            OperationName = OperationName,
            StartTime = StartTime,
            FinishTime = FinishTime,
            Duration = FinishTime.HasValue ? FinishTime.Value - StartTime : null,
            Status = Status,
            StatusDescription = StatusDescription,
            IsSampled = Context.IsSampled,
            IsFinished = IsFinished,
            TagCount = _tags.Count,
            LogCount = _logs.Count,
            HasError = GetTag("error") as bool? == true
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (!IsFinished)
            {
                Finish();
            }
            _disposed = true;
        }
    }

    public override string ToString()
    {
        var duration = FinishTime.HasValue ? $"{(FinishTime.Value - StartTime).TotalMilliseconds:F2}ms" : "ongoing";
        return $"Span[{OperationName}] TraceId={Context.TraceId[..8]}... SpanId={Context.SpanId[..8]}... Duration={duration} Status={Status}";
    }
}

/// <summary>
/// 跨度上下文实现
/// </summary>
public class SpanContext : ISpanContext
{
    private readonly Dictionary<string, string> _baggage;

    public SpanContext(string traceId, string spanId, string? parentSpanId, bool isSampled, IDictionary<string, string> baggage)
    {
        TraceId = traceId;
        SpanId = spanId;
        ParentSpanId = parentSpanId;
        IsSampled = isSampled;
        _baggage = new Dictionary<string, string>(baggage);
    }

    /// <summary>
    /// 跟踪ID
    /// </summary>
    public string TraceId { get; }

    /// <summary>
    /// 跨度ID
    /// </summary>
    public string SpanId { get; }

    /// <summary>
    /// 父跨度ID
    /// </summary>
    public string? ParentSpanId { get; }

    /// <summary>
    /// 采样标志
    /// </summary>
    public bool IsSampled { get; }

    /// <summary>
    /// 行李
    /// </summary>
    public IReadOnlyDictionary<string, string> Baggage => _baggage;

    /// <summary>
    /// 设置行李
    /// </summary>
    public ISpanContext SetBaggage(string key, string value)
    {
        var newBaggage = new Dictionary<string, string>(_baggage)
        {
            [key] = value
        };
        return new SpanContext(TraceId, SpanId, ParentSpanId, IsSampled, newBaggage);
    }

    /// <summary>
    /// 获取行李
    /// </summary>
    public string? GetBaggage(string key)
    {
        return _baggage.TryGetValue(key, out var value) ? value : null;
    }

    public override string ToString()
    {
        return $"SpanContext[TraceId={TraceId[..8]}..., SpanId={SpanId[..8]}..., ParentSpanId={ParentSpanId?[..8]}..., IsSampled={IsSampled}]";
    }
}

/// <summary>
/// 作用域实现
/// </summary>
public class Scope : IScope
{
    private readonly Tracer _tracer;
    private readonly ISpan? _previousSpan;
    private bool _disposed;

    public Scope(Tracer tracer, ISpan span)
    {
        _tracer = tracer;
        Span = span;
        _previousSpan = tracer.ActiveSpan;
        _tracer.SetCurrentSpan(span);
    }

    /// <summary>
    /// 跨度
    /// </summary>
    public ISpan Span { get; }

    public void Dispose()
    {
        if (!_disposed)
        {
            _tracer.SetCurrentSpan(_previousSpan);
            _disposed = true;
        }
    }
}

/// <summary>
/// 跨度摘要
/// </summary>
public class SpanSummary
{
    public string TraceId { get; set; } = string.Empty;
    public string SpanId { get; set; } = string.Empty;
    public string? ParentSpanId { get; set; }
    public string OperationName { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset? FinishTime { get; set; }
    public TimeSpan? Duration { get; set; }
    public SpanStatus Status { get; set; }
    public string? StatusDescription { get; set; }
    public bool IsSampled { get; set; }
    public bool IsFinished { get; set; }
    public int TagCount { get; set; }
    public int LogCount { get; set; }
    public bool HasError { get; set; }
}
