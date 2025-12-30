using DistributedGameApp.Shared.Messages;
using PulseRPC;
using System.Threading.Tasks;

namespace DistributedGameApp.Shared.Hubs;

/// <summary>
/// 战斗服务 Hub - 处理战斗逻辑和房间管理
/// </summary>
/// <remarks>
/// 这个接口定义了战斗服务的所有方法。
/// BattleServer 负责：
/// - 战斗房间创建和管理
/// - 战斗逻辑处理
/// - 战斗结算
/// - 实时战斗状态同步
///
/// 设计原则：
/// - 每个战斗房间是一个独立的 Service 实例（ServiceId: Battle:{battleId}）
/// - 使用 Actor 模型保证战斗逻辑的严格有序性
/// - 所有战斗动作在单线程中顺序执行，避免并发问题
///
/// <para>
/// [Channel("BattleServer")] 表示这个接口由 BattleServer 提供。
/// </para>
/// </remarks>
[Channel("BattleServer")]
public interface IBattleHub : IPulseHub
{
    /// <summary>
    /// 加入战斗房间
    /// </summary>
    /// <param name="request">加入战斗请求</param>
    /// <returns>战斗信息</returns>
    Task<BattleInfo> JoinBattleAsync(JoinBattleRequest request);

    /// <summary>
    /// 离开战斗房间
    /// </summary>
    /// <returns>是否成功</returns>
    Task<bool> LeaveBattleAsync();

    /// <summary>
    /// 获取战斗信息
    /// </summary>
    /// <returns>战斗信息</returns>
    Task<BattleInfo> GetBattleInfoAsync();

    /// <summary>
    /// 准备就绪
    /// </summary>
    /// <returns>是否成功</returns>
    Task<bool> ReadyAsync();

    /// <summary>
    /// 执行战斗动作
    /// </summary>
    /// <param name="action">战斗动作</param>
    /// <returns>动作结果</returns>
    Task<BattleActionResult> PerformActionAsync(BattleAction action);

    /// <summary>
    /// 投降
    /// </summary>
    /// <returns>是否成功</returns>
    Task<bool> SurrenderAsync();

    /// <summary>
    /// 获取战斗历史（最近的动作）
    /// </summary>
    /// <param name="count">数量</param>
    /// <returns>动作列表</returns>
    Task<BattleAction[]> GetActionHistoryAsync(int count);

    /// <summary>
    /// 战斗心跳（确认连接）
    /// </summary>
    /// <returns>服务器时间戳</returns>
    Task<long> BattleHeartbeatAsync();

    /// <summary>
    /// 创建战斗房间（内部调用，由 MatchmakingService 使用）
    /// </summary>
    /// <param name="request">创建房间请求</param>
    /// <returns>房间信息</returns>
    Task<CreateBattleRoomResponse> CreateBattleRoomAsync(CreateBattleRoomRequest request);
}
