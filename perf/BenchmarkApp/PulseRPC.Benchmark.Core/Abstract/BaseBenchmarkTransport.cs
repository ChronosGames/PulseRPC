using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Core.Interfaces;

namespace PulseRPC.Benchmark.Core.Abstract
{
    /// <summary>
    /// 基准测试传输层基础抽象类
    /// </summary>
    public abstract class BaseBenchmarkTransport : IBenchmarkTransport
    {
        private readonly ILogger _logger;
        private readonly object _statsLock = new();
        private TransportStatistics _statistics = new();
        private bool _disposed = false;
        private bool _isConnected = false;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="name">传输层名称</param>
        /// <param name="logger">日志记录器</param>
        protected BaseBenchmarkTransport(string name, ILogger logger)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public bool IsConnected
        {
            get => _isConnected;
            protected set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnConnectionStateChanged(value);
                }
            }
        }

        /// <inheritdoc />
        public event Action<bool>? ConnectionStateChanged;

        /// <inheritdoc />
        public event Action<Exception>? ErrorOccurred;

        /// <inheritdoc />
        public abstract Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

        /// <inheritdoc />
        public abstract Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <inheritdoc />
        public abstract Task<bool> SendAsync(byte[] data, CancellationToken cancellationToken = default);

        /// <inheritdoc />
        public abstract Task<byte[]?> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

        /// <inheritdoc />
        public virtual TransportStatistics GetStatistics()
        {
            lock (_statsLock)
            {
                // 返回统计信息的副本
                return new TransportStatistics
                {
                    TotalBytesSent = _statistics.TotalBytesSent,
                    TotalBytesReceived = _statistics.TotalBytesReceived,
                    TotalMessagesSent = _statistics.TotalMessagesSent,
                    TotalMessagesReceived = _statistics.TotalMessagesReceived,
                    LastSendTime = _statistics.LastSendTime,
                    LastReceiveTime = _statistics.LastReceiveTime,
                    ConnectTime = _statistics.ConnectTime,
                    ErrorCount = _statistics.ErrorCount,
                    LatencyMeasurements = new System.Collections.Generic.List<double>(_statistics.LatencyMeasurements)
                };
            }
        }

        /// <inheritdoc />
        public virtual void ResetStatistics()
        {
            lock (_statsLock)
            {
                _statistics = new TransportStatistics();
            }
        }

        /// <summary>
        /// 更新发送统计信息
        /// </summary>
        /// <param name="bytesCount">发送的字节数</param>
        protected void UpdateSendStatistics(int bytesCount)
        {
            lock (_statsLock)
            {
                _statistics.TotalBytesSent += bytesCount;
                _statistics.TotalMessagesSent++;
                _statistics.LastSendTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// 更新接收统计信息
        /// </summary>
        /// <param name="bytesCount">接收的字节数</param>
        protected void UpdateReceiveStatistics(int bytesCount)
        {
            lock (_statsLock)
            {
                _statistics.TotalBytesReceived += bytesCount;
                _statistics.TotalMessagesReceived++;
                _statistics.LastReceiveTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// 添加延迟测量值
        /// </summary>
        /// <param name="latencyMs">延迟毫秒数</param>
        protected void AddLatencyMeasurement(double latencyMs)
        {
            lock (_statsLock)
            {
                _statistics.LatencyMeasurements.Add(latencyMs);
                // 保持最近1000个测量值
                if (_statistics.LatencyMeasurements.Count > 1000)
                {
                    _statistics.LatencyMeasurements.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// 记录错误
        /// </summary>
        /// <param name="exception">异常信息</param>
        protected void RecordError(Exception exception)
        {
            lock (_statsLock)
            {
                _statistics.ErrorCount++;
            }

            _logger.LogError(exception, "传输层错误: {ErrorMessage}", exception.Message);
            OnErrorOccurred(exception);
        }

        /// <summary>
        /// 触发连接状态变化事件
        /// </summary>
        /// <param name="isConnected">是否已连接</param>
        protected virtual void OnConnectionStateChanged(bool isConnected)
        {
            ConnectionStateChanged?.Invoke(isConnected);
        }

        /// <summary>
        /// 触发错误发生事件
        /// </summary>
        /// <param name="exception">异常信息</param>
        protected virtual void OnErrorOccurred(Exception exception)
        {
            ErrorOccurred?.Invoke(exception);
        }

        /// <summary>
        /// 记录连接时间
        /// </summary>
        protected void RecordConnectTime()
        {
            lock (_statsLock)
            {
                _statistics.ConnectTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// 日志记录器
        /// </summary>
        protected ILogger Logger => _logger;

        /// <summary>
        /// 检查是否已释放
        /// </summary>
        protected bool IsDisposed => _disposed;

        /// <summary>
        /// 检查是否已释放
        /// </summary>
        protected void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        /// <summary>
        /// 释放资源的虚拟方法，供子类重写
        /// </summary>
        /// <param name="disposing">是否正在释放托管资源</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    if (IsConnected)
                    {
                        DisconnectAsync(CancellationToken.None).Wait(TimeSpan.FromSeconds(5));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "释放传输层时发生错误");
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_disposed)
            {
                Dispose(true);
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}
