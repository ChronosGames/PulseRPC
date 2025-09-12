using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Authentication;
using PulseRPC.Transport;

namespace PulseRPC.Tests;

/// <summary>
/// Phase 1 三层抽象架构验证测试
/// 验证传输层和会话层抽象的正确性
/// </summary>
public class Phase1ArchitectureTest
{
    /// <summary>
    /// 验证 ITransportConnection 接口的基本功能
    /// </summary>
    public void TestTransportConnectionInterface()
    {
        // 创建测试传输连接
        var connection = new TestTransportConnection();

        // 验证基本属性
        Assert.NotEmpty(connection.ConnectionId);
        Assert.Equal(ConnectionState.Connected, connection.State);
        Assert.Equal(TransportType.Tcp, connection.TransportType);
        Assert.True(connection.IsConnected);

        // 验证时间属性
        Assert.True(connection.ConnectedAt <= DateTime.UtcNow);
        Assert.True(connection.LastActivityAt <= DateTime.UtcNow);

        Console.WriteLine("✅ ITransportConnection 接口验证通过");
    }

    /// <summary>
    /// 验证 ISessionChannel 接口的会话管理功能
    /// </summary>
    public void TestSessionChannelInterface()
    {
        var sessionChannel = new TestSessionChannel();

        // 验证属性管理
        sessionChannel.SetProperty("test-key", "test-value");
        Assert.True(sessionChannel.HasProperty("test-key"));
        Assert.Equal("test-value", sessionChannel.GetProperty<string>("test-key"));

        sessionChannel.SetProperty("number-key", 42);
        Assert.Equal(42, sessionChannel.GetProperty<int>("number-key"));

        // 验证属性移除
        Assert.True(sessionChannel.RemoveProperty("test-key"));
        Assert.False(sessionChannel.HasProperty("test-key"));

        // 验证认证管理
        Assert.False(sessionChannel.IsAuthenticated);
        Assert.Null(sessionChannel.AuthenticationContext);

        var authContext = new TestAuthenticationContext("test-user");
        sessionChannel.SetAuthentication(authContext);
        Assert.True(sessionChannel.IsAuthenticated);
        Assert.Equal(authContext, sessionChannel.AuthenticationContext);

        sessionChannel.ClearAuthentication();
        Assert.False(sessionChannel.IsAuthenticated);
        Assert.Null(sessionChannel.AuthenticationContext);

        Console.WriteLine("✅ ISessionChannel 接口验证通过");
    }

    /// <summary>
    /// 验证接口继承层次的正确性
    /// </summary>
    public void TestInterfaceHierarchy()
    {
        var sessionChannel = new TestSessionChannel();

        // 验证接口继承关系
        Assert.IsAssignableFrom<ITransportConnection>(sessionChannel);
        Assert.IsAssignableFrom<ISessionChannel>(sessionChannel);

        // ISessionChannel 应该能访问 ITransportConnection 的所有功能
        Assert.NotEmpty(sessionChannel.ConnectionId);
        Assert.NotNull(sessionChannel.RemoteEndPoint);
        Assert.True(sessionChannel.IsConnected);

        Console.WriteLine("✅ 接口继承层次验证通过");
    }

    /// <summary>
    /// 验证事件机制
    /// </summary>
    public async Task TestEventMechanism()
    {
        var sessionChannel = new TestSessionChannel();

        // 测试状态变化事件
        var stateChanged = false;
        sessionChannel.StateChanged += (sender, args) =>
        {
            stateChanged = true;
            Assert.Equal(sessionChannel.ConnectionId, args.ConnectionId);
        };

        // 触发状态变化
        ((TestSessionChannel)sessionChannel).TriggerStateChange(ConnectionState.Disconnected);
        Assert.True(stateChanged);

        // 测试认证变化事件
        var authChanged = false;
        sessionChannel.AuthenticationChanged += (sender, args) =>
        {
            authChanged = true;
            Assert.Equal(sessionChannel.ConnectionId, args.ConnectionId);
        };

        // 触发认证变化
        sessionChannel.SetAuthentication(new TestAuthenticationContext("test-user"));
        Assert.True(authChanged);

        Console.WriteLine("✅ 事件机制验证通过");
    }

    /// <summary>
    /// 运行所有验证测试
    /// </summary>
    public async Task RunAllTests()
    {
        Console.WriteLine("🚀 开始 Phase 1 三层抽象架构验证测试");

        TestTransportConnectionInterface();
        TestSessionChannelInterface();
        TestInterfaceHierarchy();
        await TestEventMechanism();

        Console.WriteLine("🎉 Phase 1 三层抽象架构验证测试全部通过!");
    }
}

/// <summary>
/// 测试用的传输连接实现
/// </summary>
public class TestTransportConnection : ITransportConnection
{
    public string ConnectionId { get; } = Guid.NewGuid().ToString();
    public ConnectionState State { get; private set; } = ConnectionState.Connected;
    public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 8080);
    public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 8081);
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; private set; } = DateTime.UtcNow;
    public TransportType TransportType { get; } = TransportType.Tcp;
    public bool IsConnected => State == ConnectionState.Connected;

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;
    public event EventHandler<TransportDataEventArgs>? DataReceived;

    public Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        LastActivityAt = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        State = ConnectionState.Disconnected;
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(
            ConnectionId, ConnectionState.Connected, ConnectionState.Disconnected, "Manual close"));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // 测试实现，无需释放资源
    }
}

/// <summary>
/// 测试用的会话通道实现
/// </summary>
public class TestSessionChannel : SessionChannelBase
{
    private readonly TestTransportConnection _transport;

    public TestSessionChannel()
    {
        _transport = new TestTransportConnection();
        _transport.StateChanged += (sender, args) => StateChanged?.Invoke(this, args);
        _transport.DataReceived += (sender, args) => DataReceived?.Invoke(this, args);
    }

    public override string ConnectionId => _transport.ConnectionId;
    public override ConnectionState State => _transport.State;
    public override EndPoint RemoteEndPoint => _transport.RemoteEndPoint;
    public override EndPoint LocalEndPoint => _transport.LocalEndPoint;
    public override DateTime ConnectedAt => _transport.ConnectedAt;
    public override DateTime LastActivityAt => _transport.LastActivityAt;
    public override TransportType TransportType => _transport.TransportType;
    public override bool IsConnected => _transport.IsConnected;

    public override event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;
    public override event EventHandler<TransportDataEventArgs>? DataReceived;

    public override Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        return _transport.SendAsync(data, cancellationToken);
    }

    public override Task CloseAsync(CancellationToken cancellationToken = default)
    {
        return _transport.CloseAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _transport?.Dispose();
    }

    // 测试辅助方法
    public void TriggerStateChange(ConnectionState newState)
    {
        var args = new ConnectionStateChangedEventArgs(
            ConnectionId, State, newState, "Test trigger");
        StateChanged?.Invoke(this, args);
    }
}

/// <summary>
/// 测试用的认证上下文实现
/// </summary>
public class TestAuthenticationContext : IAuthenticationContext
{
    public string UserId { get; }
    public bool IsAuthenticated { get; private set; } = true;
    public IDictionary<string, object> Claims { get; } = new Dictionary<string, object>();

    public TestAuthenticationContext(string userId)
    {
        UserId = userId;
        Claims["user_id"] = userId;
    }

    public void Clear()
    {
        IsAuthenticated = false;
        Claims.Clear();
    }
}

/// <summary>
/// 简单的断言辅助类
/// </summary>
public static class Assert
{
    public static void True(bool condition)
    {
        if (!condition) throw new Exception("断言失败: 期望为 true");
    }

    public static void False(bool condition)
    {
        if (condition) throw new Exception("断言失败: 期望为 false");
    }

    public static void NotEmpty(string value)
    {
        if (string.IsNullOrEmpty(value)) throw new Exception("断言失败: 期望非空字符串");
    }

    public static void NotNull(object? value)
    {
        if (value == null) throw new Exception("断言失败: 期望非空对象");
    }

    public static void Null(object? value)
    {
        if (value != null) throw new Exception("断言失败: 期望空对象");
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual))
            throw new Exception($"断言失败: 期望 {expected}, 实际 {actual}");
    }

    public static void IsAssignableFrom<T>(object obj)
    {
        if (!(obj is T))
            throw new Exception($"断言失败: 对象不能赋值给类型 {typeof(T).Name}");
    }
}