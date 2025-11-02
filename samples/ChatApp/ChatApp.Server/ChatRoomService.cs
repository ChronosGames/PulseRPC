using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using PulseRPC.Server.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ChatApp;

/// <summary>
/// 聊天室服务 - 基于服务隔离架构
/// </summary>
/// <remarks>
/// <para>
/// <strong>服务隔离特性</strong>:
/// </para>
/// <list type="bullet">
/// <item><description>每个聊天室有独立的服务实例</description></item>
/// <item><description>相同房间的所有消息在同一线程顺序处理（无需加锁）</description></item>
/// <item><description>不同房间的消息可并发处理（不同线程）</description></item>
/// <item><description>单个房间故障不影响其他房间</description></item>
/// </list>
/// <para>
/// <strong>表达式树编译优化</strong>:
/// 所有方法调用使用 CompiledMethodInvoker，性能提升 ~50 倍
/// </para>
/// </remarks>
public class ChatRoomService : BaseService, IChatHub, IPulseService
{
    // 房间成员列表 (用户名)
    private readonly HashSet<string> _members = new();

    // 消息历史
    private readonly List<ChatMessage> _messageHistory = new();

    // 房间统计
    private int _totalMessages;
    private DateTime _createdAt;
    private DateTime _lastActivityAt;

    // 房间 ID (不可变，用于线程调度)
    private readonly string _roomId;

    /// <summary>
    /// 服务类型名称 (IPulseService)
    /// </summary>
    public string ServiceName => "ChatRoom";

    /// <summary>
    /// 服务实例标识符 (IPulseService)
    /// </summary>
    /// <remarks>
    /// 房间ID作为ServiceId，确保相同房间的所有请求路由到同一线程
    /// </remarks>
    public string ServiceId { get; }

    public ChatRoomService(
        string roomId,
        ILogger<ChatRoomService> logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
        : base(logger, authenticationService, permissionValidator)
    {
        if (string.IsNullOrWhiteSpace(roomId))
            throw new ArgumentException("Room ID cannot be empty", nameof(roomId));

        _roomId = roomId;
        ServiceId = $"ChatRoom:{roomId}";  // 格式: ChatRoom:room-123
        _createdAt = DateTime.UtcNow;
        _lastActivityAt = DateTime.UtcNow;

        logger.LogInformation(
            "创建聊天室服务实例 - RoomId: {RoomId}, ServiceId: {ServiceId}",
            roomId, ServiceId);
    }

    /// <summary>
    /// 加入聊天室
    /// </summary>
    /// <remarks>
    /// ✅ 无需加锁 - 服务隔离保证单线程执行
    /// ✅ 表达式树编译 - 方法调用性能提升 ~50 倍
    /// </remarks>
    public Task<bool> JoinAsync(JoinRequest request)
    {
        _lastActivityAt = DateTime.UtcNow;

        // ✅ 无需加锁 - 同一房间的所有请求在同一线程顺序处理
        if (_members.Contains(request.UserName))
        {
            Logger.LogWarning(
                "用户 {UserName} 已在房间 {RoomId} 中",
                request.UserName, _roomId);
            return Task.FromResult(false);
        }

        _members.Add(request.UserName);

        Logger.LogInformation(
            "用户 {UserName} 加入房间 {RoomId} (当前成员: {MemberCount})",
            request.UserName, _roomId, _members.Count);

        // 记录加入消息
        _messageHistory.Add(new ChatMessage
        {
            Type = MessageType.Join,
            UserName = request.UserName,
            Content = $"{request.UserName} 加入了房间",
            Timestamp = DateTime.UtcNow
        });

        return Task.FromResult(true);
    }

    /// <summary>
    /// 离开聊天室
    /// </summary>
    public Task<bool> LeaveAsync()
    {
        _lastActivityAt = DateTime.UtcNow;

        // 获取当前调用者 (来自认证上下文)
        var caller = GetCurrentCaller();

        // 优先从认证上下文的Claims中获取用户名
        var userName = caller.Claims.TryGetValue("UserName", out var userNameFromClaim)
            ? userNameFromClaim
            : (caller.UserId ?? caller.CallerId);

        // ✅ 无需加锁 - 服务隔离保证单线程执行
        if (!_members.Remove(userName))
        {
            Logger.LogWarning(
                "用户 {UserName} 不在房间 {RoomId} 中",
                userName, _roomId);
            return Task.FromResult(false);
        }

        Logger.LogInformation(
            "用户 {UserName} 离开房间 {RoomId} (剩余成员: {MemberCount})",
            userName, _roomId, _members.Count);

        // 记录离开消息
        _messageHistory.Add(new ChatMessage
        {
            Type = MessageType.Leave,
            UserName = userName,
            Content = $"{userName} 离开了房间",
            Timestamp = DateTime.UtcNow
        });

        return Task.FromResult(true);
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    /// <remarks>
    /// ✅ 表达式树编译调用 - 本方法通过 CompiledMethodInvoker 调用
    /// ✅ 性能: ~10ns vs 反射调用 ~500ns
    /// </remarks>
    [RequirePermission("chat.send")]  // 权限验证
    public Task<bool> SendMessageAsync(string message)
    {
        _lastActivityAt = DateTime.UtcNow;
        _totalMessages++;

        // 获取当前调用者
        var caller = GetCurrentCaller();

        // 优先从认证上下文的Claims中获取用户名
        var userName = caller.Claims.TryGetValue("UserName", out var userNameFromClaim)
            ? userNameFromClaim
            : (caller.UserId ?? caller.CallerId);

        // ✅ 无需加锁 - 服务隔离保证单线程执行
        if (!_members.Contains(userName))
        {
            Logger.LogWarning(
                "用户 {UserName} 未加入房间 {RoomId}，无法发送消息",
                userName, _roomId);
            return Task.FromResult(false);
        }

        var chatMessage = new ChatMessage
        {
            Type = MessageType.Chat,
            UserName = userName,
            Content = message,
            Timestamp = DateTime.UtcNow
        };

        _messageHistory.Add(chatMessage);

        Logger.LogInformation(
            "房间 {RoomId} - {UserName}: {Message} (总消息数: {TotalMessages})",
            _roomId, userName, message, _totalMessages);

        return Task.FromResult(true);
    }

    /// <summary>
    /// 生成异常 (测试错误处理)
    /// </summary>
    public Task<bool> GenerateException(string message)
    {
        Logger.LogWarning("生成测试异常: {Message}", message);
        throw new InvalidOperationException($"测试异常: {message}");
    }

    /// <summary>
    /// 获取房间统计信息
    /// </summary>
    [InternalOnly]  // 仅内部服务可调用
    public Task<RoomStats> GetStatsAsync()
    {
        return Task.FromResult(new RoomStats
        {
            RoomId = _roomId,
            MemberCount = _members.Count,
            TotalMessages = _totalMessages,
            Members = _members.ToList(),
            CreatedAt = _createdAt,
            LastActivityAt = _lastActivityAt,
            MessageHistoryCount = _messageHistory.Count
        });
    }

    /// <summary>
    /// 清空消息历史 (管理员操作)
    /// </summary>
    [RequireRole("Admin")]  // 仅管理员可调用
    public Task ClearHistoryAsync()
    {
        _messageHistory.Clear();
        Logger.LogInformation("房间 {RoomId} 的消息历史已清空", _roomId);
        return Task.CompletedTask;
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("聊天室服务 {ServiceId} 已启动", ServiceId);
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation(
            "聊天室服务 {ServiceId} 已停止 - 总成员: {MemberCount}, 总消息: {TotalMessages}",
            ServiceId, _members.Count, _totalMessages);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 聊天消息
/// </summary>
public class ChatMessage
{
    public MessageType Type { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 消息类型
/// </summary>
public enum MessageType
{
    Join,
    Leave,
    Chat
}

/// <summary>
/// 房间统计信息
/// </summary>
public class RoomStats
{
    public string RoomId { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public int TotalMessages { get; set; }
    public List<string> Members { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public int MessageHistoryCount { get; set; }
}
