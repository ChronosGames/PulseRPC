using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PulseRPC.Transport.Tcp
{
    /// <summary>
    /// 传输层消息头结构（用于大包拆解）
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct MessageHeader
    {
        public readonly int Length;
        public readonly ushort MessageId;
        public readonly ushort Flags;  // 扩展为2字节以更好对齐

        public const int Size = 8;

        // 标志位定义
        public const ushort FlagNone = 0x0000;
        public const ushort FlagLargePacket = 0x0001;
        public const ushort FlagCompressed = 0x0002;
        public const ushort FlagChunked = 0x0004;
        public const ushort FlagEndOfChunk = 0x0008;

        public MessageHeader(int length, ushort messageId, ushort flags = FlagNone)
        {
            Length = length;
            MessageId = messageId;
            Flags = flags;
        }

        public bool IsLargePacket => (Flags & FlagLargePacket) != 0;
        public bool IsChunked => (Flags & FlagChunked) != 0;
        public bool IsEndOfChunk => (Flags & FlagEndOfChunk) != 0;
        public bool IsCompressed => (Flags & FlagCompressed) != 0;
    }

    /// <summary>
    /// 块传输头
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct ChunkHeader
    {
        public readonly int ChunkId;        // 块ID
        public readonly int ChunkIndex;     // 块索引
        public readonly int TotalChunks;    // 总块数
        public readonly int ChunkSize;      // 本块大小

        public const int Size = 16;

        public ChunkHeader(int chunkId, int chunkIndex, int totalChunks, int chunkSize)
        {
            ChunkId = chunkId;
            ChunkIndex = chunkIndex;
            TotalChunks = totalChunks;
            ChunkSize = chunkSize;
        }
    }

    /// <summary>
    /// 大包接收状态（线程安全）
    /// </summary>
    public sealed class LargePacketState
    {
        private readonly object _lock = new object();
        private readonly Dictionary<int, byte[]> _chunks;
        private int _receivedChunks;

        public int ChunkId { get; }
        public int TotalChunks { get; }
        public ushort MessageId { get; }
        public DateTime StartTime { get; }

        public LargePacketState(int chunkId, int totalChunks, ushort messageId)
        {
            ChunkId = chunkId;
            TotalChunks = totalChunks;
            MessageId = messageId;
            StartTime = DateTime.UtcNow;
            _chunks = new Dictionary<int, byte[]>(totalChunks);
            _receivedChunks = 0;
        }

        public bool AddChunk(int index, byte[] data)
        {
            lock (_lock)
            {
                if (_chunks.ContainsKey(index))
                    return false;

                _chunks[index] = data;
                _receivedChunks++;
                return _receivedChunks == TotalChunks;
            }
        }

        public byte[]? GetCompleteData()
        {
            lock (_lock)
            {
                if (_receivedChunks != TotalChunks)
                    return null;

                var totalSize = 0;
                for (int i = 0; i < TotalChunks; i++)
                {
                    if (_chunks.TryGetValue(i, out var chunk))
                        totalSize += chunk.Length;
                }

                var result = new byte[totalSize];
                var offset = 0;

                for (int i = 0; i < TotalChunks; i++)
                {
                    if (_chunks.TryGetValue(i, out var chunk))
                    {
                        Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                        offset += chunk.Length;
                    }
                }

                return result;
            }
        }

        public float Progress
        {
            get
            {
                lock (_lock)
                {
                    return (float)_receivedChunks / TotalChunks;
                }
            }
        }
    }

    /// <summary>
    /// TCP传输层实现 - 专注于网络传输和大包处理
    /// </summary>
    public class TcpTransport : ITransport
    {
        protected Socket _socket;
        protected readonly TransportOptions _options;
        protected readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        protected readonly CancellationTokenSource _cts = new CancellationTokenSource();
        protected readonly ILogger _logger;

        // 大包状态管理（线程安全）
        private readonly ConcurrentDictionary<int, LargePacketState> _largePacketStates;
        private int _nextChunkId;

        protected ConnectionState _state = ConnectionState.Disconnected;
        protected NetworkStream? _stream;
        protected Task? _receiveTask;
        protected Task? _cleanupTask;
        protected byte[] _receiveBuffer;
        protected bool _disposed;

        protected long _totalBytesSent;
        protected long _totalBytesReceived;

        public string Name => "TCP";
        public TransportType Type => TransportType.Tcp;
        public bool IsConnected => _state == ConnectionState.Connected && _socket?.Connected == true;
        public ConnectionState State => _state;

        public EndPoint LocalEndPoint => _socket.LocalEndPoint!;
        public EndPoint RemoteEndPoint => _socket.RemoteEndPoint!;

        public long TotalBytesSent => Interlocked.Read(ref _totalBytesSent);
        public long TotalBytesReceived => Interlocked.Read(ref _totalBytesReceived);

        public event System.EventHandler<TransportStateEventArgs>? StateChanged;
        public event System.EventHandler<TransportDataEventArgs>? DataReceived;

        public TcpTransport(TransportOptions? options = null, ILogger? logger = null)
        {
            _options = options ?? new TransportOptions();
            _logger = logger ?? NullLogger.Instance;

            _receiveBuffer = new byte[_options.ReadBufferSize];
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // 设置Socket选项
            _socket.NoDelay = _options.NoDelay;
            _socket.ReceiveBufferSize = _options.ReadBufferSize;
            _socket.SendBufferSize = _options.WriteBufferSize;

            if (_options.KeepAlive)
            {
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                #if WINDOWS
                // 设置保活选项
                var keepAliveValues = new byte[12];
                Buffer.BlockCopy(BitConverter.GetBytes(1), 0, keepAliveValues, 0, 4);  // 启用保活
                Buffer.BlockCopy(BitConverter.GetBytes(_options.KeepAliveInterval), 0, keepAliveValues, 4, 4);  // 保活间隔
                Buffer.BlockCopy(BitConverter.GetBytes(1000), 0, keepAliveValues, 8, 4);  // 保活探测间隔
                _socket.IOControl(IOControlCode.KeepAliveValues, keepAliveValues, null);
                #endif
            }

            _largePacketStates = new ConcurrentDictionary<int, LargePacketState>();
            _nextChunkId = 0;

            // 启动清理任务
            _cleanupTask = CleanupExpiredLargePacketsAsync();
        }

        /// <summary>
        /// 发送原始字节数据（传输层核心方法）
        /// </summary>
        public virtual async Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                return false;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

            // 检查是否需要分块传输
            if (data.Length > _options.SmallPacketThreshold)
            {
                return await SendLargePacketAsync(0, data, linkedCts.Token);
            }

            await _sendLock.WaitAsync(linkedCts.Token);
            try
            {
                if (_stream == null)
                    return false;

                // 创建传输层消息头
                var header = new MessageHeader(data.Length, 0, MessageHeader.FlagNone);
                var headerBytes = new byte[MessageHeader.Size];
                WriteMessageHeader(headerBytes, 0, header);

                // 写入消息头
                await _stream.WriteAsync(headerBytes, linkedCts.Token);

                // 写入数据
                await _stream.WriteAsync(data, linkedCts.Token);
                await _stream.FlushAsync(linkedCts.Token);

                Interlocked.Add(ref _totalBytesSent, MessageHeader.Size + data.Length);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送数据失败");
                return false;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// 发送大包数据（分块传输）
        /// </summary>
        private async Task<bool> SendLargePacketAsync(ushort messageId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            var chunkId = Interlocked.Increment(ref _nextChunkId);
            var totalChunks = (data.Length + _options.ChunkSize - 1) / _options.ChunkSize;

            try
            {
                for (int i = 0; i < totalChunks; i++)
                {
                    var offset = i * _options.ChunkSize;
                    var chunkSize = Math.Min(_options.ChunkSize, data.Length - offset);
                    var chunkData = data.Slice(offset, chunkSize);

                    var flags = MessageHeader.FlagChunked;
                    if (i == totalChunks - 1)
                        flags |= MessageHeader.FlagEndOfChunk;

                    var header = new MessageHeader(
                        ChunkHeader.Size + chunkSize,
                        messageId,
                        flags);

                    var chunkHeader = new ChunkHeader(chunkId, i, totalChunks, chunkSize);

                    await _sendLock.WaitAsync(cancellationToken);
                    try
                    {
                        if (_stream == null)
                            return false;

                        // 写入消息头
                        var headerBytes = new byte[MessageHeader.Size];
                        WriteMessageHeader(headerBytes, 0, header);
                        await _stream.WriteAsync(headerBytes, cancellationToken);

                        // 写入块头
                        var chunkHeaderBytes = new byte[ChunkHeader.Size];
                        WriteChunkHeader(chunkHeaderBytes, 0, chunkHeader);
                        await _stream.WriteAsync(chunkHeaderBytes, cancellationToken);

                        // 写入块数据
                        await _stream.WriteAsync(chunkData, cancellationToken);
                        await _stream.FlushAsync(cancellationToken);

                        Interlocked.Add(ref _totalBytesSent, MessageHeader.Size + ChunkHeader.Size + chunkSize);
                    }
                    finally
                    {
                        _sendLock.Release();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送大包数据失败");
                return false;
            }
        }

        /// <summary>
        /// 接收循环 - 处理传输层消息和分块重组
        /// </summary>
        protected async Task ReceiveLoopAsync()
        {
            try
            {
                byte[] headerBuffer = new byte[MessageHeader.Size];

                while (!_cts.IsCancellationRequested && IsConnected)
                {
                    // 读取传输层消息头
                    if (!await ReadExactBytesAsync(headerBuffer, 0, MessageHeader.Size))
                        break;

                    // 解析传输层消息头
                    var header = ReadMessageHeader(headerBuffer, 0);

                    // 验证长度
                    if (header.Length <= 0 || header.Length > _options.ReadBufferSize * 2)
                    {
                        _logger.LogWarning("收到无效的消息长度: {Length}", header.Length);
                        continue;
                    }

                    // 读取消息内容
                    byte[] messageBuffer = header.Length <= _receiveBuffer.Length
                        ? _receiveBuffer : new byte[header.Length];

                    if (!await ReadExactBytesAsync(messageBuffer, 0, header.Length))
                        break;

                    Interlocked.Add(ref _totalBytesReceived, MessageHeader.Size + header.Length);

                    // 处理分块消息
                    if (header.IsChunked)
                    {
                        ProcessReceivedChunk(header, new ReadOnlyMemory<byte>(messageBuffer, 0, header.Length));
                    }
                    else
                    {
                        // 触发数据接收事件（传输层完整消息）
                        DataReceived?.Invoke(this, new TransportDataEventArgs(new ReadOnlyMemory<byte>(messageBuffer, 0, header.Length)));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex) when (ex is SocketException || ex is IOException)
            {
                // 连接中断
                if (!_cts.IsCancellationRequested)
                {
                    ChangeState(ConnectionState.Disconnected, $"连接断开: {ex.Message}", ex);
                }
            }
            catch (Exception ex)
            {
                // 其他错误
                _logger.LogError(ex, "接收循环异常");
                ChangeState(ConnectionState.Disconnected, $"接收异常: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 处理接收到的分块数据
        /// </summary>
        private void ProcessReceivedChunk(MessageHeader header, ReadOnlyMemory<byte> data)
        {
            try
            {
                if (data.Length < ChunkHeader.Size)
                {
                    _logger.LogWarning("分块数据太小，无法包含块头");
                    return;
                }

                // 读取块头
                var chunkHeader = ReadChunkHeader(data.Span, 0);
                var chunkData = data.Slice(ChunkHeader.Size);

                // 验证块大小
                if (chunkData.Length != chunkHeader.ChunkSize)
                {
                    _logger.LogWarning("分块大小不匹配: 期望 {Expected}, 实际 {Actual}",
                        chunkHeader.ChunkSize, chunkData.Length);
                    return;
                }

                // 获取或创建大包状态
                var state = _largePacketStates.GetOrAdd(chunkHeader.ChunkId,
                    _ => new LargePacketState(chunkHeader.ChunkId, chunkHeader.TotalChunks, header.MessageId));

                // 添加分块
                var chunkDataArray = chunkData.ToArray();
                if (state.AddChunk(chunkHeader.ChunkIndex, chunkDataArray))
                {
                    // 大包接收完成
                    var completeData = state.GetCompleteData();
                    if (completeData != null)
                    {
                        // 移除状态
                        _largePacketStates.TryRemove(chunkHeader.ChunkId, out _);

                        // 触发数据接收事件（完整的大包数据）
                        DataReceived?.Invoke(this, new TransportDataEventArgs(new ReadOnlyMemory<byte>(completeData)));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理分块数据异常");
            }
        }

        /// <summary>
        /// 清理过期的大包状态
        /// </summary>
        private async Task CleanupExpiredLargePacketsAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), _cts.Token);

                    var expiredIds = _largePacketStates
                        .Where(kvp => DateTime.UtcNow - kvp.Value.StartTime > TimeSpan.FromMinutes(5))
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var id in expiredIds)
                    {
                        _largePacketStates.TryRemove(id, out _);
                    }

                    if (expiredIds.Count > 0)
                    {
                        _logger.LogInformation("清理了 {Count} 个过期的大包状态", expiredIds.Count);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "清理过期大包状态异常");
                }
            }
        }

        /// <summary>
        /// 读取指定字节数
        /// </summary>
        protected async Task<bool> ReadExactBytesAsync(byte[] buffer, int offset, int count)
        {
            if (_stream == null)
                return false;

            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await _stream.ReadAsync(buffer, offset + totalRead, count - totalRead, _cts.Token);
                if (read == 0)
                    return false; // 连接关闭

                totalRead += read;
            }

            return true;
        }

        /// <summary>
        /// 写入传输层消息头
        /// </summary>
        private static void WriteMessageHeader(byte[] buffer, int offset, MessageHeader header)
        {
            BitConverter.GetBytes(header.Length).CopyTo(buffer, offset);
            BitConverter.GetBytes(header.MessageId).CopyTo(buffer, offset + 4);
            BitConverter.GetBytes(header.Flags).CopyTo(buffer, offset + 6);
        }

        /// <summary>
        /// 读取传输层消息头
        /// </summary>
        private static MessageHeader ReadMessageHeader(byte[] buffer, int offset)
        {
            var length = BitConverter.ToInt32(buffer, offset);
            var messageId = BitConverter.ToUInt16(buffer, offset + 4);
            var flags = BitConverter.ToUInt16(buffer, offset + 6);
            return new MessageHeader(length, messageId, flags);
        }

        /// <summary>
        /// 写入块头
        /// </summary>
        private static void WriteChunkHeader(byte[] buffer, int offset, ChunkHeader header)
        {
            BitConverter.GetBytes(header.ChunkId).CopyTo(buffer, offset);
            BitConverter.GetBytes(header.ChunkIndex).CopyTo(buffer, offset + 4);
            BitConverter.GetBytes(header.TotalChunks).CopyTo(buffer, offset + 8);
            BitConverter.GetBytes(header.ChunkSize).CopyTo(buffer, offset + 12);
        }

        /// <summary>
        /// 读取块头
        /// </summary>
        private static ChunkHeader ReadChunkHeader(ReadOnlySpan<byte> buffer, int offset)
        {
            var chunkId = BitConverter.ToInt32(buffer.Slice(offset, 4));
            var chunkIndex = BitConverter.ToInt32(buffer.Slice(offset + 4, 4));
            var totalChunks = BitConverter.ToInt32(buffer.Slice(offset + 8, 4));
            var chunkSize = BitConverter.ToInt32(buffer.Slice(offset + 12, 4));
            return new ChunkHeader(chunkId, chunkIndex, totalChunks, chunkSize);
        }

        /// <summary>
        /// 更改连接状态
        /// </summary>
        protected void ChangeState(ConnectionState newState, string? reason = null, Exception? exception = null)
        {
            var oldState = _state;
            if (oldState == newState)
                return;

            _state = newState;

            _logger.LogInformation("传输状态变更: {OldState} -> {NewState} ({Reason})", oldState, newState, reason ?? "未指定原因");

            StateChanged?.Invoke(this, new TransportStateEventArgs(oldState, newState, reason, exception));
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public virtual void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _cts.Cancel();

            try
            {
                _stream?.Dispose();
                _socket?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭资源异常");
            }

            _cts.Dispose();
            _sendLock.Dispose();
        }
    }

    /// <summary>
    /// TCP客户端传输
    /// </summary>
    public class TcpClientTransport : TcpTransport, IClientTransport
    {
        private int _reconnectAttempts = 0;
        private Timer? _reconnectTimer;

        public TcpClientTransport(TransportOptions? options = null, ILogger? logger = null)
            : base(options, logger)
        {
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            if (_state == ConnectionState.Connected || _state == ConnectionState.Connecting)
                return;

            ChangeState(ConnectionState.Connecting);

            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
                linkedCts.CancelAfter(_options.ConnectionTimeout);

                // 连接到服务器
                #if NET5_0_OR_GREATER
                    await _socket.ConnectAsync(host, port, linkedCts.Token);
                #else
                    await _socket.ConnectAsync(host, port);
                #endif

                // 创建网络流
                _stream = new NetworkStream(_socket, true);

                // 更新状态
                ChangeState(ConnectionState.Connected);

                // 重置重连次数
                _reconnectAttempts = 0;

                // 启动接收循环
                _receiveTask = ReceiveLoopAsync();

                _logger.LogInformation("已连接到服务器: {Host}:{Port}", host, port);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "连接到服务器失败: {Host}:{Port}", host, port);

                ChangeState(ConnectionState.Failed, $"连接失败: {ex.Message}", ex);

                // 如果启用了自动重连，则开始重连
                if (_options.AutoReconnect && _reconnectAttempts < _options.MaxReconnectAttempts)
                {
                    StartReconnect(host, port);
                }

                throw;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (_state == ConnectionState.Disconnected || _state == ConnectionState.Disconnecting)
                return Task.CompletedTask;

            ChangeState(ConnectionState.Disconnecting);

            try
            {
                // 取消自动重连
                _reconnectTimer?.Dispose();
                _reconnectTimer = null;

                // 关闭Socket
                if (_socket.Connected)
                {
                    _socket.Shutdown(SocketShutdown.Both);
                    _socket.Close();
                }

                // 更新状态
                ChangeState(ConnectionState.Disconnected);

                _logger.LogInformation("已断开连接");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "断开连接异常");

                // 即使出现异常，也更新状态
                ChangeState(ConnectionState.Disconnected, $"断开异常: {ex.Message}", ex);

                throw;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 启动重连
        /// </summary>
        private void StartReconnect(string host, int port)
        {
            if (!_options.AutoReconnect || _reconnectAttempts >= _options.MaxReconnectAttempts)
            {
                return;
            }

            _reconnectAttempts++;

            ChangeState(ConnectionState.Reconnecting, $"尝试重连({_reconnectAttempts}/{_options.MaxReconnectAttempts})");

            _reconnectTimer?.Dispose();
            _reconnectTimer = new Timer(async _ =>
            {
                try
                {
                    await ConnectAsync(host, port);
                }
                catch
                {
                    // 重连失败，下次继续尝试
                }
            }, null, _options.ReconnectInterval, Timeout.Infinite);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose()
        {
            base.Dispose();
            _reconnectTimer?.Dispose();
        }
    }

    /// <summary>
    /// TCP服务端连接
    /// </summary>
    public class TcpServerConnection : TcpTransport, IServerConnection
    {
        private readonly string _connectionId;

        public string ConnectionId => _connectionId;

        /// <summary>
        /// 使用已连接的Socket创建服务端连接
        /// </summary>
        public TcpServerConnection(string connectionId, Socket socket, TransportOptions? options = null, ILogger? logger = null)
            : base(options, logger)
        {
            _connectionId = connectionId;

            // 替换Socket
            _socket.Dispose();
            _socket = socket;

            // 创建网络流
            _stream = new NetworkStream(socket, true);

            // 设置状态
            _state = ConnectionState.Connected;

            // 启动接收循环
            _receiveTask = ReceiveLoopAsync();

            _logger.LogInformation("接受客户端连接: {ConnectionId} 从 {RemoteEndPoint}",
                _connectionId, socket.RemoteEndPoint);
        }

        /// <summary>
        /// 关闭连接
        /// </summary>
        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            if (_state == ConnectionState.Disconnected || _state == ConnectionState.Disconnecting)
                return Task.CompletedTask;

            ChangeState(ConnectionState.Disconnecting);

            try
            {
                // 关闭Socket
                if (_socket.Connected)
                {
                    _socket.Shutdown(SocketShutdown.Both);
                    _socket.Close();
                }

                // 更新状态
                ChangeState(ConnectionState.Disconnected);

                _logger.LogInformation("已关闭客户端连接: {ConnectionId}", _connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭客户端连接异常: {ConnectionId}", _connectionId);

                // 即使出现异常，也更新状态
                ChangeState(ConnectionState.Disconnected, $"关闭异常: {ex.Message}", ex);

                throw;
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// TCP服务端监听器
    /// </summary>
    public class TcpServerListener : IServerListener
    {
        private readonly TcpListener _listener;
        private readonly TransportOptions _options;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task? _acceptTask;
        private bool _isListening;
        private readonly int _port;

        public string Name => "TCP";
        public TransportType Type => TransportType.Tcp;
        public EndPoint LocalEndPoint => _listener.LocalEndpoint;
        public bool IsListening => _isListening;

        public event System.EventHandler<ServerConnectionEventArgs>? ConnectionAccepted;

        public TcpServerListener(int port, TransportOptions? options = null, ILogger? logger = null)
        {
            _port = port;
            _options = options ?? new TransportOptions();
            _logger = logger ?? NullLogger.Instance;
            _listener = new TcpListener(IPAddress.Any, port);
        }

        /// <summary>
        /// 启动监听
        /// </summary>
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isListening)
                return Task.CompletedTask;

            _isListening = true;

            try
            {
                // 启动监听
                _listener.Start();

                // 启动接受连接任务
                _acceptTask = AcceptConnectionsAsync(_cts.Token);

                _logger.LogInformation("TCP服务器监听已启动，端口: {Port}", _port);

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _isListening = false;
                _logger.LogError(ex, "启动TCP监听失败，端口: {Port}", _port);
                throw;
            }
        }

        /// <summary>
        /// 停止监听
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!_isListening)
                return;

            _isListening = false;

            // 取消接受连接任务
            _cts.Cancel();

            // 停止监听
            _listener.Stop();

            // 等待接受连接任务完成
            try
            {
                if (_acceptTask != null)
                    await _acceptTask;
            }
            catch (OperationCanceledException)
            {
                // 忽略取消异常
            }

            _logger.LogInformation("TCP服务器监听已停止，端口: {Port}", _port);
        }

        /// <summary>
        /// 接受连接循环
        /// </summary>
        private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // 接受新连接
                    #if NET5_0_OR_GREATER
                    var tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken);
                    #else
                    var tcpClient = await _listener.AcceptTcpClientAsync();
                    #endif

                    // 设置TCP选项
                    tcpClient.NoDelay = _options.NoDelay;
                    tcpClient.ReceiveBufferSize = _options.ReadBufferSize;
                    tcpClient.SendBufferSize = _options.WriteBufferSize;

                    // 生成连接ID
                    string connectionId = Guid.NewGuid().ToString("N");

                    // 创建服务端连接
                    var connection = new TcpServerConnection(connectionId, tcpClient.Client, _options, _logger);

                    // 触发连接接受事件
                    ConnectionAccepted?.Invoke(this, new ServerConnectionEventArgs(connection));
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "接受TCP连接异常");
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
            _cts.Dispose();
        }
    }
}
