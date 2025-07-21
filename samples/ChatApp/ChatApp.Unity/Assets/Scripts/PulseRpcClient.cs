using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Client;
using PulseRPC.Transport;
using UnityEngine;

namespace ChatApp.Unity
{
    /// <summary>
    /// Unity中使用PulseRPC客户端的示例
    /// </summary>
    public class PulseRpcClient : MonoBehaviour
    {
        [SerializeField] private string serverHost = "localhost";
        [SerializeField] private int tcpPort = 7000;
        [SerializeField] private int kcpPort = 7001;
        [SerializeField] private bool useKcp = true;
        
        private IPulseRpcClient? _client;
        private IPlayerHub? _playerHub;
        private ILoggerFactory? _loggerFactory;
        private bool _isConnected = false;

        private async void Start()
        {
            // 创建日志工厂（Unity环境）
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(new UnityLoggerProvider());
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // 创建客户端实例 - 使用双通道配置
            _client = PulseRpcClientFactory.CreateClient(builder =>
            {
                builder.WithLogger(_loggerFactory)
                       .WithOptions(options =>
                       {
                           // 使用现有的ClientOptions属性
                           options.ConnectionTimeout = TimeSpan.FromSeconds(10);
                           options.AutoReconnect = true;
                       })
                       // TCP通道用于可靠通信
                       .AddTcp("reliable", serverHost, tcpPort, options =>
                       {
                           options.NoDelay = true;
                       }, isDefault: !useKcp)
                       // KCP通道用于低延迟游戏数据
                       .AddKcp("gaming", serverHost, kcpPort, options =>
                       {
                           options.Kcp = new KcpOptions
                           {
                               NoDelay = 1,
                               Interval = 10,
                               Resend = 2,
                               DisableFlowControl = true
                           };
                       }, isDefault: useKcp);
            });

            Debug.Log("PulseRPC客户端已创建");
            
            // 自动连接
            await ConnectAsync();
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async Task ConnectAsync()
        {
            if (_client == null || _isConnected) return;

            try
            {
                Debug.Log("正在连接到服务器...");
                await _client.ConnectAsync();
                
                // 获取服务代理
                _playerHub = _client.GetService<IPlayerHub>();
                
                _isConnected = true;
                Debug.Log("成功连接到服务器");
                
                // 触发连接成功事件
                OnConnected?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"连接服务器失败: {ex.Message}");
                OnConnectionFailed?.Invoke(ex.Message);
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_client == null || !_isConnected) return;

            try
            {
                Debug.Log("正在断开连接...");
                await _client.DisconnectAsync();
                _isConnected = false;
                _playerHub = null;
                
                Debug.Log("已断开连接");
                OnDisconnected?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"断开连接时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送Ping消息
        /// </summary>
        public async Task<bool> SendPingAsync(string message)
        {
            if (_playerHub == null || !_isConnected)
            {
                Debug.LogWarning("客户端未连接或服务代理不可用");
                return false;
            }

            try
            {
                var request = new PingRequest
                {
                    Message = message
                };

                var response = await _playerHub.PingAsync(request);
                Debug.Log($"Ping响应: {response}");
                OnMessageSent?.Invoke(message);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"发送Ping消息时出错: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 更新玩家位置（使用KCP通道）
        /// </summary>
        public async Task UpdatePlayerPositionAsync(Vector3 position, Quaternion rotation)
        {
            if (_playerHub == null || !_isConnected) return;

            try
            {
                var request = new MoveRequest
                {
                    X = position.x,
                    Y = position.y,
                    Z = position.z,
                    RotationY = rotation.eulerAngles.y
                };

                // 这里使用KCP通道进行低延迟位置更新
                await _playerHub.MoveAsync(request);
            }
            catch (Exception ex)
            {
                Debug.LogError($"更新位置时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 登录游戏
        /// </summary>
        public async Task<bool> LoginAsync(string username, string password)
        {
            if (_playerHub == null || !_isConnected)
                return false;

            try
            {
                var request = new LoginRequest
                {
                    Username = username,
                    Password = password
                };

                var response = await _playerHub.LoginAsync(request);
                if (response.Success)
                {
                    Debug.Log($"登录成功: {response.Player?.Username}");
                    return true;
                }
                else
                {
                    Debug.LogWarning($"登录失败: {response.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"登录时出错: {ex.Message}");
                return false;
            }
        }

        private async void OnDestroy()
        {
            await DisconnectAsync();
            _client?.Dispose();
            _loggerFactory?.Dispose();
        }

        // Unity事件
        public event Action? OnConnected;
        public event Action<string>? OnConnectionFailed;
        public event Action? OnDisconnected;
        public event Action<string>? OnMessageSent;
    }

    /// <summary>
    /// Unity日志提供程序
    /// </summary>
    public class UnityLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new UnityLogger(categoryName);
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Unity日志记录器
    /// </summary>
    public class UnityLogger : ILogger
    {
        private readonly string _categoryName;

        public UnityLogger(string categoryName)
        {
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Information;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = $"[{_categoryName}] {formatter(state, exception)}";
            
            switch (logLevel)
            {
                case LogLevel.Error:
                case LogLevel.Critical:
                    Debug.LogError(message);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(message);
                    break;
                default:
                    Debug.Log(message);
                    break;
            }

            if (exception != null)
            {
                Debug.LogException(exception);
            }
        }
    }
}