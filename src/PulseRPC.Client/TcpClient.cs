using System.Collections.Concurrent;
using System.Net.Sockets;
using System.IO.Pipelines;
using System.Buffers;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol;
using PulseRPC.Protocol.Network;
using PulseRPC.Protocol.Serialization;
using PulseRPC.Protocol.Messages;
using PulseRPC.Client.Extensions;

namespace PulseRPC.Client;

/// <summary>
/// TCP客户端连接状态
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}

/// <summary>
/// 性能指标统计
/// </summary>
public class PerformanceMetrics
{
    private long _bytesReceived;
    private long _bytesSent;
    private long _messagesReceived;
    private long _messagesSent;
    private long _reconnectAttempts;
    private long _failedConnections;
    private readonly ConcurrentQueue<(DateTime Timestamp, long Size)> _messageHistory = new();
    private readonly int _messageHistoryLimit = 1000;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    public long BytesReceived => Interlocked.Read(ref _bytesReceived);
    public long BytesSent => Interlocked.Read(ref _bytesSent);
    public long MessagesReceived => Interlocked.Read(ref _messagesReceived);
    public long MessagesSent => Interlocked.Read(ref _messagesSent);
    public long ReconnectAttempts => Interlocked.Read(ref _reconnectAttempts);
    public long FailedConnections => Interlocked.Read(ref _failedConnections);
    public TimeSpan Uptime => _uptime.Elapsed;

    public void IncrementBytesReceived(long bytes) => Interlocked.Add(ref _bytesReceived, bytes);
    public void IncrementBytesSent(long bytes) => Interlocked.Add(ref _bytesSent, bytes);
    public void IncrementMessagesReceived() => Interlocked.Increment(ref _messagesReceived);
    public void IncrementMessagesSent() => Interlocked.Increment(ref _messagesSent);
    public void IncrementReconnectAttempts() => Interlocked.Increment(ref _reconnectAttempts);
    public void IncrementFailedConnections() => Interlocked.Increment(ref _failedConnections);

    public void AddMessageSize(long size)
    {
        _messageHistory.Enqueue((DateTime.UtcNow, size));
        while (_messageHistory.Count > _messageHistoryLimit)
        {
            _messageHistory.TryDequeue(out _);
        }
    }

    public (double AverageSize, long MaxSize, long MinSize, double P95Size) GetMessageStats(TimeSpan window)
    {
        var messages = _messageHistory.ToArray();
        var cutoff = DateTime.UtcNow - window;
        var recentMessages = messages.Where(m => m.Timestamp >= cutoff).Select(m => m.Size).ToList();

        if (recentMessages.Count == 0)
            return (0, 0, 0, 0);

        return (
            AverageSize: recentMessages.Average(),
            MaxSize: recentMessages.Max(),
            MinSize: recentMessages.Min(),
            P95Size: recentMessages.OrderBy(s => s).ElementAt((int)(recentMessages.Count * 0.95))
        );
    }

    public double GetMessageRate(TimeSpan window)
    {
        var messages = _messageHistory.ToArray();
        var cutoff = DateTime.UtcNow - window;
        var count = messages.Count(m => m.Timestamp >= cutoff);
        return count / window.TotalSeconds;
    }
}

/// <summary>
/// TCP客户端，用于与服务器通信
/// </summary>
public class TcpClient : IDisposable
{
    private readonly ILogger<TcpClient> _logger;
    private readonly string _host;
    private readonly int _port;
    private System.Net.Sockets.TcpClient? _client;
    private NetworkStream? _stream;
    private PipeReader? _reader;
    private PipeWriter? _writer;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _reconnectTask;
    private volatile ConnectionState _connectionState;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly AutoResetEvent _reconnectEvent = new(false);
    private int _reconnectAttempts;
    private readonly int _maxReconnectAttempts;
    private readonly TimeSpan _initialReconnectDelay;
    private readonly TimeSpan _maxReconnectDelay;
    private DateTime _lastConnectAttempt;
    private bool _autoReconnect;
    private readonly int _maxMessageSize = 1024 * 1024 * 16; // 16MB

    // 消息处理回调
    private readonly ConcurrentDictionary<Type, Delegate> _messageHandlers = new ConcurrentDictionary<Type, Delegate>();

    // 请求-响应映射
    private readonly ConcurrentDictionary<int, TaskCompletionSource<object>> _pendingRequests = new ConcurrentDictionary<int, TaskCompletionSource<object>>();
    private int _requestId = 0;

    private readonly PerformanceMetrics _metrics = new();
    private readonly Timer _metricsTimer;

    private const int MinimumBufferSize = 4096;    // 4KB
    private const int OptimalReadSize = 8192;      // 8KB
    private const int MaxBufferSize = 65536;       // 64KB

    private readonly HeartbeatOptions? _heartbeatOptions;
    private HeartbeatManager? _heartbeatManager;

    /// <summary>
    /// 获取性能指标
    /// </summary>
    public PerformanceMetrics Metrics => _metrics;

    /// <summary>
    /// 获取内存使用统计
    /// </summary>
    public (long TotalBytesReceived, long TotalBytesSent, long TotalMessagesReceived, long TotalMessagesSent) Statistics =>
    (
        _metrics.BytesReceived,
        _metrics.BytesSent,
        _metrics.MessagesReceived,
        _metrics.MessagesSent
    );

    /// <summary>
    /// 获取当前连接状态
    /// </summary>
    public ConnectionState State => _connectionState;

    /// <summary>
    /// 获取或设置是否启用自动重连
    /// </summary>
    public bool AutoReconnect
    {
        get => _autoReconnect;
        set
        {
            _autoReconnect = value;
            if (value && _connectionState == ConnectionState.Disconnected)
            {
                _ = TryReconnectAsync();
            }
        }
    }

    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    public event EventHandler<ConnectionState>? ConnectionStateChanged;

    /// <summary>
    /// 重连尝试事件
    /// </summary>
    public event EventHandler<(int Attempt, Exception? Error)>? ReconnectAttempted;

    /// <summary>
    /// 事件：连接建立
    /// </summary>
    public event EventHandler? Connected;

    /// <summary>
    /// 事件：连接断开
    /// </summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// 初始化TCP客户端
    /// </summary>
    /// <param name="host">服务器地址</param>
    /// <param name="port">服务器端口</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="options">TCP客户端配置选项</param>
    public TcpClient(string host, int port, ILogger<TcpClient> logger, TcpClientOptions? options = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        options ??= new TcpClientOptions();
        _maxReconnectAttempts = options.MaxReconnectAttempts;
        _initialReconnectDelay = options.InitialReconnectDelay;
        _maxReconnectDelay = options.MaxReconnectDelay;
        _autoReconnect = options.AutoReconnect;
        _heartbeatOptions = options.EnableHeartbeat ? new HeartbeatOptions
        {
            Interval = options.HeartbeatInterval,
            Timeout = options.HeartbeatTimeout,
            MaxTimeoutCount = options.MaxHeartbeatTimeoutCount
        } : null;

        // 注册心跳消息处理程序
        if (_heartbeatOptions != null)
        {
            RegisterHandler<HeartbeatMessage>(HandleHeartbeat);
            RegisterHandler<HeartbeatResponse>(HandleHeartbeatResponse);
        }

        // 创建性能指标监控定时器（每分钟记录一次）
        _metricsTimer = new Timer(LogMetrics, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

        if (_autoReconnect)
        {
            _reconnectTask = StartReconnectLoopAsync();
        }
    }

    /// <summary>
    /// 处理心跳消息
    /// </summary>
    private async void HandleHeartbeat(HeartbeatMessage message)
    {
        try
        {
            var response = MessagePool.Get<HeartbeatResponse>();
            response.OriginalTimestamp = message.Timestamp;
            response.ResponseTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            response.Sequence = message.Sequence;

            await SendAsync(response, _cts!.Token);
            MessagePool.Return(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理心跳消息时发生错误");
        }
    }

    /// <summary>
    /// 处理心跳响应消息
    /// </summary>
    private void HandleHeartbeatResponse(HeartbeatResponse response)
    {
        _heartbeatManager?.HandleHeartbeatResponse(response);
    }

    /// <summary>
    /// 连接到服务器
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connectionState == ConnectionState.Connected)
                return;

            UpdateConnectionState(ConnectionState.Connecting);
            _lastConnectAttempt = DateTime.UtcNow;

            _client = new System.Net.Sockets.TcpClient();

            #if NET5_0_OR_GREATER
            await _client.ConnectAsync(_host, _port, cancellationToken);
            #else
            await _client.ConnectAsync(_host, _port);
            #endif

            _stream = _client.GetStream();

            // 配置读取器选项
            var readerOptions = new StreamPipeReaderOptions(
                bufferSize: MinimumBufferSize,
                minimumReadSize: OptimalReadSize,
                pool: MemoryPool<byte>.Shared,
                leaveOpen: true
            );

            // 配置写入器选项
            var writerOptions = new StreamPipeWriterOptions(
                pool: MemoryPool<byte>.Shared,
                minimumBufferSize: MinimumBufferSize,
                leaveOpen: true
            );

            _reader = PipeReader.Create(_stream, readerOptions);
            _writer = PipeWriter.Create(_stream, writerOptions);
            _cts = new CancellationTokenSource();

            // 配置TCP客户端选项
            _client.NoDelay = true;
            _client.ReceiveBufferSize = MaxBufferSize;
            _client.SendBufferSize = MaxBufferSize;

            _receiveTask = ReceiveLoopAsync(_cts.Token);

            UpdateConnectionState(ConnectionState.Connected);
            _reconnectAttempts = 0;

            // 创建心跳管理器
            if (_heartbeatOptions != null)
            {
                _heartbeatManager?.Dispose();
                _heartbeatManager = new HeartbeatManager(
                    new SessionContext(_client),
                    _logger,
                    _heartbeatOptions);

                // 监听连接断开事件
                _heartbeatManager.ConnectionLost += (sender, args) =>
                {
                    _logger.LogWarning("心跳检测失败，连接已断开");
                    UpdateConnectionState(ConnectionState.Disconnected);
                };
            }

            _logger.LogInformation(
                "已连接到服务器 {Host}:{Port}, 缓冲区配置: 读取={ReadBuffer}KB, 写入={WriteBuffer}KB",
                _host, _port,
                _client.ReceiveBufferSize / 1024,
                _client.SendBufferSize / 1024
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接服务器失败");
            await CleanupAsync();
            UpdateConnectionState(ConnectionState.Failed);
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task StartReconnectLoopAsync()
    {
        while (!_cts?.Token.IsCancellationRequested ?? true)
        {
            try
            {
                await _reconnectEvent.WaitOneAsync(_cts?.Token ?? CancellationToken.None);

                if (_cts?.Token.IsCancellationRequested ?? true)
                {
                    break;
                }

                await TryReconnectAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重连循环中发生错误");
                await Task.Delay(1000, _cts?.Token ?? CancellationToken.None);
            }
        }
    }

    private async Task TryReconnectAsync()
    {
        if (_connectionState == ConnectionState.Connected ||
            _connectionState == ConnectionState.Connecting ||
            !_autoReconnect)
            return;

        await _connectionLock.WaitAsync();
        try
        {
            while (_connectionState != ConnectionState.Connected &&
                   _reconnectAttempts < _maxReconnectAttempts)
            {
                _reconnectAttempts++;
                _metrics.IncrementReconnectAttempts();
                UpdateConnectionState(ConnectionState.Reconnecting);

                try
                {
                    var delay = CalculateReconnectDelay(_reconnectAttempts);
                    var timeSinceLastAttempt = DateTime.UtcNow - _lastConnectAttempt;
                    if (timeSinceLastAttempt < delay)
                    {
                        await Task.Delay(delay - timeSinceLastAttempt);
                    }

                    await ConnectAsync(_cts!.Token);
                    return;
                }
                catch (Exception ex)
                {
                    _metrics.IncrementFailedConnections();
                    _logger.LogWarning(ex, "重连尝试 {Attempt}/{MaxAttempts} 失败",
                        _reconnectAttempts, _maxReconnectAttempts);

                    ReconnectAttempted?.Invoke(this, (_reconnectAttempts, ex));

                    if (_reconnectAttempts >= _maxReconnectAttempts)
                    {
                        UpdateConnectionState(ConnectionState.Failed);
                        _logger.LogError("达到最大重连次数，停止重连");
                        return;
                    }
                }
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private TimeSpan CalculateReconnectDelay(int attempt)
    {
        // 指数退避策略：delay = min(initialDelay * 2^(attempt-1), maxDelay)
        var delay = _initialReconnectDelay * Math.Pow(2, attempt - 1);
        return TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, _maxReconnectDelay.TotalMilliseconds));
    }

    private void UpdateConnectionState(ConnectionState newState)
    {
        var oldState = _connectionState;
        _connectionState = newState;

        if (oldState != newState)
        {
            _logger.LogInformation("连接状态从 {OldState} 变更为 {NewState}", oldState, newState);
            ConnectionStateChanged?.Invoke(this, newState);

            if (newState == ConnectionState.Disconnected && _autoReconnect)
            {
                _reconnectEvent.Set();
            }
        }
    }

    private async Task CleanupAsync()
    {
        try
        {
            if (_reader != null)
                await _reader.CompleteAsync();
            if (_writer != null)
                await _writer.CompleteAsync();

            _stream?.Dispose();
            _client?.Dispose();

            _reader = null;
            _writer = null;
            _stream = null;
            _client = null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理连接资源时发生错误");
        }
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="message">消息实例</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SendAsync<T>(T message, CancellationToken cancellationToken = default) where T : class, IMessage
    {
        if (!IsConnected || _writer == null)
        {
            throw new InvalidOperationException("客户端未连接");
        }

        try
        {
            // 序列化消息
            var messageBytes = MessageSerializer.Serialize(message);

            // 更新性能指标
            _metrics.IncrementBytesSent(messageBytes.Length);
            _metrics.IncrementMessagesSent();
            _metrics.AddMessageSize(messageBytes.Length);

            // 获取足够大的缓冲区
            var buffer = _writer.GetMemory(messageBytes.Length);

            // 写入消息数据
            messageBytes.CopyTo(buffer);

            // 推进写入指针
            _writer.Advance(messageBytes.Length);

            // 立即刷新数据
            var result = await _writer.FlushAsync(cancellationToken);
            if (result.IsCanceled || result.IsCompleted)
            {
                throw new OperationCanceledException("发送操作被取消或连接已关闭");
            }

            _logger.LogDebug("已发送消息 {MessageType}, 大小: {Size}字节", typeof(T).Name, messageBytes.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送消息时发生错误");
            throw;
        }
    }

    /// <summary>
    /// 发送请求并等待响应
    /// </summary>
    /// <typeparam name="TRequest">请求类型</typeparam>
    /// <typeparam name="TResponse">响应类型</typeparam>
    /// <param name="request">请求消息</param>
    /// <param name="timeout">超时时间(毫秒)</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应消息</returns>
    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(
        TRequest request,
        int timeout = 30000,
        CancellationToken cancellationToken = default)
        where TRequest : class, IMessage
        where TResponse : class, IMessage
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("客户端未连接");
        }

        // 创建请求ID
        var requestId = Interlocked.Increment(ref _requestId);

        // 创建完成源
        var tcs = new TaskCompletionSource<object>();
        _pendingRequests[requestId] = tcs;

        try
        {
            // 发送请求
            await SendAsync(request, cancellationToken);

            // 等待响应或超时
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var completedTask = await Task.WhenAny(
                tcs.Task,
                Task.Delay(timeout, linkedCts.Token)
            );

            if (completedTask == tcs.Task)
            {
                // 收到响应
                var result = await tcs.Task;
                return (TResponse)result;
            }
            else
            {
                // 超时
                throw new TimeoutException($"等待响应超时: {timeout}ms");
            }
        }
        finally
        {
            // 移除挂起的请求
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    /// <summary>
    /// 注册消息处理程序
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="handler">处理程序</param>
    public void RegisterHandler<T>(Action<T> handler) where T : IMessage
    {
        _messageHandlers[typeof(T)] = handler;
    }

    /// <summary>
    /// 消息接收循环
    /// </summary>
    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _connectionState == ConnectionState.Connected)
            {
                var result = await _reader!.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                try
                {
                    while (TryParseMessage(ref buffer, out var messageId, out var messageData))
                    {
                        _ = ProcessMessageAsync(messageId, messageData);
                    }

                    if (result.IsCanceled || result.IsCompleted)
                    {
                        break;
                    }
                }
                finally
                {
                    // 只推进到已处理的数据位置，保留未完成的消息
                    _reader.AdvanceTo(buffer.Start, buffer.End);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常取消
            _logger.LogInformation("接收循环已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "接收消息循环中发生错误");
            UpdateConnectionState(ConnectionState.Disconnected);
        }
    }

    /// <summary>
    /// 尝试从缓冲区解析一条消息
    /// </summary>
    private bool TryParseMessage(ref ReadOnlySequence<byte> buffer, out int messageId, out byte[] messageData)
    {
        messageId = 0;
        messageData = Array.Empty<byte>();

        // 消息头长度为8字节：4字节消息长度 + 4字节消息ID
        const int HeaderLength = 8;

        // 检查是否有足够的数据读取消息头
        if (buffer.Length < HeaderLength)
            return false;

        var reader = new SequenceReader<byte>(buffer);

        // 读取消息长度（不包括长度字段本身的4字节）
        if (!reader.TryReadLittleEndian(out int messageLength))
            return false;

        // 验证消息长度是否合理
        if (messageLength <= 0 || messageLength > _maxMessageSize)
        {
            _logger.LogWarning("收到无效的消息长度: {Length}", messageLength);
            buffer = buffer.Slice(4);
            return false;
        }

        // 检查是否有足够的数据读取整个消息
        if (buffer.Length < 4 + messageLength)
            return false;

        // 读取消息ID
        // 重置reader位置到第4个字节，即消息ID的起始位置
        reader = new SequenceReader<byte>(buffer.Slice(4, 4));
        if (!reader.TryReadLittleEndian(out messageId))
            return false;

        // 读取消息体（从第8个字节开始）
        var bodyLength = messageLength - 4; // 减去消息ID的4字节
        var messageBuffer = buffer.Slice(HeaderLength, bodyLength);
        messageData = messageBuffer.ToArray();

        // 移动buffer到消息结束位置
        buffer = buffer.Slice(4 + messageLength);

        // 更新性能指标
        _metrics.IncrementBytesReceived(messageLength);
        _metrics.IncrementMessagesReceived();
        _metrics.AddMessageSize(messageLength);

        return true;
    }

    /// <summary>
    /// 处理接收到的消息
    /// </summary>
    /// <param name="messageId">消息ID</param>
    /// <param name="data">消息数据</param>
    private async Task ProcessMessageAsync(int messageId, byte[] data)
    {
        try
        {
            // 获取消息类型
            var messageType = MessageRegistry.GetMessageType(messageId);
            if (messageType == null)
            {
                _logger.LogWarning("收到未知消息ID: {MessageId}", messageId);
                return;
            }

            // 反序列化消息
            var deserializeMethod = typeof(MessageSerializer)
                .GetMethod(nameof(MessageSerializer.Deserialize))!
                .MakeGenericMethod(messageType);
            var message = deserializeMethod.Invoke(null, new object[] { data });

            // 检查是否有对应的处理程序
            if (_messageHandlers.TryGetValue(messageType, out var handlerDelegate))
            {
                // 调用处理程序
                await Task.Run(() => handlerDelegate.DynamicInvoke(message));
            }

            // 检查是否有等待这个响应的请求
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetResult(message!);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理消息时发生错误");
        }
    }

    private void LogMetrics(object? state)
    {
        if (!IsConnected) return;

        var stats = _metrics.GetMessageStats(TimeSpan.FromMinutes(5));
        var messageRate = _metrics.GetMessageRate(TimeSpan.FromMinutes(1));

        _logger.LogInformation(
            "性能指标 - 已接收: {ReceivedMB:F2}MB, 已发送: {SentMB:F2}MB, " +
            "消息数: 接收{MsgRecv}, 发送{MsgSent}, " +
            "重连: {Reconnects}, 失败: {Failures}, " +
            "消息率: {MsgRate:F1}/秒, " +
            "平均大小: {AvgSize:F1}KB, P95: {P95Size:F1}KB",
            _metrics.BytesReceived / 1024.0 / 1024.0,
            _metrics.BytesSent / 1024.0 / 1024.0,
            _metrics.MessagesReceived,
            _metrics.MessagesSent,
            _metrics.ReconnectAttempts,
            _metrics.FailedConnections,
            messageRate,
            stats.AverageSize / 1024.0,
            stats.P95Size / 1024.0
        );

        // 记录内存使用情况
        var process = Process.GetCurrentProcess();
        var workingSet = process.WorkingSet64;
        var privateMemory = process.PrivateMemorySize64;
        var managedMemory = GC.GetTotalMemory(false);

        _logger.LogInformation(
            "内存使用 - 工作集: {WorkingSet}MB, 私有内存: {PrivateMemory}MB, 托管内存: {ManagedMemory}MB",
            workingSet / 1024 / 1024,
            privateMemory / 1024 / 1024,
            managedMemory / 1024 / 1024
        );
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _metricsTimer?.Dispose();
        _autoReconnect = false;
        _cts?.Cancel();
        _reconnectEvent.Set();
        _reconnectTask?.Wait(TimeSpan.FromSeconds(5));

        _heartbeatManager?.Dispose();
        _connectionLock.Dispose();
        _reconnectEvent.Dispose();
        CleanupAsync().Wait();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 连接状态
    /// </summary>
    public bool IsConnected => _client?.Connected ?? false;
}

/// <summary>
/// TCP客户端配置选项
/// </summary>
public class TcpClientOptions
{
    /// <summary>
    /// 最大重连尝试次数
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 10;

    /// <summary>
    /// 初始重连延迟
    /// </summary>
    public TimeSpan InitialReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 最大重连延迟
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 是否启用自动重连
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// 是否启用心跳检测
    /// </summary>
    public bool EnableHeartbeat { get; set; } = true;

    /// <summary>
    /// 心跳间隔（毫秒）
    /// </summary>
    public int HeartbeatInterval { get; set; } = 30000;

    /// <summary>
    /// 心跳超时时间（毫秒）
    /// </summary>
    public int HeartbeatTimeout { get; set; } = 5000;

    /// <summary>
    /// 最大心跳超时次数
    /// </summary>
    public int MaxHeartbeatTimeoutCount { get; set; } = 3;
}
