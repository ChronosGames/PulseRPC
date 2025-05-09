using Microsoft.Extensions.Logging;
using PulseRPC.Protocol;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

/// <summary>
/// 服务注册消息处理器
/// </summary>
public class ServiceRegistrationMessageHandler(IServiceRegistry serviceRegistry)
    : RequestHandlerBase<ServiceRegistration, ServiceRegistrationResponse>
{
    private readonly IServiceRegistry _serviceRegistry =
        serviceRegistry ?? throw new ArgumentNullException(nameof(serviceRegistry));

    protected override async Task<ServiceRegistrationResponse> ProcessRequestAsync(NetworkSession context,
        ServiceRegistration request)
    {
        await _serviceRegistry.RegisterServiceAsync(request);

        // 发送注册成功响应
        return new ServiceRegistrationResponse { Success = true, Message = "服务注册成功" };
    }
}

/// <summary>
/// 服务注册响应
/// </summary>
public class ServiceRegistrationResponse : IMessage
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 响应消息
    /// </summary>
    public required string Message { get; init; }
}
