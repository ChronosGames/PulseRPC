using DistributedGameApp.Shared.Messages;
using PulseRPC;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DistributedGameApp.Shared.Hubs
{

/// <summary>
/// 聊天室服务 Hub - 客户端可调用的聊天室相关方法
/// </summary>
/// <remarks>
/// 这个接口定义了客户端可以调用的所有聊天室相关方法。
/// 每个房间是一个独立的 IPulseService 实例，确保房间内的消息顺序处理。
/// </remarks>
public interface IChatRoomHub : IPulseHub
{
    /// <summary>
    /// 加入聊天室
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <param name="playerName">玩家名称</param>
    /// <returns>是否成功</returns>
    Task<bool> JoinRoomAsync(string playerId, string playerName);

    /// <summary>
    /// 离开聊天室
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <returns>是否成功</returns>
    Task<bool> LeaveRoomAsync(string playerId);

    /// <summary>
    /// 发送消息
    /// </summary>
    /// <param name="request">发送消息请求</param>
    /// <returns>发送结果</returns>
    Task<SendMessageResult> SendMessageAsync(SendMessageRequest request);

    /// <summary>
    /// 获取房间信息
    /// </summary>
    /// <returns>房间信息</returns>
    Task<RoomInfo?> GetRoomInfoAsync();

    /// <summary>
    /// 获取房间成员列表
    /// </summary>
    /// <returns>成员ID列表</returns>
    Task<string[]> GetMembersAsync();
}
}
