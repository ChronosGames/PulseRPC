using Microsoft.Extensions.Logging;

namespace PulseRPC.Server;

/// <summary>
/// 服务注册消息处理器
/// </summary>
public class ServiceRegistrationMessageHandler(
    ISerializer serializer,
    IServiceRegistry serviceRegistry,
    ILogger logger,
    int id)
    : MessageHandlerBase<ServiceRegistration>(serializer)
{
    private readonly IServiceRegistry _serviceRegistry = serviceRegistry ?? throw new ArgumentNullException(nameof(serviceRegistry));

    protected override async Task HandleMessageAsync(PulseNet.Core.INetSession session, ServiceRegistration registration)
    {
        try
        {
            await _serviceRegistry.RegisterServiceAsync(registration);

            // 发送注册成功响应
            await session.SendAsync(new ServiceRegistrationResponse
            {
                Success = true,
                Message = "服务注册成功"
            }, MessageIds.ServiceRegistrationResponse);
        }
        catch (Exception ex)
        {
            logger?.Error(ex, "处理服务注册消息时出错");

            // 发送注册失败响应
            await session.SendAsync(new ServiceRegistrationResponse
            {
                Success = false,
                Message = $"服务注册失败: {ex.Message}"
            }, MessageIds.ServiceRegistrationResponse);
        }
    }

    public bool CanHandle(int messageId)
    {
        return messageId == id;
    }
}

/// <summary>
/// 服务注册响应
/// </summary>
public class ServiceRegistrationResponse
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 响应消息
    /// </summary>
    public string Message { get; set; }
}
