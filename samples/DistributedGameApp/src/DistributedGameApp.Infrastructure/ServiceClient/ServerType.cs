namespace DistributedGameApp.Infrastructure.ServiceClient;

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
    /// ChatServer - 聊天服务
    /// </summary>
    Chat,

    /// <summary>
    /// BattleServer - 战斗服务
    /// </summary>
    Battle,

    /// <summary>
    /// GuildServer - 公会服务
    /// </summary>
    Guild,

    /// <summary>
    /// MailServer - 邮件服务
    /// </summary>
    Mail,

    /// <summary>
    /// GameServer - 游戏服务
    /// </summary>
    Game
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
            ServerType.Chat => "ChatServer",
            ServerType.Battle => "BattleServer",
            ServerType.Guild => "GuildServer",
            ServerType.Mail => "MailServer",
            ServerType.Game => "GameServer",
            _ => throw new ArgumentException($"Unknown server type: {serverType}")
        };
    }
}
