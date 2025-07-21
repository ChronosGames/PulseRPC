using PulseRPC.Transport;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Client;

/// <summary>
/// PulseRPC 客户端接口
/// </summary>
public interface IPulseRpcClient : IDisposable
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
    /// 获取服务代理
    /// </summary>
    TService GetService<TService>() where TService : class;

    /// <summary>
    /// 获取通道管理器
    /// </summary>
    IChannelManager GetChannelManager();

    /// <summary>
    /// 获取已配置的传输信息
    /// </summary>
    IReadOnlyDictionary<string, (TransportType Type, string Host, int Port, bool IsDefault)> GetTransports();
}

/// <summary>
/// PulseRPC 客户端实现
/// </summary>
internal class PulseRpcClientManager : IPulseRpcClient
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

        _logger.LogInformation("已添加 {Type} 传输: {Name}, 目标: {Host}:{Port}", 
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
            // 为每个传输创建并注册通道
            foreach (var info in _transports.Values)
            {
                // 注册通道到通道管理器
                _channelManager.RegisterChannel(info.Name, info.Type, info.Options, info.IsDefault);

                _logger.LogInformation("已注册 {Type} 传输通道: {Name}", info.Type, info.Name);
            }

            // 连接到默认通道的服务器
            var defaultTransport = _transports.Values.FirstOrDefault(t => t.IsDefault) 
                ?? _transports.Values.FirstOrDefault();

            if (defaultTransport != null)
            {
                var defaultChannel = _channelManager.GetDefaultChannel();
                await defaultChannel.ConnectAsync($"{defaultTransport.Host}", defaultTransport.Port, cancellationToken);
            }

            _isConnected = true;
            _logger.LogInformation("客户端已连接");
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
            var defaultChannel = _channelManager.GetDefaultChannel();
            await defaultChannel.DisconnectAsync(cancellationToken);

            _isConnected = false;
            _logger.LogInformation("客户端已断开连接");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "断开连接时发生错误");
            throw;
        }
    }

    /// <summary>
    /// 获取服务代理
    /// </summary>
    public TService GetService<TService>() where TService : class
    {
        // 这里需要使用代码生成或反射来创建服务代理
        // 暂时返回通道管理器的扩展方法调用
        var methodName = $"Get{typeof(TService).Name.Substring(1)}"; // 移除接口前缀 'I'
        var method = _channelManager.GetType().GetMethod(methodName);
        
        if (method != null)
        {
            return (TService)method.Invoke(_channelManager, null)!;
        }

        throw new NotSupportedException($"未找到服务代理方法: {methodName}");
    }

    /// <summary>
    /// 获取通道管理器
    /// </summary>
    public IChannelManager GetChannelManager()
    {
        return _channelManager;
    }

    /// <summary>
    /// 获取已配置的传输信息
    /// </summary>
    public IReadOnlyDictionary<string, (TransportType Type, string Host, int Port, bool IsDefault)> GetTransports()
    {
        return _transports.ToDictionary(
            kvp => kvp.Key,
            kvp => (kvp.Value.Type, kvp.Value.Host, kvp.Value.Port, kvp.Value.IsDefault));
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