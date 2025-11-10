namespace DistributedGameApp.BattleServer.Services.Generic;

/// <summary>
/// 服务器类型枚举
/// </summary>
public enum ServerType
{
    /// <summary>
    /// BackendServer - 匹配、排行榜等后端服务
    /// </summary>
    Backend,

    /// <summary>
    /// GameServer - 游戏服务器
    /// </summary>
    Game,

    /// <summary>
    /// BattleServer - 战斗服务器（用于跨服战斗等场景）
    /// </summary>
    Battle,

    /// <summary>
    /// ChatServer - 聊天服务
    /// </summary>
    Chat
}

/// <summary>
/// ServerType 扩展方法
/// </summary>
public static class ServerTypeExtensions
{
    /// <summary>
    /// 获取服务类型名称（用于 Consul 服务发现）
    /// </summary>
    public static string GetServiceName(this ServerType serverType)
    {
        return serverType switch
        {
            ServerType.Backend => "BackendServer",
            ServerType.Game => "GameServer",
            ServerType.Battle => "BattleServer",
            ServerType.Chat => "ChatServer",
            _ => throw new ArgumentException($"Unknown server type: {serverType}")
        };
    }
}
