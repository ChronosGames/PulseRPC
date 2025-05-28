using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PulseRPC.Transport.Tcp
{
    /// <summary>
    /// TCP传输基类
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
        }

        public virtual async Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                return false;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

            await _sendLock.WaitAsync(linkedCts.Token);
            try
            {
                if (_stream == null)
                    return false;

                // 写入长度前缀
                byte[] lengthBytes = BitConverter.GetBytes(data.Length);
                await _stream.WriteAsync(lengthBytes, linkedCts.Token);

                // 写入数据
                await _stream.WriteAsync(data, linkedCts.Token);
                await _stream.FlushAsync(linkedCts.Token);

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
        /// 发送数据
        /// </summary>
        public virtual Task<bool> SendAsync<T>(in Messaging.MessageHeader header, T? payload, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 接收循环
        /// </summary>
        protected async Task ReceiveLoopAsync()
        {
            try
            {
                byte[] lengthBuffer = new byte[4];

                while (!_cts.IsCancellationRequested && IsConnected)
                {
                    // 读取消息长度
                    if (!await ReadExactBytesAsync(lengthBuffer, 0, 4))
                        break;

                    // 解析消息长度
                    int messageLength = BitConverter.ToInt32(lengthBuffer, 0);

                    // 验证长度
                    if (messageLength <= 0 || messageLength > _options.ReadBufferSize * 2)
                    {
                        _logger.LogWarning("收到无效的消息长度: {Length}", messageLength);
                        continue;
                    }

                    // 读取消息内容
                    byte[] messageBuffer = messageLength <= _receiveBuffer.Length
                        ? _receiveBuffer : new byte[messageLength];

                    if (!await ReadExactBytesAsync(messageBuffer, 0, messageLength))
                        break;

                    // 触发数据接收事件
                    DataReceived?.Invoke(this, new TransportDataEventArgs(new ReadOnlyMemory<byte>(messageBuffer, 0, messageLength)));
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
