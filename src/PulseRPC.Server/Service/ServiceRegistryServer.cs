using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseNet.Core;

namespace PulseRPC.Server;

/// <summary>
/// 服务注册中心服务器
/// </summary>
public class ServiceRegistryServer : IDisposable
{
    private readonly INetServer _server;
    private readonly IServiceRegistry _registry;
    private readonly INetCoreLogger _logger;

    public ServiceRegistryServer(
        ISerializer serializer,
        INetServer server,
        IServiceRegistry registry,
        INetCoreLogger logger = null)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger;

        // 注册消息处理器
        _server.RegisterHandler(MessageIds.ServiceRegistration,
            new ServiceRegistrationMessageHandler(serializer, _registry, _logger, MessageIds.ServiceRegistration));

        _server.RegisterHandler(MessageIds.ServiceHeartbeat,
            new ServiceHeartbeatMessageHandler(serializer, _registry, _logger, MessageIds.ServiceHeartbeat));

        _server.RegisterHandler(MessageIds.ServiceUnregistration,
            new ServiceUnregistrationMessageHandler(serializer, _registry, _logger, MessageIds.ServiceUnregistration));

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

            _logger?.Info($"服务注册中心已启动，监听端口: {port}");
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "启动服务注册中心时出错");
            throw;
        }
    }

    private void OnSessionConnected(object sender, SessionEventArgs e)
    {
        _logger?.Info($"客户端已连接: {e.Session.RemoteEndPoint}");
    }

    private void OnSessionDisconnected(object sender, DisconnectedEventArgs e)
    {
        _logger?.Info($"客户端已断开: {e.Session.RemoteEndPoint}, 原因: {e.Reason}");
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
public class ServiceHeartbeatMessageHandler : MessageHandlerBase<ServiceHeartbeat>
{
    private readonly IServiceRegistry _registry;
    private readonly INetCoreLogger _logger;
    private readonly int _messageId;

    public ServiceHeartbeatMessageHandler(
        ISerializer serializer,
        IServiceRegistry registry,
        INetCoreLogger logger,
        int messageId) : base(serializer)
    {
        _registry = registry;
        _logger = logger;
        _messageId = messageId;
    }

    protected override async Task HandleMessageAsync(PulseNet.Core.INetSession session, ServiceHeartbeat heartbeat)
    {
        try
        {
            var success = await _registry.UpdateHeartbeatAsync(
                heartbeat.ServiceType,
                GetServiceId(heartbeat));

            await session.SendAsync(new ServiceHeartbeatResponse
            {
                Success = success,
                Message = success ? "心跳更新成功" : "服务不存在"
            }, MessageIds.ServiceHeartbeatResponse);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "处理服务心跳消息时出错");

            await session.SendAsync(new ServiceHeartbeatResponse
            {
                Success = false,
                Message = $"心跳更新失败: {ex.Message}"
            }, MessageIds.ServiceHeartbeatResponse);
        }
    }

    public bool CanHandle(int messageId)
    {
        return messageId == _messageId;
    }

    private string GetServiceId(ServiceHeartbeat heartbeat)
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
public class ServiceUnregistrationMessageHandler : MessageHandlerBase<ServiceUnregistration>
{
    private readonly IServiceRegistry _registry;
    private readonly ILogger _logger;
    private readonly int _messageId;

    public ServiceUnregistrationMessageHandler(
        ISerializer serializer,
        IServiceRegistry registry,
        ILogger logger,
        int messageId) : base(serializer)
    {
        _registry = registry;
        _logger = logger;
        _messageId = messageId;
    }

    protected override async Task HandleMessageAsync(PulseNet.Core.INetSession session, ServiceUnregistration unregistration)
    {
        try
        {
            await _registry.UnregisterServiceAsync(
                unregistration.ServiceType,
                GetServiceId(unregistration));

            await session.SendAsync(new ServiceRegistrationResponse
            {
                Success = true,
                Message = "服务注销成功"
            }, MessageIds.ServiceUnregistrationResponse);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "处理服务注销消息时出错");

            await session.SendAsync(new ServiceRegistrationResponse
            {
                Success = false,
                Message = $"服务注销失败: {ex.Message}"
            }, MessageIds.ServiceUnregistrationResponse);
        }
    }

    public bool CanHandle(int messageId)
    {
        return messageId == _messageId;
    }

    private string GetServiceId(ServiceUnregistration unregistration)
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
