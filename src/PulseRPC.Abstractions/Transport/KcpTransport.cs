using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MemoryPack;

namespace PulseRPC.Transport.Kcp
{
    /// <summary>
    /// KCP传输基类
    /// </summary>
    public class KcpTransport : ITransport
    {
        protected readonly KcpCore _kcp;
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

        // KCP时间戳管理
        protected readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

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
            _kcp = new KcpCore(_options.Kcp.ConversationId, OnKcpOutput, _logger);

            // 配置KCP
            _kcp.NoDelay(_options.Kcp.NoDelay, _options.Kcp.Interval, _options.Kcp.Resend,
                _options.Kcp.DisableFlowControl);
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
                // 直接发送数据到KCP
                var result = _kcp.Send(data.Span);
                return result == 0;
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

        public async Task<bool> SendAsync<T>(Messaging.MessageHeader header, T? payload, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                return false;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

            await _sendLock.WaitAsync(linkedCts.Token);
            try
            {
                // 使用MemoryPack序列化数据
                var headerBytes = MemoryPackSerializer.Serialize(header);

                byte[] payloadBytes = Array.Empty<byte>();
                if (payload != null)
                {
                    payloadBytes = MemoryPackSerializer.Serialize(payload);
                }

                // 创建完整消息包：[HeaderLength:4][Header:HeaderLength][PayloadLength:4][Payload:PayloadLength]
                var totalLength = 4 + headerBytes.Length + 4 + payloadBytes.Length;
                var messageBuffer = new byte[totalLength];

                var offset = 0;

                // 写入消息头长度
                BitConverter.TryWriteBytes(messageBuffer.AsSpan(offset), headerBytes.Length);
                offset += 4;

                // 写入消息头
                headerBytes.CopyTo(messageBuffer, offset);
                offset += headerBytes.Length;

                // 写入载荷长度
                BitConverter.TryWriteBytes(messageBuffer.AsSpan(offset), payloadBytes.Length);
                offset += 4;

                // 写入载荷
                payloadBytes.CopyTo(messageBuffer, offset);

                // 通过KCP发送数据
                var result = _kcp.Send(messageBuffer);

                if (result == 0)
                {
                    _logger.LogDebug("KCP消息发送成功: HeaderLength={HeaderLength}, PayloadLength={PayloadLength}",
                        headerBytes.Length, payloadBytes.Length);
                    return true;
                }
                else
                {
                    _logger.LogWarning("KCP消息发送失败: Result={Result}", result);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KCP发送消息异常");
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
                    uint currentTime = GetCurrentTimeMs();

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
                    else
                    {
                        await Task.Delay(1, _cts.Token); // 防止100%CPU占用
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

                    // 触发数据接收事件
                    var receivedData = new byte[size];
                    Array.Copy(buffer, 0, receivedData, 0, size);
                    DataReceived?.Invoke(this, new TransportDataEventArgs(receivedData));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理KCP接收数据异常");
            }
        }

        /// <summary>
        /// 获取当前时间戳（毫秒）
        /// </summary>
        protected uint GetCurrentTimeMs()
        {
            return (uint)_stopwatch.ElapsedMilliseconds;
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

            _kcp?.Dispose();
            _cts.Dispose();
            _sendLock.Dispose();
            _stopwatch.Stop();
        }
    }
}
