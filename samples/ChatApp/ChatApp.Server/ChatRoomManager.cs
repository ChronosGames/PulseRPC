using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChatApp;

/// <summary>
/// 聊天室服务管理器 - 负责创建和管理 ChatRoomService 实例
/// </summary>
/// <remarks>
/// <para><strong>职责</strong>:</para>
/// <list type="bullet">
/// <item><description>根据房间ID创建或获取 ChatRoomService 实例</description></item>
/// <item><description>确保每个房间只有一个服务实例</description></item>
/// <item><description>管理服务实例的生命周期</description></item>
/// <item><description>提供服务实例查找功能</description></item>
/// </list>
/// </remarks>
public class ChatRoomManager
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IAuthenticationService _authenticationService;
    private readonly PermissionValidator _permissionValidator;
    private readonly ConcurrentDictionary<string, ChatRoomService> _rooms = new();
    private readonly ILogger<ChatRoomManager> _logger;
    private int _nextPidId = 1;

    public ChatRoomManager(
        ILoggerFactory loggerFactory,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _permissionValidator = permissionValidator ?? throw new ArgumentNullException(nameof(permissionValidator));
        _logger = loggerFactory.CreateLogger<ChatRoomManager>();
    }

    /// <summary>
    /// 获取或创建聊天室服务实例
    /// </summary>
    /// <param name="roomId">房间ID</param>
    /// <returns>聊天室服务实例</returns>
    /// <remarks>
    /// 该方法是线程安全的。相同 roomId 的并发调用会返回同一个实例。
    /// </remarks>
    public ChatRoomService GetOrCreateRoom(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
            throw new ArgumentException("Room ID cannot be empty", nameof(roomId));

        return _rooms.GetOrAdd(roomId, CreateRoomService);
    }

    /// <summary>
    /// 尝试获取聊天室服务实例
    /// </summary>
    /// <param name="roomId">房间ID</param>
    /// <returns>聊天室服务实例，如果不存在则返回 null</returns>
    public ChatRoomService? TryGetRoom(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
            return null;

        _rooms.TryGetValue(roomId, out var room);
        return room;
    }

    /// <summary>
    /// 移除聊天室服务实例
    /// </summary>
    /// <param name="roomId">房间ID</param>
    /// <returns>是否成功移除</returns>
    /// <remarks>
    /// 该方法会停止服务实例并从管理器中移除。
    /// </remarks>
    public async Task<bool> RemoveRoomAsync(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
            return false;

        if (_rooms.TryRemove(roomId, out var room))
        {
            try
            {
                await room.StopAsync();
                _logger.LogInformation("聊天室服务已移除: {RoomId}", roomId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止聊天室服务时发生错误: {RoomId}", roomId);
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// 获取所有活跃的房间数量
    /// </summary>
    public int ActiveRoomCount => _rooms.Count;

    /// <summary>
    /// 创建聊天室服务实例
    /// </summary>
    private ChatRoomService CreateRoomService(string roomId)
    {
        // 生成唯一的 PID
        var pidId = System.Threading.Interlocked.Increment(ref _nextPidId);
        // 创建多实例服务PID，每个房间是一个独立实例
        var pid = PID.CreateTransient<ChatRoomService>(1, (ushort)pidId, roomId); // NodeId = 1 (单机模式)

        // 创建服务实例
        var logger = _loggerFactory.CreateLogger<ChatRoomService>();
        var service = new ChatRoomService(
            roomId,
            logger,
            _authenticationService,
            _permissionValidator);

        // 使用反射设置 PID（SetPID 是 internal 方法）
        var setPidMethod = typeof(BaseService).GetMethod("SetPID",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (setPidMethod == null)
            throw new InvalidOperationException("Cannot find SetPID method on BaseService");

        setPidMethod.Invoke(service, [pid]);

        // 启动服务
        _ = service.StartAsync();

        _logger.LogInformation(
            "创建聊天室服务实例 - RoomId: {RoomId}, PID: {PID}, ServiceId: {ServiceId}",
            roomId, pid, service.ServiceId);

        return service;
    }

    /// <summary>
    /// 停止所有聊天室服务
    /// </summary>
    public async Task StopAllRoomsAsync()
    {
        _logger.LogInformation("正在停止所有聊天室服务...");

        var stopTasks = new List<Task>();
        foreach (var room in _rooms.Values)
        {
            stopTasks.Add(room.StopAsync());
        }

        await Task.WhenAll(stopTasks);

        _rooms.Clear();

        _logger.LogInformation("所有聊天室服务已停止");
    }
}

