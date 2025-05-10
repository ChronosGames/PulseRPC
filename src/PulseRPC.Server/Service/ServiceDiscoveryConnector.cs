using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

public class ServiceDiscoveryConnector : IDisposable
{
    private readonly IServiceDiscovery _serviceDiscovery;
    private readonly ILogger _logger;
    private readonly Dictionary<string, List<ServiceNode>> _serviceCache;
    private readonly Dictionary<string, List<TcpClient>> _clientPool;
    private readonly SemaphoreSlim _cacheLock;
    private readonly Timer _refreshTimer;
    private readonly TimeSpan _cacheRefreshInterval;
    private readonly ClientOptions _defaultClientOptions;

    public ServiceDiscoveryConnector(
        IServiceDiscovery serviceDiscovery,
        ILogger logger,
        TimeSpan cacheRefreshInterval,
        ClientOptions? defaultClientOptions = null)
    {
        _serviceDiscovery = serviceDiscovery;
        _logger = logger;
        _serviceCache = new Dictionary<string, List<ServiceNode>>();
        _clientPool = new Dictionary<string, List<TcpClient>>();
        _cacheLock = new SemaphoreSlim(1, 1);
        _cacheRefreshInterval = cacheRefreshInterval;
        _defaultClientOptions = defaultClientOptions ?? new ClientOptions();

        // 启动定时刷新定时器
        _refreshTimer = new Timer(
            RefreshServiceCache,
            null,
            TimeSpan.Zero,
            _cacheRefreshInterval);
    }

    private async void RefreshServiceCache(object? state)
    {
        try
        {
            await _cacheLock.WaitAsync();

            try
            {
                foreach (var serviceType in _serviceCache.Keys.ToList())
                {
                    var nodes = await _serviceDiscovery.GetServiceNodesAsync(serviceType);
                    _serviceCache[serviceType] = nodes.ToList();

                    // 更新连接池
                    await UpdateClientPoolAsync(serviceType);
                }
            }
            finally
            {
                _cacheLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing service cache");
        }
    }

    private async Task UpdateClientPoolAsync(string serviceType)
    {
        if (!_serviceCache.TryGetValue(serviceType, out var nodes) || nodes.Count == 0)
            return;

        if (!_clientPool.TryGetValue(serviceType, out var clients))
        {
            clients = new List<TcpClient>();
            _clientPool[serviceType] = clients;
        }

        // 移除不再存在的节点连接
        // var nodeAddresses = nodes.Select(n => $"{n.Host}:{n.Port}").ToHashSet();
        // for (int i = clients.Count - 1; i >= 0; i--)
        // {
        //     var client = clients[i];
        //     if (client.Status != ClientStatus.Connected ||
        //         !nodeAddresses.Contains(client.RemoteEndpoint))
        //     {
        //         clients.RemoveAt(i);
        //         await client.DisconnectAsync();
        //         client.Dispose();
        //     }
        // }

        // 为新节点创建连接
        // foreach (var node in nodes)
        // {
        //     var nodeAddress = $"{node.Host}:{node.Port}";
        //     if (!clients.Any(c => c.RemoteEndpoint == nodeAddress && c.Status == ClientStatus.Connected))
        //     {
        //         try
        //         {
        //             var client = _clientFactory.CreateTcpClient();
        //
        //             // 应用服务特定选项
        //             var options = GetClientOptionsForService(serviceType);
        //
        //             await client.ConnectAsync(node.Host, node.Port, options);
        //             clients.Add(client);
        //         }
        //         catch (Exception ex)
        //         {
        //             _logger.LogError(ex, $"Failed to connect to {nodeAddress}");
        //         }
        //     }
        // }
    }

    private ClientOptions GetClientOptionsForService(string serviceType)
    {
        // 根据服务类型定制连接选项
        var options = _defaultClientOptions;

        switch (serviceType)
        {
            case "AccountServer":
                options.UseEncryption = true;
                options.ConnectionTimeout = TimeSpan.FromSeconds(30);
                break;

            case "BattleServer":
                options.NoDelay = true;
                options.ConnectionTimeout = TimeSpan.FromSeconds(5);
                break;

            case "ChatServer":
                options.AutoReconnect = true;
                options.ReconnectAttempts = 5;
                break;
        }

        return options;
    }

    public async Task<TcpClient> GetClientAsync(string serviceType, ServiceSelectionStrategy strategy = ServiceSelectionStrategy.RoundRobin)
    {
        await _cacheLock.WaitAsync();

        try
        {
            // 确保服务已缓存
            if (!_serviceCache.ContainsKey(serviceType))
            {
                var nodes = await _serviceDiscovery.GetServiceNodesAsync(serviceType);
                _serviceCache[serviceType] = nodes.ToList();
                await UpdateClientPoolAsync(serviceType);
            }

            // 获取可用客户端
            if (!_clientPool.TryGetValue(serviceType, out var clients) || clients.Count == 0)
                throw new ServiceUnavailableException($"No available clients for service {serviceType}");

            // 选择客户端
            return SelectClient(clients, strategy);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private TcpClient SelectClient(List<TcpClient> clients, ServiceSelectionStrategy strategy)
    {
        // 过滤已连接的客户端
        var availableClients = clients.Where(c => c.Status == ClientStatus.Connected).ToList();

        if (availableClients.Count == 0)
            throw new ServiceUnavailableException("No connected clients available");

        switch (strategy)
        {
            case ServiceSelectionStrategy.RoundRobin:
                // 简单轮询
                var index = Interlocked.Increment(ref _roundRobinCounter) % availableClients.Count;
                return availableClients[index];

            case ServiceSelectionStrategy.Random:
                // 随机选择
                return availableClients[Random.Shared.Next(availableClients.Count)];

            case ServiceSelectionStrategy.LeastConnections:
                // 最少连接
                return availableClients.OrderBy<TcpClient, object>(c => c.ConnectionCount).First();

            default:
                return availableClients[0];
        }
    }

    private int _roundRobinCounter = -1;

    public void Dispose()
    {
        _refreshTimer.Dispose();

        foreach (var clients in _clientPool.Values)
        {
            foreach (var client in clients)
            {
                client.DisconnectAsync().GetAwaiter().GetResult();
                client.Dispose();
            }
        }

        _clientPool.Clear();
        _serviceCache.Clear();
        _cacheLock.Dispose();
    }
}

public enum ServiceSelectionStrategy
{
    RoundRobin,
    Random,
    LeastConnections
}
