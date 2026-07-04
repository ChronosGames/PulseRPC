using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Shared;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChatApp.Client.Console;

/// <summary>
/// 聊天客户端 - 演示服务隔离架构的使用
/// </summary>
/// <remarks>
/// <para><strong>服务隔离架构客户端使用说明</strong>:</para>
/// <list type="bullet">
/// <item><description>客户端无需关心服务实例的创建和调度</description></item>
/// <item><description>只需调用 IChatHub 接口方法，服务端会自动路由到对应的房间服务实例</description></item>
/// <item><description>相同房间的所有消息在服务端顺序处理，保证一致性</description></item>
/// <item><description>不同房间的消息可并发处理，提高吞吐量</description></item>
/// </list>
/// <para><strong>注意</strong>: 当前客户端 API 正在重构，使用简化实现</para>
/// </remarks>
public class ChatClient
{
    private readonly ILogger<ChatClient> _logger;
    private IPulseClient? _client;
    private IChatHub? _chatHub;
    private string? _currentRoom;
    private string? _userName;

    public ChatClient(ILogger<ChatClient> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 初始化客户端
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInformation("正在初始化聊天客户端...");

        try
        {
            // 创建连接配置
            var connectionConfig = ConnectionConfig.Tcp(
                name: "ChatServer",
                host: "127.0.0.1",
                port: 7000);

            // 创建客户端
            _client = new PulseClientBuilder()
                .AddConnection(connectionConfig.ToDescriptor())
                .WithLogging(LoggerFactory.Create(builder => builder.AddConsole()))
                .Build();

            // 初始化连接
            await _client.InitializeAsync();

            // 获取聊天服务代理
            _chatHub = await _client.GetChatHubAsync();

            _logger.LogInformation("聊天客户端初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "聊天客户端初始化失败");
            throw;
        }
    }

    /// <summary>
    /// 加入聊天室
    /// </summary>
    /// <param name="roomName">房间名称</param>
    /// <param name="userName">用户名称</param>
    /// <remarks>
    /// 服务端会根据 roomName 创建或获取对应的 ChatRoomService 实例。
    /// ServiceId = "ChatRoom:{roomName}"，确保相同房间的所有请求路由到同一线程。
    /// </remarks>
    public async Task<bool> JoinRoomAsync(string roomName, string userName)
    {
        _logger.LogInformation("正在加入房间: {RoomName} (用户: {UserName})", roomName, userName);

        if (_chatHub == null)
            throw new InvalidOperationException("客户端未初始化，请先调用 InitializeAsync()");

        try
        {
            var request = new JoinRequest
            {
                RoomName = roomName,
                UserName = userName
            };

            var result = await _chatHub.JoinAsync(request);

            if (result)
            {
                _currentRoom = roomName;
                _userName = userName;
                _logger.LogInformation("成功加入房间: {RoomName}", roomName);
            }
            else
            {
                _logger.LogWarning("加入房间失败: {RoomName}", roomName);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加入房间时发生错误: {RoomName}", roomName);
            throw;
        }
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <remarks>
    /// 消息会路由到当前房间的服务实例，在该实例中顺序处理。
    /// 需要 "chat.send" 权限（由服务端的 [RequirePermission] 特性验证）。
    /// </remarks>
    public async Task<bool> SendMessageAsync(string message)
    {
        if (string.IsNullOrEmpty(_currentRoom))
            throw new InvalidOperationException("尚未加入任何房间");

        if (_chatHub == null)
            throw new InvalidOperationException("客户端未初始化");

        _logger.LogDebug("发送消息: {Message}", message);

        try
        {
            var result = await _chatHub.SendMessageAsync(message);

            if (result)
            {
                _logger.LogDebug("消息发送成功");
            }
            else
            {
                _logger.LogWarning("消息发送失败");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送消息时发生错误");
            throw;
        }
    }

    /// <summary>
    /// 离开聊天室
    /// </summary>
    public async Task<bool> LeaveRoomAsync()
    {
        if (string.IsNullOrEmpty(_currentRoom))
        {
            _logger.LogWarning("尚未加入任何房间");
            return false;
        }

        if (_chatHub == null)
            throw new InvalidOperationException("客户端未初始化");

        _logger.LogInformation("正在离开房间: {RoomName}", _currentRoom);

        try
        {
            var result = await _chatHub.LeaveAsync();

            if (result)
            {
                _logger.LogInformation("成功离开房间: {RoomName}", _currentRoom);
                _currentRoom = null;
                _userName = null;
            }
            else
            {
                _logger.LogWarning("离开房间失败");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "离开房间时发生错误");
            throw;
        }
    }

    /// <summary>
    /// 测试异常处理
    /// </summary>
    /// <remarks>
    /// 用于测试服务隔离架构的异常处理机制。
    /// 单个房间的异常不会影响其他房间。
    /// </remarks>
    public async Task TestExceptionAsync(string message)
    {
        if (string.IsNullOrEmpty(_currentRoom))
            throw new InvalidOperationException("尚未加入任何房间");

        if (_chatHub == null)
            throw new InvalidOperationException("客户端未初始化");

        _logger.LogInformation("测试异常处理: {Message}", message);

        try
        {
            await _chatHub.GenerateException(message);
            _logger.LogInformation("异常测试完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "异常测试中捕获到异常（这是预期的）");
            throw;
        }
    }

    /// <summary>
    /// 关闭客户端
    /// </summary>
    public async Task ShutdownAsync()
    {
        _logger.LogInformation("正在关闭聊天客户端...");

        try
        {
            // 如果还在房间中，先离开
            if (!string.IsNullOrEmpty(_currentRoom))
            {
                await LeaveRoomAsync();
            }

            // 停止客户端
            if (_client != null)
            {
                await _client.StopAsync();
                _client.Dispose();
                _client = null;
            }

            _logger.LogInformation("聊天客户端已关闭");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭聊天客户端时发生错误");
        }
    }

    /// <summary>
    /// 获取当前状态信息
    /// </summary>
    public string GetStatus()
    {
        if (_client == null)
            return "未初始化";

        if (string.IsNullOrEmpty(_currentRoom))
            return "已连接，未加入房间";

        return $"已加入房间: {_currentRoom} (用户: {_userName})";
    }
}
