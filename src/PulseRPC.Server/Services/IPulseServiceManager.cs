namespace PulseRPC.Server;

/// <summary>
/// PulseRPC 服务管理器 - 管理多实例服务生命周期
/// </summary>
public interface IPulseServiceManager : IDisposable
{
    // 服务创建与管理
    Task<TService> CreateServiceAsync<TService>(string serviceId) where TService : class;
    Task<TService> GetOrCreateServiceAsync<TService>(string serviceId) where TService : class;

    // 服务查询与移除
    bool TryGetService<TService>(string serviceId, out TService? service) where TService : class;
    Task<bool> TryRemoveServiceAsync<TService>(string serviceId) where TService : class;

    // 批量操作
    IEnumerable<KeyValuePair<string, TService>> GetAllServices<TService>() where TService : class;
    Task<int> RemoveExpiredServicesAsync<TService>(TimeSpan expireAfter) where TService : class;

    // 统计信息
    ServiceManagerStats GetStats();
}

public class ServiceManagerStats
{
    public int TotalServices { get; set; }
    public int ActiveServices { get; set; }
    public long TotalCreated { get; set; }
    public long TotalRemoved { get; set; }
    public Dictionary<Type, int> ServiceTypeStats { get; set; } = new();
}

