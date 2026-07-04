using ChatApp.NewArchitecture.Contracts;
using Microsoft.Extensions.Logging;
using PulseRPC.Client;
using PulseRPC.Client.Configuration;

namespace ChatApp.Client.Console;

/// <summary>
/// 聊天客户端 - 演示服务隔离架构的使用
/// </summary>
/// <remarks>
/// <para><strong>服务隔离架构客户端使用说明</strong>:</para>
/// <list type="bullet">
/// <item><description>客户端无需关心服务实例的创建和调度</description></item>
/// <item><description>只需调用 <see cref="IChatRoomHub"/> 接口方法，服务端会自动路由到对应的房间服务实例</description></item>
/// <item><description>相同房间的所有消息在服务端顺序处理，保证一致性</description></item>
/// <item><description>不同房间的消息可并发处理，提高吞吐量</description></item>
/// </list>
/// </remarks>
public class ChatClient
{
    private readonly ILogger<ChatClient> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private IPulseClient? _client;
    private IClientChannel? _channel;
    private IChatRoomHub? _chatHub;
    private string? _currentRoom;
    private string? _userName;

    public ChatClient(ILogger<ChatClient> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>
    /// 初始化客户端
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInformation("正在初始化聊天客户端...");

        try
        {
            _client = new PulseClientBuilder()
                .WithLogging(_loggerFactory)
                .Build();

            await _client.InitializeAsync();

            _channel = await _client.ConnectToServerAsync("127.0.0.1", 7000);
            _chatHub = _channel.GetHub<IChatRoomHub>();

            _logger.LogInformation("聊天客户端初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "聊天客户端初始化失败");
            throw;
        }
    }

    /// <summary>
    /// 登录（连接级认证：登录后，同一连接的后续请求会自动携带用户身份）
    /// </summary>
    public async Task<bool> LoginAsync(string userName)
    {
        if (_chatHub == null)
            throw new InvalidOperationException("客户端未初始化，请先调用 InitializeAsync()");

        _logger.LogInformation("正在登录，用户名: {UserName}", userName);

        try
        {
            // Token 格式简化为 "userId:userName"（详见 ChatRoomHub.ValidateToken）
            var token = $"{Guid.NewGuid():N}:{userName}";
            var result = await _chatHub.LoginAsync(token);

            if (result.Success)
            {
                _userName = result.UserName;
                _logger.LogInformation("登录成功: {UserName} (ID: {UserId})", result.UserName, result.UserId);
            }
            else
            {
                _logger.LogWarning("登录失败: {ErrorMessage}", result.ErrorMessage);
            }

            return result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "登录过程中发生错误");
            throw;
        }
    }

    /// <summary>
    /// 加入聊天室
    /// </summary>
    /// <param name="roomName">房间名称</param>
    /// <remarks>
    /// 服务端会根据 roomName 创建或获取对应的 ChatRoomService 实例。
    /// 相同房间的所有请求路由到同一队列，保证顺序处理。
    /// </remarks>
    public async Task<bool> JoinRoomAsync(string roomName)
    {
        _logger.LogInformation("正在加入房间: {RoomName}", roomName);

        if (_chatHub == null)
            throw new InvalidOperationException("客户端未初始化，请先调用 InitializeAsync()");

        try
        {
            var result = await _chatHub.JoinRoomAsync(roomName);

            if (result.Success)
            {
                _currentRoom = roomName;
                _logger.LogInformation("成功加入房间: {RoomName} (成员数: {MemberCount})", roomName, result.MemberCount);
            }
            else
            {
                _logger.LogWarning("加入房间失败: {ErrorMessage}", result.ErrorMessage);
            }

            return result.Success;
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

            if (result.Success)
            {
                _logger.LogDebug("消息发送成功 (MessageId: {MessageId})", result.MessageId);
            }
            else
            {
                _logger.LogWarning("消息发送失败: {ErrorMessage}", result.ErrorMessage);
            }

            return result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送消息时发生错误");
            throw;
        }
    }

    /// <summary>
    /// 获取当前房间的成员列表
    /// </summary>
    public async Task<string[]> GetMembersAsync()
    {
        if (_chatHub == null)
            throw new InvalidOperationException("客户端未初始化");

        return await _chatHub.GetMembersAsync();
    }

    /// <summary>
    /// 获取最近消息
    /// </summary>
    public async Task<ChatMessage[]> GetRecentMessagesAsync(int count)
    {
        if (_chatHub == null)
            throw new InvalidOperationException("客户端未初始化");

        return await _chatHub.GetRecentMessagesAsync(count);
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
            var result = await _chatHub.LeaveRoomAsync();

            if (result)
            {
                _logger.LogInformation("成功离开房间: {RoomName}", _currentRoom);
                _currentRoom = null;
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

            // 登出
            if (_chatHub != null && _userName != null)
            {
                await _chatHub.LogoutAsync();
            }

            if (_channel != null)
            {
                await _channel.DisconnectAsync();
                _channel = null;
            }

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
