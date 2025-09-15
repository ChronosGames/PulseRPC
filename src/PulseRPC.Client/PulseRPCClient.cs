using PulseRPC.Transport;
using Microsoft.Extensions.Logging;
using System;
using Microsoft.Extensions.Logging.Abstractions;

namespace PulseRPC.Client;

/// <summary>
/// 动态连接令牌接口
/// </summary>
public interface IDynamicConnectionToken : IDisposable
{
    /// <summary>
    /// 连接名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 传输类型
    /// </summary>
    TransportType Type { get; }

    /// <summary>
    /// 主机地址
    /// </summary>
    string Host { get; }

    /// <summary>
    /// 端口号
    /// </summary>
    int Port { get; }

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 创建时间
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// 获取该连接上的服务代理
    /// </summary>
    Task<T> GetServiceAsync<T>(string? serviceName = null, CancellationToken cancellationToken = default)
        where T : class, IPulseService;

    /// <summary>
    /// 在该连接上注册事件监听器
    /// </summary>
    Task<ISubscriptionToken> RegisterEventListenerAsync<T>(T listener, string? serviceName = null,
        CancellationToken cancellationToken = default) where T : class, IPulseEventHandler;
}

/// <summary>
/// PulseRPC 统一客户端接口 - 整合所有客户端功能
/// </summary>
public interface IPulseRPCClient : IDisposable
{
    /// <summary>
    /// 连接到服务器
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 获取服务代理 - 自动处理服务发现和连接管理
    /// </summary>
    /// <typeparam name="T">服务接口类型</typeparam>
    /// <param name="serviceName">服务名称，为空则使用接口名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务代理</returns>
    Task<T> GetServiceAsync2<T>(string? serviceName = null, CancellationToken cancellationToken = default)
        where T : class, IPulseService;

    /// <summary>
    /// 注册事件监听器
    /// </summary>
    /// <typeparam name="T">监听器接口类型</typeparam>
    /// <param name="listener">监听器实例</param>
    /// <param name="serviceName">服务名称，为空则使用接口名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>订阅令牌</returns>
    Task<ISubscriptionToken> RegisterEventListenerAsync2<T>(T listener, string? serviceName = null,
        CancellationToken cancellationToken = default) where T : class, IPulseEventHandler;

    /// <summary>
    /// 获取连接统计信息
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接统计</returns>
    Task<ConnectionStatistics> GetConnectionStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

    /// <summary>
    /// 获取通道管理器 - 用于底层通道操作
    /// </summary>
    /// <returns>通道管理器实例</returns>
    IChannelManager GetChannelManager();

    // 动态连接管理接口
    
    /// <summary>
    /// 动态添加并连接新的传输通道
    /// </summary>
    /// <param name="name">通道名称</param>
    /// <param name="type">传输类型</param>
    /// <param name="host">主机地址</param>
    /// <param name="port">端口号</param>
    /// <param name="options">传输选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>动态连接令牌</returns>
    Task<IDynamicConnectionToken> AddDynamicConnectionAsync(
        string name, 
        TransportType type, 
        string host, 
        int port, 
        TransportOptions? options = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除动态连接
    /// </summary>
    /// <param name="connectionToken">连接令牌</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    Task RemoveDynamicConnectionAsync(IDynamicConnectionToken connectionToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定通道的服务代理
    /// </summary>
    /// <typeparam name="T">服务接口类型</typeparam>
    /// <param name="channelName">通道名称</param>
    /// <param name="serviceName">服务名称，为空则使用接口名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务代理</returns>
    Task<T> GetServiceAsync2<T>(string channelName, string? serviceName = null, CancellationToken cancellationToken = default)
        where T : class, IPulseService;

    /// <summary>
    /// 在指定通道注册事件监听器
    /// </summary>
    /// <typeparam name="T">监听器接口类型</typeparam>
    /// <param name="channelName">通道名称</param>
    /// <param name="listener">监听器实例</param>
    /// <param name="serviceName">服务名称，为空则使用接口名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>订阅令牌</returns>
    Task<ISubscriptionToken> RegisterEventListenerAsync2<T>(string channelName, T listener, string? serviceName = null,
        CancellationToken cancellationToken = default) where T : class, IPulseEventHandler;
}

/// <summary>
/// PulseRPC 客户端实现
/// </summary>
internal class PulseRPCClient : IPulseRPCClient
{
    private readonly IChannelManager _channelManager;
    private readonly ILogger<PulseRPCClient> _logger;
    private readonly Dictionary<string, ClientTransportInfo> _transports = new();
    private readonly Dictionary<string, DynamicConnectionToken> _dynamicConnections = new();
    private readonly object _dynamicLock = new();
    private bool _isConnected;
    private bool _disposed;

    public PulseRPCClient(IChannelManager? channelManager = null, ILogger<PulseRPCClient>? logger = null)
    {
        _channelManager = channelManager ?? new ChannelManager();
        _logger = logger ?? new NullLogger<PulseRPCClient>();
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
            throw new InvalidOperationException("客户端已连接，无法添加静态传输。请使用 AddDynamicConnectionAsync 添加动态连接");
        }

        if (_transports.ContainsKey(config.Name))
        {
            throw new ArgumentException($"传输通道已存在: {config.Name}");
        }

        // 检查动态连接中是否已存在同名连接
        lock (_dynamicLock)
        {
            if (_dynamicConnections.ContainsKey(config.Name))
            {
                throw new ArgumentException($"动态连接已存在: {config.Name}");
            }
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
    public async Task<T> GetServiceAsync2<T>(string? serviceName = null, CancellationToken cancellationToken = default) where T : class, IPulseService
    {
        // TODO: 实现服务代理获取逻辑
        throw new NotImplementedException("服务代理功能待实现");
    }

    /// <summary>
    /// 注册事件监听器
    /// </summary>
    public async Task<ISubscriptionToken> RegisterEventListenerAsync2<T>(T listener, string? serviceName = null, CancellationToken cancellationToken = default) where T : class, IPulseEventHandler
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
    /// 动态添加并连接新的传输通道
    /// </summary>
    public async Task<IDynamicConnectionToken> AddDynamicConnectionAsync(
        string name, 
        TransportType type, 
        string host, 
        int port, 
        TransportOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("连接名称不能为空", nameof(name));

        lock (_dynamicLock)
        {
            if (_dynamicConnections.ContainsKey(name))
                throw new ArgumentException($"动态连接已存在: {name}");

            if (_transports.ContainsKey(name))
                throw new ArgumentException($"静态传输通道已存在: {name}");
        }

        _logger.LogInformation("正在创建动态连接: {Name} ({Type}) to {Host}:{Port}", name, type, host, port);

        try
        {
            // 创建并注册通道
            _channelManager.RegisterChannel(name, type, options ?? new TransportOptions(), isDefault: false);

            // 连接通道
            var channel = _channelManager.GetChannel(name);
            await channel.ConnectAsync(host, port, cancellationToken);

            // 创建动态连接令牌
            var token = new DynamicConnectionToken(name, type, host, port, this, _logger);

            lock (_dynamicLock)
            {
                _dynamicConnections[name] = token;
            }

            _logger.LogInformation("动态连接创建成功: {Name}", name);
            return token;
        }
        catch (Exception ex)
        {
            // 清理失败的连接
            if (_channelManager.HasChannel(name))
            {
                _channelManager.UnregisterChannel(name);
            }

            _logger.LogError(ex, "创建动态连接失败: {Name}", name);
            throw;
        }
    }

    /// <summary>
    /// 移除动态连接
    /// </summary>
    public async Task RemoveDynamicConnectionAsync(IDynamicConnectionToken connectionToken, CancellationToken cancellationToken = default)
    {
        if (connectionToken is not DynamicConnectionToken token)
            throw new ArgumentException("无效的连接令牌", nameof(connectionToken));

        _logger.LogInformation("正在移除动态连接: {Name}", token.Name);

        try
        {
            // 断开连接
            if (_channelManager.HasChannel(token.Name))
            {
                var channel = _channelManager.GetChannel(token.Name);
                if (channel.IsConnected)
                {
                    await channel.DisconnectAsync(cancellationToken);
                }
                _channelManager.UnregisterChannel(token.Name);
            }

            // 从动态连接列表中移除
            lock (_dynamicLock)
            {
                _dynamicConnections.Remove(token.Name);
            }

            // 释放令牌资源
            token.Dispose();

            _logger.LogInformation("动态连接移除成功: {Name}", token.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "移除动态连接失败: {Name}", token.Name);
            throw;
        }
    }

    /// <summary>
    /// 获取指定通道的服务代理
    /// </summary>
    public async Task<T> GetServiceAsync2<T>(string channelName, string? serviceName = null, CancellationToken cancellationToken = default) where T : class, IPulseService
    {
        if (!_channelManager.HasChannel(channelName))
            throw new ArgumentException($"通道不存在: {channelName}");

        // TODO: 实现特定通道的服务代理获取逻辑
        throw new NotImplementedException($"指定通道 {channelName} 的服务代理功能待实现");
    }

    /// <summary>
    /// 在指定通道注册事件监听器
    /// </summary>
    public async Task<ISubscriptionToken> RegisterEventListenerAsync2<T>(string channelName, T listener, string? serviceName = null, CancellationToken cancellationToken = default) where T : class, IPulseEventHandler
    {
        if (!_channelManager.HasChannel(channelName))
            throw new ArgumentException($"通道不存在: {channelName}");

        // TODO: 实现特定通道的事件监听器注册逻辑
        throw new NotImplementedException($"指定通道 {channelName} 的事件监听器功能待实现");
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

            // 清理动态连接
            lock (_dynamicLock)
            {
                foreach (var dynamicConnection in _dynamicConnections.Values)
                {
                    try
                    {
                        dynamicConnection.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "释放动态连接失败: {Name}", dynamicConnection.Name);
                    }
                }
                _dynamicConnections.Clear();
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

/// <summary>
/// 动态连接令牌实现
/// </summary>
internal class DynamicConnectionToken : IDynamicConnectionToken
{
    private readonly PulseRPCClient _client;
    private readonly ILogger _logger;
    private bool _disposed;

    public string Name { get; }
    public TransportType Type { get; }
    public string Host { get; }
    public int Port { get; }
    public DateTime CreatedAt { get; }

    public bool IsConnected 
    { 
        get
        {
            var channelManager = _client.GetChannelManager();
            if (!channelManager.HasChannel(Name))
                return false;
            
            var channel = channelManager.GetChannel(Name);
            return channel.IsConnected;
        }
    }

    public DynamicConnectionToken(
        string name,
        TransportType type,
        string host,
        int port,
        PulseRPCClient client,
        ILogger logger)
    {
        Name = name;
        Type = type;
        Host = host;
        Port = port;
        CreatedAt = DateTime.UtcNow;
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// 获取该连接上的服务代理
    /// </summary>
    public async Task<T> GetServiceAsync<T>(string? serviceName = null, CancellationToken cancellationToken = default) 
        where T : class, IPulseService
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DynamicConnectionToken));

        return await _client.GetServiceAsync2<T>(Name, serviceName, cancellationToken);
    }

    /// <summary>
    /// 在该连接上注册事件监听器
    /// </summary>
    public async Task<ISubscriptionToken> RegisterEventListenerAsync<T>(T listener, string? serviceName = null, CancellationToken cancellationToken = default) 
        where T : class, IPulseEventHandler
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DynamicConnectionToken));

        return await _client.RegisterEventListenerAsync2(Name, listener, serviceName, cancellationToken);
    }

    /// <summary>
    /// 释放连接令牌资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _logger.LogDebug("动态连接令牌已释放: {Name}", Name);
    }
}
