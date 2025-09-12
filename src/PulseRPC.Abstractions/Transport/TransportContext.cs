using System;
using System.Collections.Concurrent;
using System.Net;
using PulseRPC.Authentication;

namespace PulseRPC.Transport;

/// <summary>
/// 传输上下文 - 封装连接相关的所有信息
/// 客户端和服务端通用的连接上下文
/// </summary>
public sealed class TransportContext : IDisposable
{
    private readonly ConcurrentDictionary<string, object> _properties = new();
    private volatile bool _disposed;

    /// <summary>
    /// 连接ID（唯一标识）
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// 传输实例
    /// </summary>
    public ITransport Transport { get; }

    /// <summary>
    /// 传输类型
    /// </summary>
    public TransportType TransportType => Transport.Type;

    /// <summary>
    /// 连接状态
    /// </summary>
    public ConnectionState State => Transport.State;

    /// <summary>
    /// 是否已连接
    /// </summary>
    public bool IsConnected => Transport.IsConnected;

    /// <summary>
    /// 本地端点
    /// </summary>
    public EndPoint LocalEndPoint => Transport.LocalEndPoint;

    /// <summary>
    /// 远程端点
    /// </summary>
    public EndPoint RemoteEndPoint => Transport.RemoteEndPoint;

    /// <summary>
    /// 认证上下文
    /// </summary>
    public IAuthenticationContext? AuthenticationContext { get; set; }

    /// <summary>
    /// 是否已认证
    /// </summary>
    public bool IsAuthenticated => AuthenticationContext != null;

    /// <summary>
    /// 连接建立时间
    /// </summary>
    public DateTime ConnectedTime { get; }

    /// <summary>
    /// 最后活跃时间
    /// </summary>
    public DateTime LastActiveTime { get; set; }

    /// <summary>
    /// 自定义属性集合
    /// </summary>
    public ConcurrentDictionary<string, object> Properties => _properties;

    /// <summary>
    /// 远程地址字符串
    /// </summary>
    public string RemoteAddress => RemoteEndPoint?.ToString() ?? "Unknown";

    /// <summary>
    /// 连接持续时间
    /// </summary>
    public TimeSpan ConnectionDuration => DateTime.UtcNow - ConnectedTime;

    /// <summary>
    /// 空闲时间
    /// </summary>
    public TimeSpan IdleTime => DateTime.UtcNow - LastActiveTime;

    /// <summary>
    /// 是否已释放
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// 统计信息
    /// </summary>
    public TransportStatistics Statistics { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <param name="transport">传输实例</param>
    public TransportContext(string connectionId, ITransport transport)
    {
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        ConnectedTime = DateTime.UtcNow;
        LastActiveTime = DateTime.UtcNow;
        Statistics = new TransportStatistics();
    }

    /// <summary>
    /// 发送数据
    /// </summary>
    /// <param name="data">要发送的数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否发送成功</returns>
    public async Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return false;

        try
        {
            var result = await Transport.SendAsync(data, cancellationToken);

            if (result)
            {
                LastActiveTime = DateTime.UtcNow;
                Statistics.IncrementBytesSent(data.Length);
                Statistics.IncrementMessagesSent();
            }

            return result;
        }
        catch
        {
            Statistics.IncrementSendErrors();
            return false;
        }
    }

    /// <summary>
    /// 设置属性
    /// </summary>
    /// <param name="key">属性键</param>
    /// <param name="value">属性值</param>
    public void SetProperty(string key, object value)
    {
        if (!_disposed)
        {
            _properties[key] = value;
        }
    }

    /// <summary>
    /// 获取属性
    /// </summary>
    /// <typeparam name="T">属性类型</typeparam>
    /// <param name="key">属性键</param>
    /// <returns>属性值，如果不存在则返回默认值</returns>
    public T? GetProperty<T>(string key)
    {
        if (_properties.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }

    /// <summary>
    /// 移除属性
    /// </summary>
    /// <param name="key">属性键</param>
    /// <returns>是否成功移除</returns>
    public bool RemoveProperty(string key)
    {
        return _properties.TryRemove(key, out _);
    }

    /// <summary>
    /// 更新最后活跃时间
    /// </summary>
    public void UpdateLastActiveTime()
    {
        if (!_disposed)
        {
            LastActiveTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 记录接收数据
    /// </summary>
    /// <param name="dataLength">数据长度</param>
    internal void RecordDataReceived(int dataLength)
    {
        if (!_disposed)
        {
            LastActiveTime = DateTime.UtcNow;
            Statistics.IncrementBytesReceived(dataLength);
            Statistics.IncrementMessagesReceived();
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            // 清理自定义属性
            _properties.Clear();

            // 不释放 Transport，因为它可能被其他地方引用
            // Transport 的生命周期应该由 TransportManager 管理
        }
    }

    public override string ToString()
    {
        return $"TransportContext[{ConnectionId}] {TransportType} {RemoteAddress} {State}";
    }
}

/// <summary>
/// 传输统计信息
/// </summary>
public sealed class TransportStatistics
{
    private long _messagesSent;
    private long _messagesReceived;
    private long _bytesSent;
    private long _bytesReceived;
    private long _sendErrors;
    private long _receiveErrors;

    /// <summary>
    /// 已发送消息数
    /// </summary>
    public long MessagesSent => _messagesSent;

    /// <summary>
    /// 已接收消息数
    /// </summary>
    public long MessagesReceived => _messagesReceived;

    /// <summary>
    /// 已发送字节数
    /// </summary>
    public long BytesSent => _bytesSent;

    /// <summary>
    /// 已接收字节数
    /// </summary>
    public long BytesReceived => _bytesReceived;

    /// <summary>
    /// 发送错误数
    /// </summary>
    public long SendErrors => _sendErrors;

    /// <summary>
    /// 接收错误数
    /// </summary>
    public long ReceiveErrors => _receiveErrors;

    /// <summary>
    /// 增加发送消息计数
    /// </summary>
    internal void IncrementMessagesSent() => Interlocked.Increment(ref _messagesSent);

    /// <summary>
    /// 增加接收消息计数
    /// </summary>
    internal void IncrementMessagesReceived() => Interlocked.Increment(ref _messagesReceived);

    /// <summary>
    /// 增加发送字节数
    /// </summary>
    internal void IncrementBytesSent(long bytes) => Interlocked.Add(ref _bytesSent, bytes);

    /// <summary>
    /// 增加接收字节数
    /// </summary>
    internal void IncrementBytesReceived(long bytes) => Interlocked.Add(ref _bytesReceived, bytes);

    /// <summary>
    /// 增加发送错误计数
    /// </summary>
    internal void IncrementSendErrors() => Interlocked.Increment(ref _sendErrors);

    /// <summary>
    /// 增加接收错误计数
    /// </summary>
    internal void IncrementReceiveErrors() => Interlocked.Increment(ref _receiveErrors);

    /// <summary>
    /// 重置统计信息
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _messagesSent, 0);
        Interlocked.Exchange(ref _messagesReceived, 0);
        Interlocked.Exchange(ref _bytesSent, 0);
        Interlocked.Exchange(ref _bytesReceived, 0);
        Interlocked.Exchange(ref _sendErrors, 0);
        Interlocked.Exchange(ref _receiveErrors, 0);
    }
}
