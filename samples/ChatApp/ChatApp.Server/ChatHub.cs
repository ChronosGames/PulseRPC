using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using System.Threading.Tasks;

namespace ChatApp;

/// <summary>
/// ChatHub implementation for testing message dispatcher
/// </summary>
public class ChatHub : IChatHub
{
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(ILogger<ChatHub> logger)
    {
        _logger = logger;
    }

    public Task<bool> JoinAsync(JoinRequest request)
    {
        _logger.LogInformation("用户加入聊天室: {UserName} -> {RoomName}", request.UserName, request.RoomName);
        return Task.FromResult(true);
    }

    public Task<bool> LeaveAsync()
    {
        _logger.LogInformation("用户离开聊天室");
        return Task.FromResult(true);
    }

    public Task<bool> SendMessageAsync(string message)
    {
        _logger.LogInformation("收到聊天消息: {Message}", message);
        return Task.FromResult(true);
    }

    public Task<bool> GenerateException(string message)
    {
        _logger.LogInformation("生成异常测试: {Message}", message);
        throw new System.Exception($"测试异常: {message}");
    }
}