using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using PulseRPC.Client;

#nullable enable

namespace PulseRPC.Client.Unity
{
    /// <summary>
    /// Unity MonoBehaviour 组件，提供 Unity 生命周期管理的客户端包装
    /// </summary>
    public class UnityPulseRPCClientComponent : MonoBehaviour
    {
        [SerializeField] private string serverAddress = "localhost";
        [SerializeField] private int serverPort = 12345;
        [SerializeField] private bool autoConnectOnStart = true;
        [SerializeField] private bool autoDisconnectOnDestroy = true;
        [SerializeField] private bool enableDebugLogging = true;

        private IPulseRPCClient? _client;
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>
        /// 当前客户端实例
        /// </summary>
        public IPulseRPCClient? Client => _client;

        /// <summary>
        /// 客户端是否已连接
        /// </summary>
        public bool IsConnected => _client?.IsConnected ?? false;

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

        /// <summary>
        /// 错误发生事件
        /// </summary>
        public event EventHandler<ErrorEventArgs>? ErrorOccurred;

        private void Start()
        {
            if (autoConnectOnStart && _client == null)
            {
                _ = InitializeAndConnectAsync();
            }
        }

        private void OnDestroy()
        {
            if (autoDisconnectOnDestroy)
            {
                DisconnectAndCleanup();
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                // 应用暂停时断开连接
                _ = DisconnectAsync();
            }
            else
            {
                // 应用恢复时重新连接
                if (_client != null && !_client.IsConnected)
                {
                    _ = ConnectAsync();
                }
            }
        }

        /// <summary>
        /// 使用外部创建的客户端实例初始化组件
        /// </summary>
        /// <param name="client">客户端实例</param>
        public void Initialize(IPulseRPCClient client)
        {
            if (_client != null)
            {
                Debug.LogWarning("[PulseRPC] Client already initialized, disposing previous instance");
                DisconnectAndCleanup();
            }

            _client = client ?? throw new ArgumentNullException(nameof(client));
            _cancellationTokenSource = new CancellationTokenSource();

            SubscribeToClientEvents();

            if (enableDebugLogging)
            {
                Debug.Log("[PulseRPC] Client component initialized");
            }
        }

        /// <summary>
        /// 使用默认配置初始化并连接客户端
        /// </summary>
        public async Task InitializeAndConnectAsync()
        {
            try
            {
                if (_client != null)
                {
                    Debug.LogWarning("[PulseRPC] Client already initialized");
                    return;
                }

                if (enableDebugLogging)
                {
                    Debug.Log($"[PulseRPC] Initializing client for {serverAddress}:{serverPort}");
                }

                _client = await UnityPulseRPCClientFactory.CreateClientAsync(serverAddress, serverPort);
                _cancellationTokenSource = new CancellationTokenSource();

                SubscribeToClientEvents();

                await ConnectAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PulseRPC] Failed to initialize and connect client: {ex.Message}");
                OnErrorOccurred(new ErrorEventArgs(ex, "Initialization failed"));
            }
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async Task ConnectAsync()
        {
            if (_client == null)
            {
                Debug.LogError("[PulseRPC] Client not initialized");
                return;
            }

            if (_client.IsConnected)
            {
                Debug.LogWarning("[PulseRPC] Client already connected");
                return;
            }

            try
            {
                if (enableDebugLogging)
                {
                    Debug.Log("[PulseRPC] Connecting to server...");
                }

                await _client.ConnectAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PulseRPC] Connection failed: {ex.Message}");
                OnErrorOccurred(new ErrorEventArgs(ex, "Connection failed"));
            }
        }

        /// <summary>
        /// 断开与服务器的连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_client == null || !_client.IsConnected)
            {
                return;
            }

            try
            {
                if (enableDebugLogging)
                {
                    Debug.Log("[PulseRPC] Disconnecting from server...");
                }

                await _client.DisconnectAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PulseRPC] Disconnection failed: {ex.Message}");
                OnErrorOccurred(new ErrorEventArgs(ex, "Disconnection failed"));
            }
        }

        /// <summary>
        /// 发送 RPC 请求
        /// </summary>
        /// <typeparam name="TRequest">请求类型</typeparam>
        /// <typeparam name="TResponse">响应类型</typeparam>
        /// <param name="request">请求对象</param>
        /// <returns>响应对象</returns>
        public async Task<TResponse> CallAsync<TRequest, TResponse>(TRequest request)
            where TRequest : class
            where TResponse : class
        {
            if (_client == null)
            {
                throw new InvalidOperationException("Client not initialized");
            }

            return await _client.CallAsync<TRequest, TResponse>(request, _cancellationTokenSource?.Token ?? CancellationToken.None);
        }

        /// <summary>
        /// 发送单向请求
        /// </summary>
        /// <typeparam name="TRequest">请求类型</typeparam>
        /// <param name="request">请求对象</param>
        public async Task SendAsync<TRequest>(TRequest request)
            where TRequest : class
        {
            if (_client == null)
            {
                throw new InvalidOperationException("Client not initialized");
            }

            await _client.SendAsync(request, _cancellationTokenSource?.Token ?? CancellationToken.None);
        }

        private void SubscribeToClientEvents()
        {
            if (_client == null)
                return;

            _client.ConnectionStateChanged += OnClientConnectionStateChanged;
            _client.ErrorOccurred += OnClientErrorOccurred;
        }

        private void UnsubscribeFromClientEvents()
        {
            if (_client == null)
                return;

            _client.ConnectionStateChanged -= OnClientConnectionStateChanged;
            _client.ErrorOccurred -= OnClientErrorOccurred;
        }

        private void OnClientConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
        {
            if (enableDebugLogging)
            {
                Debug.Log($"[PulseRPC] Connection state changed: {(e.IsConnected ? "Connected" : "Disconnected")}");
                if (!e.IsConnected && !string.IsNullOrEmpty(e.DisconnectReason))
                {
                    Debug.Log($"[PulseRPC] Disconnect reason: {e.DisconnectReason}");
                }
            }

            // 确保在主线程上触发事件
            if (!Application.isPlaying)
                return;

            ConnectionStateChanged?.Invoke(this, e);
        }

        private void OnClientErrorOccurred(object? sender, ErrorEventArgs e)
        {
            Debug.LogError($"[PulseRPC] Client error: {e.Exception.Message}");
            if (!string.IsNullOrEmpty(e.Context))
            {
                Debug.LogError($"[PulseRPC] Error context: {e.Context}");
            }

            // 确保在主线程上触发事件
            if (!Application.isPlaying)
                return;

            OnErrorOccurred(e);
        }

        private void OnErrorOccurred(ErrorEventArgs e)
        {
            ErrorOccurred?.Invoke(this, e);
        }

        private void DisconnectAndCleanup()
        {
            try
            {
                _cancellationTokenSource?.Cancel();

                if (_client != null)
                {
                    UnsubscribeFromClientEvents();

                    if (_client.IsConnected)
                    {
                        // 同步等待断开连接，因为在 OnDestroy 中不能使用 async
                        _client.DisconnectAsync().Wait(TimeSpan.FromSeconds(2));
                    }

                    _client.Dispose();
                    _client = null;
                }

                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                if (enableDebugLogging)
                {
                    Debug.Log("[PulseRPC] Client component cleaned up");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PulseRPC] Error during cleanup: {ex.Message}");
            }
        }

        // Unity Inspector 中的配置方法
        [ContextMenu("Connect")]
        private void MenuConnect()
        {
            if (Application.isPlaying)
            {
                _ = ConnectAsync();
            }
        }

        [ContextMenu("Disconnect")]
        private void MenuDisconnect()
        {
            if (Application.isPlaying)
            {
                _ = DisconnectAsync();
            }
        }

        [ContextMenu("Initialize and Connect")]
        private void MenuInitializeAndConnect()
        {
            if (Application.isPlaying)
            {
                _ = InitializeAndConnectAsync();
            }
        }
    }
}
