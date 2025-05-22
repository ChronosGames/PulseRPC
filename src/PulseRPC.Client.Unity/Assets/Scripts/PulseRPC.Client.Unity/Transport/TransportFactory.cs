using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PulseRPC.Transport
{
    /// <summary>
    /// 传输类型枚举
    /// </summary>
    public enum TransportType
    {
        /// <summary>
        /// TCP 传输
        /// </summary>
        Tcp,

        /// <summary>
        /// UDP 传输
        /// </summary>
        Udp,

        /// <summary>
        /// KCP 传输 (基于UDP的可靠传输)
        /// </summary>
        Kcp,

        /// <summary>
        /// WebSocket 传输
        /// </summary>
        WebSocket
    }

    /// <summary>
    /// KCP配置选项
    /// </summary>
    public class KcpOptions
    {
        /// <summary>
        /// 是否启用无延迟模式
        /// 0: 不启用
        /// 1: 启用
        /// </summary>
        public int NoDelay { get; set; } = 1;

        /// <summary>
        /// 更新间隔（毫秒）
        /// </summary>
        public int Interval { get; set; } = 10;

        /// <summary>
        /// 快速重传模式
        /// 0: 不启用
        /// 1: 允许快速重传
        /// 2: 激进快速重传
        /// </summary>
        public int Resend { get; set; } = 2;

        /// <summary>
        /// 是否禁用流控制
        /// </summary>
        public bool DisableFlowControl { get; set; }

        /// <summary>
        /// 最大传输单元
        /// </summary>
        public int Mtu { get; set; } = 1400;
    }

    /// <summary>
    /// 传输选项
    /// </summary>
    public class TransportOptions
    {
        /// <summary>
        /// 连接超时时间（毫秒）
        /// </summary>
        public int ConnectionTimeout { get; set; } = 5000;

        /// <summary>
        /// 发送超时时间（毫秒）
        /// </summary>
        public int SendTimeout { get; set; } = 5000;

        /// <summary>
        /// 接收超时时间（毫秒）
        /// </summary>
        public int ReceiveTimeout { get; set; } = 0; // 0表示不超时

        /// <summary>
        /// 是否启用TCP无延迟
        /// </summary>
        public bool NoDelay { get; set; } = true;

        /// <summary>
        /// 是否启用保持连接
        /// </summary>
        public bool KeepAlive { get; set; } = true;

        /// <summary>
        /// 保持连接间隔（秒）
        /// </summary>
        public int KeepAliveInterval { get; set; } = 30;

        /// <summary>
        /// 接收缓冲区大小
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 8192;

        /// <summary>
        /// 发送缓冲区大小
        /// </summary>
        public int SendBufferSize { get; set; } = 8192;

        /// <summary>
        /// 是否启用自动重连
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// 最大重连次数
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = 3;

        /// <summary>
        /// 重连间隔（毫秒）
        /// </summary>
        public int ReconnectInterval { get; set; } = 3000;

        /// <summary>
        /// KCP 配置选项
        /// </summary>
        public KcpOptions Kcp { get; set; } = new KcpOptions();

        /// <summary>
        /// 自定义选项
        /// </summary>
        public Dictionary<string, object> CustomOptions { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// 客户端传输接口
    /// </summary>
    public interface IClientTransport
    {
        /// <summary>
        /// 是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        /// <param name="host">主机地址</param>
        /// <param name="port">端口</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

        /// <summary>
        /// 断开连接
        /// </summary>
        void Disconnect();

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="data">要发送的数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task SendAsync(byte[] data, CancellationToken cancellationToken = default);

        /// <summary>
        /// 已连接事件
        /// </summary>
        event Action Connected;

        /// <summary>
        /// 已断开连接事件
        /// </summary>
        event Action Disconnected;

        /// <summary>
        /// 收到消息事件
        /// </summary>
        event Action<byte[]> MessageReceived;
    }

    /// <summary>
    /// 传输工厂类
    /// </summary>
    public class TransportFactory
    {
        /// <summary>
        /// 创建客户端传输实例
        /// </summary>
        /// <param name="transportType">传输类型</param>
        /// <param name="options">传输选项</param>
        /// <returns>客户端传输实例</returns>
        public async Task<IClientTransport> CreateClientTransportAsync(TransportType transportType, TransportOptions options = null)
        {
            options ??= new TransportOptions();

            IClientTransport transport = transportType switch
            {
                TransportType.Tcp => new TcpTransport(options),
                TransportType.Kcp => new KcpTransport(options),
                TransportType.WebSocket => new WebSocketTransport(options),
                _ => throw new ArgumentException($"不支持的传输类型: {transportType}")
            };

            return transport;
        }
    }

    /// <summary>
    /// TCP传输实现
    /// </summary>
    public class TcpTransport : IClientTransport, IDisposable
    {
        private readonly TransportOptions _options;
        private System.Net.Sockets.TcpClient _client;
        private System.Net.Sockets.NetworkStream _stream;
        private System.Threading.CancellationTokenSource _cts;
        private Task _receiveTask;
        private bool _isDisposed;
        private readonly object _syncLock = new object();

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// 已连接事件
        /// </summary>
        public event Action Connected;

        /// <summary>
        /// 已断开连接事件
        /// </summary>
        public event Action Disconnected;

        /// <summary>
        /// 收到消息事件
        /// </summary>
        public event Action<byte[]> MessageReceived;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="options">传输选项</param>
        public TcpTransport(TransportOptions options)
        {
            _options = options ?? new TransportOptions();
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // 如果已连接，先断开
            if (IsConnected)
            {
                Disconnect();
            }

            Debug.Log($"[TCP] 正在连接到 {host}:{port}...");

            try
            {
                // 创建客户端
                _client = new System.Net.Sockets.TcpClient
                {
                    ReceiveBufferSize = _options.ReceiveBufferSize,
                    SendBufferSize = _options.SendBufferSize,
                    NoDelay = _options.NoDelay
                };

                // 连接超时设置
                var connectCts = new CancellationTokenSource(_options.ConnectionTimeout);
                var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(connectCts.Token, cancellationToken);

                try
                {
                    // 连接到服务器
                    await _client.ConnectAsync(host, port).WaitAsync(combinedCts.Token);
                }
                catch (OperationCanceledException) when (connectCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"连接超时: {_options.ConnectionTimeout}ms");
                }
                finally
                {
                    connectCts.Dispose();
                    combinedCts.Dispose();
                }

                // 获取网络流
                _stream = _client.GetStream();

                // 设置超时
                if (_options.ReceiveTimeout > 0)
                    _stream.ReadTimeout = _options.ReceiveTimeout;
                if (_options.SendTimeout > 0)
                    _stream.WriteTimeout = _options.SendTimeout;

                // 启动接收任务
                _cts = new CancellationTokenSource();
                _receiveTask = Task.Run(ReceiveLoopAsync, _cts.Token);

                // 标记为已连接
                IsConnected = true;

                // 触发连接事件
                Connected?.Invoke();

                Debug.Log($"[TCP] 已连接到 {host}:{port}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TCP] 连接失败: {ex.Message}");

                // 清理资源
                CleanupConnection();

                throw;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            if (!IsConnected)
                return;

            Debug.Log("[TCP] 断开连接");

            // 清理连接
            CleanupConnection();

            // 触发断开连接事件
            Disconnected?.Invoke();
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!IsConnected || _stream == null)
                throw new InvalidOperationException("未连接到服务器");

            try
            {
                // 写入数据长度
                var lengthBytes = BitConverter.GetBytes(data.Length);
                await _stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken);

                // 写入数据
                await _stream.WriteAsync(data, 0, data.Length, cancellationToken);
                await _stream.FlushAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TCP] 发送数据失败: {ex.Message}");

                // 处理连接断开
                HandleDisconnect();

                throw;
            }
        }

        /// <summary>
        /// 接收循环
        /// </summary>
        private async Task ReceiveLoopAsync()
        {
            var headerBuffer = new byte[4]; // 消息头（长度）
            var buffer = new byte[_options.ReceiveBufferSize];

            try
            {
                while (!_cts.Token.IsCancellationRequested && _client.Connected)
                {
                    // 读取消息长度
                    int headerRead = 0;
                    while (headerRead < 4)
                    {
                        int bytesRead = await _stream.ReadAsync(headerBuffer, headerRead, 4 - headerRead, _cts.Token);
                        if (bytesRead == 0) // 连接已关闭
                        {
                            throw new System.IO.EndOfStreamException("连接已关闭");
                        }
                        headerRead += bytesRead;
                    }

                    // 解析消息长度
                    int messageLength = BitConverter.ToInt32(headerBuffer, 0);
                    if (messageLength <= 0 || messageLength > _options.ReceiveBufferSize)
                    {
                        throw new InvalidOperationException($"无效的消息长度: {messageLength}");
                    }

                    // 确保缓冲区足够大
                    if (messageLength > buffer.Length)
                    {
                        buffer = new byte[messageLength];
                    }

                    // 读取消息内容
                    int totalRead = 0;
                    while (totalRead < messageLength)
                    {
                        int bytesRead = await _stream.ReadAsync(buffer, totalRead, messageLength - totalRead, _cts.Token);
                        if (bytesRead == 0) // 连接已关闭
                        {
                            throw new System.IO.EndOfStreamException("连接已关闭");
                        }
                        totalRead += bytesRead;
                    }

                    // 复制消息数据并触发事件
                    var messageData = new byte[messageLength];
                    Array.Copy(buffer, messageData, messageLength);
                    MessageReceived?.Invoke(messageData);
                }
            }
            catch (OperationCanceledException) when (_cts.Token.IsCancellationRequested)
            {
                // 正常取消，不做处理
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TCP] 接收数据出错: {ex.Message}");
            }
            finally
            {
                // 处理连接断开
                if (IsConnected)
                {
                    HandleDisconnect();
                }
            }
        }

        /// <summary>
        /// 处理连接断开
        /// </summary>
        private void HandleDisconnect()
        {
            lock (_syncLock)
            {
                if (!IsConnected)
                    return;

                Debug.Log("[TCP] 连接断开");

                // 清理连接
                CleanupConnection();

                // 触发断开连接事件
                try
                {
                    Disconnected?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[TCP] 处理断开连接事件出错: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 清理连接资源
        /// </summary>
        private void CleanupConnection()
        {
            lock (_syncLock)
            {
                // 取消接收任务
                if (_cts != null)
                {
                    try
                    {
                        _cts.Cancel();
                        _cts.Dispose();
                        _cts = null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[TCP] 取消接收任务出错: {ex.Message}");
                    }
                }

                // 关闭网络流
                if (_stream != null)
                {
                    try
                    {
                        _stream.Close();
                        _stream.Dispose();
                        _stream = null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[TCP] 关闭网络流出错: {ex.Message}");
                    }
                }

                // 关闭客户端
                if (_client != null)
                {
                    try
                    {
                        _client.Close();
                        _client.Dispose();
                        _client = null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[TCP] 关闭客户端出错: {ex.Message}");
                    }
                }

                // 标记为已断开
                IsConnected = false;
            }
        }

        /// <summary>
        /// 检查是否已释放
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            // 断开连接
            if (IsConnected)
            {
                Disconnect();
            }

            _isDisposed = true;
        }
    }

    /// <summary>
    /// KCP传输实现
    /// 注：Unity环境中的KCP实现需要专门的库支持，此处为示例框架
    /// </summary>
    public class KcpTransport : IClientTransport, IDisposable
    {
        private readonly TransportOptions _options;
        private bool _isDisposed;

        // KCP相关字段...

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// 已连接事件
        /// </summary>
        public event Action Connected;

        /// <summary>
        /// 已断开连接事件
        /// </summary>
        public event Action Disconnected;

        /// <summary>
        /// 收到消息事件
        /// </summary>
        public event Action<byte[]> MessageReceived;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="options">传输选项</param>
        public KcpTransport(TransportOptions options)
        {
            _options = options ?? new TransportOptions();
            // 在实际项目中初始化KCP实例...
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            // 示例实现，实际使用时需要替换为真正的KCP实现
            Debug.Log($"[KCP] 正在连接到 {host}:{port}...");

            // 模拟连接成功
            IsConnected = true;
            Connected?.Invoke();

            Debug.Log($"[KCP] 已连接到 {host}:{port}");

            return Task.CompletedTask;
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            if (!IsConnected)
                return;

            Debug.Log("[KCP] 断开连接");

            // 清理连接...

            IsConnected = false;
            Disconnected?.Invoke();
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        public Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            // 示例实现，实际使用时需要替换为真正的KCP实现
            if (!IsConnected)
                throw new InvalidOperationException("未连接到服务器");

            Debug.Log($"[KCP] 发送数据: {data.Length} 字节");

            return Task.CompletedTask;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            if (IsConnected)
            {
                Disconnect();
            }

            _isDisposed = true;
        }
    }

    /// <summary>
    /// WebSocket传输实现
    /// </summary>
    public class WebSocketTransport : IClientTransport, IDisposable
    {
        private readonly TransportOptions _options;
        private bool _isDisposed;

        // WebSocket相关字段...

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// 已连接事件
        /// </summary>
        public event Action Connected;

        /// <summary>
        /// 已断开连接事件
        /// </summary>
        public event Action Disconnected;

        /// <summary>
        /// 收到消息事件
        /// </summary>
        public event Action<byte[]> MessageReceived;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="options">传输选项</param>
        public WebSocketTransport(TransportOptions options)
        {
            _options = options ?? new TransportOptions();
            // 在实际项目中初始化WebSocket实例...
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            // 示例实现，实际使用时需要替换为真正的WebSocket实现
            Debug.Log($"[WebSocket] 正在连接到 {host}:{port}...");

            // 模拟连接成功
            IsConnected = true;
            Connected?.Invoke();

            Debug.Log($"[WebSocket] 已连接到 {host}:{port}");

            return Task.CompletedTask;
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            if (!IsConnected)
                return;

            Debug.Log("[WebSocket] 断开连接");

            // 清理连接...

            IsConnected = false;
            Disconnected?.Invoke();
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        public Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            // 示例实现，实际使用时需要替换为真正的WebSocket实现
            if (!IsConnected)
                throw new InvalidOperationException("未连接到服务器");

            Debug.Log($"[WebSocket] 发送数据: {data.Length} 字节");

            return Task.CompletedTask;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            if (IsConnected)
            {
                Disconnect();
            }

            _isDisposed = true;
        }
    }
}
