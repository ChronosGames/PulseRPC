using System.Collections.Concurrent;

namespace PulseRPC.Client.Core;

/// <summary>
/// 简单连接注册表实现 - Stage 1 基础版本
/// </summary>
public sealed class SimpleConnectionRegistry : IConnectionRegistry
{
    private readonly ConcurrentDictionary<string, IConnection> _connections = new();
    private readonly object _lock = new();

    /// <summary>
    /// 连接注册事件
    /// </summary>
    public event EventHandler<ConnectionRegisteredEventArgs>? ConnectionRegistered;

    /// <summary>
    /// 连接注销事件
    /// </summary>
    public event EventHandler<ConnectionUnregisteredEventArgs>? ConnectionUnregistered;

    /// <summary>
    /// 注册连接
    /// </summary>
    public void RegisterConnection(IConnection connection)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        if (string.IsNullOrEmpty(connection.Id))
            throw new ArgumentException("连接ID不能为空", nameof(connection));

        var added = _connections.TryAdd(connection.Id, connection);
        if (added)
        {
            ConnectionRegistered?.Invoke(this, new ConnectionRegisteredEventArgs { Connection = connection });
        }
    }

    /// <summary>
    /// 注销连接
    /// </summary>
    public void UnregisterConnection(string connectionId, string reason = "手动注销")
    {
        if (string.IsNullOrEmpty(connectionId))
            return;

        if (_connections.TryRemove(connectionId, out _))
        {
            ConnectionUnregistered?.Invoke(this, new ConnectionUnregisteredEventArgs
            {
                ConnectionId = connectionId,
                Reason = reason
            });
        }
    }

    /// <summary>
    /// 根据标签获取连接
    /// </summary>
    public IReadOnlyList<IConnection> GetConnectionsByTags(Dictionary<string, string> tags)
    {
        if (tags == null || tags.Count == 0)
            return Array.Empty<IConnection>();

        var matchingConnections = new List<IConnection>();

        foreach (var connection in _connections.Values)
        {
            if (connection.Descriptor?.Tags != null && MatchesTags(connection.Descriptor.Tags, tags))
            {
                matchingConnections.Add(connection);
            }
        }

        return matchingConnections.AsReadOnly();
    }

    /// <summary>
    /// 获取连接
    /// </summary>
    public IConnection? GetConnection(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return null;

        return _connections.TryGetValue(connectionId, out var connection) ? connection : null;
    }

    /// <summary>
    /// 获取所有连接
    /// </summary>
    public IReadOnlyList<IConnection> GetAllConnections()
    {
        return _connections.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// 根据服务名称获取连接
    /// </summary>
    public IReadOnlyList<IConnection> GetConnectionsByServiceName(string serviceName)
    {
        if (string.IsNullOrEmpty(serviceName))
            return Array.Empty<IConnection>();

        var matchingConnections = _connections.Values
            .Where(c => string.Equals(c.Descriptor?.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matchingConnections.AsReadOnly();
    }

    /// <summary>
    /// 根据连接状态获取连接
    /// </summary>
    public IReadOnlyList<IConnection> GetConnectionsByState(ExtendedConnectionState state)
    {
        var matchingConnections = _connections.Values
            .Where(c => c.State == state)
            .ToList();

        return matchingConnections.AsReadOnly();
    }

    /// <summary>
    /// 获取健康的连接
    /// </summary>
    public IReadOnlyList<IConnection> GetHealthyConnections()
    {
        var healthyConnections = _connections.Values
            .Where(c => c.State == ExtendedConnectionState.Connected ||
                       c.State == ExtendedConnectionState.Active)
            .ToList();

        return healthyConnections.AsReadOnly();
    }

    /// <summary>
    /// 清理所有连接
    /// </summary>
    public void Clear()
    {
        var connectionIds = _connections.Keys.ToList();
        _connections.Clear();

        // 触发注销事件
        foreach (var connectionId in connectionIds)
        {
            ConnectionUnregistered?.Invoke(this, new ConnectionUnregisteredEventArgs
            {
                ConnectionId = connectionId,
                Reason = "注册表清理"
            });
        }
    }

    /// <summary>
    /// 获取连接数量
    /// </summary>
    public int Count => _connections.Count;

    /// <summary>
    /// 检查标签是否匹配
    /// </summary>
    private static bool MatchesTags(Dictionary<string, string> connectionTags, Dictionary<string, string> queryTags)
    {
        foreach (var queryTag in queryTags)
        {
            if (!connectionTags.TryGetValue(queryTag.Key, out var connectionTagValue) ||
                !string.Equals(connectionTagValue, queryTag.Value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }
}