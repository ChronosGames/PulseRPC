using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityTCP.Memory;
using UnityTCP.Serialization;

namespace UnityTCP.ZeroCopy
{
    /// <summary>
    /// TCP服务器配置选项
    /// </summary>
    public class EnhancedTCPServerOptions
    {
        public int SendBufferSize { get; set; } = 262144; // 256KB
        public int ReceiveBufferSize { get; set; } = 262144; // 256KB
        public bool DisableNagle { get; set; } = true;
        public bool UseZeroCopy { get; set; } = true;
        public int MaxConcurrentSends { get; set; } = 8;
        public bool EnableSocketPollOptimization { get; set; } = true;
        public int AcceptBacklog { get; set; } = 100;
        public bool ReuseAddress { get; set; } = true;
    }

    /// <summary>
    /// 增强的TCP服务器实现，支持零拷贝和高性能IO
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public class EnhancedTCPServer : IDisposable
    {
        private readonly EnhancedTCPServerOptions _options;
        private TcpListener _listener;
        private bool _isRunning;
        private CancellationTokenSource _cts;
        private readonly ConcurrentDictionary<string, ClientSession> _clients = new ConcurrentDictionary<string, ClientSession>();
        private readonly NetworkMemoryAllocator _memoryAllocator = new NetworkMemoryAllocator();
        private readonly SemaphoreSlim _maxConcurrentAccepts = new SemaphoreSlim(5); // 限制并发接受连接数

        // 事件
        public event Action<string> ClientConnected;
        public event Action<string> ClientDisconnected;
        public event Action<string, ReadOnlySequence<byte>> DataReceived;
        public event Action<Exception> ErrorOccurred;

        public EnhancedTCPServer(EnhancedTCPServerOptions options = null)
        {
            _options = options ?? new EnhancedTCPServerOptions();
        }

        /// <summary>
        /// 启动服务器
        /// </summary>
        public async Task StartAsync(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);

            // 配置Socket选项
            if (_options.ReuseAddress)
            {
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }

            _listener.Start(_options.AcceptBacklog);
            _isRunning = true;
            _cts = new CancellationTokenSource();

            try
            {
                // 启动多个Accept任务以提高并发性能
                for (int i = 0; i < Environment.ProcessorCount; i++)
                {
                    _ = AcceptConnectionsAsync();
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
                Stop();
                throw;
            }
        }

        /// <summary>
        /// 接受客户端连接的任务
        /// </summary>
        private async Task AcceptConnectionsAsync()
        {
            while (_isRunning && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await _maxConcurrentAccepts.WaitAsync(_cts.Token);

                    try
                    {
                        var client = await _listener.AcceptTcpClientAsync();
                        _ = HandleClientAsync(client);
                    }
                    finally
                    {
                        _maxConcurrentAccepts.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常取消
                    break;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(ex);

                    // 短暂延迟，避免CPU占用过高
                    await Task.Delay(100, CancellationToken.None);
                }
            }
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _cts?.Cancel();
            _listener?.Stop();

            // 断开所有客户端连接
            foreach (var client in _clients.Values)
            {
                try
                {
                    client.Dispose();
                }
                catch
                {
                    // ignored
                }
            }

            _clients.Clear();
        }

        /// <summary>
        /// 处理客户端连接
        /// </summary>
        private async Task HandleClientAsync(TcpClient client)
        {
            string clientId = Guid.NewGuid().ToString();

            try
            {
                // 配置客户端Socket
                ConfigureClientSocket(client.Client);

                var session = new ClientSession(client, _options, _memoryAllocator);
                _clients[clientId] = session;

                ClientConnected?.Invoke(clientId);

                // 开始处理消息
                await session.ProcessMessagesAsync(
                    data => DataReceived?.Invoke(clientId, data),
                    ex => ErrorOccurred?.Invoke(ex),
                    _cts.Token);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }
            finally
            {
                if (_clients.TryRemove(clientId, out var session))
                {
                    session.Dispose();
                }

                ClientDisconnected?.Invoke(clientId);
            }
        }

        /// <summary>
        /// 配置客户端Socket以优化性能
        /// </summary>
        private void ConfigureClientSocket(Socket socket)
        {
            socket.NoDelay = _options.DisableNagle;
            socket.SendBufferSize = _options.SendBufferSize;
            socket.ReceiveBufferSize = _options.ReceiveBufferSize;

            // 启用TCP保活
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            // 处理SocketException，避免Socket.Shutdown引发异常
            socket.LingerState = new LingerOption(true, 0);

            if (_options.UseZeroCopy && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // 在Linux上启用零拷贝
                try
                {
                    const int SO_ZEROCOPY = 60;
                    socket.SetSocketOption(SocketOptionLevel.Socket, (SocketOptionName)SO_ZEROCOPY, 1);
                }
                catch (Exception)
                {
                    // 忽略不支持的平台错误
                }
            }
        }

        /// <summary>
        /// 向所有客户端广播网络对象
        /// </summary>
        public async Task BroadcastObjectAsync<T>(T obj) where T : struct, INetworkSerializable
        {
            var tasks = new List<Task>();

            foreach (var client in _clients.Values)
            {
                tasks.Add(client.SendObjectAsync(obj));
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 向特定客户端发送网络对象
        /// </summary>
        public Task SendObjectToClientAsync<T>(string clientId, T obj) where T : struct, INetworkSerializable
        {
            if (_clients.TryGetValue(clientId, out var client))
            {
                return client.SendObjectAsync(obj);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 向客户端发送Blittable对象（完全零拷贝）
        /// </summary>
        public Task SendBlittableToClientAsync<T>(string clientId, T obj) where T : unmanaged
        {
            if (_clients.TryGetValue(clientId, out var client))
            {
                return client.SendBlittableAsync(obj);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 获取已连接的客户端数
        /// </summary>
        public int GetConnectedClientCount()
        {
            return _clients.Count;
        }

        /// <summary>
        /// 获取所有已连接的客户端ID
        /// </summary>
        public IEnumerable<string> GetConnectedClientIds()
        {
            return _clients.Keys;
        }

        /// <summary>
        /// 检查特定客户端是否连接
        /// </summary>
        public bool IsClientConnected(string clientId)
        {
            return _clients.ContainsKey(clientId);
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            _maxConcurrentAccepts?.Dispose();
            _memoryAllocator.Dispose();
        }

        /// <summary>
        /// 客户端会话类，管理单个客户端连接
        /// </summary>
        private class ClientSession : IDisposable
        {
            private readonly TcpClient _client;
            private readonly NetworkStream _stream;
            private readonly PipeReader _reader;
            private readonly PipeWriter _writer;
            private readonly EnhancedTCPServerOptions _options;
            private readonly NetworkMemoryAllocator _memoryAllocator;
            private readonly SemaphoreSlim _sendSemaphore;

            public ClientSession(TcpClient client, EnhancedTCPServerOptions options, NetworkMemoryAllocator memoryAllocator)
            {
                _client = client;
                _options = options;
                _memoryAllocator = memoryAllocator;
                _stream = client.GetStream();

                // 使用PipeOptions自定义缓冲区大小和内存分配策略
                var readerOptions = new StreamPipeReaderOptions(
                    pool: MemoryPool<byte>.Shared,
                    minimumReadSize: 4096,
                    leaveOpen: false);

                var writerOptions = new StreamPipeWriterOptions(
                    pool: MemoryPool<byte>.Shared,
                    minimumBufferSize: 4096,
                    leaveOpen: false
                );

                _reader = PipeReader.Create(_stream, readerOptions);
                _writer = PipeWriter.Create(_stream, writerOptions);
                _sendSemaphore = new SemaphoreSlim(options.MaxConcurrentSends);
            }

            /// <summary>
            /// 处理来自客户端的消息
            /// </summary>
            public async Task ProcessMessagesAsync(
                Action<ReadOnlySequence<byte>> onDataReceived,
                Action<Exception> onError,
                CancellationToken cancellationToken)
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var result = await _reader.ReadAsync(cancellationToken);
                        var buffer = result.Buffer;

                        if (buffer.IsEmpty && result.IsCompleted)
                        {
                            break;
                        }

                        if (!buffer.IsEmpty)
                        {
                            try
                            {
                                // 尝试解析消息
                                ParseMessages(ref buffer, onDataReceived);
                            }
                            catch (Exception ex)
                            {
                                onError(ex);
                            }
                        }

                        _reader.AdvanceTo(buffer.Start, buffer.End);

                        if (result.IsCompleted)
                        {
                            break;
                        }

                        // 如果启用了Socket轮询优化，当没有数据时让出CPU时间
                        if (_options.EnableSocketPollOptimization && buffer.IsEmpty)
                        {
                            await Task.Yield();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常取消
                }
                catch (Exception ex)
                {
                    onError(ex);
                }
                finally
                {
                    await _reader.CompleteAsync();
                    await _writer.CompleteAsync();
                }
            }

            /// <summary>
            /// 解析接收到的消息缓冲区
            /// </summary>
            private void ParseMessages(ref ReadOnlySequence<byte> buffer, Action<ReadOnlySequence<byte>> onDataReceived)
            {
                // 寻找完整的消息
                while (TryReadMessage(ref buffer, out var message))
                {
                    onDataReceived(message);
                }
            }

            /// <summary>
            /// 尝试从缓冲区中读取一条完整消息
            /// </summary>
            private bool TryReadMessage(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> message)
            {
                message = default;

                if (buffer.Length < 4)
                {
                    return false;
                }

                // 读取长度前缀
                int length = BitConverter.ToInt32(buffer.Slice(0, 4).ToArray(), 0);

                if (buffer.Length < length + 4)
                {
                    return false;
                }

                message = buffer.Slice(4, length);
                buffer = buffer.Slice(length + 4);
                return true;
            }

            /// <summary>
            /// 发送网络对象
            /// </summary>
            public async Task SendObjectAsync<T>(T obj) where T : struct, INetworkSerializable
            {
                await _sendSemaphore.WaitAsync();
                try
                {
                    await NetworkSerializer.SerializeDirectAsync(obj, _writer);
                }
                finally
                {
                    _sendSemaphore.Release();
                }
            }

            /// <summary>
            /// 发送Blittable对象（完全零拷贝）
            /// </summary>
            public async Task SendBlittableAsync<T>(T obj) where T : unmanaged
            {
                await _sendSemaphore.WaitAsync();
                try
                {
                    var size = Marshal.SizeOf<T>();
                    var memory = _writer.GetMemory(size + sizeof(int));

                    // 写入长度前缀
                    BitConverter.TryWriteBytes(memory.Span, size);

                    // 直接将结构体写入内存
                    unsafe
                    {
                        fixed (byte* destPtr = &memory.Span[sizeof(int)])
                        {
                            *(T*)destPtr = obj;
                        }
                    }

                    _writer.Advance(size + sizeof(int));
                    await _writer.FlushAsync();
                }
                finally
                {
                    _sendSemaphore.Release();
                }
            }

            /// <summary>
            /// 使用Socket直接发送（绕过流层）
            /// </summary>
            public Task SendDirectAsync(ReadOnlyMemory<byte> data)
            {
                // 先写入长度前缀
                byte[] lengthPrefix = BitConverter.GetBytes(data.Length);

                // 使用ArrayPool以减少内存分配
                byte[] buffer = _memoryAllocator.GetBuffer(data.Length + lengthPrefix.Length);

                try
                {
                    Array.Copy(lengthPrefix, 0, buffer, 0, lengthPrefix.Length);
                    data.CopyTo(new Memory<byte>(buffer, lengthPrefix.Length, data.Length));

                    var tcs = new TaskCompletionSource<bool>();
                    var args = new SocketAsyncEventArgs();
                    args.SetBuffer(buffer, 0, data.Length + lengthPrefix.Length);
                    args.UserToken = new SendOperationContext
                    {
                        TaskSource = tcs,
                        Buffer = buffer,
                        Allocator = _memoryAllocator
                    };

                    args.Completed += OnSendCompleted;

                    if (!_client.Client.SendAsync(args))
                    {
                        OnSendCompleted(null, args);
                    }

                    return tcs.Task;
                }
                catch (Exception)
                {
                    _memoryAllocator.ReturnBuffer(buffer);
                    throw;
                }
            }

            private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
            {
                var context = (SendOperationContext)e.UserToken;

                try
                {
                    if (e.SocketError == SocketError.Success)
                    {
                        context.TaskSource.SetResult(true);
                    }
                    else
                    {
                        context.TaskSource.SetException(new SocketException((int)e.SocketError));
                    }
                }
                finally
                {
                    // 归还缓冲区
                    context.Allocator.ReturnBuffer(context.Buffer);
                    e.Dispose();
                }
            }

            public void Dispose()
            {
                try
                {
                    _sendSemaphore.Dispose();
                    _client.Close();
                }
                catch
                {
                    // 忽略错误
                }
            }

            // 发送操作上下文
            private class SendOperationContext
            {
                public TaskCompletionSource<bool> TaskSource { get; set; }
                public byte[] Buffer { get; set; }
                public NetworkMemoryAllocator Allocator { get; set; }
            }
        }
    }
}
