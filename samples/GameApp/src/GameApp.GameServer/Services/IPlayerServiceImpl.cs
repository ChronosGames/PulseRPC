using GameApp.Shared.Services;

namespace GameApp.GameServer.Services;

/// <summary>
/// 玩家服务实现接口
/// </summary>
public interface IPlayerServiceImpl
{
    /// <summary>
    /// 玩家登录游戏服务器
    /// </summary>
    Task<LoginResponse> LoginAsync(LoginRequest request);

    /// <summary>
    /// 获取玩家信息
    /// </summary>
    Task<PlayerInfo> GetPlayerInfoAsync(GetPlayerInfoRequest request);

    /// <summary>
    /// 更新玩家位置
    /// </summary>
    Task UpdatePositionAsync(UpdatePositionRequest request);

    /// <summary>
    /// 玩家登出
    /// </summary>
    Task LogoutAsync(LogoutRequest request);

    /// <summary>
    /// 获取玩家统计信息
    /// </summary>
    Task<PlayerStatistics> GetStatisticsAsync(GetStatisticsRequest request);
}

/// <summary>
/// 世界服务实现接口
/// </summary>
public interface IWorldServiceImpl
{
    /// <summary>
    /// 加入世界
    /// </summary>
    Task<JoinWorldResponse> JoinWorldAsync(JoinWorldRequest request);

    /// <summary>
    /// 离开世界
    /// </summary>
    Task LeaveWorldAsync(LeaveWorldRequest request);

    /// <summary>
    /// 获取世界状态
    /// </summary>
    Task<WorldState> GetWorldStateAsync(GetWorldStateRequest request);

    /// <summary>
    /// 世界聊天
    /// </summary>
    Task SendWorldChatAsync(WorldChatRequest request);

    /// <summary>
    /// 获取附近玩家
    /// </summary>
    Task<NearbyPlayersResponse> GetNearbyPlayersAsync(NearbyPlayersRequest request);
}

/// <summary>
/// 玩家事件发布器接口
/// </summary>
public interface IPlayerEventPublisher
{
    /// <summary>
    /// 发布玩家状态更新事件
    /// </summary>
    Task PublishPlayerStatusUpdateAsync(int playerId, PlayerStatusUpdateEvent eventData);

    /// <summary>
    /// 发布玩家升级事件
    /// </summary>
    Task PublishPlayerLevelUpAsync(int playerId, PlayerLevelUpEvent eventData);

    /// <summary>
    /// 发布玩家移动事件
    /// </summary>
    Task PublishPlayerMovedAsync(string worldId, PlayerMovedEvent eventData);
}

/// <summary>
/// 世界事件发布器接口
/// </summary>
public interface IWorldEventPublisher
{
    /// <summary>
    /// 发布世界更新事件
    /// </summary>
    Task PublishWorldUpdateAsync(string worldId, WorldUpdateEvent eventData);

    /// <summary>
    /// 发布玩家加入世界事件
    /// </summary>
    Task PublishPlayerJoinedAsync(string worldId, PlayerJoinedEvent eventData);

    /// <summary>
    /// 发布玩家离开世界事件
    /// </summary>
    Task PublishPlayerLeftAsync(string worldId, PlayerLeftEvent eventData);

    /// <summary>
    /// 发布世界聊天消息事件
    /// </summary>
    Task PublishWorldChatMessageAsync(string worldId, WorldChatMessageEvent eventData);

    /// <summary>
    /// 发布世界事件通知
    /// </summary>
    Task PublishWorldEventNotificationAsync(string worldId, WorldEventNotificationEvent eventData);
}
