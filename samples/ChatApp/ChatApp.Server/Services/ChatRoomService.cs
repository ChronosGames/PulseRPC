using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChatApp.NewArchitecture.Contracts;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Services;
using PulseRPC.Server.Services.Management;

namespace ChatApp.NewArchitecture.Services;

/// <summary>
/// 聊天室服务 - 有状态，每个房间一个实例
/// </summary>
/// <remarks>
/// <para><strong>核心特性</strong>：</para>
/// <list type="bullet">
/// <item><description>继承 <see cref="UnifiedPulseServiceBase"/>，获得消息队列支持</description></item>
/// <item><description>每个房间（RoomId）对应一个独立实例</description></item>
/// <item><description>所有状态修改都在队列中顺序执行，无需加锁</description></item>
/// <item><description>由 <see cref="UnifiedServiceManager"/> 管理生命周期</description></item>
/// </list>
/// <para><strong>状态管理</strong>：</para>
/// <code>
/// // 这些状态只在队列线程中访问，线程安全
/// private readonly HashSet&lt;string&gt; _members;      // 房间成员
/// private readonly List&lt;ChatMessage&gt; _messages;   // 消息历史
/// </code>
/// </remarks>
[PulseService(
    Scenario = ServiceScenario.Actor,  // 单线程顺序执行，保证消息顺序和线程安全
    StartupType = ServiceStartupType.OnDemand,           // 按需创建
    InstanceScope = ServiceInstanceScope.MultiInstance)]  // 每个房间一个实例
public class ChatRoomService : UnifiedPulseServiceBase
{
    // ════════════════════════════════════════════════════════════════════════
    // 房间状态（只在队列中访问，无需加锁）
    // ════════════════════════════════════════════════════════════════════════

    private readonly HashSet<string> _members = new();
    private readonly Dictionary<string, string> _userNames = new(); // userId -> userName
    private readonly List<ChatMessage> _messages = new();
    private long _nextMessageId = 1;

    /// <summary>
    /// 房间 ID（= ServiceId）
    /// </summary>
    public string RoomId => ServiceId;

    /// <summary>
    /// 房间成员数
    /// </summary>
    public int MemberCount => _members.Count;

    // ════════════════════════════════════════════════════════════════════════
    // 构造函数
    // ════════════════════════════════════════════════════════════════════════

    public ChatRoomService(string roomId, ILogger<ChatRoomService> logger)
        : base("ChatRoom", roomId, logger)
    {
    }

    // ════════════════════════════════════════════════════════════════════════
    // 生命周期钩子
    // ════════════════════════════════════════════════════════════════════════

    public override Task OnStartingAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("ChatRoom {RoomId} starting", RoomId);
        return Task.CompletedTask;
    }

    public override Task OnStoppingAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("ChatRoom {RoomId} stopping with {MemberCount} members",
            RoomId, _members.Count);
        return Task.CompletedTask;
    }

    // ════════════════════════════════════════════════════════════════════════
    // 业务方法（由 Hub 通过 EnqueueAsync 调用）
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 加入房间
    /// </summary>
    public Task<JoinRoomResult> JoinAsync(string userId, string userName)
    {
        if (_members.Contains(userId))
        {
            Logger.LogWarning("User {UserId} already in room {RoomId}", userId, RoomId);
            return Task.FromResult(JoinRoomResult.Failed("Already in room"));
        }

        _members.Add(userId);
        _userNames[userId] = userName;

        Logger.LogInformation("User {UserName}({UserId}) joined room {RoomId}, members: {Count}",
            userName, userId, RoomId, _members.Count);

        // 添加系统消息
        AddSystemMessage($"{userName} joined the room");

        return Task.FromResult(JoinRoomResult.Ok(RoomId, _members.Count));
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    public Task<SendMessageResult> SendMessageAsync(string userId, string content)
    {
        if (!_members.Contains(userId))
        {
            return Task.FromResult(SendMessageResult.Failed("Not in room"));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Task.FromResult(SendMessageResult.Failed("Empty message"));
        }

        var userName = _userNames.GetValueOrDefault(userId, userId);
        var message = new ChatMessage
        {
            Id = _nextMessageId++,
            UserId = userId,
            UserName = userName,
            Content = content,
            Timestamp = DateTime.UtcNow
        };

        _messages.Add(message);

        // 限制消息历史长度
        if (_messages.Count > 1000)
        {
            _messages.RemoveRange(0, _messages.Count - 1000);
        }

        Logger.LogDebug("Room {RoomId}: {UserName} says: {Content}",
            RoomId, userName, content);

        return Task.FromResult(SendMessageResult.Ok(message.Id));
    }

    /// <summary>
    /// 获取成员列表
    /// </summary>
    public Task<string[]> GetMembersAsync()
    {
        return Task.FromResult(_userNames.Values.ToArray());
    }

    /// <summary>
    /// 获取最近消息
    /// </summary>
    public Task<ChatMessage[]> GetRecentMessagesAsync(int count)
    {
        var messages = _messages
            .TakeLast(Math.Min(count, 100))
            .ToArray();

        return Task.FromResult(messages);
    }

    /// <summary>
    /// 离开房间
    /// </summary>
    public Task<bool> LeaveAsync(string userId)
    {
        if (!_members.Contains(userId))
        {
            return Task.FromResult(false);
        }

        _members.Remove(userId);
        var userName = _userNames.GetValueOrDefault(userId, userId);
        _userNames.Remove(userId);

        Logger.LogInformation("User {UserName}({UserId}) left room {RoomId}, members: {Count}",
            userName, userId, RoomId, _members.Count);

        // 添加系统消息
        AddSystemMessage($"{userName} left the room");

        return Task.FromResult(true);
    }

    /// <summary>
    /// 检查用户是否在房间内
    /// </summary>
    public bool IsMember(string userId)
    {
        return _members.Contains(userId);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 内部方法
    // ════════════════════════════════════════════════════════════════════════

    private void AddSystemMessage(string content)
    {
        _messages.Add(new ChatMessage
        {
            Id = _nextMessageId++,
            UserId = "system",
            UserName = "System",
            Content = content,
            Timestamp = DateTime.UtcNow
        });
    }
}

