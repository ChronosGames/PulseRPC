using System;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Security;
using Xunit;

namespace PulseRPC.Server.Tests;

/// <summary>
/// 验证 P-6 facet 级 client-facing 可见性门闸的运行时强制逻辑（<see cref="ClientFacingGate"/>）：
/// <list type="bullet">
/// <item>门闸默认关闭（<see cref="ClientFacingGate.EnforcementEnabled"/> = false），保持向后兼容；</item>
/// <item>开启后，标记为 [ClientFacing] 的方法，无论调用来源如何，均放行；</item>
/// <item>开启后，未标记的方法，仅拒绝「外部客户端」（<see cref="CallSourceType.ExternalUser"/>）来源的调用；</item>
/// <item>服务间调用 / 系统调用 / 无上下文（直接内部调用）不受此门闸影响，只受各自的业务鉴权约束；</item>
/// <item>拒绝时抛出的 <see cref="ClientFacingAccessDeniedException"/> 携带协议号与方法名，便于诊断。</item>
/// </list>
/// </summary>
/// <remarks>
/// <see cref="ClientFacingGate.EnforcementEnabled"/> 是旧调用路径的进程级兼容开关，测试通过
/// try/finally 显式还原，避免影响同进程内的其它测试。
/// </remarks>
[Collection(ClientFacingGateTestCollection.Name)]
public class ClientFacingGateTests
{
    [Fact]
    public void EnforcementEnabled_DefaultsToFalse()
    {
        ClientFacingGate.EnforcementEnabled.Should().BeFalse();
    }

    [Fact]
    public void Enforce_WhenEnforcementDisabled_AllowsExternalUserCallEvenIfNotClientFacing()
    {
        // 默认（未开启门闸）：与升级前行为一致，未标注 [ClientFacing] 的方法照常可被外部客户端调用。
        using (PulseContext.SetContext(CreateContext(CallSourceType.ExternalUser)))
        {
            var act = () => ClientFacingGate.Enforce(isClientFacing: false, protocolId: 0x1234, methodDisplayName: "IFoo.Bar");

            act.Should().NotThrow();
        }
    }

    [Fact]
    public void Enforce_WhenEnabled_AndClientFacing_AllowsExternalUserCall()
    {
        WithEnforcementEnabled(() =>
        {
            using (PulseContext.SetContext(CreateContext(CallSourceType.ExternalUser)))
            {
                var act = () => ClientFacingGate.Enforce(isClientFacing: true, protocolId: 0x1234, methodDisplayName: "IFoo.Bar");

                act.Should().NotThrow();
            }
        });
    }

    [Fact]
    public void Enforce_WhenEnabled_AndNotClientFacing_AndExternalUser_Throws()
    {
        WithEnforcementEnabled(() =>
        {
            using (PulseContext.SetContext(CreateContext(CallSourceType.ExternalUser)))
            {
                var act = () => ClientFacingGate.Enforce(isClientFacing: false, protocolId: 0x1234, methodDisplayName: "IFoo.Bar");

                var exception = act.Should().Throw<ClientFacingAccessDeniedException>().Which;
                exception.ProtocolId.Should().Be((ushort)0x1234);
                exception.MethodDisplayName.Should().Be("IFoo.Bar");
                exception.Message.Should().Contain("IFoo.Bar").And.Contain("0x1234");
            }
        });
    }

    [Theory]
    [InlineData(CallSourceType.InternalService)]
    [InlineData(CallSourceType.SystemTimer)]
    [InlineData(CallSourceType.AdminConsole)]
    public void Enforce_WhenEnabled_AndNotClientFacing_AndNotExternalUser_Allows(CallSourceType sourceType)
    {
        WithEnforcementEnabled(() =>
        {
            using (PulseContext.SetContext(CreateContext(sourceType)))
            {
                var act = () => ClientFacingGate.Enforce(isClientFacing: false, protocolId: 0x1234, methodDisplayName: "IFoo.Bar");

                act.Should().NotThrow();
            }
        });
    }

    [Fact]
    public void Enforce_WhenEnabled_AndNotClientFacing_AndNoAmbientContext_Allows()
    {
        // 没有请求上下文（例如测试直接调用、或非经由传输管线的内部调用）时，视为不受门闸约束，
        // 与 PermissionValidator 对「无认证上下文」的处理保持一致的保守放行策略。
        PulseContext.Current.Should().BeNull();

        WithEnforcementEnabled(() =>
        {
            var act = () => ClientFacingGate.Enforce(isClientFacing: false, protocolId: 0x1234, methodDisplayName: "IFoo.Bar");

            act.Should().NotThrow();
        });
    }

    [Fact]
    public void Enforce_WithHostPolicyDisabled_MustOverrideLegacyStaticTrue()
    {
        WithEnforcementEnabled(() =>
        {
            var services = new ServiceCollection();
            services.AddSingleton<IClientFacingGatePolicy>(new ClientFacingGatePolicy(false));
            using var provider = services.BuildServiceProvider();
            using var context = PulseContext.SetContext(CreateContext(CallSourceType.ExternalUser));

            var act = () => ClientFacingGate.Enforce(
                provider,
                isClientFacing: false,
                protocolId: 0x1234,
                methodDisplayName: "IFoo.Bar");

            act.Should().NotThrow();
        });
    }

    [Fact]
    public void Enforce_WithoutHostPolicy_MustFallBackToLegacyStaticTrue()
    {
        WithEnforcementEnabled(() =>
        {
            using var provider = new ServiceCollection().BuildServiceProvider();
            using var context = PulseContext.SetContext(CreateContext(CallSourceType.ExternalUser));

            var act = () => ClientFacingGate.Enforce(
                provider,
                isClientFacing: false,
                protocolId: 0x1234,
                methodDisplayName: "IFoo.Bar");

            act.Should().Throw<ClientFacingAccessDeniedException>();
        });
    }

    [Fact]
    public void HostPolicyScope_MustNestRestoreAndOverrideSharedProviderPolicy()
    {
        var previous = ClientFacingGate.EnforcementEnabled;
        ClientFacingGate.EnforcementEnabled = false;

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IClientFacingGatePolicy>(new ClientFacingGatePolicy(false));
            using var rootProvider = services.BuildServiceProvider();
            var enabledProvider = new ClientFacingGateServiceProvider(
                rootProvider,
                new ClientFacingGatePolicy(enforcementEnabled: true));
            var disabledProvider = new ClientFacingGateServiceProvider(
                rootProvider,
                new ClientFacingGatePolicy(enforcementEnabled: false));
            using var context = PulseContext.SetContext(CreateContext(CallSourceType.ExternalUser));

            Action enforceRoot = () => ClientFacingGate.Enforce(
                rootProvider,
                isClientFacing: false,
                protocolId: 0x1234,
                methodDisplayName: "IFoo.Bar");
            Action enforceLegacy = () => ClientFacingGate.Enforce(
                isClientFacing: false,
                protocolId: 0x1234,
                methodDisplayName: "IFoo.Bar");

            enforceRoot.Should().NotThrow();
            enforceLegacy.Should().NotThrow();

            using (ClientFacingGate.EnterHostPolicyScope(enabledProvider))
            {
                enforceRoot.Should().Throw<ClientFacingAccessDeniedException>();
                enforceLegacy.Should().Throw<ClientFacingAccessDeniedException>();

                var explicitDisabled = () => ClientFacingGate.Enforce(
                    disabledProvider,
                    isClientFacing: false,
                    protocolId: 0x1234,
                    methodDisplayName: "IFoo.Bar");
                explicitDisabled.Should().Throw<ClientFacingAccessDeniedException>(
                    "活动宿主策略不能被共享或嵌套 provider 降级");

                using (ClientFacingGate.EnterHostPolicyScope(disabledProvider))
                {
                    enforceRoot.Should().NotThrow();

                    var explicitEnabled = () => ClientFacingGate.Enforce(
                        enabledProvider,
                        isClientFacing: false,
                        protocolId: 0x1234,
                        methodDisplayName: "IFoo.Bar");
                    explicitEnabled.Should().NotThrow(
                        "活动的 disabled 宿主策略应覆盖其它 provider 的配置");
                }

                enforceRoot.Should().Throw<ClientFacingAccessDeniedException>();
            }

            enforceRoot.Should().NotThrow();
            enforceLegacy.Should().NotThrow();
        }
        finally
        {
            ClientFacingGate.EnforcementEnabled = previous;
        }
    }

    [Fact]
    public async Task CompletedHostPolicyScope_MustNotLeakIntoCapturedBackgroundContext()
    {
        var previous = ClientFacingGate.EnforcementEnabled;
        ClientFacingGate.EnforcementEnabled = false;

        try
        {
            using var rootProvider = new ServiceCollection().BuildServiceProvider();
            var enabledProvider = new ClientFacingGateServiceProvider(
                rootProvider,
                new ClientFacingGatePolicy(enforcementEnabled: true));
            using var context = PulseContext.SetContext(CreateContext(CallSourceType.ExternalUser));
            var releaseBackground = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Task<bool> backgroundDenied;
            using (ClientFacingGate.EnterHostPolicyScope(enabledProvider))
            {
                backgroundDenied = Task.Run(async () =>
                {
                    await releaseBackground.Task;
                    try
                    {
                        ClientFacingGate.Enforce(
                            rootProvider,
                            isClientFacing: false,
                            protocolId: 0x1234,
                            methodDisplayName: "IFoo.Bar");
                        return false;
                    }
                    catch (ClientFacingAccessDeniedException)
                    {
                        return true;
                    }
                });
            }

            releaseBackground.TrySetResult(true);
            (await backgroundDenied).Should().BeFalse(
                "已结束 dispatch 捕获的 ExecutionContext 不得继续携带宿主策略");
        }
        finally
        {
            ClientFacingGate.EnforcementEnabled = previous;
        }
    }

    private static void WithEnforcementEnabled(Action action)
    {
        var previous = ClientFacingGate.EnforcementEnabled;
        ClientFacingGate.EnforcementEnabled = true;
        try
        {
            action();
        }
        finally
        {
            ClientFacingGate.EnforcementEnabled = previous;
        }
    }

    private static PulseContextData CreateContext(CallSourceType sourceType)
    {
        return sourceType switch
        {
            CallSourceType.ExternalUser => PulseContextData.CreateUserContext(userId: "user-1"),
            CallSourceType.InternalService => PulseContextData.CreateServiceContext(serviceType: "Svc", serviceId: "svc-1"),
            CallSourceType.SystemTimer => PulseContextData.CreateSystemContext(),
            _ => new PulseContextData { SourceType = sourceType, CallerId = "admin-1" }
        };
    }
}
