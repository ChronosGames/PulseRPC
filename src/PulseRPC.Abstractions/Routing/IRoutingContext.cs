using System.Collections.Generic;

namespace PulseRPC.Routing;

/// <summary>
/// 路由上下文 - 用于选择服务实例
/// </summary>
public interface IRoutingContext
{
    /// <summary>
    /// 路由键（如：房间ID、用户ID、地区代码等）
    /// </summary>
    string RoutingKey { get; }

    /// <summary>
    /// 附加路由参数
    /// </summary>
    IReadOnlyDictionary<string, object> Parameters { get; }

    /// <summary>
    /// 亲和性标识（倾向于使用特定实例）
    /// </summary>
    string? AffinityId { get; }
}

/// <summary>
/// 基础路由上下文实现
/// </summary>
public class RoutingContext : IRoutingContext
{
    public string RoutingKey { get; set; } = "";
    public IReadOnlyDictionary<string, object> Parameters { get; private set; } = new Dictionary<string, object>();
    public string? AffinityId { get; set; }

    private readonly Dictionary<string, object> _parameters = new();

    public RoutingContext()
    {
        Parameters = _parameters;
    }

    /// <summary>
    /// 添加路由参数
    /// </summary>
    public RoutingContext WithParameter(string key, object value)
    {
        _parameters[key] = value;
        return this;
    }

    /// <summary>
    /// 设置亲和性标识
    /// </summary>
    public RoutingContext WithAffinity(string affinityId)
    {
        AffinityId = affinityId;
        return this;
    }

    /// <summary>
    /// 通过键创建路由上下文
    /// </summary>
    public static RoutingContext ByKey(string key) => new() { RoutingKey = key };

    /// <summary>
    /// 通过战斗房间创建路由上下文
    /// </summary>
    public static RoutingContext ByBattleRoom(string roomId) => new() 
    { 
        RoutingKey = roomId, 
        AffinityId = $"battle:{roomId}" 
    };

    /// <summary>
    /// 通过用户ID创建路由上下文
    /// </summary>
    public static RoutingContext ByUserId(string userId) => new() { RoutingKey = userId };

    /// <summary>
    /// 通过地区创建路由上下文
    /// </summary>
    public static RoutingContext ByRegion(string region) => new() 
    { 
        RoutingKey = region, 
        AffinityId = $"region:{region}" 
    };
} 