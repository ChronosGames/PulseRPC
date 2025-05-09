using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PulseNet.Server;
using PulseRPC.Server.Monitoring;

namespace PulseRPC.Server;

public class ZoneServiceConnector : IDisposable
{
    private readonly string _zoneId;
    private readonly ServiceDiscoveryConnector _serviceConnector;
    private readonly Dictionary<string, INetClient> _directConnections;
    private readonly ILogger _logger;

    public ZoneServiceConnector(
        string zoneId,
        ServiceDiscoveryConnector serviceConnector,
        ILogger logger)
    {
        _zoneId = zoneId;
        _serviceConnector = serviceConnector;
        _directConnections = new Dictionary<string, INetClient>();
        _logger = logger;
    }

    public async Task<INetClient> GetServiceClientAsync(string serviceType, string serverId = null)
    {
        // 对于区内服务通信，格式化为 {服务类型}.{区ID}.{可选服务器ID}
        string serviceKey = string.IsNullOrEmpty(serverId)
            ? $"{serviceType}.{_zoneId}"
            : $"{serviceType}.{_zoneId}.{serverId}";

        // 为频繁通信的服务维护直连
        if (_directConnections.TryGetValue(serviceKey, out var client) &&
            client.Status == ClientStatus.Connected)
        {
            return client;
        }

        // 从服务发现获取连接
        client = await _serviceConnector.GetClientAsync(serviceKey);

        // 缓存直连客户端
        if (ShouldCacheDirectConnection(serviceType))
        {
            _directConnections[serviceKey] = client;

            // 监听断开事件，移除缓存
            client.Disconnected += (sender, args) =>
            {
                _directConnections.Remove(serviceKey);
            };
        }

        return client;
    }

    public async Task Register(string serviceType, string serverId)
    {
        // 获取注册服务客户端
        var registryClient = await GetServiceClientAsync("ServiceRegistry");

        // 注册服务信息
        var registration = new ServiceRegistration()
        {
            ServiceType = serviceType,
            ZoneId = _zoneId,
            ServerId = serverId,
            Host = GetLocalIPAddress(),
            Port = ((IPEndPoint)_server.LocalEndPoint).Port,
            Metadata = new Dictionary<string, string>
            {
                ["Version"] = "1.0.0",
                ["StartTime"] = DateTime.UtcNow.ToString("o")
            }
        };

        // 发送注册请求
        await registryClient.SendAsync(registration, MessageIds.ServiceRegistration);

        _logger.LogInformation($"Registered GameServer with Zone {_zoneId}, Server {serverId}");

        // 开始定期发送心跳
        _ = SendHeartbeatsAsync(serviceType, serverId);
    }

    private async Task SendHeartbeatsAsync(string serviceType, string serverId)
    {
        while (true)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15));

                var registryClient = await GetServiceClientAsync("ServiceRegistry");

                var heartbeat = new ServiceHeartbeat
                {
                    ServiceType = serviceType,
                    ZoneId = _zoneId,
                    ServerId = serverId,
                    Timestamp = DateTime.UtcNow,
                    Metrics = new Dictionary<string, object>
                    {
                        ["ConnectionCount"] = _server.ConnectionCount,
                        ["CpuUsage"] = GetCpuUsage(),
                        ["MemoryUsage"] = GetMemoryUsage()
                    }
                };

                await registryClient.SendAsync(heartbeat, MessageIds.ServiceHeartbeat);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to send heartbeat: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    private bool ShouldCacheDirectConnection(string serviceType)
    {
        // 对于频繁通信的服务类型启用直连缓存
        return serviceType switch
        {
            "GameServer" => true,
            "BattleServer" => true,
            "ChatServer" => true,
            _ => false
        };
    }

    public void Dispose()
    {
        foreach (var client in _directConnections.Values)
        {
            client.DisconnectAsync().GetAwaiter().GetResult();
        }

        _directConnections.Clear();
    }

    // 辅助方法
    private string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }

        return "127.0.0.1";
    }

    private double GetCpuUsage()
    {
        return CpuUsageHelper.GetCpuUsage();
    }

    private long GetMemoryUsage()
    {
        return GC.GetTotalMemory(false);
    }
}
