namespace GameApp.GameServer.Configuration;

/// <summary>
/// GameServer 配置选项
/// </summary>
public class GameServerOptions
{
    public const string SectionName = "GameServer";

    /// <summary>
    /// TCP 端口
    /// </summary>
    public int TcpPort { get; set; } = 7000;

    /// <summary>
    /// KCP 端口
    /// </summary>
    public int KcpPort { get; set; } = 7001;

    /// <summary>
    /// 服务器ID
    /// </summary>
    public string ServerId { get; set; } = "gameserver-dev";

    /// <summary>
    /// 服务器名称
    /// </summary>
    public string ServerName { get; set; } = "GameServer Development";

    /// <summary>
    /// 最大连接数
    /// </summary>
    public int MaxConnections { get; set; } = 1000;

    /// <summary>
    /// 心跳间隔（秒）
    /// </summary>
    public int HeartbeatInterval { get; set; } = 30;

    /// <summary>
    /// 玩家位置更新间隔（毫秒）
    /// </summary>
    public int PositionUpdateInterval { get; set; } = 100;

    /// <summary>
    /// 世界状态更新间隔（毫秒）
    /// </summary>
    public int WorldUpdateInterval { get; set; } = 1000;

    /// <summary>
    /// 启用调试模式
    /// </summary>
    public bool EnableDebugMode { get; set; }
}
