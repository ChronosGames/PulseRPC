using System.Collections.Concurrent;

namespace PulseRPC.Channels;

/// <summary>
/// 请求上下文默认实现
/// </summary>
public sealed class RequestContext : IRequestContext
{
    private readonly ConcurrentDictionary<string, object?> _properties = new();

    /// <summary>
    /// 创建请求上下文
    /// </summary>
    /// <param name="requestId">请求ID</param>
    /// <param name="connectionId">连接ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    public RequestContext(Guid requestId, string connectionId, CancellationToken cancellationToken = default)
    {
        RequestId = requestId;
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        CancellationToken = cancellationToken;
    }

    /// <inheritdoc />
    public Guid RequestId { get; }

    /// <inheritdoc />
    public string ConnectionId { get; }

    /// <inheritdoc />
    public CancellationToken CancellationToken { get; }

    /// <inheritdoc />
    public object? GetProperty(string key)
    {
        _properties.TryGetValue(key, out var value);
        return value;
    }

    /// <inheritdoc />
    public void SetProperty(string key, object? value)
    {
        if (value == null)
        {
            _properties.TryRemove(key, out _);
        }
        else
        {
            _properties[key] = value;
        }
    }
}
