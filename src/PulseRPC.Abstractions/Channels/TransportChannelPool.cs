using System.Collections.Concurrent;

namespace PulseRPC.Channels;

/// <summary>
/// 传输通道连接池默认实现
/// </summary>
/// <remarks>
/// 实现要点：
/// - 使用 ConcurrentDictionary 保证线程安全
/// - 采用快照模式返回集合，避免并发修改异常
/// - 提供快速的 O(1) 查询性能
/// </remarks>
public sealed class TransportChannelPool : ITransportChannelPool
{
    private readonly ConcurrentDictionary<string, ITransportChannel> _channels = new();

    /// <inheritdoc />
    public void Register(string connectionId, ITransportChannel channel)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("ConnectionId cannot be null or empty", nameof(connectionId));

        if (channel == null)
            throw new ArgumentNullException(nameof(channel));

        if (!_channels.TryAdd(connectionId, channel))
        {
            throw new ArgumentException($"Connection '{connectionId}' already exists in the pool", nameof(connectionId));
        }
    }

    /// <inheritdoc />
    public bool Unregister(string connectionId)
    {
        return _channels.TryRemove(connectionId, out _);
    }

    /// <inheritdoc />
    public ITransportChannel? GetChannel(string connectionId)
    {
        return _channels.TryGetValue(connectionId, out var channel) ? channel : null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ITransportChannel> GetAllChannels()
    {
        // 创建快照，避免并发修改异常
        return _channels.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetAllConnectionIds()
    {
        // 创建快照，避免并发修改异常
        return _channels.Keys.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public bool Contains(string connectionId)
    {
        return _channels.ContainsKey(connectionId);
    }

    /// <inheritdoc />
    public int Count => _channels.Count;

    /// <inheritdoc />
    public void Clear()
    {
        _channels.Clear();
    }
}
