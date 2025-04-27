using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace UnityTCP
{
    /// <summary>
    /// 适用于Unity的高性能TCP通信库，使用NativeAOT和现代IO
    /// </summary>
    public class TCPNetworkManager : MonoBehaviour
    {
        private TCPServer _server;
        private TCPClient _client;

        // 事件委托
        public delegate void OnMessageReceived(byte[] data);
        public delegate void OnClientConnected(string clientId);
        public delegate void OnClientDisconnected(string clientId);

        // 事件
        public event OnMessageReceived MessageReceived;
        public event OnClientConnected ClientConnected;
        public event OnClientDisconnected ClientDisconnected;

        // Unity生命周期方法
        private void Start()
        {
            Application.runInBackground = true;
        }

        private void OnDestroy()
        {
            StopServer();
            DisconnectClient();
        }

        // 服务器方法
        public async Task StartServer(int port)
        {
            if (_server != null)
            {
                Debug.LogWarning("Server is already running.");
                return;
            }

            _server = new TCPServer();
            _server.ClientConnected += (clientId) =>
            {
                ClientConnected?.Invoke(clientId);
            };

            _server.ClientDisconnected += (clientId) =>
            {
                ClientDisconnected?.Invoke(clientId);
            };

            _server.MessageReceived += (data) =>
            {
                MessageReceived?.Invoke(data);
            };

            await _server.StartAsync(port);
            Debug.Log($"Server started on port {port}");
        }

        public void StopServer()
        {
            _server?.Stop();
            _server = null;
            Debug.Log("Server stopped");
        }

        public async Task BroadcastToAll(byte[] data)
        {
            if (_server == null)
            {
                Debug.LogError("Server is not running.");
                return;
            }

            await _server.BroadcastAsync(data);
        }

        public async Task SendToClient(string clientId, byte[] data)
        {
            if (_server == null)
            {
                Debug.LogError("Server is not running.");
                return;
            }

            await _server.SendToClientAsync(clientId, data);
        }

        // 客户端方法
        public async Task ConnectToServer(string ip, int port)
        {
            if (_client != null)
            {
                Debug.LogWarning("Client is already connected.");
                return;
            }

            _client = new TCPClient();
            _client.MessageReceived += (data) =>
            {
                MessageReceived?.Invoke(data);
            };

            _client.Disconnected += () =>
            {
                Debug.Log("Disconnected from server");
                _client = null;
            };

            await _client.ConnectAsync(ip, port);
            Debug.Log($"Connected to server at {ip}:{port}");
        }

        public void DisconnectClient()
        {
            _client?.Disconnect();
            _client = null;
            Debug.Log("Disconnected from server");
        }

        public async Task SendToServer(byte[] data)
        {
            if (_client == null)
            {
                Debug.LogError("Client is not connected.");
                return;
            }

            await _client.SendAsync(data);
        }
    }

    /// <summary>
    /// TCP服务器实现，使用管道和高性能IO
    /// </summary>
    public class TCPServer
    {
        private TcpListener _listener;
        private bool _isRunning;
        private CancellationTokenSource _cts;
        private readonly ConcurrentDictionary<string, ClientConnection> _clients = new();

        // 事件
        public event Action<string> ClientConnected;
        public event Action<string> ClientDisconnected;
        public event Action<byte[]> MessageReceived;

        public async Task StartAsync(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _isRunning = true;
            _cts = new CancellationTokenSource();

            try
            {
                while (_isRunning && !_cts.Token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = HandleClientAsync(client);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                Debug.LogError($"Server error: {ex.Message}");
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _cts?.Cancel();
            _listener?.Stop();

            foreach (var client in _clients.Values)
            {
                client.Close();
            }

            _clients.Clear();
        }

        public async Task BroadcastAsync(byte[] data)
        {
            var tasks = new List<Task>();
            foreach (var client in _clients.Values)
            {
                tasks.Add(client.SendAsync(data));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public async Task SendToClientAsync(string clientId, byte[] data)
        {
            if (_clients.TryGetValue(clientId, out var client))
            {
                await client.SendAsync(data).ConfigureAwait(false);
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            var clientId = Guid.NewGuid().ToString();
            var connection = new ClientConnection(client);

            _clients[clientId] = connection;
            ClientConnected?.Invoke(clientId);

            try
            {
                await connection.ProcessMessagesAsync(data =>
                {
                    MessageReceived?.Invoke(data);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing client {clientId}: {ex.Message}");
            }
            finally
            {
                _clients.TryRemove(clientId, out _);
                connection.Close();
                ClientDisconnected?.Invoke(clientId);
            }
        }

        private class ClientConnection
        {
            private readonly TcpClient _client;
            private readonly NetworkStream _stream;
            private readonly PipeReader _reader;
            private readonly PipeWriter _writer;

            public ClientConnection(TcpClient client)
            {
                _client = client;
                _stream = client.GetStream();
                _reader = PipeReader.Create(_stream);
                _writer = PipeWriter.Create(_stream);
            }

            public async Task ProcessMessagesAsync(Action<byte[]> messageHandler)
            {
                try
                {
                    while (true)
                    {
                        var result = await _reader.ReadAsync().ConfigureAwait(false);
                        var buffer = result.Buffer;

                        while (TryReadMessage(ref buffer, out var message))
                        {
                            messageHandler(message);
                        }

                        _reader.AdvanceTo(buffer.Start, buffer.End);

                        if (result.IsCompleted)
                        {
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    // 连接关闭
                }
                finally
                {
                    await _reader.CompleteAsync().ConfigureAwait(false);
                }
            }

            // 简单的消息格式: [4字节长度前缀][消息内容]
            private bool TryReadMessage(ref ReadOnlySequence<byte> buffer, out byte[] message)
            {
                message = null;

                if (buffer.Length < 4)
                {
                    return false;
                }

                int length = BitConverter.ToInt32(buffer.Slice(0, 4).ToArray());

                if (buffer.Length < length + 4)
                {
                    return false;
                }

                message = buffer.Slice(4, length).ToArray();
                buffer = buffer.Slice(length + 4);
                return true;
            }

            public async Task SendAsync(byte[] data)
            {
                var lengthPrefix = BitConverter.GetBytes(data.Length);
                await _writer.WriteAsync(lengthPrefix).ConfigureAwait(false);
                await _writer.WriteAsync(data).ConfigureAwait(false);
                await _writer.FlushAsync().ConfigureAwait(false);
            }

            public void Close()
            {
                _reader.Complete();
                _writer.Complete();
                _client.Close();
            }
        }
    }

    /// <summary>
    /// TCP客户端实现，使用管道和高性能IO
    /// </summary>
    public class TCPClient
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private PipeReader _reader;
        private PipeWriter _writer;
        private CancellationTokenSource _cts;
        private Task _receiveTask;

        // 事件
        public event Action<byte[]> MessageReceived;
        public event Action Disconnected;

        public async Task ConnectAsync(string ip, int port)
        {
            _client = new TcpClient();
            await _client.ConnectAsync(ip, port).ConfigureAwait(false);

            _stream = _client.GetStream();
            _reader = PipeReader.Create(_stream);
            _writer = PipeWriter.Create(_stream);
            _cts = new CancellationTokenSource();

            _receiveTask = ReceiveMessagesAsync();
        }

        public void Disconnect()
        {
            _cts?.Cancel();
            _reader?.Complete();
            _writer?.Complete();
            _client?.Close();
            _client = null;
        }

        public async Task SendAsync(byte[] data)
        {
            if (_client == null || !_client.Connected)
            {
                throw new InvalidOperationException("Client is not connected.");
            }

            var lengthPrefix = BitConverter.GetBytes(data.Length);
            await _writer.WriteAsync(lengthPrefix).ConfigureAwait(false);
            await _writer.WriteAsync(data).ConfigureAwait(false);
            await _writer.FlushAsync().ConfigureAwait(false);
        }

        private async Task ReceiveMessagesAsync()
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var result = await _reader.ReadAsync(_cts.Token).ConfigureAwait(false);
                    var buffer = result.Buffer;

                    while (TryReadMessage(ref buffer, out var message))
                    {
                        MessageReceived?.Invoke(message);
                    }

                    _reader.AdvanceTo(buffer.Start, buffer.End);

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception)
            {
                // 连接已关闭
            }
            finally
            {
                Disconnect();
                Disconnected?.Invoke();
            }
        }

        // 简单的消息格式: [4字节长度前缀][消息内容]
        private bool TryReadMessage(ref ReadOnlySequence<byte> buffer, out byte[] message)
        {
            message = null;

            if (buffer.Length < 4)
            {
                return false;
            }

            int length = BitConverter.ToInt32(buffer.Slice(0, 4).ToArray());

            if (buffer.Length < length + 4)
            {
                return false;
            }

            message = buffer.Slice(4, length).ToArray();
            buffer = buffer.Slice(length + 4);
            return true;
        }
    }
}