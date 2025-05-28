using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;

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
}

/// <summary>
/// 高性能服务器管理器 - 负责处理所有网络连接和消息路由
/// </summary>
public class ServerManager : IServerManager
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ServerManager> _logger;
    private readonly Dictionary<string, TransportInfo> _transports = new();
    private readonly Dictionary<string, IServerListener> _listeners = new();
    private readonly IServerChannelManager _channelManager;
    private readonly IServerTransportFactory _transportFactory;
    private bool _isRunning;

    public ServerManager(
        IServerChannelManager serverChannelManager,
        ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ServerManager>();
        _channelManager = serverChannelManager;
        _transportFactory = new ServerTransportFactory(loggerFactory);
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

        _logger.LogInformation("已添加 {Type} 传输: {Name}, 端口: {Port}",
            transportType, channelName, port);
    }

    /// <summary>
    /// 启动服务器
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            return;

        _logger.LogInformation("正在启动服务器...");

        try
        {
            // 为每个传输创建并启动监听器
            foreach (var transportInfo in _transports.Values)
            {
                // 创建监听器
                var listener = await _transportFactory.CreateListenerAsync(
                    transportInfo.Type,
                    transportInfo.Port,
                    transportInfo.Options);

                // 注册连接接受事件
                listener.ConnectionAccepted += OnConnectionAccepted;

                // 启动监听器
                await listener.StartAsync(cancellationToken);

                // 添加到监听器集合
                _listeners.Add(transportInfo.Name, listener);

                _logger.LogInformation("已启动 {Type} 监听器: {Name}, 端口: {Port}",
                    transportInfo.Type, transportInfo.Name, transportInfo.Port);
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
        _channelManager.Dispose();

        _logger.LogInformation("服务器已停止");
    }

    /// <summary>
    /// 处理新连接
    /// </summary>
    private void OnConnectionAccepted(object? sender, ServerConnectionEventArgs e)
    {
        try
        {
            _logger.LogInformation("接受新连接: {ConnectionId} 从 {RemoteAddress}",
                e.Connection.ConnectionId, e.Connection.RemoteEndPoint);

            // 将连接添加到通道管理器
            var channel = _channelManager.AddChannel(e.Connection);

            _logger.LogDebug("已为连接 {ConnectionId} 创建传输通道", e.Connection.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理新连接 {ConnectionId} 时发生错误", e.Connection.ConnectionId);

            // 关闭有问题的连接
            try
            {
                _ = e.Connection.CloseAsync();
            }
            catch
            {
                // 忽略关闭时的异常
            }
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_isRunning)
        {
            // 停止服务器
            _ = StopAsync();
        }

        // 释放监听器资源
        foreach (var listener in _listeners.Values)
        {
            try
            {
                listener.Dispose();
            }
            catch
            {
                // 忽略释放时的异常
            }
        }

        _listeners.Clear();

        // 释放通道资源
        _channelManager.Dispose();
    }

    /// <summary>
    /// 传输信息
    /// </summary>
    private class TransportInfo
    {
        public string Name { get; set; } = string.Empty;
        public TransportType Type { get; set; }
        public int Port { get; set; }
        public TransportOptions Options { get; set; } = new();
        public bool IsDefault { get; set; }
    }
}
