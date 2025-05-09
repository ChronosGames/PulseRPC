using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server;

/// <summary>
/// 基于IServiceRegistry的服务发现实现
/// </summary>
public class ServiceDiscovery : IServiceDiscovery
{
    private readonly IServiceRegistry _registry;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, ServiceNode> _nodeCache;
    private readonly ConcurrentDictionary<string, List<ServiceWatcher>> _watchers;
    private readonly Timer _refreshTimer;
    private readonly TimeSpan _cacheRefreshInterval;
    private readonly SemaphoreSlim _cacheLock;

    public ServiceDiscovery(
        IServiceRegistry registry,
        ILogger logger,
        TimeSpan? cacheRefreshInterval = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _nodeCache = new ConcurrentDictionary<string, ServiceNode>();
        _watchers = new ConcurrentDictionary<string, List<ServiceWatcher>>();
        _cacheRefreshInterval = cacheRefreshInterval ?? TimeSpan.FromSeconds(10);
        _cacheLock = new SemaphoreSlim(1, 1);

        // 启动定时刷新定时器
        _refreshTimer = new Timer(
            RefreshCache,
            null,
            TimeSpan.Zero,
            _cacheRefreshInterval);
    }

    public async Task<IEnumerable<ServiceNode>> GetServiceNodesAsync(string serviceType)
    {
        var registrations = await _registry.GetServicesAsync(serviceType);
        var nodes = new List<ServiceNode>();

        foreach (var registration in registrations)
        {
            var nodeKey = GetNodeKey(registration.ServiceType, GetServiceId(registration));
            var node = _nodeCache.GetOrAdd(nodeKey, _ => ServiceNode.FromRegistration(registration));
            nodes.Add(node);
        }

        return nodes;
    }

    public async Task<ServiceNode?> GetServiceNodeAsync(string serviceType, string serviceId)
    {
        var registration = await _registry.GetServiceAsync(serviceType, serviceId);
        if (registration == null)
            return null;

        var nodeKey = GetNodeKey(serviceType, serviceId);
        return _nodeCache.GetOrAdd(nodeKey, _ => ServiceNode.FromRegistration(registration));
    }

    public async Task<IEnumerable<ServiceNode>> GetServiceNodesByZoneAsync(string serviceType, string zoneId)
    {
        var allNodes = await GetServiceNodesAsync(serviceType);
        return allNodes.Where(n => n.ZoneId == zoneId);
    }

    public string WatchServiceChanges(string serviceType, Action<ServiceChangeEvent> callback)
    {
        var watcherId = Guid.NewGuid().ToString();
        var watcher = new ServiceWatcher(watcherId, callback);

        _watchers.AddOrUpdate(
            serviceType,
            _ => new List<ServiceWatcher> { watcher },
            (_, list) =>
            {
                list.Add(watcher);
                return list;
            });

        return watcherId;
    }

    public void UnwatchServiceChanges(string watcherId)
    {
        foreach (var watcherList in _watchers.Values)
        {
            watcherList.RemoveAll(w => w.Id == watcherId);
        }
    }

    private async void RefreshCache(object? state)
    {
        try
        {
            await _cacheLock.WaitAsync();

            try
            {
                var serviceTypes = _nodeCache.Keys
                    .Select(key => key.Split(':')[0])
                    .Distinct()
                    .ToList();

                foreach (var serviceType in serviceTypes)
                {
                    var registrations = await _registry.GetServicesAsync(serviceType);
                    var currentNodes = registrations.ToDictionary(
                        r => GetNodeKey(r.ServiceType, GetServiceId(r)),
                        r => ServiceNode.FromRegistration(r));

                    // 查找新增和更新的节点
                    foreach (var kvp in currentNodes)
                    {
                        var nodeKey = kvp.Key;
                        var newNode = kvp.Value;

                        if (_nodeCache.TryGetValue(nodeKey, out var oldNode))
                        {
                            // 节点已存在，检查是否有更新
                            if (HasNodeChanged(oldNode, newNode))
                            {
                                _nodeCache[nodeKey] = newNode;
                                NotifyWatchers(serviceType, new ServiceChangeEvent
                                {
                                    ChangeType = ServiceChangeType.Update,
                                    Node = newNode
                                });
                            }
                        }
                        else
                        {
                            // 新节点
                            _nodeCache[nodeKey] = newNode;
                            NotifyWatchers(serviceType, new ServiceChangeEvent
                            {
                                ChangeType = ServiceChangeType.Register,
                                Node = newNode
                            });
                        }
                    }

                    // 查找已删除的节点
                    var removedKeys = _nodeCache.Keys
                        .Where(k => k.StartsWith($"{serviceType}:"))
                        .Where(k => !currentNodes.ContainsKey(k))
                        .ToList();

                    foreach (var key in removedKeys)
                    {
                        if (_nodeCache.TryRemove(key, out var removedNode))
                        {
                            NotifyWatchers(serviceType, new ServiceChangeEvent
                            {
                                ChangeType = ServiceChangeType.Unregister,
                                Node = removedNode
                            });
                        }
                    }
                }
            }
            finally
            {
                _cacheLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刷新服务节点缓存时出错");
        }
    }

    private string GetNodeKey(string serviceType, string serviceId)
    {
        return $"{serviceType}:{serviceId}";
    }

    private string GetServiceId(ServiceRegistration registration)
    {
        if (!string.IsNullOrEmpty(registration.InstanceId))
        {
            return $"{registration.ZoneId}:{registration.InstanceId}";
        }
        else if (!string.IsNullOrEmpty(registration.ServerId))
        {
            return $"{registration.ZoneId}:{registration.ServerId}";
        }
        else
        {
            return $"{registration.ZoneId}:{registration.Host}:{registration.Port}";
        }
    }

    private bool HasNodeChanged(ServiceNode oldNode, ServiceNode newNode)
    {
        return oldNode.Host != newNode.Host ||
               oldNode.Port != newNode.Port ||
               oldNode.UdpPort != newNode.UdpPort ||
               oldNode.Health != newNode.Health ||
               !oldNode.Metadata.SequenceEqual(newNode.Metadata);
    }

    private void NotifyWatchers(string serviceType, ServiceChangeEvent changeEvent)
    {
        if (_watchers.TryGetValue(serviceType, out var watchers))
        {
            foreach (var watcher in watchers)
            {
                try
                {
                    watcher.Callback(changeEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"调用服务变更监听器时出错: {watcher.Id}");
                }
            }
        }
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
        _cacheLock?.Dispose();
        _watchers.Clear();
        _nodeCache.Clear();
    }
}

/// <summary>
/// 服务变更监听器
/// </summary>
internal class ServiceWatcher
{
    public string Id { get; }
    public Action<ServiceChangeEvent> Callback { get; }

    public ServiceWatcher(string id, Action<ServiceChangeEvent> callback)
    {
        Id = id;
        Callback = callback;
    }
}
