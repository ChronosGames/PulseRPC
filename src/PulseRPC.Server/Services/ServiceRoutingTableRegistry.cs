using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Server.Services;

namespace PulseRPC.Server;

/// <summary>
/// 服务路由表注册中心，用于组合各程序集由 Source Generator 生成的路由表。
/// </summary>
public static class ServiceRoutingTableRegistry
{
    private static readonly Lock _lock = new();
    private static readonly List<IServiceRoutingTable> _routingTables = new();
    private static readonly IServiceRoutingTable _compositeRoutingTable = new CompositeServiceRoutingTable();

    /// <summary>获取所有已注册路由表的组合视图。</summary>
    public static IServiceRoutingTable? Instance
    {
        get
        {
            using (_lock.EnterScope())
            {
                return _routingTables.Count == 0 ? null : _compositeRoutingTable;
            }
        }
    }

    /// <summary>检查是否至少注册了一个路由表。</summary>
    public static bool IsRegistered => Instance is not null;

    /// <summary>注册当前程序集生成的路由表。</summary>
    public static void Register(IServiceRoutingTable routingTable)
    {
        ArgumentNullException.ThrowIfNull(routingTable);

        using (_lock.EnterScope())
        {
            if (_routingTables.Contains(routingTable))
            {
                return;
            }

            _routingTables.Add(routingTable);
            System.Diagnostics.Debug.WriteLine($"[PulseRPC] ServiceRoutingTable registered: {routingTable.GetType().FullName}");
        }
    }

    /// <summary>清除注册（仅用于测试）。</summary>
    internal static void Clear()
    {
        using (_lock.EnterScope())
        {
            _routingTables.Clear();
        }
    }

    private static IServiceRoutingTable[] GetSnapshot()
    {
        using (_lock.EnterScope())
        {
            return _routingTables.ToArray();
        }
    }

    private sealed class CompositeServiceRoutingTable : IServiceRoutingTable
    {
        public bool IsProtocolIdValid(string hub, ushort protocolId)
        {
            foreach (var routingTable in GetSnapshot())
            {
                if (routingTable.IsProtocolIdValid(hub, protocolId))
                {
                    return true;
                }
            }

            return false;
        }

        public ReadOnlySpan<ushort> EnumerateProtocolIds()
        {
            var protocolIds = new HashSet<ushort>();
            foreach (var routingTable in GetSnapshot())
            {
                foreach (var protocolId in routingTable.EnumerateProtocolIds())
                {
                    protocolIds.Add(protocolId);
                }
            }

            return protocolIds.ToArray();
        }

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            ushort protocolId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => ResolveByProtocolId(protocolId).RouteByProtocolIdAsync(
                serviceProvider,
                protocolId,
                data,
                cancellationToken);

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            string hub,
            ushort protocolId,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => ResolveByHub(hub, protocolId).RouteByProtocolIdAsync(
                serviceProvider,
                hub,
                protocolId,
                data,
                cancellationToken);

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            ushort protocolId,
            string serviceKey,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => ResolveByProtocolId(protocolId).RouteByProtocolIdAsync(
                serviceProvider,
                protocolId,
                serviceKey,
                data,
                cancellationToken);

        public ValueTask<object?> RouteByProtocolIdAsync(
            IServiceProvider serviceProvider,
            string hub,
            ushort protocolId,
            string serviceKey,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => ResolveByHub(hub, protocolId).RouteByProtocolIdAsync(
                serviceProvider,
                hub,
                protocolId,
                serviceKey,
                data,
                cancellationToken);

        private static IServiceRoutingTable ResolveByHub(string hub, ushort protocolId)
            => ResolveSingle(
                table => table.IsProtocolIdValid(hub, protocolId),
                $"canonical Hub '{hub}' and protocol ID 0x{protocolId:X4}");

        private static IServiceRoutingTable ResolveByProtocolId(ushort protocolId)
            => ResolveSingle(
                table => ContainsProtocolId(table, protocolId),
                $"protocol ID 0x{protocolId:X4}");

        private static IServiceRoutingTable ResolveSingle(
            Func<IServiceRoutingTable, bool> predicate,
            string routeDescription)
        {
            IServiceRoutingTable? match = null;
            foreach (var routingTable in GetSnapshot())
            {
                if (!predicate(routingTable))
                {
                    continue;
                }

                if (match is not null)
                {
                    throw new InvalidOperationException(
                        $"Multiple generated service routing tables contain {routeDescription}. " +
                        "Use unique canonical Hub names and protocol IDs for composed assemblies.");
                }

                match = routingTable;
            }

            return match ?? throw new ProtocolIdNotFoundException(
                $"No generated service routing table contains {routeDescription}.");
        }

        private static bool ContainsProtocolId(IServiceRoutingTable routingTable, ushort protocolId)
        {
            foreach (var candidate in routingTable.EnumerateProtocolIds())
            {
                if (candidate == protocolId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
