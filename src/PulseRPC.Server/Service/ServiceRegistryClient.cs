using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using Microsoft.Extensions.Logging;
using PulseNet.Core;
using PulseRPC.Protocol;

namespace PulseRPC.Server;

/// <summary>
/// 服务注册客户端
/// </summary>
public class ServiceRegistryClient : IDisposable
{
    private readonly INetClient _client;
    private readonly ILogger? _logger;
    private readonly Timer _heartbeatTimer;
    private ServiceRegistration _registration;
    private bool _isRegistered;

    public ServiceRegistryClient(INetClient client, ILogger? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger;

        // 注册响应处理器
        _client.RegisterHandler<ServiceRegistrationResponse>(
            MessageIds.ServiceRegistrationResponse,
            HandleRegistrationResponse);

        _client.RegisterHandler<ServiceHeartbeatResponse>(
            MessageIds.ServiceHeartbeatResponse,
            HandleHeartbeatResponse);

        // 创建心跳定时器（默认15秒发送一次）
        _heartbeatTimer = new Timer(SendHeartbeat, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task RegisterAsync(ServiceRegistration registration)
    {
        if (registration == null)
            throw new ArgumentNullException(nameof(registration));

        _registration = registration;

        try
        {
            // 发送注册请求
            await _client.SendAsync(registration, MessageIds.ServiceRegistration);
            _logger?.LogInformation($"发送服务注册请求: {registration.ServiceType}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "发送服务注册请求时出错");
            throw;
        }
    }

    public async Task UnregisterAsync()
    {
        if (_registration == null || !_isRegistered)
            return;

        try
        {
            // 停止心跳
            _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);

            // 发送注销请求
            await _client.SendAsync(new ServiceUnregistration
            {
                ServiceType = _registration.ServiceType,
                ZoneId = _registration.ZoneId,
                ServerId = _registration.ServerId,
                InstanceId = _registration.InstanceId
            }, MessageIds.ServiceUnregistration);

            _isRegistered = false;
            _logger?.LogInformation($"发送服务注销请求: {_registration.ServiceType}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "发送服务注销请求时出错");
            throw;
        }
    }

    private async void SendHeartbeat(object state)
    {
        if (!_isRegistered || _registration == null)
            return;

        try
        {
            var heartbeat = new ServiceHeartbeat
            {
                ServiceType = _registration.ServiceType,
                ZoneId = _registration.ZoneId,
                ServerId = _registration.ServerId,
                InstanceId = _registration.InstanceId,
                Timestamp = DateTime.UtcNow,
                Metrics = GetServiceMetrics()
            };

            await _client.SendAsync(heartbeat, MessageIds.ServiceHeartbeat);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "发送服务心跳时出错");
        }
    }

    private async Task HandleRegistrationResponse(ServiceRegistrationResponse response)
    {
        if (response.Success)
        {
            _isRegistered = true;
            _logger?.LogInformation($"服务注册成功: {response.Message}");

            // 启动心跳定时器
            _heartbeatTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(15));
        }
        else
        {
            _isRegistered = false;
            _logger?.LogError($"服务注册失败: {response.Message}");
        }
    }

    private Task HandleHeartbeatResponse(ServiceHeartbeatResponse response)
    {
        if (!response.Success)
        {
            _logger?.LogWarning($"服务心跳响应失败: {response.Message}");
        }

        return Task.CompletedTask;
    }

    private Dictionary<string, object> GetServiceMetrics()
    {
        return new Dictionary<string, object>
        {
            ["CpuUsage"] = GetCpuUsage(),
            ["MemoryUsage"] = GetMemoryUsage(),
            ["Uptime"] = (DateTime.UtcNow - _registration.RegistrationTime).TotalSeconds
        };
    }

    private double GetCpuUsage()
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            return proc.TotalProcessorTime.TotalMilliseconds /
                   (Environment.ProcessorCount * proc.UserProcessorTime.TotalMilliseconds) * 100;
        }
        catch
        {
            return 0;
        }
    }

    private long GetMemoryUsage()
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            return proc.WorkingSet64;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
    }
}

/// <summary>
/// 服务注销请求
/// </summary>
[MemoryPackable]
public partial record ServiceUnregistration(string ServiceType, string ZoneId, string ServerId, string InstanceId) : IMessage;

/// <summary>
/// 服务心跳响应
/// </summary>
[MemoryPackable]
public partial record ServiceHeartbeatResponse(bool Success, string Message = "") : IMessage;
