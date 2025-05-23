using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Client
{
    /// <summary>
    /// PulseRPC 客户端的基础实现
    /// </summary>
    public class PulseRPCClient : IPulseRPCClient
    {
        private readonly PulseRPCClientOptions _options;
        private readonly IPlatformAdapter _platformAdapter;
        private readonly ILogger<PulseRPCClient> _logger;
        private volatile bool _isConnected;
        private volatile bool _disposed;

        public bool IsConnected => _isConnected && !_disposed;

        public event PulseRPC.EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
        public event PulseRPC.EventHandler<ErrorEventArgs>? ErrorOccurred;

        private PulseRPCClient(PulseRPCClientOptions options, IPlatformAdapter platformAdapter)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _platformAdapter = platformAdapter ?? throw new ArgumentNullException(nameof(platformAdapter));
            _logger = _platformAdapter.CreateLogger<PulseRPCClient>();

            _options.Validate();
            _logger.LogInformation("PulseRPC Client initialized with server {Address}:{Port}",
                _options.ServerAddress, _options.ServerPort);
        }

        /// <summary>
        /// 创建 PulseRPC 客户端实例
        /// </summary>
        /// <param name="options">客户端配置选项</param>
        /// <returns>客户端实例</returns>
        public static async Task<IPulseRPCClient> CreateAsync(PulseRPCClientOptions options)
        {
            var platformAdapter = options.PlatformAdapter ?? PlatformAdapterFactory.CreateAdapter();
            var client = new PulseRPCClient(options, platformAdapter);

            // 根据配置进行初始化
            if (options.UseUnityOptimizations)
            {
                await client.InitializeUnityOptimizationsAsync();
            }

            return client;
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (_isConnected)
            {
                _logger.LogWarning("Client is already connected");
                return;
            }

            try
            {
                _logger.LogInformation("Connecting to server {Address}:{Port}", _options.ServerAddress, _options.ServerPort);

                // TODO: 实现实际的连接逻辑
                await SimulateConnectionAsync(cancellationToken);

                _isConnected = true;
                OnConnectionStateChanged(true);

                _logger.LogInformation("Successfully connected to server");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to server");
                OnErrorOccurred(ex, "Connection failed");
                throw;
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
            {
                return;
            }

            try
            {
                _logger.LogInformation("Disconnecting from server");

                // TODO: 实现实际的断开连接逻辑
                await SimulateDisconnectionAsync(cancellationToken);

                _isConnected = false;
                OnConnectionStateChanged(false);

                _logger.LogInformation("Successfully disconnected from server");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during disconnection");
                OnErrorOccurred(ex, "Disconnection error");
            }
        }

        public async Task<TResponse> CallAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class
        {
            ThrowIfDisposed();
            ThrowIfNotConnected();

            if (request == null) throw new ArgumentNullException(nameof(request));

            try
            {
                _logger.LogDebug("Sending RPC request of type {RequestType}", typeof(TRequest).Name);

                // TODO: 实现实际的 RPC 调用逻辑
                var response = await SimulateRpcCallAsync<TRequest, TResponse>(request, cancellationToken);

                _logger.LogDebug("Received RPC response of type {ResponseType}", typeof(TResponse).Name);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RPC call failed for request type {RequestType}", typeof(TRequest).Name);
                OnErrorOccurred(ex, $"RPC call failed: {typeof(TRequest).Name}");
                throw;
            }
        }

        public async Task SendAsync<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : class
        {
            ThrowIfDisposed();
            ThrowIfNotConnected();

            if (request == null) throw new ArgumentNullException(nameof(request));

            try
            {
                _logger.LogDebug("Sending one-way request of type {RequestType}", typeof(TRequest).Name);

                // TODO: 实现实际的单向发送逻辑
                await SimulateSendAsync(request, cancellationToken);

                _logger.LogDebug("Successfully sent one-way request of type {RequestType}", typeof(TRequest).Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send one-way request of type {RequestType}", typeof(TRequest).Name);
                OnErrorOccurred(ex, $"Send failed: {typeof(TRequest).Name}");
                throw;
            }
        }

        private async Task InitializeUnityOptimizationsAsync()
        {
            _logger.LogInformation("Initializing Unity optimizations");

            // Unity 特定的初始化逻辑
            _platformAdapter.ConfigureThreading();

            await Task.CompletedTask;
        }

        private async Task SimulateConnectionAsync(CancellationToken cancellationToken)
        {
            // 模拟连接过程
            await _platformAdapter.Delay(100, cancellationToken);
        }

        private async Task SimulateDisconnectionAsync(CancellationToken cancellationToken)
        {
            // 模拟断开连接过程
            await _platformAdapter.Delay(50, cancellationToken);
        }

        private async Task<TResponse> SimulateRpcCallAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken)
            where TRequest : class
            where TResponse : class
        {
            // 模拟 RPC 调用
            await _platformAdapter.Delay(10, cancellationToken);

            // 这里应该返回实际的响应，现在返回默认值作为占位符
            return Activator.CreateInstance<TResponse>();
        }

        private async Task SimulateSendAsync<TRequest>(TRequest request, CancellationToken cancellationToken)
            where TRequest : class
        {
            // 模拟发送过程
            await _platformAdapter.Delay(5, cancellationToken);
        }

        private void OnConnectionStateChanged(bool isConnected, string? disconnectReason = null)
        {
            var args = new ConnectionStateChangedEventArgs(isConnected, disconnectReason);

            if (_options.EnableUnityMainThreadDispatch && !_platformAdapter.IsMainThread())
            {
                _platformAdapter.InvokeOnMainThread(() => ConnectionStateChanged?.Invoke(this, args));
            }
            else
            {
                ConnectionStateChanged?.Invoke(this, args);
            }
        }

        private void OnErrorOccurred(Exception exception, string? context = null)
        {
            var args = new ErrorEventArgs(exception, context);

            if (_options.EnableUnityMainThreadDispatch && !_platformAdapter.IsMainThread())
            {
                _platformAdapter.InvokeOnMainThread(() => ErrorOccurred?.Invoke(this, args));
            }
            else
            {
                ErrorOccurred?.Invoke(this, args);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PulseRPCClient));
        }

        private void ThrowIfNotConnected()
        {
            if (!_isConnected)
                throw new InvalidOperationException("Client is not connected");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                if (_isConnected)
                {
                    DisconnectAsync().Wait(TimeSpan.FromSeconds(5));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during disposal");
            }

            _logger.LogInformation("PulseRPC Client disposed");
        }
    }
}
