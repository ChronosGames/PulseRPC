using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Network;
using PulseRPC.Protocol.Serialization;

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

        while (!_cts!.Token.IsCancellationRequested && client.Connected)
        {
            try
            {
                // 读取消息
                var (messageId, data) = await TcpProtocol.ReadMessageAsync(stream, _cts.Token);

                // 处理消息
                await _dispatcher.DispatchAsync(messageId, data, session);
            }
            catch (EndOfStreamException)
            {
                // 客户端已断开连接
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理消息时发生错误: {SessionId}", session.Id);
                // 继续处理下一条消息，除非是关键错误
                if (ex is OutOfMemoryException || !client.Connected)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 广播消息给所有客户端
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="message">消息实例</param>
    public async Task BroadcastAsync<T>(T message) where T : class, Protocol.IMessage
    {
        var messageId = MessageRegistry.GetMessageId<T>();
        var data = MessageSerializer.Serialize(message);

        var tasks = new Task[_sessions.Count];
        int i = 0;

        // 并行发送给所有客户端
        foreach (var session in _sessions.Values)
        {
            tasks[i++] = session.SendAsync(messageId, data);
        }

        await Task.WhenAll(tasks);
    }
}
