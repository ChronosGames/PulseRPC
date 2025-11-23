using MemoryPack;
using System.Collections.Generic;

namespace DistributedGameApp.Shared.Messages;

/// <summary>
/// 创建战斗房间请求
/// </summary>
[MemoryPackable]
public partial class CreateBattleRoomRequest
{
    /// <summary>
    /// 匹配ID（用作房间ID）
    /// </summary>
    public string MatchId { get; set; } = string.Empty;

    /// <summary>
    /// 匹配类型（1v1, 2v2, 3v3, 5v5）
    /// </summary>
    public string MatchType { get; set; } = string.Empty;

    /// <summary>
    /// 参与玩家ID列表
    /// </summary>
    public List<string> PlayerIds { get; set; } = new();
}

/// <summary>
/// 创建战斗房间响应
/// </summary>
[MemoryPackable]
public partial class CreateBattleRoomResponse
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 房间ID
    /// </summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>
    /// 战斗服务器地址
    /// </summary>
    public string ServerHost { get; set; } = string.Empty;

    /// <summary>
    /// 战斗服务器端口
    /// </summary>
    public int ServerPort { get; set; }

    /// <summary>
    /// 访问令牌（用于加入房间）
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;
}
