using System.Net;
using System.Net.Sockets;
using PulseRPC.Messaging;
using PulseRPC.Serialization;

namespace PulseRPC.Server;

/// <summary>
/// 服务器端监听器
/// </summary>
public class NetworkServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly ISerializer _serializer;
    private readonly List<ClientSession> _clients = new();
    private readonly Dictionary<string, object> _serviceImplementations = new();
    private readonly object _syncLock = new object();
    private bool _isRunning;
    private CancellationTokenSource? _cts;

    public NetworkServer(int port = 7000)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _serializer = new PulseRPCSerializer();
    }

    public void RegisterService<T>(T implementation) where T : class, INetworkService
    {
        string serviceName = typeof(T).Name;
        lock (_syncLock)
        {
            _serviceImplementations[serviceName] = implementation;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isRunning = true;

        _listener.Start();

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);

                // 创建客户端会话
                var session = new ClientSession(client, _serializer, this);

                // 添加到客户端列表
                lock (_syncLock)
                {
                    _clients.Add(session);
                }

                // 启动处理
                _ = HandleClientSessionAsync(session);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Server error: {ex.Message}");
        }
        finally
        {
            _listener.Stop();
            _isRunning = false;
        }
    }

    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        _cts?.Cancel();

        // 关闭所有客户端连接
        ClientSession[] clients;
        lock (_syncLock)
        {
            clients = _clients.ToArray();
            _clients.Clear();
        }

        foreach (var client in clients)
        {
            await client.CloseAsync();
        }

        _isRunning = false;
    }

    public async Task BroadcastEventAsync<T>(string eventName, T eventData)
    {
        ClientSession[] clients;
        lock (_syncLock)
        {
            clients = _clients.ToArray();
        }

        byte[] eventBytes = _serializer.Serialize(eventData);

        foreach (var client in clients)
        {
            try
            {
                await client.SendEventAsync(eventName, eventBytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error broadcasting to client: {ex.Message}");
            }
        }
    }

    internal object? GetServiceImplementation(string serviceName)
    {
        lock (_syncLock)
        {
            if (_serviceImplementations.TryGetValue(serviceName, out var implementation))
            {
                return implementation;
            }

            return null;
        }
    }

    private async Task HandleClientSessionAsync(ClientSession session)
    {
        try
        {
            await session.ProcessMessagesAsync();
        }
        finally
        {
            // 移除客户端
            lock (_syncLock)
            {
                _clients.Remove(session);
            }

            // 关闭会话
            await session.CloseAsync();
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _cts?.Dispose();
    }

    // 内部类：客户端会话
    private class ClientSession
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly ISerializer _serializer;
        private readonly NetworkServer _server;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private bool _isClosed;

        public ClientSession(TcpClient client, ISerializer serializer, NetworkServer server)
        {
            _client = client;
            _stream = client.GetStream();
            _serializer = serializer;
            _server = server;
        }

        public async Task ProcessMessagesAsync()
        {
            byte[] lengthBuffer = new byte[4];

            while (!_isClosed && _client.Connected)
            {
                try
                {
                    // 读取消息长度
                    if (!await ReadExactBytesAsync(lengthBuffer, 0, 4))
                        break;

                    int messageLength = BitConverter.ToInt32(lengthBuffer);

                    // 读取消息内容
                    byte[] messageBuffer = new byte[messageLength];
                    if (!await ReadExactBytesAsync(messageBuffer, 0, messageLength))
                        break;

                    // 处理消息
                    await HandleMessageAsync(messageBuffer);
                }
                catch (IOException)
                {
                    // 连接断开
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing client message: {ex.Message}");
                }
            }
        }

        private async Task<bool> ReadExactBytesAsync(byte[] buffer, int offset, int count)
        {
            int bytesRead = 0;
            while (bytesRead < count)
            {
                int read = await _stream.ReadAsync(buffer, offset + bytesRead, count - bytesRead);
                if (read == 0)
                    return false; // 连接关闭

                bytesRead += read;
            }

            return true;
        }

        private async Task HandleMessageAsync(byte[] messageData)
        {
            using var ms = new MemoryStream(messageData);
            using var reader = new BinaryReader(ms);

            // 读取头部长度
            int headerLength = reader.ReadInt32();

            // 读取头部
            byte[] headerBytes = reader.ReadBytes(headerLength);
            var header = _serializer.Deserialize<MessageHeader>(headerBytes);

            // 读取消息体
            byte[] body = new byte[messageData.Length - 4 - headerLength];
            reader.Read(body, 0, body.Length);

            switch (header.Type)
            {
                case MessageType.Request:
                    await HandleRequestAsync(header, body);
                    break;

                case MessageType.Ping:
                    await HandlePingAsync(header);
                    break;

                default:
                    Console.WriteLine($"Received unsupported message type: {header.Type}");
                    break;
            }
        }

        private async Task HandleRequestAsync(MessageHeader header, byte[] body)
        {
            // 查找服务实现
            var service = _server.GetServiceImplementation(header.ServiceName);
            if (service == null)
            {
                await SendErrorResponseAsync(header, "Service not found");
                return;
            }

            // 查找方法
            var method = service.GetType().GetMethod(header.MethodName);
            if (method == null)
            {
                await SendErrorResponseAsync(header, "Method not found");
                return;
            }

            try
            {
                // 反序列化请求参数
                var parameters = method.GetParameters();
                object[] args = new object[parameters.Length];

                if (parameters.Length > 0)
                {
                    // 第一个参数是请求对象
                    args[0] = _serializer.Deserialize(body, parameters[0].ParameterType);

                    // 最后一个参数可能是取消令牌
                    if (parameters.Length > 1 &&
                        parameters[parameters.Length - 1].ParameterType == typeof(CancellationToken))
                    {
                        args[parameters.Length - 1] = CancellationToken.None;
                    }
                }

                // 调用方法
                object result = method.Invoke(service, args)!;

                // 处理异步结果
                if (result is Task task)
                {
                    if (method.ReturnType.IsGenericType)
                    {
                        // 有返回值的任务
                        await task;

                        // 获取结果
                        var resultProperty = task.GetType().GetProperty("Result");
                        var taskResult = resultProperty!.GetValue(task);

                        // 发送响应
                        await SendSuccessResponseAsync(header, taskResult!);
                    }
                    else
                    {
                        // 无返回值的任务
                        await task;

                        // 发送空响应
                        await SendSuccessResponseAsync(header, new { });
                    }
                }
                else if (result is ValueTask valueTask)
                {
                    // 有返回值的ValueTask
                    var taskAwaiter = valueTask.GetType().GetMethod("GetAwaiter")!.Invoke(valueTask, null);
                    var getResultMethod = taskAwaiter!.GetType().GetMethod("GetResult");

                    await ((valueTask.GetType().GetMethod("AsTask")!.Invoke(valueTask, null)! as Task)!);

                    var taskResult = getResultMethod!.Invoke(taskAwaiter, null);

                    // 发送响应
                    await SendSuccessResponseAsync(header, taskResult!);
                }
                else
                {
                    // 同步结果
                    await SendSuccessResponseAsync(header, result!);
                }
            }
            catch (Exception ex)
            {
                // 处理异常
                await SendErrorResponseAsync(header, ex.InnerException?.Message ?? ex.Message);
            }
        }

        private async Task HandlePingAsync(MessageHeader header)
        {
            // 创建响应头部
            var responseHeader = new MessageHeader
            {
                Type = MessageType.Pong,
                MessageId = header.MessageId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // 发送Pong响应
            await SendMessageAsync(responseHeader, Array.Empty<byte>());
        }

        private async Task SendSuccessResponseAsync(MessageHeader requestHeader, object responseData)
        {
            // 创建响应头部
            var responseHeader = new MessageHeader
            {
                Type = MessageType.Response,
                MessageId = requestHeader.MessageId,
                ServiceName = requestHeader.ServiceName,
                MethodName = requestHeader.MethodName,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // 序列化响应数据
            byte[] responseBody = _serializer.Serialize(responseData);

            // 发送响应
            await SendMessageAsync(responseHeader, responseBody);
        }

        private async Task SendErrorResponseAsync(MessageHeader requestHeader, string errorMessage)
        {
            // 创建错误响应
            var errorResponse = new { Success = false, ErrorMessage = errorMessage };

            // 创建响应头部
            var responseHeader = new MessageHeader
            {
                Type = MessageType.Response,
                MessageId = requestHeader.MessageId,
                ServiceName = requestHeader.ServiceName,
                MethodName = requestHeader.MethodName,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // 序列化错误响应
            byte[] responseBody = _serializer.Serialize(errorResponse);

            // 发送响应
            await SendMessageAsync(responseHeader, responseBody);
        }

        public async Task SendEventAsync(string eventName, byte[] eventData)
        {
            // 创建事件头部
            var eventHeader = new MessageHeader
            {
                Type = MessageType.Event,
                MessageId = Guid.NewGuid(),
                MethodName = eventName,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // 发送事件
            await SendMessageAsync(eventHeader, eventData);
        }

        private async Task SendMessageAsync(MessageHeader header, byte[] body)
        {
            // 序列化头部
            byte[] headerBytes = _serializer.Serialize(header);

            // 创建完整消息
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // 写入头部长度
            writer.Write(headerBytes.Length);
            // 写入头部
            writer.Write(headerBytes);
            // 写入消息体
            writer.Write(body);

            // 发送消息
            byte[] messageData = ms.ToArray();

            await _sendLock.WaitAsync();
            try
            {
                if (!_isClosed && _client.Connected)
                {
                    // 发送长度前缀
                    byte[] lengthBytes = BitConverter.GetBytes(messageData.Length);
                    await _stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);

                    // 发送消息数据
                    await _stream.WriteAsync(messageData, 0, messageData.Length);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task CloseAsync()
        {
            if (_isClosed)
                return;

            _isClosed = true;

            await _sendLock.WaitAsync();
            try
            {
                _client?.Close();
            }
            finally
            {
                _sendLock.Release();
            }

            _sendLock.Dispose();
        }
    }
}
