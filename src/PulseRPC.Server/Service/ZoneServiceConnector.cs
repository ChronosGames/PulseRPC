using Microsoft.Extensions.Logging;
using PulseNet.Server;

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
}
