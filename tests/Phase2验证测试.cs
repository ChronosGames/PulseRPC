using System;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Client.Core;
using PulseRPC.Transport;
using PulseRPC.Authentication;

namespace PulseRPC.Tests;

/// <summary>
/// Phase 2 客户端架构适配验证测试
/// 验证三层抽象架构在客户端的正确集成
/// </summary>
public class Phase2ArchitectureTest
{
    /// <summary>
    /// 验证 IConnection 继承 ISessionChannel 的正确性
    /// </summary>
    public void TestConnectionInterfaceHierarchy()
    {
        var connection = new TestConnectionImplementation();

        // 验证接口继承关系
        Assert.IsAssignableFrom<ISessionChannel>(connection);
        Assert.IsAssignableFrom<ITransportConnection>(connection);
        Assert.IsAssignableFrom<IConnection>(connection);

        // 验证可以访问所有层的功能
        // 传输层功能
        Assert.NotEmpty(connection.ConnectionId);
        Assert.NotNull(connection.RemoteEndPoint);

        // 会话层功能
        Assert.NotNull(connection.Properties);
        Assert.False(connection.IsAuthenticated);

        // 应用层功能
        Assert.NotNull(connection.Descriptor);
        Assert.True(connection.IsAvailable);

        Console.WriteLine("✅ IConnection 接口继承层次验证通过");
    }

    /// <summary>
    /// 验证新的 IPulseHub 接口标准化
    /// </summary>
    public void TestServiceInterfaceStandardization()
    {
        // 验证新的服务接口可以被识别
        var calculatorService = new TestCalculatorService();
        Assert.IsAssignableFrom<IPulseHub>(calculatorService);

        // 验证向后兼容性 - IPulseService 仍然有效
        var legacyService = new TestLegacyService();
        Assert.IsAssignableFrom<IPulseService>(legacyService);
        Assert.IsAssignableFrom<IPulseHub>(legacyService); // IPulseService 继承 IPulseHub

        Console.WriteLine("✅ 服务接口标准化验证通过");
    }

    /// <summary>
    /// 验证连接适配器的功能
    /// </summary>
    public async Task TestConnectionAdapter()
    {
        var sessionChannel = new TestSessionChannel();
        var descriptor = new ConnectionDescriptor
        {
            Id = "test-connection",
            Name = "test",
            ServiceName = "test-service",
            Transport = TransportType.Tcp,
            Strategy = ConnectionStrategy.Persistent,
            AutoReconnect = true
        };
        var connectionManager = new MockConnectionManager();

        var adapter = new ConnectionAdapter(sessionChannel, descriptor, connectionManager);

        // 验证适配器正确委托到会话通道
        Assert.Equal(sessionChannel.ConnectionId, adapter.ConnectionId);
        Assert.Equal(sessionChannel.State, adapter.State);
        Assert.Equal(sessionChannel.IsConnected, adapter.IsConnected);

        // 验证业务层功能
        Assert.Equal(descriptor, adapter.Descriptor);
        Assert.True(adapter.IsAvailable);

        // 验证健康检查功能
        var healthResult = await adapter.CheckHealthAsync();
        Assert.NotNull(healthResult);
        Assert.Equal(adapter.ConnectionId, healthResult.ConnectionId);

        Console.WriteLine("✅ 连接适配器功能验证通过");
    }

    /// <summary>
    /// 验证事件机制的正确转发
    /// </summary>
    public async Task TestEventForwarding()
    {
        var sessionChannel = new TestSessionChannel();
        var descriptor = new ConnectionDescriptor
        {
            Id = "test-connection",
            Name = "test",
            ServiceName = "test-service",
            Transport = TransportType.Tcp,
            Strategy = ConnectionStrategy.Persistent,
            AutoReconnect = true
        };
        var connectionManager = new MockConnectionManager();

        var adapter = new ConnectionAdapter(sessionChannel, descriptor, connectionManager);

        // 测试状态变化事件转发
        var stateChanged = false;
        adapter.StateChanged += (sender, args) =>
        {
            stateChanged = true;
            Assert.Equal(adapter.ConnectionId, args.ConnectionId);
        };

        // 触发底层状态变化
        sessionChannel.TriggerStateChange(ConnectionState.Disconnected);
        Assert.True(stateChanged);

        // 测试健康状态变化事件
        var healthChanged = false;
        adapter.HealthChanged += (sender, args) =>
        {
            healthChanged = true;
            Assert.Equal(adapter.ConnectionId, args.ConnectionId);
        };

        // 状态变为断开应该触发健康状态变化
        sessionChannel.TriggerStateChange(ConnectionState.Failed);
        Assert.True(healthChanged);

        Console.WriteLine("✅ 事件机制转发验证通过");
    }

    /// <summary>
    /// 运行所有验证测试
    /// </summary>
    public async Task RunAllTests()
    {
        Console.WriteLine("🚀 开始 Phase 2 客户端架构适配验证测试");

        TestConnectionInterfaceHierarchy();
        TestServiceInterfaceStandardization();
        await TestConnectionAdapter();
        await TestEventForwarding();

        Console.WriteLine("🎉 Phase 2 客户端架构适配验证测试全部通过!");
    }
}

/// <summary>
/// 测试用的连接实现
/// </summary>
public class TestConnectionImplementation : IConnection
{
    private readonly TestSessionChannel _sessionChannel;
    private readonly ConnectionDescriptor _descriptor;

    public TestConnectionImplementation()
    {
        _sessionChannel = new TestSessionChannel();
        _descriptor = new ConnectionDescriptor
        {
            Id = "test-connection",
            Name = "test",
            ServiceName = "test-service",
            Transport = TransportType.Tcp,
            Strategy = ConnectionStrategy.Persistent,
            AutoReconnect = true
        };
    }

    // IConnection 业务层实现
    public ConnectionDescriptor Descriptor => _descriptor;
    public ConnectionHealth Health => ConnectionHealth.Healthy;
    public ConnectionStatistics Statistics => new ConnectionStatistics();
    public bool IsAvailable => IsConnected && Health == ConnectionHealth.Healthy;

    public Task<T> GetServiceAsync<T>(ServiceProxyOptions? options = null, CancellationToken cancellationToken = default)
        where T : class, IPulseHub
    {
        throw new NotImplementedException("测试实现");
    }

    public Task<ISubscriptionToken> RegisterEventListenerAsync<T>(T listener, EventListenerOptions? options = null, CancellationToken cancellationToken = default)
        where T : class, IPulseEventHandler
    {
        throw new NotImplementedException("测试实现");
    }

    public Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new HealthCheckResult
        {
            ConnectionId = ConnectionId,
            Health = Health,
            ResponseTime = TimeSpan.FromMilliseconds(10),
            Message = "Test health check",
            CheckedAt = DateTime.UtcNow
        });
    }

    public Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public event EventHandler<ConnectionHealthChangedEventArgs>? HealthChanged;

    // ISessionChannel 委托实现
    public string ConnectionId => _sessionChannel.ConnectionId;
    public ConnectionState State => _sessionChannel.State;
    public System.Net.EndPoint RemoteEndPoint => _sessionChannel.RemoteEndPoint;
    public System.Net.EndPoint LocalEndPoint => _sessionChannel.LocalEndPoint;
    public DateTime ConnectedAt => _sessionChannel.ConnectedAt;
    public DateTime LastActivityAt => _sessionChannel.LastActivityAt;
    public TransportType TransportType => _sessionChannel.TransportType;
    public bool IsConnected => _sessionChannel.IsConnected;
    public IAuthenticationContext? AuthenticationContext
    {
        get => _sessionChannel.AuthenticationContext;
        set => _sessionChannel.AuthenticationContext = value;
    }
    public bool IsAuthenticated => _sessionChannel.IsAuthenticated;
    public IDictionary<string, object> Properties => _sessionChannel.Properties;
    public string RemoteAddress => _sessionChannel.RemoteAddress;

    public void SetAuthentication(IAuthenticationContext authContext) => _sessionChannel.SetAuthentication(authContext);
    public void ClearAuthentication() => _sessionChannel.ClearAuthentication();
    public T? GetProperty<T>(string key) => _sessionChannel.GetProperty<T>(key);
    public void SetProperty<T>(string key, T value) => _sessionChannel.SetProperty(key, value);
    public bool RemoveProperty(string key) => _sessionChannel.RemoveProperty(key);
    public bool HasProperty(string key) => _sessionChannel.HasProperty(key);

    public Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        => _sessionChannel.SendAsync(data, cancellationToken);
    public Task CloseAsync(CancellationToken cancellationToken = default)
        => _sessionChannel.CloseAsync(cancellationToken);

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged
    {
        add => _sessionChannel.StateChanged += value;
        remove => _sessionChannel.StateChanged -= value;
    }
    public event EventHandler<TransportDataEventArgs>? DataReceived
    {
        add => _sessionChannel.DataReceived += value;
        remove => _sessionChannel.DataReceived -= value;
    }
    public event EventHandler<AuthenticationChangedEventArgs>? AuthenticationChanged
    {
        add => _sessionChannel.AuthenticationChanged += value;
        remove => _sessionChannel.AuthenticationChanged -= value;
    }

    public void Dispose() => _sessionChannel?.Dispose();
}

/// <summary>
/// 测试用的新服务接口实现
/// </summary>
public interface ITestCalculatorService : IPulseHub
{
    Task<int> AddAsync(int a, int b);
}

public class TestCalculatorService : ITestCalculatorService
{
    public Task<int> AddAsync(int a, int b)
    {
        return Task.FromResult(a + b);
    }
}

/// <summary>
/// 测试用的遗留服务接口实现
/// </summary>
public interface ITestLegacyService : IPulseService
{
    Task<string> GetDataAsync();
}

public class TestLegacyService : ITestLegacyService
{
    public Task<string> GetDataAsync()
    {
        return Task.FromResult("Legacy data");
    }
}

/// <summary>
/// 模拟连接管理器
/// </summary>
public class MockConnectionManager : IConnectionManager
{
    public Task<T> CreateServiceProxyAsync<T>(IConnection connection, ServiceProxyOptions? options, CancellationToken cancellationToken)
        where T : class, IPulseHub
    {
        throw new NotImplementedException("模拟实现");
    }

    public Task<ISubscriptionToken> RegisterEventListenerAsync<T>(IConnection connection, T listener, EventListenerOptions? options, CancellationToken cancellationToken)
        where T : class, IPulseEventHandler
    {
        throw new NotImplementedException("模拟实现");
    }

    public Task ReconnectAsync(string connectionId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}