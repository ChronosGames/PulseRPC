using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Network;
using PulseRPC.Protocol.Serialization;
using System.Buffers;
using System.IO.Pipelines;
using MemoryPack;
using Microsoft.VisualBasic;
using PulseRPC.Protocol;
using PulseRPC.Protocol.Messages;
using Constants = PulseRPC.Protocol.Constants;

namespace PulseRPC.Server;

/// <summary>
/// TCP服务器，用于管理客户端连接和消息处理
/// </summary>
public class TcpServer
{
    private readonly TcpListener _listener;
    private readonly ILogger<TcpServer> _logger;
    private readonly MessageDispatcher _dispatcher;
    private readonly ConcurrentDictionary<Guid, SessionContext> _sessions = new ConcurrentDictionary<Guid, SessionContext>();
    private CancellationTokenSource? _cts;

    /// <summary>
    /// 初始化TCP服务器
    /// </summary>
    /// <param name="ipAddress">监听地址</param>
    /// <param name="port">监听端口</param>
    /// <param name="dispatcher">消息分发器</param>
    /// <param name="logger">日志记录器</param>
    public TcpServer(string ipAddress, int port, MessageDispatcher dispatcher, ILogger<TcpServer> logger)
    {
        var address = string.IsNullOrEmpty(ipAddress) ? IPAddress.Any : IPAddress.Parse(ipAddress);
        _listener = new TcpListener(address, port);
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 启动服务器
    /// </summary>
    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();

        _logger.LogInformation("TCP服务器已启动，监听端口：{Port}", ((IPEndPoint)_listener.LocalEndpoint).Port);

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                // 等待客户端连接
                var client = await _listener.AcceptTcpClientAsync();

                // 处理新的客户端连接
                _ = HandleClientAsync(client).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "处理客户端连接时发生错误");
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
            _logger.LogInformation("服务器正常关闭");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "服务器运行时发生错误");
            throw;
        }
        finally
        {
            _listener.Stop();
        }
    }

    /// <summary>
    /// 停止服务器
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            await _cts.CancelAsync();

            // 关闭所有客户端连接
            foreach (var session in _sessions.Values)
            {
                try
                {
                    session.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "关闭客户端连接时发生错误: {SessionId}", session.Id);
                }
            }

            _sessions.Clear();
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理客户端连接
    /// </summary>
    /// <param name="client">TCP客户端</param>
    private async Task HandleClientAsync(TcpClient client)
    {
        var session = new SessionContext(client);
        _sessions.TryAdd(session.Id, session);

        _logger.LogInformation("客户端已连接: {SessionId}", session.Id);

        try
        {
            await ProcessClientMessagesAsync(client, session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理客户端消息时发生错误: {SessionId}", session.Id);
        }
        finally
        {
            // 移除会话并关闭连接
            if (_sessions.TryRemove(session.Id, out _))
            {
                session.Close();
                _logger.LogInformation("客户端已断开连接: {SessionId}", session.Id);
            }
        }
    }

    /// <summary>
    /// 处理客户端消息
    /// </summary>
    /// <param name="client">TCP客户端</param>
    /// <param name="session">会话上下文</param>
    private async Task ProcessClientMessagesAsync(TcpClient client, SessionContext session)
    {
        var stream = client.GetStream();
        var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(
            pool: MemoryPool<byte>.Shared,
            bufferSize: 4096,
            minimumReadSize: 1024
        ));

        try
        {
            while (!_cts!.Token.IsCancellationRequested && client.Connected)
            {
                ReadResult result;
                try
                {
                    result = await reader.ReadAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var buffer = result.Buffer;

                try
                {
                    while (TryParseMessage(ref buffer, out var message))
                    {
                        if (message != null)
                        {
                            await _dispatcher.DispatchAsync(message, session);
                        }
                    }

                    reader.AdvanceTo(buffer.Start, buffer.End);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _logger.LogError(ex, "处理消息时发生错误: {SessionId}", session.Id);
                    reader.AdvanceTo(buffer.Start, buffer.End);
                    continue;
                }

                if (result.IsCompleted || result.IsCanceled)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "客户端处理循环发生错误: {SessionId}", session.Id);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    /// <summary>
    /// 尝试从缓冲区解析一条消息
    /// </summary>
    private bool TryParseMessage(ref ReadOnlySequence<byte> buffer, out IMessage? message)
    {
        message = null;

        try
        {
            // 检查是否有足够的数据读取消息包
            if (buffer.Length < sizeof(int))
            {
                return false;
            }

            var reader = new SequenceReader<byte>(buffer);

            // 读取消息包长度
            if (!reader.TryReadLittleEndian(out int packetLength))
            {
                return false;
            }

            // 验证消息长度
            if (packetLength <= 0 || packetLength > Constants.MaxMessageSize)
            {
                _logger.LogWarning("收到无效的消息长度: {Length}", packetLength);
                buffer = buffer.Slice(sizeof(int));
                return false;
            }

            // 检查是否有完整的消息包
            if (buffer.Length < sizeof(int) + packetLength)
            {
                return false;
            }

            // 获取消息包数据
            var packetData = buffer.Slice(sizeof(int), packetLength);

            if (packetData.IsSingleSegment)
            {
                // 单段缓冲区，直接反序列化
                var packet = MemoryPackSerializer.Deserialize<MessagePacket>(packetData.First.Span);
                if (packet == null)
                {
                    throw new MessageDeserializationException("无法反序列化消息包");
                }

                // 处理压缩
                var messageData = packet.Payload;
                if ((packet.Header.Flags & MessageFlags.Compressed) != 0)
                {
                    messageData = MessageCompressor.Decompress(packet.Payload);
                }

                // 根据消息ID获取消息类型并反序列化
                var messageType = PulseRPCFormatterProvider.GetMessageType(packet.Header.MessageId);
                message = (IMessage?)MemoryPackSerializer.Deserialize(messageType, messageData);
            }
            else
            {
                // 多段缓冲区，需要复制
                var temp = new byte[packetLength];
                packetData.CopyTo(temp);

                var packet = MemoryPackSerializer.Deserialize<MessagePacket>(temp);
                if (packet == null)
                {
                    throw new MessageDeserializationException("无法反序列化消息包");
                }

                // 处理压缩
                byte[] messageData = packet.Payload;
                if ((packet.Header.Flags & MessageFlags.Compressed) != 0)
                {
                    messageData = MessageCompressor.Decompress(packet.Payload);
                }

                // 根据消息ID获取消息类型并反序列化
                var messageType = PulseRPCFormatterProvider.GetMessageType(packet.Header.MessageId);
                message = (IMessage?)MemoryPackSerializer.Deserialize(messageType, messageData);
            }

            // 更新缓冲区位置
            buffer = buffer.Slice(sizeof(int) + packetLength);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "消息解析失败");
            // 跳过当前消息
            if (buffer.Length >= sizeof(int))
            {
                buffer = buffer.Slice(sizeof(int));
            }
            return false;
        }
    }

    /// <summary>
    /// 广播消息给所有客户端
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="message">消息实例</param>
    public async Task BroadcastAsync<T>(T message) where T : class, IMessage
    {
        var tasks = new Task[_sessions.Count];
        var i = 0;

        // 并行发送给所有客户端
        foreach (var session in _sessions.Values)
        {
            tasks[i++] = session.SendAsync(message);
        }

        await Task.WhenAll(tasks);
    }
}
