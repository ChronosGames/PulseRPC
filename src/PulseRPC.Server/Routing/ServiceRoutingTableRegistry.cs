using System;
using System.Threading;

namespace PulseRPC.Server.Routing;

/// <summary>
/// 服务路由表注册中心 - 用于桥接编译时生成的 ServiceRoutingTable
/// 使用静态单例模式,在程序集加载时由 ModuleInitializer 自动注册
/// </summary>
public static class ServiceRoutingTableRegistry
{
    private static IServiceRoutingTable? _instance;
    private static readonly object _lock = new();
    private static int _initialized = 0;

    /// <summary>
    /// 获取当前注册的路由表实例
    /// </summary>
    public static IServiceRoutingTable? Instance => _instance;

    /// <summary>
    /// 检查路由表是否已注册
    /// </summary>
    public static bool IsRegistered => _instance != null;

    /// <summary>
    /// 注册路由表实例（线程安全）
    /// 此方法由编译时生成的 ServiceRoutingTable 的 ModuleInitializer 自动调用
    /// </summary>
    /// <param name="routingTable">路由表实例</param>
    public static void Register(IServiceRoutingTable routingTable)
    {
        if (routingTable == null)
            throw new ArgumentNullException(nameof(routingTable));

        // 使用 Interlocked 确保只注册一次
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
        {
            lock (_lock)
            {
                _instance = routingTable;
                System.Diagnostics.Debug.WriteLine($"[PulseRPC] ServiceRoutingTable registered: {routingTable.GetType().FullName}");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[PulseRPC] ServiceRoutingTable already registered, ignoring duplicate registration");
        }
    }

    /// <summary>
    /// 清除注册（仅用于测试）
    /// </summary>
    internal static void Clear()
    {
        lock (_lock)
        {
            _instance = null;
            Interlocked.Exchange(ref _initialized, 0);
        }
    }
}
