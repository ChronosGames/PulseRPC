using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Transport;
using PulseRPC.Server.ServiceDiscovery;
using PulseRPC.ServiceDiscovery;
using PulseRPC.Transport;
using System.Net;

namespace PulseRPC.Server;

/// <summary>
/// 服务器管理器接口
/// </summary>
public interface IServerManager : IDisposable
{
    /// <summary>
    /// 添加传输
    /// </summary>
    void AddTransport(
        string channelName,
        TransportType transportType,
        int port,
        TransportOptions? options = null,
        bool isDefault = false);

    /// <summary>
    /// 启动服务器
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止服务器
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取已注册的服务端点
    /// </summary>
    Task<IReadOnlyList<PulseRPC.ServiceDiscovery.ServiceEndpoint>> GetRegisteredServicesAsync();
}

/// <summary>
/// 高性能服务器管理器 - 负责处理所有网络连接和消息路由
/// </summary>
public class ServerManager : IServerManager
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServerChannelManager _serverChannelManager;
    private readonly IServiceRegistry? _serviceRegistry;
    private readonly ServerOptions _serverOptions;
    private readonly ILogger<ServerManager> _logger;
    private readonly Dictionary<string, TransportInfo> _transports = new();
    private readonly Dictionary<string, IServerListener> _listeners = new();
    private readonly Dictionary<string, string> _registeredServiceIds = new(); // 传输名称 -> 服务ID
    private bool _isRunning;

    public ServerManager(
        IServerChannelManager serverChannelManager,
        ILoggerFactory loggerFactory,
        IOptions<ServerOptions> serverOptions,
        IServiceRegistry? serviceRegistry = null)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _serverChannelManager = serverChannelManager;
        _serviceRegistry = serviceRegistry;
        _serverOptions = serverOptions.Value;
        _logger = loggerFactory.CreateLogger<ServerManager>();

        _logger.LogInformation("ServerManager 已初始化，服务注册: {ServiceRegistryEnabled}, 服务名称: {ServiceName}",
            _serverOptions.EnableServiceRegistry && _serviceRegistry != null, _serverOptions.ServiceName);
    }

    /// <summary>
    /// 添加传输层
    /// </summary>
    public void AddTransport(
        string channelName,
        TransportType transportType,
        int port,
        TransportOptions? options = null,
        bool isDefault = false)
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("服务器运行中，无法添加传输");
        }

        if (_transports.ContainsKey(channelName))
        {
            throw new ArgumentException($"通道已存在: {channelName}");
        }

        // 创建传输信息
        var transportInfo = new TransportInfo
        {
            Name = channelName,
            Type = transportType,
            Port = port,
            Options = options ?? new TransportOptions(),
            IsDefault = isDefault
        };

        _transports.Add(channelName, transportInfo);

        _logger.LogInformation("已添加 {Type} 传输: {Name}, 端口: {Port}", transportType, channelName, port);
    }

    /// <summary>
    /// 启动服务器
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return;
        }

        _logger.LogInformation("正在启动服务器...");

        try
        {
            // 为每个传输创建并启动监听器
            foreach (var info in _transports.Values)
            {
                // 创建监听器
                IServerListener listener = info.Type switch
                {
                    TransportType.Tcp => new TcpServerListener(info.Port, info.Options, _loggerFactory.CreateLogger<TcpServerListener>()),
                    TransportType.Kcp => new KcpServerListener(info.Port, info.Options, _loggerFactory.CreateLogger<KcpServerListener>()),
                    _ => throw new NotSupportedException($"不支持的传输类型: {info.Type}")
                };

                // 注册连接接受事件
                listener.ConnectionAccepted += OnConnectionAccepted;

                // 启动监听器
                await listener.StartAsync(cancellationToken);

                // 添加到监听器集合
                _listeners.Add(info.Name, listener);

                _logger.LogInformation("已启动 {Type} 监听器: {Name}, 端口: {Port}", info.Type, info.Name, info.Port);

                // 注册服务到服务发现
                await RegisterServiceAsync(info, cancellationToken);
            }

            _isRunning = true;
            _logger.LogInformation("服务器已启动");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动服务器失败");
            await StopAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// 停止服务器
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
            return;

        _logger.LogInformation("正在停止服务器...");

        _isRunning = false;

        // 注销所有已注册的服务
        await UnregisterAllServicesAsync(cancellationToken);

        // 停止所有监听器
        foreach (var kvp in _listeners)
        {
            try
            {
                var listener = kvp.Value;
                listener.ConnectionAccepted -= OnConnectionAccepted;
                await listener.StopAsync(cancellationToken);
                listener.Dispose();

                _logger.LogInformation("已停止监听器: {Name}", kvp.Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止监听器 {Name} 时发生错误", kvp.Key);
            }
        }

        _listeners.Clear();

        // 释放通道资源
        _serverChannelManager.Dispose();

        _logger.LogInformation("服务器已停止");
    }

    /// <summary>
    /// 获取已注册的服务端点
    /// </summary>
    public async Task<IReadOnlyList<ServiceEndpoint>> GetRegisteredServicesAsync()
    {
        if (_serviceRegistry != null)
        {
            try
            {
                return await _serviceRegistry.GetRegisteredServicesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取已注册服务列表失败");
            }
        }

        return Array.Empty<ServiceEndpoint>();
    }

    /// <summary>
    /// 处理新连接
    /// </summary>
    private void OnConnectionAccepted(object? sender, ServerConnectionEventArgs e)
    {
        try
        {
            _logger.LogInformation("接受新连接: {ConnectionId} 从 {RemoteAddress}",
                e.Transport.ConnectionId, e.Transport.RemoteEndPoint);

            // 将连接添加到通道管理器
            var channel = _serverChannelManager.AddChannel(e.Transport);

            _logger.LogDebug("已为连接 {ConnectionId} 创建传输通道", e.Transport.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理新连接 {ConnectionId} 时发生错误", e.Transport.ConnectionId);

            // 关闭有问题的连接
            try
            {
                _ = e.Transport.CloseAsync();
            }
            catch
            {
                // 忽略关闭时的异常
            }
        }
    }

    /// <summary>
    /// 注册服务到服务发现
    /// </summary>
    private async Task RegisterServiceAsync(TransportInfo transportInfo, CancellationToken cancellationToken)
    {
        if (!_serverOptions.EnableServiceRegistry || _serviceRegistry == null)
        {
            return;
        }

        try
        {
            // 获取服务地址
            var serviceAddress = GetServiceAddress();
            var endpoint = new IPEndPoint(IPAddress.Parse(serviceAddress), transportInfo.Port);

            // 生成服务ID
            var serviceId = $"{_serverOptions.ServiceName}-{transportInfo.Name}-{Environment.MachineName}-{transportInfo.Port}";

            // 创建服务端点
            var serviceEndpoint = new ServiceEndpoint
            {
                ServiceId = serviceId,
                ServiceName = _serverOptions.ServiceName,
                Version = _serverOptions.ServiceVersion,
                EndPoint = endpoint,
                Weight = _serverOptions.ServiceWeight,
                Tags = new Dictionary<string, string>(_serverOptions.ServiceTags)
                {
                    ["transport"] = transportInfo.Type.ToString().ToLower(),
                    ["channel"] = transportInfo.Name,
                    ["machine"] = Environment.MachineName,
                    ["framework"] = nameof(PulseRPC)
                },
                Metadata = new Dictionary<string, object>(_serverOptions.ServiceMetadata)
            };

            // 注册服务
            await _serviceRegistry.RegisterAsync(serviceEndpoint, cancellationToken);

            // 记录已注册的服务ID
            _registeredServiceIds[transportInfo.Name] = serviceId;

            _logger.LogInformation("已注册服务: {ServiceName}({ServiceId}) @ {EndPoint}",
                _serverOptions.ServiceName, serviceId, endpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注册服务失败: {TransportName}", transportInfo.Name);
            // 不抛出异常，允许服务器继续启动
        }
    }

    /// <summary>
    /// 注销所有已注册的服务
    /// </summary>
    private async Task UnregisterAllServicesAsync(CancellationToken cancellationToken)
    {
        if (_serviceRegistry == null || _registeredServiceIds.Count == 0)
        {
            return;
        }

        var unregisterTasks = _registeredServiceIds.Values.Select(async serviceId =>
        {
            try
            {
                await _serviceRegistry.UnregisterAsync(serviceId, cancellationToken);
                _logger.LogInformation("已注销服务: {ServiceId}", serviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注销服务 {ServiceId} 失败", serviceId);
            }
        });

        await Task.WhenAll(unregisterTasks);
        _registeredServiceIds.Clear();
    }

    /// <summary>
    /// 获取服务地址
    /// </summary>
    private string GetServiceAddress()
    {
        if (!_serverOptions.AutoDetectAddress && !string.IsNullOrEmpty(_serverOptions.ServiceAddress))
        {
            return _serverOptions.ServiceAddress;
        }

        // 自动检测本机IP地址
        try
        {
            // 优先使用非回环地址
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var localIp = host.AddressList
                .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip));

            if (localIp != null)
            {
                return localIp.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "自动检测IP地址失败，使用回环地址");
        }

        // 降级到回环地址
        return "127.0.0.1";
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_isRunning)
        {
            try
            {
                StopAsync().Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放资源时停止服务器失败");
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 传输信息
    /// </summary>
    private class TransportInfo
    {
        public required string Name { get; init; }
        public TransportType Type { get; set; }
        public int Port { get; set; }
        public TransportOptions Options { get; set; } = new();
        public bool IsDefault { get; set; }
    }
}
