using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Transport;

namespace PulseRPC.Server.Channels;

/// <summary>
/// 基于传输层的服务器通道
/// </summary>
public class ServerTransportChannel : IServerChannel
{
    private readonly string _name;
    private readonly IServerListener _listener;
    private readonly ISerializer _serializer;
    private readonly ConcurrentDictionary<string, IServerConnection> _connections = new();
    private readonly ILogger<ServerTransportChannel> _logger;

    public string Name => _name;

    public event System.EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event System.EventHandler<ClientConnectedEventArgs>? ClientConnected;
    public event System.EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

    public ServerTransportChannel(string name, IServerListener listener, ISerializer serializer, ILogger<ServerTransportChannel>? logger = null)
    {
        _name = name;
        _listener = listener;
        _serializer = serializer;
        _logger = logger ?? NullLogger<ServerTransportChannel>.Instance;

        // 注册监听器事件
        _listener.ConnectionAccepted += OnConnectionAccepted;
    }

    /// <summary>
    /// 发送消息到客户端
    /// </summary>
    public async Task SendMessageAsync(string clientId, MessageHeader header, object body)
    {
        if (!_connections.TryGetValue(clientId, out var connection))
        {
            throw new InvalidOperationException($"客户端 {clientId} 不存在或已断开连接");
        }

        try
        {
            // 序列化头部
            byte[] headerBytes = _serializer.Serialize(header);

            // 序列化消息体
            byte[] bodyBytes = body != null ? _serializer.Serialize(body) : Array.Empty<byte>();

            // 创建完整消息
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // 写入头部长度
            writer.Write(headerBytes.Length);
            // 写入头部
            writer.Write(headerBytes);
            // 写入消息体
            writer.Write(bodyBytes);

            // 发送消息
            byte[] messageData = ms.ToArray();
            await connection.SendAsync(messageData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "向客户端 {ClientId} 发送消息失败", clientId);

            // 如果发送失败，可能是连接已断开
            if (ex is IOException || ex is SocketException)
            {
                await CloseClientConnectionAsync(clientId, "发送失败");
            }

            throw;
        }
    }

    /// <summary>
    /// 广播事件到所有客户端
    /// </summary>
    public async Task BroadcastEventAsync<T>(string eventName, T eventData)
    {
        // 创建消息头
        var header = new MessageHeader
        {
            Type = MessageType.Event,
            MessageId = Guid.NewGuid(),
            MethodName = eventName,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // 序列化头部
        byte[] headerBytes = _serializer.Serialize(header);

        // 序列化消息体
        byte[] bodyBytes = _serializer.Serialize(eventData);

        // 创建完整消息
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // 写入头部长度
        writer.Write(headerBytes.Length);
        // 写入头部
        writer.Write(headerBytes);
        // 写入消息体
        writer.Write(bodyBytes);

        // 获取完整消息数据
        byte[] messageData = ms.ToArray();

        // 获取所有客户端连接
        var connections = _connections.Values.ToArray();

        // 创建发送任务列表
        var tasks = new List<Task>();

        // 广播消息
        foreach (var connection in connections)
        {
            try
            {
                tasks.Add(connection.SendAsync(messageData));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "向客户端 {ClientId} 广播事件失败", connection.ConnectionId);
            }
        }

        // 等待所有发送完成
        await Task.WhenAll(tasks);

        _logger.LogDebug("已广播事件 {EventName} 到 {ClientCount} 个客户端", eventName, connections.Length);
    }

    /// <summary>
    /// 广播事件到指定客户端
    /// </summary>
    public async Task BroadcastEventAsync<T>(string eventName, T eventData, IEnumerable<string> clientIds)
    {
        // 创建消息头
        var header = new MessageHeader
        {
            Type = MessageType.Event,
            MessageId = Guid.NewGuid(),
            MethodName = eventName,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // 序列化头部
        byte[] headerBytes = _serializer.Serialize(header);

        // 序列化消息体
        byte[] bodyBytes = _serializer.Serialize(eventData);

        // 创建完整消息
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // 写入头部长度
        writer.Write(headerBytes.Length);
        // 写入头部
        writer.Write(headerBytes);
        // 写入消息体
        writer.Write(bodyBytes);

        // 获取完整消息数据
        byte[] messageData = ms.ToArray();

        // 创建发送任务列表
        var tasks = new List<Task>();

        // 广播消息到指定客户端
        foreach (var clientId in clientIds)
        {
            if (_connections.TryGetValue(clientId, out var connection))
            {
                try
                {
                    tasks.Add(connection.SendAsync(messageData));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "向客户端 {ClientId} 广播事件失败", clientId);
                }
            }
        }

        // 等待所有发送完成
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 处理客户端连接接受
    /// </summary>
    private void OnConnectionAccepted(object? sender, ServerConnectionEventArgs e)
    {
        var connection = e.Connection;

        // 注册连接事件
        connection.DataReceived += OnConnectionDataReceived;
        connection.StateChanged += OnConnectionStateChanged;

        // 添加到连接集合
        _connections[connection.ConnectionId] = connection;

        // 触发客户端连接事件
        ClientConnected?.Invoke(this, new ClientConnectedEventArgs(
            connection.ConnectionId,
            connection.RemoteEndPoint.ToString()!));

        _logger.LogInformation("客户端 {ClientId} 已连接到通道 {ChannelName}",
            connection.ConnectionId, _name);
    }

    /// <summary>
    /// 处理连接数据接收
    /// </summary>
    private void OnConnectionDataReceived(object? sender, TransportDataEventArgs e)
    {
        var connection = (IServerConnection)sender!;

        try
        {
            using var ms = new MemoryStream(e.Data.ToArray());
            using var reader = new BinaryReader(ms);

            // 读取头部长度
            int headerLength = reader.ReadInt32();

            // 验证头部长度
            if (headerLength <= 0 || headerLength > e.Data.Length - 4)
            {
                _logger.LogWarning("收到来自客户端 {ClientId} 的无效消息头", connection.ConnectionId);
                return;
            }

            // 读取头部
            byte[] headerBytes = reader.ReadBytes(headerLength);
            var header = _serializer.Deserialize<MessageHeader>(headerBytes);

            // 读取消息体
            byte[] bodyBytes = reader.ReadBytes((int)(ms.Length - ms.Position));

            // 创建网络消息
            var message = new NetworkMessage
            {
                Header = header,
                Body = bodyBytes
            };

            // 触发消息接收事件
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(connection.ConnectionId, message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理来自客户端 {ClientId} 的消息失败", connection.ConnectionId);
        }
    }

    /// <summary>
    /// 处理连接状态变化
    /// </summary>
    private void OnConnectionStateChanged(object? sender, TransportStateEventArgs e)
    {
        var connection = (IServerConnection)sender!;

        // 如果连接断开，清理资源
        if (e.CurrentState == ConnectionState.Disconnected)
        {
            // 移除连接
            if (_connections.TryRemove(connection.ConnectionId, out _))
            {
                // 取消事件订阅
                connection.DataReceived -= OnConnectionDataReceived;
                connection.StateChanged -= OnConnectionStateChanged;

                // 触发客户端断开连接事件
                ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(
                    connection.ConnectionId,
                    e.Reason ?? "连接断开"));

                _logger.LogInformation("客户端 {ClientId} 已断开与通道 {ChannelName} 的连接",
                    connection.ConnectionId, _name);
            }
        }
    }

    /// <summary>
    /// 关闭客户端连接
    /// </summary>
    private async Task CloseClientConnectionAsync(string clientId, string reason)
    {
        if (_connections.TryGetValue(clientId, out var connection))
        {
            try
            {
                await connection.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭客户端 {ClientId} 连接失败", clientId);
            }
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        // 取消监听器事件订阅
        _listener.ConnectionAccepted -= OnConnectionAccepted;

        // 关闭所有连接
        foreach (var connection in _connections.Values)
        {
            try
            {
                // 取消事件订阅
                connection.DataReceived -= OnConnectionDataReceived;
                connection.StateChanged -= OnConnectionStateChanged;

                // 关闭连接
                connection.Dispose();
            }
            catch
            {
                // 忽略关闭异常
            }
        }

        _connections.Clear();
    }
}
