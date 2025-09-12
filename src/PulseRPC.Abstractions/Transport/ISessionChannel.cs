using System;
using System.Collections.Generic;
using PulseRPC.Authentication;

namespace PulseRPC.Transport;

/// <summary>
/// 会话通道接口 - 表示一个具有认证和属性管理能力的连接
/// 这是三层抽象架构中的会话层(Session Layer)核心抽象
/// 继承自传输层的ITransportConnection，添加认证和会话管理功能
/// </summary>
public interface ISessionChannel : ITransportConnection
{
    /// <summary>
    /// 认证上下文，包含用户或服务的认证信息
    /// </summary>
    IAuthenticationContext? AuthenticationContext { get; set; }

    /// <summary>
    /// 是否已认证
    /// </summary>
    bool IsAuthenticated => AuthenticationContext?.IsAuthenticated ?? false;

    /// <summary>
    /// 会话属性字典，用于存储自定义数据
    /// </summary>
    IDictionary<string, object> Properties { get; }

    /// <summary>
    /// 远程地址的字符串表示形式
    /// </summary>
    string RemoteAddress => RemoteEndPoint?.ToString() ?? "Unknown";

    /// <summary>
    /// 设置认证信息
    /// </summary>
    /// <param name="authContext">认证上下文</param>
    void SetAuthentication(IAuthenticationContext authContext);

    /// <summary>
    /// 清除认证信息
    /// </summary>
    void ClearAuthentication();

    /// <summary>
    /// 获取会话属性
    /// </summary>
    /// <typeparam name="T">属性值类型</typeparam>
    /// <param name="key">属性键</param>
    /// <returns>属性值，如果不存在返回default</returns>
    T? GetProperty<T>(string key);

    /// <summary>
    /// 设置会话属性
    /// </summary>
    /// <typeparam name="T">属性值类型</typeparam>
    /// <param name="key">属性键</param>
    /// <param name="value">属性值</param>
    void SetProperty<T>(string key, T value);

    /// <summary>
    /// 移除会话属性
    /// </summary>
    /// <param name="key">属性键</param>
    /// <returns>是否成功移除</returns>
    bool RemoveProperty(string key);

    /// <summary>
    /// 检查是否包含指定属性
    /// </summary>
    /// <param name="key">属性键</param>
    /// <returns>是否包含该属性</returns>
    bool HasProperty(string key);

    /// <summary>
    /// 认证状态变化事件
    /// </summary>
    event EventHandler<AuthenticationChangedEventArgs> AuthenticationChanged;
}

/// <summary>
/// 认证状态变化事件参数
/// </summary>
public class AuthenticationChangedEventArgs : EventArgs
{
    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// 旧的认证上下文
    /// </summary>
    public IAuthenticationContext? PreviousAuthentication { get; }

    /// <summary>
    /// 新的认证上下文
    /// </summary>
    public IAuthenticationContext? CurrentAuthentication { get; }

    /// <summary>
    /// 变化时间
    /// </summary>
    public DateTime ChangedAt { get; }

    public AuthenticationChangedEventArgs(
        string connectionId,
        IAuthenticationContext? previousAuthentication,
        IAuthenticationContext? currentAuthentication)
    {
        ConnectionId = connectionId;
        PreviousAuthentication = previousAuthentication;
        CurrentAuthentication = currentAuthentication;
        ChangedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// 会话通道基础实现 - 为具体实现提供通用的会话管理功能
/// </summary>
public abstract class SessionChannelBase : ISessionChannel
{
    private readonly Dictionary<string, object> _properties;
    private IAuthenticationContext? _authenticationContext;
    private readonly object _authLock = new object();

    protected SessionChannelBase()
    {
        _properties = new Dictionary<string, object>();
    }

    #region ITransportConnection Implementation (Abstract)
    public abstract string ConnectionId { get; }
    public abstract ConnectionState State { get; }
    public abstract System.Net.EndPoint RemoteEndPoint { get; }
    public abstract System.Net.EndPoint LocalEndPoint { get; }
    public abstract DateTime ConnectedAt { get; }
    public abstract DateTime LastActivityAt { get; }
    public abstract TransportType TransportType { get; }
    public abstract bool IsConnected { get; }

    public abstract event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;
    public abstract event EventHandler<TransportDataEventArgs>? DataReceived;

    public abstract Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
    public abstract Task CloseAsync(CancellationToken cancellationToken = default);
    public abstract void Dispose();
    #endregion

    #region ISessionChannel Implementation
    public IAuthenticationContext? AuthenticationContext
    {
        get
        {
            lock (_authLock)
            {
                return _authenticationContext;
            }
        }
        set
        {
            IAuthenticationContext? previous;
            lock (_authLock)
            {
                previous = _authenticationContext;
                _authenticationContext = value;
            }

            // 在锁外触发事件，避免死锁
            AuthenticationChanged?.Invoke(this, new AuthenticationChangedEventArgs(
                ConnectionId, previous, value));
        }
    }

    public bool IsAuthenticated => AuthenticationContext?.IsAuthenticated ?? false;

    public IDictionary<string, object> Properties => _properties;

    public string RemoteAddress => RemoteEndPoint?.ToString() ?? "Unknown";

    public void SetAuthentication(IAuthenticationContext authContext)
    {
        AuthenticationContext = authContext;
    }

    public void ClearAuthentication()
    {
        AuthenticationContext = null;
    }

    public T? GetProperty<T>(string key)
    {
        if (_properties.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }

    public void SetProperty<T>(string key, T value)
    {
        if (value != null)
        {
            _properties[key] = value;
        }
        else
        {
            _properties.Remove(key);
        }
    }

    public bool RemoveProperty(string key)
    {
        return _properties.Remove(key);
    }

    public bool HasProperty(string key)
    {
        return _properties.ContainsKey(key);
    }

    public event EventHandler<AuthenticationChangedEventArgs>? AuthenticationChanged;
    #endregion
}