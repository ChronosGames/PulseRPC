namespace GameApp.BattleServer.Configuration;

/// <summary>
/// BattleServer 配置选项
/// </summary>
public class BattleServerOptions
{
    public const string SectionName = "BattleServer";

    /// <summary>
    /// TCP 端口
    /// </summary>
    public int TcpPort { get; set; } = 8000;

    /// <summary>
    /// KCP 端口 (主要用于战斗)
    /// </summary>
    public int KcpPort { get; set; } = 8001;

    /// <summary>
    /// 服务器ID
    /// </summary>
    public string ServerId { get; set; } = "battleserver-dev";

    /// <summary>
    /// 服务器名称
    /// </summary>
    public string ServerName { get; set; } = "BattleServer Development";

    /// <summary>
    /// 最大战斗房间数
    /// </summary>
    public int MaxBattleRooms { get; set; } = 100;

    /// <summary>
    /// 每个房间最大玩家数
    /// </summary>
    public int MaxPlayersPerRoom { get; set; } = 10;

    /// <summary>
    /// 战斗帧率 (每秒tick数)
    /// </summary>
    public int BattleTickRate { get; set; } = 20;

    /// <summary>
    /// 技能释放间隔检查频率 (毫秒)
    /// </summary>
    public int SkillCheckInterval { get; set; } = 50;

    /// <summary>
    /// 战斗超时时间 (分钟)
    /// </summary>
    public int BattleTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// 启用调试模式
    /// </summary>
    public bool EnableDebugMode { get; set; }

    /// <summary>
    /// 启用详细战斗日志
    /// </summary>
    public bool EnableVerboseBattleLogging { get; set; }
}
