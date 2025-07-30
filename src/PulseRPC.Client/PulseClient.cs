using PulseRPC.Transport;
using Microsoft.Extensions.Logging;
using System;

namespace PulseRPC.Client;

// 客户端项目中的具体实现，继承抽象接口
// IPulseRpcClient接口已在PulseRPC.Abstractions中定义

/// <summary>
/// PulseRPC 客户端实现
/// </summary>
internal class PulseRpcClientManager : global::PulseRPC.IPulseClient
{
    private readonly IChannelManager _channelManager;
    private readonly ILogger<PulseRpcClientManager> _logger;
    private readonly Dictionary<string, ClientTransportInfo> _transports = new();
    private bool _isConnected;
    private bool _disposed;

    public PulseRpcClientManager(IChannelManager channelManager, ILoggerFactory loggerFactory)
    {
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _logger = loggerFactory.CreateLogger<PulseRpcClientManager>();
    }

    /// <summary>
    /// 是否已连接
    /// </summary>
    public bool IsConnected => _isConnected;

    /// <summary>
    /// 添加传输配置
    /// </summary>
    internal void AddTransport(ClientTransportConfiguration config)
    {
        if (_isConnected)
        {
            throw new InvalidOperationException("客户端已连接，无法添加传输");
        }

        if (_transports.ContainsKey(config.Name))
        {
            throw new ArgumentException($"传输通道已存在: {config.Name}");
        }

        var transportInfo = new ClientTransportInfo
        {
            Name = config.Name,
            Type = config.Type,
            Host = config.Host,
            Port = config.Port,
            Options = config.Options ?? new TransportOptions(),
            IsDefault = config.IsDefault
        };

        _transports.Add(config.Name, transportInfo);

        // 立即注册通道到通道管理器，这样用户可以在连接之前获取通道
        _channelManager.RegisterChannel(transportInfo.Name, transportInfo.Type, transportInfo.Options, transportInfo.IsDefault);

        _logger.LogInformation("已添加并注册 {Type} 传输通道: {Name}, 目标: {Host}:{Port}",
            config.Type, config.Name, config.Host, config.Port);
    }

    /// <summary>
    /// 批量添加传输配置
    /// </summary>
    internal void AddTransports(IEnumerable<ClientTransportConfiguration> configurations)
    {
        foreach (var config in configurations)
        {
            AddTransport(config);
        }
    }

    /// <summary>
    /// 连接到服务器
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            return;
        }

        _logger.LogInformation("正在连接到服务器...");

        try
        {
            // 连接所有配置的传输通道
            var connectionTasks = new List<Task>();

            foreach (var transportInfo in _transports.Values)
            {
                var channel = _channelManager.GetChannel(transportInfo.Name);
                var connectionTask = channel.ConnectAsync(transportInfo.Host, transportInfo.Port, cancellationToken);
                connectionTasks.Add(connectionTask);

                _logger.LogInformation("正在连接 {Type} 传输: {Name} at {Host}:{Port}",
                    transportInfo.Type, transportInfo.Name, transportInfo.Host, transportInfo.Port);
            }

            // 等待所有通道连接完成
            await Task.WhenAll(connectionTasks);

            _isConnected = true;
            _logger.LogInformation("所有通道已连接，客户端连接完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接服务器失败");
            throw;
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
            return;

        _logger.LogInformation("正在断开连接...");

        try
        {
            // 断开所有通道的连接
            var disconnectionTasks = new List<Task>();

            foreach (var transportInfo in _transports.Values)
            {
                if (!_channelManager.HasChannel(transportInfo.Name))
                {
                    continue;
                }

                var channel = _channelManager.GetChannel(transportInfo.Name);
                var disconnectionTask = channel.DisconnectAsync(cancellationToken);
                disconnectionTasks.Add(disconnectionTask);

                _logger.LogInformation("正在断开 {Type} 传输: {Name}", transportInfo.Type, transportInfo.Name);
            }

            // 等待所有通道断开完成
            await Task.WhenAll(disconnectionTasks);

            _isConnected = false;
            _logger.LogInformation("所有通道已断开，客户端断开连接完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "断开连接时发生错误");
            throw;
        }
    }


    /// <summary>
    /// 获取服务代理 - 自动处理服务发现和连接管理
    /// </summary>
    public async Task<T> GetServiceAsync<T>(string? serviceName = null, CancellationToken cancellationToken = default) where T : class, IPulseService
    {
        // TODO: 实现服务代理获取逻辑
        throw new NotImplementedException("服务代理功能待实现");
    }

    /// <summary>
    /// 注册事件监听器
    /// </summary>
    public async Task<ISubscriptionToken> RegisterEventListenerAsync<T>(T listener, string? serviceName = null, CancellationToken cancellationToken = default) where T : class, IPulseEventHandler
    {
        // TODO: 实现事件监听器注册逻辑
        throw new NotImplementedException("事件监听器功能待实现");
    }

    /// <summary>
    /// 获取连接统计信息
    /// </summary>
    public async Task<ConnectionStatistics> GetConnectionStatisticsAsync(CancellationToken cancellationToken = default)
    {
        return new ConnectionStatistics
        {
            TotalConnections = _transports.Count,
            ActiveConnections = _isConnected ? _transports.Count : 0,
            IdleConnections = 0,
            FailedConnections = 0,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>
    /// 获取通道管理器 - 用于底层通道操作
    /// </summary>
    public IChannelManager GetChannelManager()
    {
        return _channelManager;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (_isConnected)
            {
                DisconnectAsync().Wait(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放资源时断开连接失败");
        }

        _channelManager?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 客户端传输信息
    /// </summary>
    private class ClientTransportInfo
    {
        public string Name { get; set; } = string.Empty;
        public TransportType Type { get; set; }
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public TransportOptions Options { get; set; } = new();
        public bool IsDefault { get; set; }
    }
}
