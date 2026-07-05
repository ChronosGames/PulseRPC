using System;
using System.Collections.Generic;
using System.Threading;

namespace PulseRPC.Server;

/// <summary>
/// 源生成器输出的服务元数据清单。
/// </summary>
public interface IServiceManifest
{
    /// <summary>
    /// 获取当前程序集生成的服务契约列表。
    /// </summary>
    IReadOnlyList<ServiceInfo> Services { get; }
}

/// <summary>
/// 服务元数据清单注册中心。
/// </summary>
public static class ServiceManifestRegistry
{
    private static readonly Lock _lock = new();
    private static readonly List<IServiceManifest> _manifests = new();
    private static readonly IServiceManifest _compositeManifest = new CompositeServiceManifest();

    /// <summary>
    /// 当前注册的服务元数据清单。
    /// </summary>
    public static IServiceManifest? Instance
    {
        get
        {
            using (_lock.EnterScope())
            {
                return _manifests.Count == 0 ? null : _compositeManifest;
            }
        }
    }

    /// <summary>
    /// 注册服务元数据清单。
    /// </summary>
    public static void Register(IServiceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        using (_lock.EnterScope())
        {
            if (_manifests.Contains(manifest))
            {
                return;
            }

            _manifests.Add(manifest);
            System.Diagnostics.Debug.WriteLine($"[PulseRPC] ServiceManifest registered: {manifest.GetType().FullName}");
        }
    }

    /// <summary>
    /// 清除注册（仅用于测试）。
    /// </summary>
    internal static void Clear()
    {
        using (_lock.EnterScope())
        {
            _manifests.Clear();
        }
    }

    private sealed class CompositeServiceManifest : IServiceManifest
    {
        public IReadOnlyList<ServiceInfo> Services
        {
            get
            {
                using (_lock.EnterScope())
                {
                    if (_manifests.Count == 1)
                    {
                        return _manifests[0].Services;
                    }

                    var services = new List<ServiceInfo>();
                    foreach (var manifest in _manifests)
                    {
                        services.AddRange(manifest.Services);
                    }

                    return services;
                }
            }
        }
    }
}
