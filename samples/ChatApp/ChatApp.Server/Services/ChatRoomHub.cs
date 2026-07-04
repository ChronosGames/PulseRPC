using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using ChatApp.NewArchitecture.Contracts;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Security;
using PulseRPC.Server.Services;
using PulseRPC.Server.Transport;

namespace ChatApp.NewArchitecture.Services;

/// <summary>
/// 聊天室 Hub - 无状态 Singleton，负责路由和验证
/// </summary>
/// <remarks>
/// <para><strong>核心设计</strong>：</para>
/// <list type="bullet">
/// <item><description>✅ 无状态，注册为 Singleton，全局复用</description></item>
/// <item><description>✅ 只做路由和验证，不持有任何业务状态</description></item>
/// <item><description>✅ 通过 <see cref="IServiceAccessor{TService}"/> 获取 Service 实例</description></item>
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
    private readonly IServerChannelManager _channelManager;
    private readonly ILogger<ChatRoomHub> _logger;

    // ════════════════════════════════════════════════════════════════════════
    // 用户会话信息（通过 RequestContext 传递，这里只是读取）
    // 注意：这些信息存储在 Connection 上，不是 Hub 的状态
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 获取当前用户 ID（从 PulseContext）
    /// </summary>
    private string? CurrentUserId => PulseContext.CurrentUserId;

    /// <summary>
    /// 获取当前用户所在的房间 ID。
    /// </summary>
    /// <remarks>
    /// 注意：<see cref="PulseContext.Current"/> 是"每请求"新建的（见 <c>MessageEngine.ProcessSingleMessage</c>），
    /// 因此不能用它的 <c>Properties</c> 跨请求保存会话状态。这里改为存放在
    /// <see cref="IAuthenticationContext.Properties"/> 中——该对象与连接的生命周期绑定
    /// （<c>IServerChannel.AuthenticationContext</c>），能在同一连接的多次请求间持久化。
    /// </remarks>
    private string? CurrentRoomId => PulseContext.Current?.AuthenticationContext?.Properties.TryGetValue("RoomId", out var roomId) == true
        ? roomId as string
        : null;

    /// <summary>
    /// 获取当前用户名（登录时写入的 ClaimTypes.Name 声明）
    /// </summary>
    private string? CurrentUserName => PulseContext.Current?.User?.FindFirst(ClaimTypes.Name)?.Value;

    // ════════════════════════════════════════════════════════════════════════
    // 构造函数
    // ════════════════════════════════════════════════════════════════════════

    public ChatRoomHub(
        IServiceAccessor<ChatRoomService> roomService,
        IServerChannelManager channelManager,
        ILogger<ChatRoomHub> logger)
    {
        _roomService = roomService ?? throw new ArgumentNullException(nameof(roomService));
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
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

        // 2. 建立连接级认证：把身份信息写入当前连接的 AuthenticationContext，
        //    使后续同一连接上的所有调用都自动携带该身份（PulseContext.Current.UserId/User）。
        AuthenticateCurrentConnection(userId, userName!);

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

        // 清除连接级认证信息（同时清空其 Properties，包括 RoomId）
        var connectionId = PulseContext.CurrentConnectionId;
        if (!string.IsNullOrEmpty(connectionId))
        {
            _channelManager.GetChannel(connectionId)?.ClearAuthentication();
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

            // 7. 如果成功，记录当前房间（存放在连接级 AuthenticationContext.Properties 中）
            if (result.Success)
            {
                PulseContext.Current?.AuthenticationContext?.Properties.TryAdd("RoomId", roomId);
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
                PulseContext.Current?.AuthenticationContext?.Properties.Remove("RoomId");
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
    /// 把身份信息写入当前连接的 <see cref="IServerChannel.AuthenticationContext"/>，使后续同一连接上的所有
    /// 调用都自动携带该身份（<c>PulseContext.Current.UserId</c>），无需每次请求单独传递 Token。
    /// </summary>
    private void AuthenticateCurrentConnection(string userId, string userName)
    {
        var connectionId = PulseContext.CurrentConnectionId;
        if (string.IsNullOrEmpty(connectionId))
        {
            throw new InvalidOperationException("No active connection to authenticate.");
        }

        var channel = _channelManager.GetChannel(connectionId)
            ?? throw new InvalidOperationException($"Connection '{connectionId}' not found.");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userName),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "chatapp"));

        var authContext = new AuthenticationContext(connectionId);
        authContext.SetClientAuthentication(userId, userName, token: string.Empty, principal);

        channel.SetAuthentication(authContext);
    }

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

