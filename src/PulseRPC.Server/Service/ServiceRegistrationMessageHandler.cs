using MemoryPack;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

/// <summary>
/// 服务注册消息处理器
/// </summary>
public class ServiceRegistrationMessageHandler(IServiceRegistry serviceRegistry) : IRequestHandler<ServiceRegistration, ServiceRegistrationResponse>
{
    private readonly IServiceRegistry _serviceRegistry =
        serviceRegistry ?? throw new ArgumentNullException(nameof(serviceRegistry));

    public async Task<ServiceRegistrationResponse> HandleAsync(NetworkSession session, ServiceRegistration request)
    {
        await _serviceRegistry.RegisterServiceAsync(request);

        // 发送注册成功响应
        return new ServiceRegistrationResponse { Success = true, Message = "服务注册成功" };
    }
}

/// <summary>
/// 服务注册响应
/// </summary>
[MemoryPackable]
public partial class ServiceRegistrationResponse : Response
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
