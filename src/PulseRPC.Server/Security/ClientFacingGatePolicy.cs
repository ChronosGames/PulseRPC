using Microsoft.Extensions.DependencyInjection;

namespace PulseRPC.Server.Security;

internal interface IClientFacingGatePolicy
{
    bool EnforcementEnabled { get; }
}

internal sealed class ClientFacingGatePolicy : IClientFacingGatePolicy
{
    public ClientFacingGatePolicy(bool enforcementEnabled)
    {
        EnforcementEnabled = enforcementEnabled;
    }

    public bool EnforcementEnabled { get; }
}

/// <summary>
/// 为单个服务器宿主附加不可变 gate 策略，其余服务解析委托给原始容器。
/// </summary>
internal sealed class ClientFacingGateServiceProvider : IServiceProvider, IKeyedServiceProvider
{
    private readonly IServiceProvider _innerProvider;
    private readonly IClientFacingGatePolicy _policy;

    public ClientFacingGateServiceProvider(
        IServiceProvider innerProvider,
        IClientFacingGatePolicy policy)
    {
        _innerProvider = innerProvider ?? throw new ArgumentNullException(nameof(innerProvider));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceType == typeof(IClientFacingGatePolicy))
        {
            return _policy;
        }

        if (serviceType == typeof(IServiceProvider) ||
            serviceType == typeof(IKeyedServiceProvider) ||
            serviceType == typeof(ClientFacingGateServiceProvider))
        {
            return this;
        }

        return _innerProvider.GetService(serviceType);
    }

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        return GetInnerKeyedProvider().GetKeyedService(serviceType, serviceKey);
    }

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        return GetInnerKeyedProvider().GetRequiredKeyedService(serviceType, serviceKey);
    }

    private IKeyedServiceProvider GetInnerKeyedProvider()
    {
        return _innerProvider as IKeyedServiceProvider
            ?? throw new InvalidOperationException("The inner service provider does not support keyed services.");
    }
}
