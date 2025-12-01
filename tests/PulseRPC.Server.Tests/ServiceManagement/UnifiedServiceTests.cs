using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.ServiceManagement;
using PulseRPC.Server.Services;
using Xunit;

namespace PulseRPC.Server.Tests.ServiceManagement;

/// <summary>
/// 统一服务系统测试
/// </summary>
public class UnifiedServiceTests
{
    #region 服务分类测试

    [Fact]
    public void ServiceStartupType_Should_Have_AutoStart_And_OnDemand()
    {
        // 验证启动类型枚举值
        Enum.GetNames<ServiceStartupType>().Should().Contain("AutoStart");
        Enum.GetNames<ServiceStartupType>().Should().Contain("OnDemand");
    }

    [Fact]
    public void ServiceInstanceScope_Should_Have_ThreeScopes()
    {
        // 验证实例范围枚举值
        Enum.GetNames<ServiceInstanceScope>().Should().Contain("ClusterSingleton");
        Enum.GetNames<ServiceInstanceScope>().Should().Contain("ProcessSingleton");
        Enum.GetNames<ServiceInstanceScope>().Should().Contain("MultiInstance");
    }

    [Fact]
    public void ServiceSchedulingMode_Should_Have_ThreeModes()
    {
        // 验证调度模式枚举值
        Enum.GetNames<ServiceSchedulingMode>().Should().Contain("DefaultPool");
        Enum.GetNames<ServiceSchedulingMode>().Should().Contain("DedicatedQueue");
        Enum.GetNames<ServiceSchedulingMode>().Should().Contain("ThreadAffinity");
    }

    #endregion

    #region PulseServiceAttribute 测试

    [Fact]
    public void PulseServiceAttribute_Should_Have_Default_Values()
    {
        var attr = new PulseServiceAttribute();

        attr.StartupType.Should().Be(ServiceStartupType.OnDemand);
        attr.InstanceScope.Should().Be(ServiceInstanceScope.MultiInstance);
        attr.SchedulingMode.Should().Be(ServiceSchedulingMode.DedicatedQueue);
        attr.QueueCapacity.Should().Be(1000);
        attr.IdleTimeoutSeconds.Should().Be(300);
        attr.EnableHealthCheck.Should().BeTrue();
    }

    [Fact]
    public void PulseServiceAttribute_Should_Allow_Custom_Values()
    {
        var attr = new PulseServiceAttribute
        {
            StartupType = ServiceStartupType.AutoStart,
            InstanceScope = ServiceInstanceScope.ClusterSingleton,
            SchedulingMode = ServiceSchedulingMode.ThreadAffinity,
            QueueCapacity = 500,
            IdleTimeoutSeconds = 600,
            EnableHealthCheck = false,
            DisplayName = "MyService",
            Description = "Test service"
        };

        attr.StartupType.Should().Be(ServiceStartupType.AutoStart);
        attr.InstanceScope.Should().Be(ServiceInstanceScope.ClusterSingleton);
        attr.SchedulingMode.Should().Be(ServiceSchedulingMode.ThreadAffinity);
        attr.QueueCapacity.Should().Be(500);
        attr.IdleTimeoutSeconds.Should().Be(600);
        attr.EnableHealthCheck.Should().BeFalse();
        attr.DisplayName.Should().Be("MyService");
        attr.Description.Should().Be("Test service");
    }

    #endregion

    #region IUnifiedPulseService 测试

    [Fact]
    public void ServiceAddress_Should_Combine_ServiceType_And_ServiceId()
    {
        var service = new TestChatRoomService("room-123");

        service.ServiceType.Should().Be("ChatRoom");
        service.ServiceId.Should().Be("room-123");
        ((IUnifiedPulseService)service).ServiceAddress.Should().Be("ChatRoom:room-123");
    }

    [Fact]
    public async Task Service_Should_Start_And_Stop_Correctly()
    {
        var service = new TestChatRoomService("room-123");

        service.State.Should().Be(ServiceLifecycleState.Created);

        await service.StartAsync();
        service.State.Should().Be(ServiceLifecycleState.Running);

        await service.StopAsync();
        service.State.Should().Be(ServiceLifecycleState.Stopped);
    }

    [Fact]
    public async Task Service_Should_Execute_Work_In_Queue()
    {
        var service = new TestChatRoomService("room-123");
        await service.StartAsync();

        var executionOrder = new List<int>();

        // 提交多个工作项
        var tasks = new[]
        {
            service.EnqueueAsync(async () =>
            {
                await Task.Delay(50);
                executionOrder.Add(1);
            }),
            service.EnqueueAsync(async () =>
            {
                await Task.Delay(10);
                executionOrder.Add(2);
            }),
            service.EnqueueAsync(async () =>
            {
                executionOrder.Add(3);
            })
        };

        await Task.WhenAll(tasks);

        // 验证顺序执行（FIFO）
        executionOrder.Should().Equal(1, 2, 3);

        await service.StopAsync();
    }

    [Fact]
    public async Task Service_Should_Return_Result_From_Queue()
    {
        var service = new TestChatRoomService("room-123");
        await service.StartAsync();

        var result = await service.EnqueueAsync(() => Task.FromResult(42));

        result.Should().Be(42);

        await service.StopAsync();
    }

    #endregion

    #region UnifiedServiceManager 测试

    [Fact]
    public void ServiceManager_Should_Register_Service()
    {
        var serviceProvider = CreateServiceProvider();
        var logger = NullLogger<UnifiedServiceManager>.Instance;
        var manager = new UnifiedServiceManager(serviceProvider, logger);

        manager.Register<TestChatRoomService>();

        var stats = manager.GetStatistics();
        stats.RegisteredTypes.Should().Be(1);
    }

    [Fact]
    public async Task ServiceManager_Should_Create_Service_On_Demand()
    {
        var serviceProvider = CreateServiceProvider();
        var logger = NullLogger<UnifiedServiceManager>.Instance;
        var manager = new UnifiedServiceManager(serviceProvider, logger);

        manager.Register<TestChatRoomService>();

        var service = await manager.GetOrCreateServiceAsync("ChatRoom", "room-123");

        service.Should().NotBeNull();
        service.ServiceType.Should().Be("ChatRoom");
        service.ServiceId.Should().Be("room-123");
        service.State.Should().Be(ServiceLifecycleState.Running); // OnDemand 服务自动启动
    }

    [Fact]
    public async Task ServiceManager_Should_Return_Same_Instance_For_Same_ServiceId()
    {
        var serviceProvider = CreateServiceProvider();
        var logger = NullLogger<UnifiedServiceManager>.Instance;
        var manager = new UnifiedServiceManager(serviceProvider, logger);

        manager.Register<TestChatRoomService>();

        var service1 = await manager.GetOrCreateServiceAsync("ChatRoom", "room-123");
        var service2 = await manager.GetOrCreateServiceAsync("ChatRoom", "room-123");

        service1.Should().BeSameAs(service2);
    }

    [Fact]
    public async Task ServiceManager_Should_Create_Different_Instances_For_Different_ServiceIds()
    {
        var serviceProvider = CreateServiceProvider();
        var logger = NullLogger<UnifiedServiceManager>.Instance;
        var manager = new UnifiedServiceManager(serviceProvider, logger);

        manager.Register<TestChatRoomService>();

        var service1 = await manager.GetOrCreateServiceAsync("ChatRoom", "room-123");
        var service2 = await manager.GetOrCreateServiceAsync("ChatRoom", "room-456");

        service1.Should().NotBeSameAs(service2);
        service1.ServiceId.Should().Be("room-123");
        service2.ServiceId.Should().Be("room-456");
    }

    [Fact]
    public async Task ServiceManager_Should_Remove_Service()
    {
        var serviceProvider = CreateServiceProvider();
        var logger = NullLogger<UnifiedServiceManager>.Instance;
        var manager = new UnifiedServiceManager(serviceProvider, logger);

        manager.Register<TestChatRoomService>();

        await manager.GetOrCreateServiceAsync("ChatRoom", "room-123");
        var removed = await manager.RemoveServiceAsync("ChatRoom", "room-123");

        removed.Should().BeTrue();
        manager.GetService("ChatRoom", "room-123").Should().BeNull();
    }

    [Fact]
    public void ServiceManager_Should_Validate_Singleton_ServiceId()
    {
        var serviceProvider = CreateServiceProvider();
        var logger = NullLogger<UnifiedServiceManager>.Instance;
        var manager = new UnifiedServiceManager(serviceProvider, logger);

        manager.Register<TestSingletonService>();

        // ProcessSingleton 必须使用 "local" 作为 ServiceId
        var act = async () => await manager.GetOrCreateServiceAsync("Singleton", "invalid-id");
        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must have ServiceId 'local'*");
    }

    [Fact]
    public async Task ServiceManager_Should_Allow_Valid_Singleton_ServiceId()
    {
        var serviceProvider = CreateServiceProvider();
        var logger = NullLogger<UnifiedServiceManager>.Instance;
        var manager = new UnifiedServiceManager(serviceProvider, logger);

        manager.Register<TestSingletonService>();

        var service = await manager.GetOrCreateServiceAsync("Singleton", "local");
        service.Should().NotBeNull();
    }

    #endregion

    #region HubToServiceDispatcher 测试

    [Fact]
    public async Task Dispatcher_Should_Route_To_Service_Queue()
    {
        var serviceProvider = CreateServiceProvider();
        var managerLogger = NullLogger<UnifiedServiceManager>.Instance;
        var dispatcherLogger = NullLogger<HubToServiceDispatcher>.Instance;

        var manager = new UnifiedServiceManager(serviceProvider, managerLogger);
        manager.Register<TestChatRoomService>();

        var dispatcher = new HubToServiceDispatcher(manager, dispatcherLogger);

        var executed = false;
        var context = new DispatchContext
        {
            MethodName = "SendMessage",
            ExplicitServiceType = "ChatRoom",
            ExplicitServiceId = "room-123",
            InvokeAsync = () =>
            {
                executed = true;
                return Task.FromResult<object?>("OK");
            }
        };

        await dispatcher.DispatchAsync(context);

        // 等待队列处理
        await Task.Delay(100);

        executed.Should().BeTrue();

        var stats = dispatcher.GetStatistics();
        stats.DispatchedToService.Should().Be(1);
        stats.DispatchedToPool.Should().Be(0);
    }

    [Fact]
    public async Task Dispatcher_Should_Route_To_Default_Pool_When_No_ServiceId()
    {
        var serviceProvider = CreateServiceProvider();
        var managerLogger = NullLogger<UnifiedServiceManager>.Instance;
        var dispatcherLogger = NullLogger<HubToServiceDispatcher>.Instance;

        var manager = new UnifiedServiceManager(serviceProvider, managerLogger);
        var dispatcher = new HubToServiceDispatcher(manager, dispatcherLogger);

        var executed = false;
        var context = new DispatchContext
        {
            MethodName = "SomeMethod",
            ExplicitServiceType = "SomeService",
            ExplicitServiceId = null, // 无 ServiceId
            InvokeAsync = () =>
            {
                executed = true;
                return Task.FromResult<object?>("OK");
            }
        };

        await dispatcher.DispatchAsync(context);

        // 等待队列处理
        await Task.Delay(100);

        executed.Should().BeTrue();

        var stats = dispatcher.GetStatistics();
        stats.DispatchedToService.Should().Be(0);
        stats.DispatchedToPool.Should().Be(1);
    }

    #endregion

    #region 辅助方法

    private static IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    #endregion
}

#region 测试用服务实现

/// <summary>
/// 测试用聊天室服务
/// </summary>
[PulseService(
    StartupType = ServiceStartupType.OnDemand,
    InstanceScope = ServiceInstanceScope.MultiInstance,
    SchedulingMode = ServiceSchedulingMode.DedicatedQueue,
    DisplayName = "ChatRoom")]
internal class TestChatRoomService : UnifiedPulseServiceBase
{
    private readonly List<string> _messages = new();

    public TestChatRoomService(string serviceId)
        : base("ChatRoom", serviceId)
    {
    }

    public IReadOnlyList<string> Messages => _messages;

    public Task AddMessageAsync(string message)
    {
        _messages.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 测试用单例服务
/// </summary>
[PulseService(
    StartupType = ServiceStartupType.AutoStart,
    InstanceScope = ServiceInstanceScope.ProcessSingleton,
    SchedulingMode = ServiceSchedulingMode.DedicatedQueue,
    DisplayName = "Singleton")]
internal class TestSingletonService : UnifiedPulseServiceBase
{
    public TestSingletonService()
        : base("Singleton", "local")
    {
    }
}

/// <summary>
/// 测试用全局服务
/// </summary>
[PulseService(
    StartupType = ServiceStartupType.AutoStart,
    InstanceScope = ServiceInstanceScope.ClusterSingleton,
    SchedulingMode = ServiceSchedulingMode.DedicatedQueue,
    DisplayName = "Global")]
internal class TestGlobalService : UnifiedPulseServiceBase
{
    public TestGlobalService()
        : base("Global", "global")
    {
    }
}

#endregion

