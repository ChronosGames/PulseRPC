using DistributedGameApp.Shared.Messages;
using PulseRPC;
using System;
using System.Threading.Tasks;

namespace DistributedGameApp.Shared.Hubs
{

/// <summary>
/// 玩家服务 Hub - 客户端可调用的玩家相关方法
/// </summary>
/// <remarks>
/// 这个接口定义了客户端可以调用的所有玩家相关方法。
/// 每个方法都会被分配一个唯一的协议号（通过源生成器自动生成）。
/// </remarks>
public interface IPlayerHub : IPulseHub
{
    /// <summary>
    /// 获取玩家信息
    /// </summary>
    /// <returns>玩家信息</returns>
    Task<PlayerInfo?> GetPlayerInfoAsync();

    /// <summary>
    /// 移动玩家位置
    /// </summary>
    /// <param name="request">移动请求</param>
    /// <returns>移动结果</returns>
    Task<MoveResult> MoveAsync(MoveRequest request);

    /// <summary>
    /// 升级玩家
    /// </summary>
    /// <returns>升级后的玩家信息</returns>
    Task<PlayerInfo?> LevelUpAsync();

    /// <summary>
    /// 增加经验值
    /// </summary>
    /// <param name="exp">经验值</param>
    /// <returns>增加后的玩家信息</returns>
    Task<PlayerInfo?> AddExpAsync(long exp);
}
}
