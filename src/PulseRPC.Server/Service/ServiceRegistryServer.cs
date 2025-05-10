using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

/// <summary>
/// 服务注册中心服务器
/// </summary>
public class ServiceRegistryServer : IDisposable
{
    private readonly INetServer _server;
    private readonly IServiceRegistry _registry;
    private readonly ILogger? _logger;

    public ServiceRegistryServer(
        INetServer server,
        IServiceRegistry registry,
        ILogger? logger = null)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger;

        // 注册会话事件
        _server.SessionConnected += OnSessionConnected;
        _server.SessionDisconnected += OnSessionDisconnected;
    }

    public async Task StartAsync(int port)
    {
        try
        {
            await _server.StartAsync(new ServerOptions
            {
                Port = port,
                MaxConnections = 1000,
                ReceiveBufferSize = 8192,
                SendBufferSize = 8192,
                HeartbeatInterval = TimeSpan.FromSeconds(30),
                IdleTimeout = TimeSpan.FromMinutes(2),
                NoDelay = true
            });

            _logger?.LogInformation("服务注册中心已启动，监听端口: {Port}", port);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "启动服务注册中心时出错");
            throw;
        }
    }

    private void OnSessionConnected(object sender, SessionEventArgs e)
    {
        _logger?.LogInformation((string)"客户端已连接: {RemoteEndPoint}", (object?)e.Session.RemoteEndPoint);
    }

    private void OnSessionDisconnected(object sender, DisconnectedEventArgs e)
    {
        _logger?.LogError("客户端已断开: {RemoteEndPoint}, 原因: {Reason}", e.Session.RemoteEndPoint, e.Reason);
    }

    public void Dispose()
    {
        _server?.Dispose();
        _registry?.Dispose();
    }
}

/// <summary>
/// 服务心跳消息处理器
/// </summary>
public class ServiceHeartbeatMessageHandler(
    IServiceRegistry registry,
    ILogger logger) : IRequestHandler<ServiceHeartbeat, ServiceHeartbeatResponse>
{
    private readonly ILogger _logger = logger;

    public async Task<ServiceHeartbeatResponse> HandleAsync(NetworkSession session, ServiceHeartbeat heartbeat)
    {
        var success = await registry.UpdateHeartbeatAsync(
            heartbeat.ServiceType,
            GetServiceId(heartbeat));

        return new ServiceHeartbeatResponse { Success = success, Message = success ? "心跳更新成功" : "服务不存在" };
    }

    private static string GetServiceId(ServiceHeartbeat heartbeat)
    {
        if (!string.IsNullOrEmpty(heartbeat.InstanceId))
        {
            return $"{heartbeat.ZoneId}:{heartbeat.InstanceId}";
        }
        else if (!string.IsNullOrEmpty(heartbeat.ServerId))
        {
            return $"{heartbeat.ZoneId}:{heartbeat.ServerId}";
        }
        else
        {
            throw new ArgumentException("服务ID无效");
        }
    }
}

/// <summary>
/// 服务注销消息处理器
/// </summary>
public class ServiceUnregistrationMessageHandler(
    IServiceRegistry registry,
    ILogger<ServiceUnregistrationMessageHandler> logger)
    : IRequestHandler<ServiceUnregistration, ServiceRegistrationResponse>
{
    private readonly ILogger _logger = logger;

    public async Task<ServiceRegistrationResponse> HandleAsync(NetworkSession session,
        ServiceUnregistration unregistration)
    {
        await registry.UnregisterServiceAsync(
            unregistration.ServiceType,
            GetServiceId(unregistration));

        return new ServiceRegistrationResponse { Success = true, Message = "服务注销成功" };
    }

    private static string GetServiceId(ServiceUnregistration unregistration)
    {
        if (!string.IsNullOrEmpty(unregistration.InstanceId))
        {
            return $"{unregistration.ZoneId}:{unregistration.InstanceId}";
        }
        else if (!string.IsNullOrEmpty(unregistration.ServerId))
        {
            return $"{unregistration.ZoneId}:{unregistration.ServerId}";
        }
        else
        {
            throw new ArgumentException("服务ID无效");
        }
    }
}
