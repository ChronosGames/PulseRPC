using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using PulseRPC.Server.Transport;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using PulseRPC.Server.Abstractions;
using PulseRPC.Generated;
using PulseRPC.Protocol;

namespace ChatApp;

/// <summary>
/// ChatHub 实现 - 基于服务隔离架构的聊天室调度器
/// </summary>
/// <remarks>
/// <para><strong>架构说明</strong>:</para>
/// <list type="bullet">
/// <item><description>ChatHub 作为前端调度器，负责将请求路由到对应的 ChatRoomService 实例</description></item>
/// <item><description>每个房间对应一个 ChatRoomService 实例，确保房间内消息顺序处理</description></item>
/// <item><description>使用 ChatRoomManager 管理服务实例的生命周期</description></item>
/// <item><description>支持多房间并发处理（不同房间在不同线程）</description></item>
/// </list>
/// </remarks>
public class ChatHub : BaseService, IChatHub, IPulseService
{
    private readonly ILogger<ChatHub> _logger;
    private readonly ChatRoomManager _roomManager;

    // 用户到房间的映射（用于 LeaveAsync 和 SendMessageAsync）
    private readonly ConcurrentDictionary<string, string> _userRoomMap = new();

    // 连接ID到用户名的映射（用于从连接上下文获取用户名）
    private readonly ConcurrentDictionary<string, string> _connectionUserMap = new();

    public ChatHub(
        ILogger<ChatHub> logger,
        ChatRoomManager roomManager,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
        : base(logger, authenticationService, permissionValidator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _roomManager = roomManager ?? throw new ArgumentNullException(nameof(roomManager));
    }

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
    public string ServiceId => string.Empty;

    /// <summary>
    /// 加入聊天室 - 路由到对应的 ChatRoomService 实例
    /// </summary>
    public async Task<bool> JoinAsync(JoinRequest request)
    {
        _logger.LogInformation(
            "用户加入聊天室请求: {UserName} -> {RoomName}",
            request.UserName, request.RoomName);

        try
        {
            // 获取或创建房间服务实例
            var roomService = _roomManager.GetOrCreateRoom(request.RoomName);

            // 先记录连接ID到用户名的映射（在调用前记录，以便后续使用）
            var connection = RequestContext.Current;
            if (connection != null)
            {
                _connectionUserMap[connection.Id.ToString()] = request.UserName;
            }

            // 调用房间服务的 JoinAsync 方法
            // 这会通过 AuthenticatedActorMessageQueue 排队，确保顺序处理
            var result = await roomService.InvokeAsync<bool>(new ProtocolId(ProtocolIdMapping.ProtocolIds.ChatHub_JoinAsync),
                [request]);

            if (result)
            {
                // 记录用户到房间的映射
                _userRoomMap[request.UserName] = request.RoomName;

                _logger.LogInformation(
                    "用户 {UserName} 成功加入房间 {RoomName}",
                    request.UserName, request.RoomName);
            }
            else
            {
                // 如果加入失败，移除连接映射
                if (connection != null)
                {
                    _connectionUserMap.TryRemove(connection.Id.ToString(), out _);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "用户 {UserName} 加入房间 {RoomName} 失败",
                request.UserName, request.RoomName);
            throw;
        }
    }

    /// <summary>
    /// 离开聊天室 - 从认证上下文获取用户信息并路由到对应房间
    /// </summary>
    public async Task<bool> LeaveAsync()
    {
        try
        {
            // 从请求上下文获取当前连接
            var connection = RequestContext.Current;
            if (connection == null)
            {
                _logger.LogWarning("无法获取当前连接上下文");
                return false;
            }

            // 从认证上下文获取用户名
            // 注意：这里简化处理，实际应该从认证上下文获取
            // 由于当前架构限制，我们使用用户房间映射
            var userName = GetUserNameFromContext();
            if (string.IsNullOrEmpty(userName))
            {
                _logger.LogWarning("无法获取当前用户名");
                return false;
            }

            // 查找用户所在的房间
            if (!_userRoomMap.TryGetValue(userName, out var roomName))
            {
                _logger.LogWarning("用户 {UserName} 未加入任何房间", userName);
                return false;
            }

            // 获取房间服务实例
            var roomService = _roomManager.TryGetRoom(roomName);
            if (roomService == null)
            {
                _logger.LogWarning("房间 {RoomName} 不存在", roomName);
                return false;
            }

            // 创建一个包含用户名信息的认证上下文，传递给房间服务
            // 这样ChatRoomService的LeaveAsync就能通过GetCurrentCaller获取用户名
            var authContext = ServiceAuthenticationContext.CreateUserContext(
                userName,
                string.Empty, // Token
                new HashSet<string> { "chat.send" }, // 授予聊天权限
                new HashSet<string> { "User" }); // 用户角色

            // 在Claims中设置用户名，让ChatRoomService能获取
            authContext.Claims["UserName"] = userName;

            // 使用认证上下文调用房间服务的 LeaveAsync 方法
            using (ServiceAuthenticationContextProvider.SetContext(authContext))
            {
                var result = await roomService.InvokeAsync<bool>(
                    new ProtocolId(ProtocolIdMapping.ProtocolIds.ChatHub_LeaveAsync),
                    []);

                if (result)
                {
                    // 移除用户到房间的映射
                    _userRoomMap.TryRemove(userName, out _);

                    // 移除连接ID到用户名的映射
                    if (connection != null)
                    {
                        _connectionUserMap.TryRemove(connection.Id.ToString(), out _);
                    }

                    _logger.LogInformation(
                        "用户 {UserName} 成功离开房间 {RoomName}",
                        userName, roomName);
                }

                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "用户离开聊天室失败");
            throw;
        }
    }

    /// <summary>
    /// 发送消息 - 路由到用户所在的房间服务实例
    /// </summary>
    public async Task<bool> SendMessageAsync(string message)
    {
        try
        {
            // 获取当前用户名
            var userName = GetUserNameFromContext();
            if (string.IsNullOrEmpty(userName))
            {
                _logger.LogWarning("无法获取当前用户名");
                return false;
            }

            // 查找用户所在的房间
            if (!_userRoomMap.TryGetValue(userName, out var roomName))
            {
                _logger.LogWarning("用户 {UserName} 未加入任何房间", userName);
                return false;
            }

            // 获取房间服务实例
            var roomService = _roomManager.TryGetRoom(roomName);
            if (roomService == null)
            {
                _logger.LogWarning("房间 {RoomName} 不存在", roomName);
                return false;
            }

            // 创建一个包含用户名信息的认证上下文
            var authContext = ServiceAuthenticationContext.CreateUserContext(
                userName,
                string.Empty, // Token
                new HashSet<string> { "chat.send" }, // 授予聊天权限
                new HashSet<string> { "User" }); // 用户角色

            // 在Claims中设置用户名，让ChatRoomService能获取
            authContext.Claims["UserName"] = userName;

            // 使用认证上下文调用房间服务的 SendMessageAsync 方法
            using (ServiceAuthenticationContextProvider.SetContext(authContext))
            {
                var result = await roomService.InvokeAsync<bool>(
                    new ProtocolId(ProtocolIdMapping.ProtocolIds.ChatHub_SendMessageAsync),
                    [message]);

                _logger.LogDebug(
                    "用户 {UserName} 在房间 {RoomName} 发送消息: {Message}",
                    userName, roomName, message);

                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送消息失败");
            throw;
        }
    }

    /// <summary>
    /// 生成异常（测试错误处理）- 路由到对应房间
    /// </summary>
    public async Task<bool> GenerateException(string message)
    {
        try
        {
            // 获取当前用户名
            var userName = GetUserNameFromContext();
            if (string.IsNullOrEmpty(userName))
            {
                _logger.LogWarning("无法获取当前用户名");
                return false;
            }

            // 查找用户所在的房间
            if (!_userRoomMap.TryGetValue(userName, out var roomName))
            {
                _logger.LogWarning("用户 {UserName} 未加入任何房间", userName);
                return false;
            }

            // 获取房间服务实例
            var roomService = _roomManager.TryGetRoom(roomName);
            if (roomService == null)
            {
                _logger.LogWarning("房间 {RoomName} 不存在", roomName);
                return false;
            }

            // 创建一个包含用户名信息的认证上下文
            var authContext = ServiceAuthenticationContext.CreateUserContext(
                userName,
                string.Empty, // Token
                new HashSet<string> { "chat.send" }, // 授予聊天权限
                new HashSet<string> { "User" }); // 用户角色

            // 在Claims中设置用户名
            authContext.Claims["UserName"] = userName;

            // 使用认证上下文调用房间服务的 GenerateException 方法
            using (ServiceAuthenticationContextProvider.SetContext(authContext))
            {
                return await roomService.InvokeAsync<bool>(
                    new ProtocolId(ProtocolIdMapping.ProtocolIds.ChatHub_GenerateException),
                    [message]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成异常测试失败");
            throw;
        }
    }

    /// <summary>
    /// 从认证上下文获取用户名
    /// </summary>
    private string? GetUserNameFromContext()
    {
        // 从请求上下文获取连接
        var connection = RequestContext.Current;
        if (connection == null)
            return null;

        // 尝试从认证上下文获取用户名
        // 认证上下文是在客户端连接时或JoinAsync时设置的
        try
        {
            // 尝试通过连接ID从用户房间映射中获取用户名
            var connectionId = connection.Id.ToString();
            if (_connectionUserMap.TryGetValue(connectionId, out var userName))
            {
                return userName;
            }

            _logger.LogWarning("无法获取用户名，ConnectionId: {ConnectionId}", connectionId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取用户名时发生异常");
            return null;
        }
    }
}
