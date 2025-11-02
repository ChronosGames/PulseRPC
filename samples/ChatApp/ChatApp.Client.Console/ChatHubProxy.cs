using PulseRPC.Client;
using System.Threading.Tasks;

namespace ChatApp.Client.Console;

/// <summary>
/// ChatHub 客户端代理 - 手动实现版本
/// </summary>
/// <remarks>
/// 这是一个临时的手动实现，用于在源代码生成器修复之前提供功能支持。
/// 未来应该由 PulseRPC.Client.SourceGenerator 自动生成此类代码。
/// </remarks>
public class ChatHubProxy : IChatHub
{
    private readonly IClientChannel _channel;
    private const string ServiceName = "IChatHub";

    public ChatHubProxy(IClientChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    /// <summary>
    /// 加入聊天室
    /// </summary>
    public async Task<bool> JoinAsync(JoinRequest request)
    {
        return await _channel.InvokeAsync<JoinRequest, bool>(
            ServiceName,
            nameof(JoinAsync),
            request);
    }

    /// <summary>
    /// 离开聊天室
    /// </summary>
    public async Task<bool> LeaveAsync()
    {
        // LeaveAsync 没有参数，传递一个空的结构体
        return await _channel.InvokeAsync<EmptyRequest, bool>(
            ServiceName,
            nameof(LeaveAsync),
            default);
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    public async Task<bool> SendMessageAsync(string message)
    {
        return await _channel.InvokeAsync<string, bool>(
            ServiceName,
            nameof(SendMessageAsync),
            message);
    }

    /// <summary>
    /// 生成异常（测试）
    /// </summary>
    public async Task<bool> GenerateException(string message)
    {
        return await _channel.InvokeAsync<string, bool>(
            ServiceName,
            nameof(GenerateException),
            message);
    }

    /// <summary>
    /// 空请求结构体 - 用于无参数的方法
    /// </summary>
    private struct EmptyRequest { }
}
