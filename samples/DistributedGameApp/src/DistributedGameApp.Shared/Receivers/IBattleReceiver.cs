using DistributedGameApp.Shared.Messages;
using PulseRPC;
using System.Threading.Tasks;

namespace DistributedGameApp.Shared.Receivers;

/// <summary>
/// 战斗事件接收器 - 服务器向客户端推送战斗相关事件
/// </summary>
/// <remarks>
/// 这个接口定义了服务器可以向客户端推送的所有战斗事件。
/// 用于实时同步战斗状态，确保所有玩家看到一致的战斗画面。
/// <para>
/// 标注 <see cref="ChannelAttribute"/>("CLIENT") 并继承 <see cref="IPulseHub"/> 表示这是客户端实现的接口
/// （统一 IPulseHub 架构，原 IPulseReceiver 已硬移除）。
/// 服务端使用 <c>IHubContext&lt;IBattleReceiver&gt;</c> 推送消息。
/// </para>
/// </remarks>
[Channel("CLIENT")]
public interface IBattleReceiver : IPulseHub
{
    /// <summary>
    /// 战斗开始通知
    /// </summary>
    /// <param name="battleInfo">战斗信息</param>
    Task OnBattleStartedAsync(BattleInfo battleInfo);

    /// <summary>
    /// 战斗结束通知
    /// </summary>
    /// <param name="result">战斗结果</param>
    Task OnBattleEndedAsync(BattleResult result);

    /// <summary>
    /// 玩家加入战斗通知
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <param name="playerName">玩家名称</param>
    /// <param name="team">队伍编号</param>
    Task OnPlayerJoinedBattleAsync(string playerId, string playerName, int team);

    /// <summary>
    /// 玩家离开战斗通知
    /// </summary>
    /// <param name="characterId">离开的角色ID</param>
    /// <param name="reason">离开原因</param>
    Task OnPlayerLeftBattleAsync(string characterId, string reason);

    /// <summary>
    /// 战斗动作执行通知（广播给所有玩家）
    /// </summary>
    /// <param name="action">执行的动作</param>
    Task OnBattleActionAsync(BattleAction action);

    /// <summary>
    /// 回合开始通知
    /// </summary>
    /// <param name="roundNumber">回合数</param>
    Task OnRoundStartedAsync(int roundNumber);

    /// <summary>
    /// 回合结束通知
    /// </summary>
    /// <param name="roundNumber">回合数</param>
    Task OnRoundEndedAsync(int roundNumber);

    /// <summary>
    /// 玩家准备就绪通知
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <param name="isReady">是否准备就绪</param>
    Task OnPlayerReadyAsync(string playerId, bool isReady);

    /// <summary>
    /// 玩家死亡通知
    /// </summary>
    /// <param name="characterId">死亡的角色ID</param>
    /// <param name="killerId">击杀者ID</param>
    Task OnPlayerDiedAsync(string characterId, string killerId);

    /// <summary>
    /// 玩家复活通知
    /// </summary>
    /// <param name="characterId">复活的角色ID</param>
    Task OnPlayerRespawnedAsync(string characterId);

    /// <summary>
    /// 战斗倒计时通知
    /// </summary>
    /// <param name="seconds">剩余秒数</param>
    Task OnCountdownAsync(int seconds);

    /// <summary>
    /// 玩家断线通知
    /// </summary>
    /// <param name="characterId">断线的角色ID</param>
    Task OnPlayerDisconnectedAsync(string characterId);

    /// <summary>
    /// 玩家重连通知
    /// </summary>
    /// <param name="characterId">重连的角色ID</param>
    Task OnPlayerReconnectedAsync(string characterId);
}
