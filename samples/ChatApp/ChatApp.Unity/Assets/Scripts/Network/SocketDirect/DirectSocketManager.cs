using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace UnityTCP.SocketDirect
{
    /// <summary>
    /// 直接Socket网络优化，跳过所有中间层，实现最低延迟
    /// </summary>
    public class DirectSocketManager : IDisposable
    {
        private Socket _socket;
        private readonly byte[] _sendBuffer;
        private readonly byte[] _receiveBuffer;
        private readonly object _sendLock = new object();
        private readonly ManualResetEventSlim _connectEvent = new ManualResetEventSlim(false);
        private readonly List<byte[]> _bufferPool = new List<byte[]>();
        private readonly Action<byte[], int> _dataReceivedCallback;
        private bool _isConnected;
        private bool _isListening;
        private bool _isDisposed;

        public bool IsConnected => _isConnected;

        /// <summary>
        /// 创建直接Socket管理器
        /// </summary>
        /// <param name="sendBufferSize">发送缓冲区大小</param>
        /// <param name="receiveBufferSize">接收缓冲区大小</param>
        /// <param name="dataReceivedCallback">数据接收回调</param>
        public DirectSocketManager(int sendBufferSize = 65536, int receiveBufferSize = 65536,
            Action<byte[], int> dataReceivedCallback = null)
        {
            _sendBuffer = new byte[sendBufferSize];
            _receiveBuffer = new byte[receiveBufferSize];
            _dataReceivedCallback = dataReceivedCallback;

            // 预分配缓冲区
            for (int i = 0; i < 10; i++)
            {
                _bufferPool.Add(new byte[4096]);
            }
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public bool Connect(string host, int port, int timeoutMs = 5000)
        {
            if (_isConnected)
                return true;

            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                OptimizeSocket(_socket);

                _connectEvent.Reset();

                var connectArgs = new SocketAsyncEventArgs();
                connectArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Parse(host), port);
                connectArgs.Completed += (s, e) => _connectEvent.Set();

                if (_socket.ConnectAsync(connectArgs))
                {
                    // 等待连接完成
                    if (!_connectEvent.Wait(timeoutMs))
                    {
                        CloseSocket();
                        return false;
                    }
                }

                if (connectArgs.SocketError != SocketError.Success)
                {
                    CloseSocket();
                    return false;
                }

                _isConnected = true;
                StartReceiving();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Connection error: {ex.Message}");
                CloseSocket();
                return false;
            }
        }

        /// <summary>
        /// 开始服务器监听
        /// </summary>
        public bool StartListening(int port, int backlog = 10)
        {
            if (_isListening)
                return true;

            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                OptimizeSocket(_socket);

                // 允许地址重用
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                _socket.Bind(new IPEndPoint(IPAddress.Any, port));
                _socket.Listen(backlog);

                _isListening = true;
                AcceptConnections();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Listen error: {ex.Message}");
                CloseSocket();
                return false;
            }
        }

        /// <summary>
        /// 接受客户端连接
        /// </summary>
        private async void AcceptConnections()
        {
            while (_isListening && !_isDisposed)
            {
                try
                {
                    var acceptArgs = new SocketAsyncEventArgs();
                    var tcs = new TaskCompletionSource<Socket>();

                    acceptArgs.Completed += (s, e) =>
                    {
                        if (e.SocketError == SocketError.Success)
                            tcs.SetResult(e.AcceptSocket);
                        else
                            tcs.SetException(new SocketException((int)e.SocketError));
                    };

                    if (_socket.AcceptAsync(acceptArgs))
                    {
                        var clientSocket = await tcs.Task;
                        HandleNewClient(clientSocket);
                    }
                    else
                    {
                        if (acceptArgs.SocketError == SocketError.Success)
                            HandleNewClient(acceptArgs.AcceptSocket);
                    }
                }
                catch (Exception ex)
                {
                    if (!_isDisposed)
                        Debug.LogError($"Accept error: {ex.Message}");

                    await Task.Delay(100); // 避免CPU占用过高
                }
            }
        }

        /// <summary>
        /// 处理新客户端连接
        /// </summary>
        private void HandleNewClient(Socket clientSocket)
        {
            // 这里应该创建一个客户端会话实例
            // 在实际应用中，这里会处理多客户端连接

            OptimizeSocket(clientSocket);
            _socket = clientSocket;
            _isConnected = true;
            StartReceiving();
        }

        /// <summary>
        /// 开始接收数据
        /// </summary>
        private void StartReceiving()
        {
            if (_isDisposed) return;

            try
            {
                var receiveArgs = new SocketAsyncEventArgs();
                receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
                receiveArgs.Completed += OnReceiveCompleted;

                if (!_socket.ReceiveAsync(receiveArgs))
                {
                    OnReceiveCompleted(null, receiveArgs);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Receive setup error: {ex.Message}");
                CloseSocket();
            }
        }

        /// <summary>
        /// 接收完成回调
        /// </summary>
        private void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success && e.BytesTransferred > 0)
            {
                // 处理接收到的数据
                if (_dataReceivedCallback != null)
                {
                    _dataReceivedCallback(_receiveBuffer, e.BytesTransferred);
                }

                // 继续接收
                try
                {
                    if (!_socket.ReceiveAsync(e))
                    {
                        OnReceiveCompleted(null, e);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Receive error: {ex.Message}");
                    CloseSocket();
                }
            }
            else
            {
                // 连接已关闭或出错
                CloseSocket();
            }
        }

        /// <summary>
        /// 优化Socket配置
        /// </summary>
        private void OptimizeSocket(Socket socket)
        {
            try
            {
                // 禁用Nagle算法
                socket.NoDelay = true;

                // 设置发送和接收缓冲区
                socket.SendBufferSize = _sendBuffer.Length;
                socket.ReceiveBufferSize = _receiveBuffer.Length;

                // 禁用延迟发送
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, false);
                socket.LingerState = new LingerOption(true, 0);

                // 启用保活
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                // 尝试启用零拷贝（Linux）
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    try
                    {
                        const int SO_ZEROCOPY = 60;
                        socket.SetSocketOption(SocketOptionLevel.Socket, (SocketOptionName)SO_ZEROCOPY, 1);
                    }
                    catch
                    {
                        // 忽略不支持的错误
                    }
                }

                // 尝试设置为高优先级流量
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.TypeOfService, 0x10);
                }
                catch
                {
                    // 忽略不支持的错误
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Socket optimization error: {ex.Message}");
            }
        }

        /// <summary>
        /// 直接发送C#结构体（零拷贝）
        /// </summary>
        public unsafe bool SendStruct<T>(T data) where T : unmanaged
        {
            if (!_isConnected || _isDisposed)
                return false;

            int size = sizeof(T);

            lock (_sendLock)
            {
                try
                {
                    // 写入长度前缀
                    BitConverter.GetBytes(size).CopyTo(_sendBuffer, 0);

                    // 直接将结构体写入发送缓冲区
                    fixed (byte* destPtr = &_sendBuffer[sizeof(int)])
                    {
                        *(T*)destPtr = data;
                    }

                    // 发送数据
                    _socket.Send(_sendBuffer, 0, size + sizeof(int), SocketFlags.None);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Send error: {ex.Message}");
                    CloseSocket();
                    return false;
                }
            }
        }

        /// <summary>
        /// 直接从NativeArray发送（零拷贝）
        /// </summary>
        public unsafe bool SendNativeArray<T>(NativeArray<T> array) where T : unmanaged
        {
            if (!_isConnected || _isDisposed || !array.IsCreated)
                return false;

            int byteSize = array.Length * sizeof(T);

            lock (_sendLock)
            {
                try
                {
                    // 写入长度前缀
                    BitConverter.GetBytes(byteSize).CopyTo(_sendBuffer, 0);

                    // 直接从NativeArray复制到发送缓冲区
                    void* srcPtr = NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(array);

                    fixed (byte* destPtr = &_sendBuffer[sizeof(int)])
                    {
                        UnsafeUtility.MemCpy(destPtr, srcPtr, byteSize);
                    }

                    // 发送数据
                    _socket.Send(_sendBuffer, 0, byteSize + sizeof(int), SocketFlags.None);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Send error: {ex.Message}");
                    CloseSocket();
                    return false;
                }
            }
        }

        /// <summary>
        /// 使用固定GCHandle直接发送对象（零拷贝）
        /// </summary>
        public unsafe bool SendWithFixedObject<T>(T obj) where T : unmanaged
        {
            if (!_isConnected || _isDisposed)
                return false;

            int size = sizeof(T);

            // 固定对象在内存中
            GCHandle handle = GCHandle.Alloc(obj, GCHandleType.Pinned);

            try
            {
                lock (_sendLock)
                {
                    // 获取固定对象的指针
                    IntPtr ptr = handle.AddrOfPinnedObject();

                    // 写入长度前缀
                    BitConverter.GetBytes(size).CopyTo(_sendBuffer, 0);

                    // 直接从对象内存复制到发送缓冲区
                    Marshal.Copy(ptr, _sendBuffer, sizeof(int), size);

                    // 发送数据
                    _socket.Send(_sendBuffer, 0, size + sizeof(int), SocketFlags.None);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Send error: {ex.Message}");
                CloseSocket();
                return false;
            }
            finally
            {
                // 释放固定的对象
                handle.Free();
            }
        }

        /// <summary>
        /// 关闭Socket连接
        /// </summary>
        private void CloseSocket()
        {
            if (_socket != null)
            {
                try
                {
                    _socket.Shutdown(SocketShutdown.Both);
                }
                catch
                {
                }

                try
                {
                    _socket.Close();
                }
                catch
                {
                }

                _socket = null;
            }

            _isConnected = false;
            _isListening = false;
        }

        /// <summary>
        /// 从缓冲区池获取一个缓冲区
        /// </summary>
        private byte[] GetBufferFromPool()
        {
            lock (_bufferPool)
            {
                if (_bufferPool.Count > 0)
                {
                    byte[] buffer = _bufferPool[_bufferPool.Count - 1];
                    _bufferPool.RemoveAt(_bufferPool.Count - 1);
                    return buffer;
                }
            }

            return new byte[4096]; // 默认大小
        }

        /// <summary>
        /// 归还缓冲区到池
        /// </summary>
        private void ReturnBufferToPool(byte[] buffer)
        {
            if (buffer == null || buffer.Length != 4096)
                return;

            lock (_bufferPool)
            {
                // 限制池大小，避免内存泄漏
                if (_bufferPool.Count < 20)
                {
                    Array.Clear(buffer, 0, buffer.Length);
                    _bufferPool.Add(buffer);
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            CloseSocket();
            _connectEvent.Dispose();

            lock (_bufferPool)
            {
                _bufferPool.Clear();
            }
        }
    }

    /// <summary>
    /// 直接Socket网络的Unity组件
    /// </summary>
    public class DirectSocketNetworking : MonoBehaviour
    {
        [SerializeField] private bool isServer = false;
        [SerializeField] private string serverIp = "127.0.0.1";
        [SerializeField] private int serverPort = 7777;
        [SerializeField] private int bufferSize = 65536;

        private DirectSocketManager _socketManager;
        private Queue<Action> _mainThreadActions = new Queue<Action>();

        // 对象池，用于减少GC压力
        private readonly Queue<byte[]> _messagePool = new Queue<byte[]>();
        private readonly object _poolLock = new object();

        // 事件
        public event Action<byte[]> MessageReceived;
        public event Action Connected;
        public event Action Disconnected;

        private void Awake()
        {
            // 预分配消息池
            for (int i = 0; i < 20; i++)
            {
                _messagePool.Enqueue(new byte[1024]);
            }
        }

        private void Start()
        {
            // 创建Socket管理器
            _socketManager = new DirectSocketManager(
                bufferSize,
                bufferSize,
                OnDataReceived);

            if (isServer)
            {
                // 启动服务器
                if (_socketManager.StartListening(serverPort))
                {
                    Debug.Log($"Server started on port {serverPort}");
                }
                else
                {
                    Debug.LogError("Failed to start server");
                }
            }
            else
            {
                // 连接到服务器
                ConnectToServer();
            }
        }

        private void Update()
        {
            // 处理主线程回调
            lock (_mainThreadActions)
            {
                while (_mainThreadActions.Count > 0)
                {
                    _mainThreadActions.Dequeue()?.Invoke();
                }
            }
        }

        private void OnDestroy()
        {
            _socketManager?.Dispose();

            lock (_poolLock)
            {
                _messagePool.Clear();
            }
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public void ConnectToServer()
        {
            if (_socketManager.Connect(serverIp, serverPort))
            {
                Debug.Log($"Connected to server: {serverIp}:{serverPort}");

                lock (_mainThreadActions)
                {
                    _mainThreadActions.Enqueue(() => Connected?.Invoke());
                }
            }
            else
            {
                Debug.LogError("Failed to connect to server");
            }
        }

        /// <summary>
        /// 处理接收到的数据
        /// </summary>
        private void OnDataReceived(byte[] data, int length)
        {
            if (length <= 0)
                return;

            // 从池获取消息缓冲区
            byte[] message = null;

            lock (_poolLock)
            {
                if (_messagePool.Count > 0)
                {
                    message = _messagePool.Dequeue();
                }
                else
                {
                    message = new byte[Math.Max(length, 1024)];
                }
            }

            // 复制接收到的数据
            Array.Copy(data, 0, message, 0, length);

            // 调度到主线程处理
            lock (_mainThreadActions)
            {
                _mainThreadActions.Enqueue(() =>
                {
                    try
                    {
                        MessageReceived?.Invoke(message);
                    }
                    finally
                    {
                        // 归还消息缓冲区
                        ReturnMessageToPool(message);
                    }
                });
            }
        }

        /// <summary>
        /// 将消息缓冲区归还到池
        /// </summary>
        private void ReturnMessageToPool(byte[] message)
        {
            if (message == null)
                return;

            lock (_poolLock)
            {
                // 限制池大小，避免内存泄漏
                if (_messagePool.Count < 50)
                {
                    Array.Clear(message, 0, message.Length);
                    _messagePool.Enqueue(message);
                }
            }
        }

        /// <summary>
        /// 发送结构体数据
        /// </summary>
        public bool SendStruct<T>(T data) where T : unmanaged
        {
            if (_socketManager == null || !_socketManager.IsConnected)
                return false;

            return _socketManager.SendStruct(data);
        }

        /// <summary>
        /// 发送NativeArray数据
        /// </summary>
        public bool SendNativeArray<T>(NativeArray<T> array) where T : unmanaged
        {
            if (_socketManager == null || !_socketManager.IsConnected)
                return false;

            return _socketManager.SendNativeArray(array);
        }
    }

    /// <summary>
    /// 基于DMA的网络优化（概念性代码）
    /// </summary>
    public class DMANetworkAccess
    {
        // 注意：此类是概念性的，实际实现需要使用本机代码
        // 真实的DMA（直接内存访问）需要操作系统和硬件支持

        /// <summary>
        /// 使用DMA进行内存复制
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void DMAMemoryCopy(void* source, void* destination, int length)
        {
            // 在真实实现中，这里会调用本机API来设置DMA传输
            // 例如Windows中的DeviceIoControl或Linux中的mmap

            // 模拟DMA复制
            UnsafeUtility.MemCpy(destination, source, length);
        }

        /// <summary>
        /// 分配DMA可访问的内存
        /// </summary>
        public static unsafe IntPtr AllocateDMAMemory(int size)
        {
            // 在真实实现中，这会分配页对齐的、非分页的物理内存
            // 通常使用VirtualAlloc（Windows）或mmap（Linux）

            // 模拟分配
            void* ptr = UnsafeUtility.Malloc(size, 16, Allocator.Persistent);
            return new IntPtr(ptr);
        }

        /// <summary>
        /// 释放DMA内存
        /// </summary>
        public static unsafe void FreeDMAMemory(IntPtr ptr)
        {
            // 释放之前分配的DMA内存
            UnsafeUtility.Free(ptr.ToPointer(), Allocator.Persistent);
        }
    }
}