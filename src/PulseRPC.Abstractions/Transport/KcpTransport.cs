using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PulseRPC.Transport.Kcp
{
    /// <summary>
    /// KCP传输基类
    /// </summary>
    public class KcpTransport : ITransport
    {
        protected readonly IKcpInternal _kcp;
        protected Socket _socket;
        protected readonly TransportOptions _options;
        protected readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        protected readonly CancellationTokenSource _cts = new CancellationTokenSource();
        protected readonly ILogger _logger;

        protected ConnectionState _state = ConnectionState.Disconnected;
        protected IPEndPoint? _remoteEndpoint;
        protected IPEndPoint? _localEndpoint;
        protected Task? _updateTask;
        protected byte[] _receiveBuffer;
        protected bool _disposed;

        public string Name => "KCP";
        public TransportType Type => TransportType.Kcp;
        public bool IsConnected => _state == ConnectionState.Connected;
        public ConnectionState State => _state;

        public EndPoint LocalEndPoint => _localEndpoint!;
        public EndPoint RemoteEndPoint => _remoteEndpoint!;

        public event System.EventHandler<TransportStateEventArgs>? StateChanged;
        public event System.EventHandler<TransportDataEventArgs>? DataReceived;

        public KcpTransport(TransportOptions? options = null, ILogger? logger = null)
        {
            _options = options ?? new TransportOptions();
            _logger = logger ?? NullLogger.Instance;

            _receiveBuffer = new byte[_options.ReadBufferSize];
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            // 创建KCP对象
            _kcp = new KcpInternal(OnKcpOutput);

            // 配置KCP
            _kcp.NoDelay(_options.Kcp.NoDelay, _options.Kcp.Interval, _options.Kcp.Resend,
                !_options.Kcp.DisableFlowControl);
            _kcp.SetWindowSize(_options.Kcp.SendWindow, _options.Kcp.ReceiveWindow);
            _kcp.SetMtu(1400); // UDP推荐MTU
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        public virtual async Task<bool> SendAsync(ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                return false;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

            await _sendLock.WaitAsync(linkedCts.Token);
            try
            {
                // KCP需要先发送长度
                byte[] lengthBytes = BitConverter.GetBytes(data.Length);
                _kcp.Send(lengthBytes);

                // 发送数据
                _kcp.Send(data.Span);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KCP发送数据失败");
                return false;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// KCP输出回调
        /// </summary>
        protected virtual void OnKcpOutput(byte[] buffer, int size)
        {
            try
            {
                if (_remoteEndpoint != null)
                {
                    _socket.SendTo(buffer, 0, size, SocketFlags.None, _remoteEndpoint);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KCP输出异常");
            }
        }

        /// <summary>
        /// KCP更新循环
        /// </summary>
        protected async Task KcpUpdateLoopAsync()
        {
            try
            {
                byte[] recvBuffer = new byte[4096];
                uint nextUpdateTime = 0;

                // 启动UDP接收
                EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                _socket.BeginReceiveFrom(recvBuffer, 0, recvBuffer.Length, SocketFlags.None, ref remoteEp, OnUdpReceive, recvBuffer);

                // KCP更新循环
                while (!_cts.IsCancellationRequested && IsConnected)
                {
                    uint currentTime = (uint)Environment.TickCount;

                    if (currentTime >= nextUpdateTime)
                    {
                        // 更新KCP
                        _kcp.Update(currentTime);
                        nextUpdateTime = _kcp.Check(currentTime);

                        // 处理接收数据
                        ProcessKcpReceive();
                    }

                    // 等待下次更新
                    int sleepTime = (int)(nextUpdateTime - currentTime);
                    if (sleepTime > 0)
                    {
                        await Task.Delay(Math.Min(sleepTime, 10), _cts.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KCP更新循环异常");
                ChangeState(ConnectionState.Disconnected, $"KCP异常: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// UDP数据接收回调
        /// </summary>
        protected void OnUdpReceive(IAsyncResult ar)
        {
            try
            {
                // 检查对象是否已释放
                if (_disposed || _cts.IsCancellationRequested)
                    return;

                EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                var buffer = (byte[])ar.AsyncState!;
                
                // 使用正确的结束方法
                var recvSize = _socket.EndReceiveFrom(ar, ref remoteEp);

                if (recvSize > 0)
                {
                    // 输入数据到KCP
                    _kcp.Input(new Span<byte>(buffer, 0, recvSize));

                    // 更新远程端点
                    if (_remoteEndpoint == null)
                    {
                        _remoteEndpoint = (IPEndPoint)remoteEp;
                    }
                }

                // 继续接收
                if (!_disposed && !_cts.IsCancellationRequested && _socket.IsBound)
                {
                    EndPoint newRemoteEp = new IPEndPoint(IPAddress.Any, 0);
                    _socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref newRemoteEp, OnUdpReceive, buffer);
                }
            }
            catch (ObjectDisposedException)
            {
                // Socket 已被释放，这是正常情况
                _logger.LogDebug("Socket已释放，停止UDP接收");
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted || 
                                             ex.SocketErrorCode == SocketError.ConnectionReset ||
                                             ex.SocketErrorCode == SocketError.Interrupted)
            {
                // 连接被中止或重置，这在网络断开时是正常的
                _logger.LogDebug("Socket操作被中止: {ErrorCode}", ex.SocketErrorCode);
                if (!_cts.IsCancellationRequested && !_disposed)
                {
                    ChangeState(ConnectionState.Disconnected, $"网络连接中断: {ex.SocketErrorCode}", ex);
                }
            }
            catch (Exception ex) when (!_cts.IsCancellationRequested && !_disposed)
            {
                _logger.LogError(ex, "UDP接收异常");
                ChangeState(ConnectionState.Disconnected, $"UDP接收异常: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 处理KCP接收数据
        /// </summary>
        protected void ProcessKcpReceive()
        {
            try
            {
                const int BufferSize = 4096;
                byte[] buffer = new byte[BufferSize];

                while (true)
                {
                    int size = _kcp.Recv(buffer);
                    if (size <= 0)
                        break;

                    if (size == 4)
                    {
                        // 这是长度前缀，接收实际数据
                        int messageLength = BitConverter.ToInt32(buffer, 0);

                        // 验证长度
                        if (messageLength <= 0 || messageLength > _options.ReadBufferSize * 2)
                        {
                            _logger.LogWarning("收到无效的KCP消息长度: {Length}", messageLength);
                            continue;
                        }

                        // 接收实际数据
                        byte[] messageBuffer = new byte[messageLength];
                        int recvSize = _kcp.Recv(messageBuffer);

                        if (recvSize == messageLength)
                        {
                            // 触发数据接收事件
                            DataReceived?.Invoke(this, new TransportDataEventArgs(messageBuffer));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理KCP接收数据异常");
            }
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

            _logger.LogInformation("KCP传输状态变更: {OldState} -> {NewState} ({Reason})",
                oldState, newState, reason ?? "未指定原因");

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
                _socket?.Close();
                _socket?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭KCP资源异常");
            }

            _cts.Dispose();
            _sendLock.Dispose();
        }

        /// <summary>
        /// KCP内部接口
        /// </summary>
        protected interface IKcpInternal
        {
            int Send(byte[] buffer);
            int Send(ReadOnlySpan<byte> buffer);
            int Recv(byte[] buffer);
            void Update(uint currentTimeMilliseconds);
            uint Check(uint currentTimeMilliseconds);
            void NoDelay(int noDelay, int interval, int resend, bool nocwnd);
            void SetWindowSize(int sndwnd, int rcvwnd);
            void SetMtu(int mtu);
            void SetMaximumTransmissionUnit(int mtu);
            void Input(Span<byte> data);
        }

        /// <summary>
        /// KCP内部实现 - 这里简化为示例
        /// 实际项目中应使用成熟的C# KCP库，如KCP.NET
        /// </summary>
        protected class KcpInternal : IKcpInternal
        {
            private readonly uint _conv;
            private readonly Action<byte[], int> _output;

            public KcpInternal(Action<byte[], int> output)
            {
                _conv = 0; // 这里需要一个有效的conv值
                _output = output;
            }

            // 以下为简化实现，实际项目需替换为实际KCP库
            public int Send(byte[] buffer) => Send(new Span<byte>(buffer));

            public int Send(ReadOnlySpan<byte> buffer)
            {
                // 简化实现，直接输出
                byte[] tmp = new byte[buffer.Length];
                buffer.CopyTo(tmp);
                _output(tmp, tmp.Length);
                return buffer.Length;
            }

            public int Recv(byte[] buffer) => 0; // 简化实现
            public void Update(uint currentTimeMilliseconds) { } // 简化实现
            public uint Check(uint currentTimeMilliseconds) => currentTimeMilliseconds + 10; // 简化实现
            public void NoDelay(int noDelay, int interval, int resend, bool nocwnd) { } // 简化实现
            public void SetWindowSize(int sndwnd, int rcvwnd) { } // 简化实现
            public void SetMtu(int mtu) { } // 简化实现
            public void SetMaximumTransmissionUnit(int mtu) { } // 简化实现
            public void Input(Span<byte> data) { } // 简化实现
        }
    }

    /// <summary>
    /// KCP客户端传输
    /// </summary>
    public class KcpClientTransport : KcpTransport, IClientTransport
    {
        private int _reconnectAttempts = 0;
        private Timer? _reconnectTimer;
        private string? _host;
        private int _port;

        public KcpClientTransport(TransportOptions? options = null, ILogger? logger = null)
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

            _host = host;
            _port = port;

            ChangeState(ConnectionState.Connecting);

            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
                linkedCts.CancelAfter(_options.ConnectionTimeout);

                // 解析服务器地址
#if NET5_0_OR_GREATER
                var addresses = await Dns.GetHostAddressesAsync(host, linkedCts.Token);
#else
                var addresses = await Dns.GetHostAddressesAsync(host);
#endif
                var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

                if (address == null)
                    throw new InvalidOperationException($"无法解析主机: {host}");

                // 设置远程端点
                _remoteEndpoint = new IPEndPoint(address, port);

                // 绑定本地端点
                _socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                _localEndpoint = (IPEndPoint)_socket.LocalEndPoint!;

                // 发送握手包
                byte[] handshakeData = BitConverter.GetBytes(_options.Kcp.ConversationId);
                _socket.SendTo(handshakeData, _remoteEndpoint);

                // 等待连接建立
                await Task.Delay(100, linkedCts.Token);

                // 启动KCP更新循环
                _updateTask = KcpUpdateLoopAsync();

                // 更新状态
                ChangeState(ConnectionState.Connected);

                // 重置重连次数
                _reconnectAttempts = 0;

                _logger.LogInformation("已连接到KCP服务器: {Host}:{Port}", host, port);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "连接到KCP服务器失败: {Host}:{Port}", host, port);

                ChangeState(ConnectionState.Failed, $"连接失败: {ex.Message}", ex);

                // 如果启用了自动重连，则开始重连
                if (_options.AutoReconnect && _reconnectAttempts < _options.MaxReconnectAttempts)
                {
                    StartReconnect();
                }

                throw;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (_state == ConnectionState.Disconnected || _state == ConnectionState.Disconnecting)
                return;

            ChangeState(ConnectionState.Disconnecting);

            try
            {
                // 取消自动重连
                _reconnectTimer?.Dispose();
                _reconnectTimer = null;

                // 发送断开连接包
                if (_remoteEndpoint != null)
                {
                    byte[] disconnectData = BitConverter.GetBytes(0xFFFFFFFF);
                    _socket.SendTo(disconnectData, _remoteEndpoint);
                }

                // 等待发送完成
                await Task.Delay(100, cancellationToken);

                // 更新状态
                ChangeState(ConnectionState.Disconnected);

                _logger.LogInformation("已断开KCP连接");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "断开KCP连接异常");

                // 即使出现异常，也更新状态
                ChangeState(ConnectionState.Disconnected, $"断开异常: {ex.Message}", ex);

                throw;
            }
        }

        /// <summary>
        /// 启动重连
        /// </summary>
        private void StartReconnect()
        {
            if (_options.AutoReconnect && _reconnectAttempts < _options.MaxReconnectAttempts)
            {
                _reconnectAttempts++;

                ChangeState(ConnectionState.Reconnecting,
                    $"尝试重连({_reconnectAttempts}/{_options.MaxReconnectAttempts})");

                _reconnectTimer?.Dispose();
                _reconnectTimer = new Timer(async _ =>
                {
                    try
                    {
                        await ConnectAsync(_host!, _port);
                    }
                    catch
                    {
                        // 重连失败，下次继续尝试
                    }
                }, null, _options.ReconnectInterval, Timeout.Infinite);
            }
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
    /// KCP服务端连接
    /// </summary>
    public class KcpServerConnection : KcpTransport, IServerConnection
    {
        private readonly string _connectionId;

        public string ConnectionId => _connectionId;

        /// <summary>
        /// 使用远程端点创建KCP服务端连接
        /// </summary>
        public KcpServerConnection(string connectionId, Socket udpSocket, IPEndPoint remoteEndpoint, uint conv,
            TransportOptions? options = null, ILogger? logger = null)
            : base(options, logger)
        {
            _connectionId = connectionId;

            // 替换Socket和端点
            _socket.Dispose();
            _socket = udpSocket;
            _remoteEndpoint = remoteEndpoint;
            _localEndpoint = (IPEndPoint)udpSocket.LocalEndPoint!;

            // 创建KCP对象 - 使用指定会话ID
            //_kcp = new KcpInternal(conv, OnKcpOutput);

            // 设置状态
            _state = ConnectionState.Connected;

            // 启动KCP更新循环
            _updateTask = KcpUpdateLoopAsync();

            _logger.LogInformation("接受KCP客户端连接: {ConnectionId} 从 {RemoteEndPoint}",
                _connectionId, remoteEndpoint);
        }

        /// <summary>
        /// 关闭连接
        /// </summary>
        public async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            if (_state is ConnectionState.Disconnected or ConnectionState.Disconnecting)
                return;

            ChangeState(ConnectionState.Disconnecting);

            try
            {
                // 发送断开连接包
                if (_remoteEndpoint != null)
                {
                    byte[] disconnectData = BitConverter.GetBytes(0xFFFFFFFF);
                    _socket.SendTo(disconnectData, _remoteEndpoint);
                }

                // 等待发送完成
                await Task.Delay(100, cancellationToken);

                // 更新状态
                ChangeState(ConnectionState.Disconnected);

                _logger.LogInformation("已关闭KCP客户端连接: {ConnectionId}", _connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭KCP客户端连接异常: {ConnectionId}", _connectionId);

                // 即使出现异常，也更新状态
                ChangeState(ConnectionState.Disconnected, $"关闭异常: {ex.Message}", ex);

                throw;
            }
        }

        /// <summary>
        /// 重写KCP输出回调以支持共享Socket
        /// </summary>
        protected override void OnKcpOutput(byte[] buffer, int size)
        {
            try
            {
                if (_remoteEndpoint != null)
                {
                    _socket.SendTo(buffer, 0, size, SocketFlags.None, _remoteEndpoint);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KCP服务端输出异常: {ConnectionId}", _connectionId);
            }
        }
    }

    /// <summary>
    /// KCP服务端监听器
    /// </summary>
    public class KcpServerListener : IServerListener
    {
        private readonly Socket _socket;
        private readonly TransportOptions _options;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task? _listenTask;
        private bool _isListening;
        private readonly int _port;
        private readonly ConcurrentDictionary<string, KcpServerConnection> _connections = new();

        public string Name => "KCP";
        public TransportType Type => TransportType.Kcp;
        public EndPoint LocalEndPoint => _socket.LocalEndPoint!;
        public bool IsListening => _isListening;

        public event System.EventHandler<ServerConnectionEventArgs>? ConnectionAccepted;

        public KcpServerListener(int port, TransportOptions? options = null, ILogger? logger = null)
        {
            _port = port;
            _options = options ?? new TransportOptions();
            _logger = logger ?? NullLogger.Instance;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
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
                // 绑定端口
                _socket.Bind(new IPEndPoint(IPAddress.Any, _port));

                // 启动监听任务
                _listenTask = ListenAsync(_cts.Token);

                _logger.LogInformation("KCP服务器监听已启动，端口: {Port}", _port);

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _isListening = false;
                _logger.LogError(ex, "启动KCP监听失败，端口: {Port}", _port);
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

            // 取消监听任务
            _cts.Cancel();

            // 关闭Socket
            _socket.Close();

            // 等待监听任务完成
            try
            {
                if (_listenTask != null)
                    await _listenTask;
            }
            catch (OperationCanceledException)
            {
                // 忽略取消异常
            }

            // 关闭所有连接
            foreach (var connection in _connections.Values)
            {
                try
                {
                    await connection.CloseAsync(cancellationToken);
                }
                catch
                {
                    // 忽略关闭异常
                }
            }

            _connections.Clear();

            _logger.LogInformation("KCP服务器监听已停止，端口: {Port}", _port);
        }

        /// <summary>
        /// 监听循环
        /// </summary>
        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            try
            {
                byte[] buffer = new byte[4096];
                EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

                // 开始接收数据 - 使用正确的 BeginReceiveFrom
                _socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remoteEp, OnUdpReceive, buffer);

                // 保持任务运行
                await Task.Delay(-1, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "KCP监听异常");
            }
        }

        /// <summary>
        /// UDP数据接收回调
        /// </summary>
        private void OnUdpReceive(IAsyncResult ar)
        {
            try
            {
                // 检查是否已停止监听
                if (!_isListening || _cts.IsCancellationRequested)
                    return;

                EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                byte[] buffer = (byte[])ar.AsyncState!;
                int recvSize = _socket.EndReceiveFrom(ar, ref remoteEp);

                if (recvSize >= 4)
                {
                    // 获取远程端点
                    var clientEp = (IPEndPoint)remoteEp;
                    string clientId = $"{clientEp.Address}:{clientEp.Port}";

                    // 检查是否是断开连接包
                    if (recvSize == 4 && BitConverter.ToUInt32(buffer, 0) == 0xFFFFFFFF)
                    {
                        // 处理断开连接
                        if (_connections.TryRemove(clientId, out var connection))
                        {
                            connection.Dispose();
                        }
                    }
                    else
                    {
                        // 提取会话ID
                        uint conv = BitConverter.ToUInt32(buffer, 0);

                        // 查找或创建连接
                        if (!_connections.TryGetValue(clientId, out var connection))
                        {
                            // 检查是否是握手包
                            if (recvSize == 4)
                            {
                                // 创建新连接
                                connection = new KcpServerConnection(clientId, _socket, clientEp, conv, _options,
                                    _logger);

                                if (_connections.TryAdd(clientId, connection))
                                {
                                    // 连接事件处理
                                    connection.StateChanged += OnConnectionStateChanged;

                                    // 触发连接接受事件
                                    ConnectionAccepted?.Invoke(this, new ServerConnectionEventArgs(connection));
                                }
                                else
                                {
                                    // 连接已存在
                                    connection.Dispose();
                                }
                            }
                        }
                        else
                        {
                            // 处理现有连接数据
                            // 这里需要将数据传递给对应的KCP实例
                            // 实际实现需要建立UDP数据包到KCP实例的映射
                        }
                    }
                }

                // 继续接收 - 使用正确的 BeginReceiveFrom
                if (_isListening && !_cts.IsCancellationRequested)
                {
                    EndPoint newRemoteEp = new IPEndPoint(IPAddress.Any, 0);
                    _socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref newRemoteEp, OnUdpReceive, buffer);
                }
            }
            catch (ObjectDisposedException)
            {
                // Socket 已被释放，这是正常情况
                _logger.LogDebug("监听Socket已释放");
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted || 
                                             ex.SocketErrorCode == SocketError.Interrupted)
            {
                // 操作被中止，通常在停止监听时发生
                _logger.LogDebug("监听操作被中止: {ErrorCode}", ex.SocketErrorCode);
            }
            catch (Exception ex) when (!_cts.IsCancellationRequested)
            {
                _logger.LogError(ex, "KCP接收异常");

                // 尝试恢复接收
                try
                {
                    if (_isListening && !_cts.IsCancellationRequested)
                    {
                        byte[] newBuffer = new byte[4096];
                        EndPoint newRemoteEp = new IPEndPoint(IPAddress.Any, 0);
                        _socket.BeginReceiveFrom(newBuffer, 0, newBuffer.Length, SocketFlags.None, ref newRemoteEp, OnUdpReceive, newBuffer);
                    }
                }
                catch
                {
                    // 恢复失败，停止监听
                    _logger.LogWarning("无法恢复UDP接收，停止监听");
                }
            }
        }

        /// <summary>
        /// 连接状态变化处理
        /// </summary>
        private void OnConnectionStateChanged(object? sender, TransportStateEventArgs e)
        {
            if (e.CurrentState == ConnectionState.Disconnected)
            {
                var connection = (KcpServerConnection)sender!;

                // 移除断开的连接
                if (_connections.TryRemove(connection.ConnectionId, out _))
                {
                    // 取消事件订阅
                    connection.StateChanged -= OnConnectionStateChanged;
                }
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
