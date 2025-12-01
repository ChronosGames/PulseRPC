using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChatApp.NewArchitecture.Contracts;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.ServiceManagement;

namespace ChatApp.NewArchitecture.Services;

/// <summary>
/// 聊天室 Hub - 无状态 Singleton，负责路由和验证
/// </summary>
/// <remarks>
/// <para><strong>核心设计</strong>：</para>
/// <list type="bullet">
/// <item><description>✅ 无状态，注册为 Singleton，全局复用</description></item>
/// <item><description>✅ 只做路由和验证，不持有任何业务状态</description></item>
/// <item><description>✅ 通过 <see cref="IServiceAccessor{T}"/> 获取 Service 实例</description></item>
/// <item><description>✅ 所有操作通过 ExecuteAsync 在 Service 队列中执行</description></item>
/// </list>
/// <para><strong>请求流程</strong>：</para>
/// <code>
/// Client Request
///     ↓
/// ChatRoomHub (Singleton，无状态)
///     ↓ 参数验证 + 从上下文获取 userId/roomId
///     ↓
/// IServiceAccessor&lt;ChatRoomService&gt;.GetAsync(roomId)
///     ↓
/// ChatRoomService.EnqueueAsync(work)
///     ↓ 在专属队列中顺序执行
/// 返回结果
/// </code>
/// </remarks>
public class ChatRoomHub : IChatRoomHub
{
    private readonly IServiceAccessor<ChatRoomService> _roomService;
    private readonly ILogger<ChatRoomHub> _logger;

    // ════════════════════════════════════════════════════════════════════════
    // 用户会话信息（通过 RequestContext 传递，这里只是读取）
    // 注意：这些信息存储在 Connection 上，不是 Hub 的状态
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 获取当前用户 ID（从 RequestContext）
    /// </summary>
    private string? CurrentUserId => RequestContext.Current?.UserId;

    /// <summary>
    /// 获取当前用户所在的房间 ID（从 Connection 属性）
    /// </summary>
    private string? CurrentRoomId => UnifiedRequestContext.Current != null ? UnifiedRequestContext.Current.Properties.TryGetValue("RoomId", out var roomId)
        ? roomId as string
        : null : null;

    /// <summary>
    /// 获取当前用户名（从 Connection 属性）
    /// </summary>
    private string? CurrentUserName => UnifiedRequestContext.Current != null ? UnifiedRequestContext.Current.Properties.TryGetValue("UserName", out var name)
        ? name as string
        : null : null;

    // ════════════════════════════════════════════════════════════════════════
    // 构造函数
    // ════════════════════════════════════════════════════════════════════════

    public ChatRoomHub(
        IServiceAccessor<ChatRoomService> roomService,
        ILogger<ChatRoomHub> logger)
    {
        _roomService = roomService ?? throw new ArgumentNullException(nameof(roomService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ════════════════════════════════════════════════════════════════════════
    // 认证相关
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 用户登录
    /// </summary>
    /// <remarks>
    /// <para><strong>认证流程</strong>：</para>
    /// <list type="number">
    /// <item><description>验证 token（实际项目中应使用 JWT）</description></item>
    /// <item><description>将 userId 存入 Connection（框架层面）</description></item>
    /// <item><description>后续请求自动携带 RequestContext.UserId</description></item>
    /// </list>
    /// </remarks>
    public Task<ChatLoginResult> LoginAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult(ChatLoginResult.Failed("Token is required"));
        }

        // 1. 验证 token（简化实现）
        var (userId, userName) = ValidateToken(token);
        if (userId == null)
        {
            return Task.FromResult(ChatLoginResult.Failed("Invalid token"));
        }

        // 2. 将认证信息存入连接上下文
        //    框架会在后续请求中自动设置 RequestContext.Current.UserId
        var context = RequestContext.Current;
        if (context != null)
        {
            context.Properties["UserId"] = userId;
            context.Properties["UserName"] = userName;
            // 注意：实际项目中应该调用 connection.SetAuthentication(...)
        }

        _logger.LogInformation("User {UserId} ({UserName}) logged in", userId, userName);

        return Task.FromResult(ChatLoginResult.Ok(userId, userName!));
    }

    /// <summary>
    /// 登出
    /// </summary>
    public async Task LogoutAsync()
    {
        var userId = CurrentUserId;
        var roomId = CurrentRoomId;

        // 如果在房间中，先离开
        if (!string.IsNullOrEmpty(roomId) && !string.IsNullOrEmpty(userId))
        {
            await LeaveRoomAsync();
        }

        // 清除认证信息
        var context = RequestContext.Current;
        if (context != null)
        {
            context.Properties.Remove("UserId");
            context.Properties.Remove("UserName");
            context.Properties.Remove("RoomId");
        }

        _logger.LogInformation("User {UserId} logged out", userId);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 房间操作
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 加入房间
    /// </summary>
    public async Task<JoinRoomResult> JoinRoomAsync(string roomId)
    {
        // 1. 验证参数
        if (string.IsNullOrWhiteSpace(roomId))
        {
            return JoinRoomResult.Failed("RoomId is required");
        }

        // 2. 验证已登录
        var userId = CurrentUserId;
        var userName = CurrentUserName;
        if (string.IsNullOrEmpty(userId))
        {
            return JoinRoomResult.Failed("Not authenticated");
        }

        // 3. 检查是否已在房间中
        if (CurrentRoomId != null)
        {
            return JoinRoomResult.Failed("Already in a room. Leave first.");
        }

        try
        {
            // 4. 获取房间服务实例
            var service = await _roomService.GetAsync(roomId);

            // 5. 确保服务已启动
            if (service.State == ServiceLifecycleState.Created)
            {
                await service.StartAsync();
            }

            // 6. 在服务队列中执行加入操作
            var result = await service.EnqueueAsync(
                () => service.JoinAsync(userId, userName ?? userId));

            // 7. 如果成功，记录当前房间
            if (result.Success)
            {
                UnifiedRequestContext.Current?.Properties.TryAdd("RoomId", roomId);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join room {RoomId}", roomId);
            return JoinRoomResult.Failed($"Internal error: {ex.Message}");
        }
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    public async Task<SendMessageResult> SendMessageAsync(string message)
    {
        // 1. 验证已登录
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId))
        {
            return SendMessageResult.Failed("Not authenticated");
        }

        // 2. 验证在房间中
        var roomId = CurrentRoomId;
        if (string.IsNullOrEmpty(roomId))
        {
            return SendMessageResult.Failed("Not in any room");
        }

        try
        {
            // 3. 获取房间服务并在队列中执行
            var service = await _roomService.GetAsync(roomId);
            return await service.EnqueueAsync(
                () => service.SendMessageAsync(userId, message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to room {RoomId}", roomId);
            return SendMessageResult.Failed($"Internal error: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取成员列表
    /// </summary>
    public async Task<string[]> GetMembersAsync()
    {
        var roomId = CurrentRoomId;
        if (string.IsNullOrEmpty(roomId))
        {
            return Array.Empty<string>();
        }

        try
        {
            var service = await _roomService.GetAsync(roomId);
            return await service.EnqueueAsync(() => service.GetMembersAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get members for room {RoomId}", roomId);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 获取最近消息
    /// </summary>
    public async Task<ChatMessage[]> GetRecentMessagesAsync(int count)
    {
        var roomId = CurrentRoomId;
        if (string.IsNullOrEmpty(roomId))
        {
            return Array.Empty<ChatMessage>();
        }

        try
        {
            var service = await _roomService.GetAsync(roomId);
            return await service.EnqueueAsync(() => service.GetRecentMessagesAsync(count));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get messages for room {RoomId}", roomId);
            return Array.Empty<ChatMessage>();
        }
    }

    /// <summary>
    /// 离开房间
    /// </summary>
    public async Task<bool> LeaveRoomAsync()
    {
        var userId = CurrentUserId;
        var roomId = CurrentRoomId;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(roomId))
        {
            return false;
        }

        try
        {
            var service = await _roomService.GetAsync(roomId);
            var result = await service.EnqueueAsync(() => service.LeaveAsync(userId));

            if (result)
            {
                RequestContext.Current?.Properties.Remove("RoomId");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to leave room {RoomId}", roomId);
            return false;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // 辅助方法
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 验证 token（简化实现）
    /// </summary>
    private (string? userId, string? userName) ValidateToken(string token)
    {
        // TODO: 实际项目中应该使用 JWT 库验证
        // 这里简化：token 格式为 "userId:userName"
        var parts = token.Split(':');
        if (parts.Length >= 2)
        {
            return (parts[0], parts[1]);
        }
        if (parts.Length == 1)
        {
            return (parts[0], parts[0]);
        }
        return (null, null);
    }
}

