using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Authentication;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Sessions;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;

namespace PulseRPC.Tests;

/// <summary>
/// Phase 3 服务端架构增强验证测试
/// 验证三层抽象架构在服务端的完整集成
/// </summary>
public class Phase3ArchitectureTest
{
    /// <summary>
    /// 验证 IClientSession 接口继承层次的正确性
    /// </summary>
    public void TestClientSessionInterfaceHierarchy()
    {
        var descriptor = new ClientSessionDescriptor
        {
            Id = "test-session",
            Name = "TestSession",
            Transport = TransportType.Tcp,
            TimeoutMs = 300000,
            AutoCleanup = true
        };

        var serverChannel = new TestServerChannel();
        var sessionManager = new TestClientSessionManager();
        var session = new ClientSessionAdapter(serverChannel, descriptor, sessionManager);

        // 验证接口继承关系
        Assert.IsAssignableFrom<ISessionChannel>(session);
        Assert.IsAssignableFrom<ITransportConnection>(session);
        Assert.IsAssignableFrom<IClientSession>(session);

        // 验证可以访问所有层的功能
        // 传输层功能
        Assert.NotEmpty(session.ConnectionId);
        Assert.NotNull(session.RemoteEndPoint);
        Assert.True(session.IsConnected);

        // 会话层功能
        Assert.NotNull(session.Properties);
        Assert.False(session.IsAuthenticated);

        // 应用层功能 (服务端会话)
        Assert.NotNull(session.Descriptor);
        Assert.Equal(descriptor.Id, session.Descriptor.Id);
        Assert.True(session.IsAvailable);
        Assert.Equal(SessionHealth.Healthy, session.Health);

        Console.WriteLine("✅ IClientSession 接口继承层次验证通过");
    }

    /// <summary>
    /// 验证服务端会话管理器功能
    /// </summary>
    public async Task TestServerSessionManagerFunctionality()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<ServerSessionManager>>();
        var sessionManager = new ServerSessionManager(logger);

        try
        {
            // 创建测试会话
            var serverChannel = new TestServerChannel();
            var descriptor = new ClientSessionDescriptor
            {
                Id = "test-session-1",
                Name = "TestSession1",
                Transport = TransportType.Tcp,
                ClientId = serverChannel.ConnectionId,
                TimeoutMs = 300000,
                AutoCleanup = true
            };

            // 测试会话创建
            var session = sessionManager.CreateSession(serverChannel, descriptor);
            Assert.NotNull(session);
            Assert.Equal(descriptor.Id, session.Descriptor.Id);
            Assert.Equal(1, sessionManager.SessionCount);

            // 测试会话检索
            var retrievedSession = sessionManager.GetSession(descriptor.Id);
            Assert.NotNull(retrievedSession);
            Assert.Equal(session.Descriptor.Id, retrievedSession.Descriptor.Id);

            // 按连接ID检索
            var sessionByConnection = sessionManager.GetSessionByConnectionId(serverChannel.ConnectionId);
            Assert.NotNull(sessionByConnection);
            Assert.Equal(session.Descriptor.Id, sessionByConnection.Descriptor.Id);

            // 测试会话组功能
            session.SetGroups(new[] { "admin", "authenticated" });
            Assert.Contains("admin", session.Groups);
            Assert.Contains("authenticated", session.Groups);

            var adminSessions = sessionManager.GetSessionsByGroup("admin");
            Assert.Single(adminSessions);

            // 测试会话标签功能
            session.SetTag("environment", "test");
            session.SetTag("version", "1.0.0");

            var taggedSessions = sessionManager.GetSessionsByTag("environment", "test");
            Assert.Single(taggedSessions);

            // 测试健康检查
            var healthResult = await session.CheckHealthAsync();
            Assert.NotNull(healthResult);
            Assert.Equal(descriptor.Id, healthResult.SessionId);

            // 测试统计信息
            var stats = sessionManager.GetSessionManagerStats();
            Assert.Equal(1, stats.ActiveSessions);
            Assert.Equal(1, stats.TotalSessionsCreated);

            // 测试会话移除
            var removed = sessionManager.RemoveSession(descriptor.Id);
            Assert.True(removed);
            Assert.Equal(0, sessionManager.SessionCount);

            Console.WriteLine("✅ 服务端会话管理器功能验证通过");
        }
        finally
        {
            sessionManager.Dispose();
        }
    }

    /// <summary>
    /// 验证增强的服务端通道管理器桥接功能
    /// </summary>
    public async Task TestEnhancedServerChannelManagerBridge()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        // 创建管理器实例
        var channelManagerLogger = loggerFactory.CreateLogger<ServerChannelManager>();
        var sessionManagerLogger = loggerFactory.CreateLogger<ServerSessionManager>();
        var enhancedManagerLogger = loggerFactory.CreateLogger<EnhancedServerChannelManager>();

        var processorOptions = Options.Create(new MessageEngineConfiguration { Enabled = false });
        var channelManager = new ServerChannelManager(channelManagerLogger, processorOptions, loggerFactory);
        var sessionManager = new ServerSessionManager(sessionManagerLogger);
        var enhancedManager = new EnhancedServerChannelManager(channelManager, sessionManager, enhancedManagerLogger);

        try
        {
            // 测试桥接接口
            Assert.IsAssignableFrom<IServerChannelManager>(enhancedManager);
            Assert.IsAssignableFrom<IEnhancedServerChannelManager>(enhancedManager);
            Assert.NotNull(enhancedManager.SessionManager);

            // 创建测试传输连接
            var transport = new TestServerTransport("test-connection-1");

            // 通过增强管理器添加通道（应该自动创建会话）
            var channel = enhancedManager.AddChannel(transport);
            Assert.NotNull(channel);
            Assert.Equal(1, enhancedManager.ConnectionCount);

            // 验证自动会话创建
            await Task.Delay(100); // 给异步创建时间
            Assert.Equal(1, enhancedManager.SessionManager.SessionCount);

            var session = enhancedManager.SessionManager.GetSessionByConnectionId(channel.ConnectionId);
            Assert.NotNull(session);

            // 测试通道管理功能委托
            var retrievedChannel = enhancedManager.GetChannel(channel.ConnectionId);
            Assert.NotNull(retrievedChannel);
            Assert.Equal(channel.ConnectionId, retrievedChannel.ConnectionId);

            var allChannels = enhancedManager.GetAllChannels();
            Assert.Single(allChannels);

            // 测试移除功能（应该自动移除会话）
            var removed = enhancedManager.RemoveChannel(channel.ConnectionId);
            Assert.True(removed);
            Assert.Equal(0, enhancedManager.ConnectionCount);
            Assert.Equal(0, enhancedManager.SessionManager.SessionCount);

            Console.WriteLine("✅ 增强服务端通道管理器桥接功能验证通过");
        }
        finally
        {
            enhancedManager.Dispose();
        }
    }

    /// <summary>
    /// 验证依赖注入集成
    /// </summary>
    public void TestDependencyInjectionIntegration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 测试传统模式
        services.AddPulseRPCServer();
        var traditionalProvider = services.BuildServiceProvider();

        var traditionalChannelManager = traditionalProvider.GetService<IServerChannelManager>();
        Assert.NotNull(traditionalChannelManager);
        Assert.IsType<ServerChannelManager>(traditionalChannelManager);

        // 重建服务集合测试增强模式
        services = new ServiceCollection();
        services.AddLogging();
        services.AddEnhancedPulseRPCServer(options =>
        {
            options.EnableSessionManagement = true;
            options.SessionTimeoutMs = 300000;
            options.EnableAutoSessionCleanup = true;
        });

        var enhancedProvider = services.BuildServiceProvider();

        // 验证服务注册
        var enhancedChannelManager = enhancedProvider.GetService<IEnhancedServerChannelManager>();
        Assert.NotNull(enhancedChannelManager);
        Assert.IsType<EnhancedServerChannelManager>(enhancedChannelManager);

        var sessionManager = enhancedProvider.GetService<IServerSessionManager>();
        Assert.NotNull(sessionManager);
        Assert.IsType<ServerSessionManager>(sessionManager);

        var clientSessionManager = enhancedProvider.GetService<IClientSessionManager>();
        Assert.NotNull(clientSessionManager);
        Assert.Same(sessionManager, clientSessionManager);

        // 验证可选服务
        services.AddSessionHealthChecks();
        services.AddSessionBroadcast();

        var fullProvider = services.BuildServiceProvider();
        var healthChecker = fullProvider.GetService<ISessionHealthChecker>();
        var broadcastService = fullProvider.GetService<ISessionBroadcastService>();

        Assert.NotNull(healthChecker);
        Assert.NotNull(broadcastService);

        Console.WriteLine("✅ 依赖注入集成验证通过");
    }

    /// <summary>
    /// 验证会话健康检查功能
    /// </summary>
    public async Task TestSessionHealthCheckFunctionality()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSessionHealthChecks(options =>
        {
            options.CheckTimeoutMs = 5000;
            options.MaxUnhealthyDurationMs = 60000;
            options.EnableAutoCleanup = true;
        });

        var provider = services.BuildServiceProvider();
        var healthChecker = provider.GetRequiredService<ISessionHealthChecker>();

        // 创建测试会话
        var serverChannel = new TestServerChannel();
        var descriptor = new ClientSessionDescriptor
        {
            Id = "health-test-session",
            Name = "HealthTestSession",
            Transport = TransportType.Tcp,
            TimeoutMs = 300000
        };

        var sessionManager = new TestClientSessionManager();
        var session = new ClientSessionAdapter(serverChannel, descriptor, sessionManager);

        try
        {
            // 测试健康检查
            var result = await healthChecker.CheckSessionHealthAsync(session);
            Assert.NotNull(result);
            Assert.Equal(session.Descriptor.Id, result.SessionId);
            Assert.Equal(SessionHealth.Healthy, result.Health);

            // 测试批量健康检查
            var sessions = new List<IClientSession> { session };
            var results = await healthChecker.CheckSessionsHealthAsync(sessions);
            Assert.Single(results);

            Console.WriteLine("✅ 会话健康检查功能验证通过");
        }
        finally
        {
            session.Dispose();
        }
    }

    /// <summary>
    /// 验证事件机制的正确转发和处理
    /// </summary>
    public async Task TestSessionEventHandling()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<ServerSessionManager>>();

        var sessionManager = new ServerSessionManager(logger);

        // 设置事件监听器
        var sessionCreatedCount = 0;
        var sessionRemovedCount = 0;
        var sessionAuthenticatedCount = 0;

        sessionManager.SessionCreated += (sender, e) =>
        {
            sessionCreatedCount++;
            Assert.NotNull(e.Session);
        };

        sessionManager.SessionRemoved += (sender, e) =>
        {
            sessionRemovedCount++;
            Assert.NotNull(e.Session);
        };

        sessionManager.SessionAuthenticated += (sender, e) =>
        {
            sessionAuthenticatedCount++;
            Assert.NotNull(e.Session);
            Assert.NotNull(e.AuthenticationContext);
        };

        try
        {
            // 创建会话 - 应该触发SessionCreated事件
            var serverChannel = new TestServerChannel();
            var descriptor = new ClientSessionDescriptor
            {
                Id = "event-test-session",
                Name = "EventTestSession",
                Transport = TransportType.Tcp,
                ClientId = serverChannel.ConnectionId
            };

            var session = sessionManager.CreateSession(serverChannel, descriptor);
            Assert.Equal(1, sessionCreatedCount);

            // 模拟认证 - 应该触发SessionAuthenticated事件
            var authContext = new TestAuthenticationContext("testuser");
            session.AuthenticationContext = authContext;
            await Task.Delay(50); // 给事件处理时间

            Assert.Equal(1, sessionAuthenticatedCount);

            // 移除会话 - 应该触发SessionRemoved事件
            sessionManager.RemoveSession(session.Descriptor.Id);
            Assert.Equal(1, sessionRemovedCount);

            Console.WriteLine("✅ 会话事件处理验证通过");
        }
        finally
        {
            sessionManager.Dispose();
        }
    }

    /// <summary>
    /// 运行所有验证测试
    /// </summary>
    public async Task RunAllTests()
    {
        Console.WriteLine("🚀 开始 Phase 3 服务端架构增强验证测试");

        TestClientSessionInterfaceHierarchy();
        await TestServerSessionManagerFunctionality();
        await TestEnhancedServerChannelManagerBridge();
        TestDependencyInjectionIntegration();
        await TestSessionHealthCheckFunctionality();
        await TestSessionEventHandling();

        Console.WriteLine("🎉 Phase 3 服务端架构增强验证测试全部通过!");
    }
}

#region 测试支持类

/// <summary>
/// 测试用的服务端通道实现
/// </summary>
public class TestServerChannel : IServerChannel
{
    private readonly string _connectionId;
    private readonly TestServerTransport _transport;

    public TestServerChannel(string connectionId = "test-connection")
    {
        _connectionId = connectionId;
        _transport = new TestServerTransport(connectionId);
    }

    // IServerChannel Implementation
    public IServerTransport Transport => _transport;

    // ITransportConnection Implementation
    public string ConnectionId => _connectionId;
    public ConnectionState State => ConnectionState.Connected;
    public System.Net.EndPoint RemoteEndPoint => new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 12345);
    public System.Net.EndPoint LocalEndPoint => new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 8080);
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public TransportType TransportType => TransportType.Tcp;
    public bool IsConnected => State == ConnectionState.Connected;

    // ISessionChannel Implementation
    public IAuthenticationContext? AuthenticationContext { get; set; }
    public bool IsAuthenticated => AuthenticationContext?.IsAuthenticated ?? false;
    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();
    public string RemoteAddress => RemoteEndPoint.ToString() ?? "Unknown";

    public void SetAuthentication(IAuthenticationContext authContext) => AuthenticationContext = authContext;
    public void ClearAuthentication() => AuthenticationContext = null;

    public T? GetProperty<T>(string key) => Properties.TryGetValue(key, out var value) && value is T typedValue ? typedValue : default;
    public void SetProperty<T>(string key, T value) { if (value != null) Properties[key] = value; else Properties.Remove(key); }
    public bool RemoveProperty(string key) => Properties.Remove(key);
    public bool HasProperty(string key) => Properties.ContainsKey(key);

    public DateTime LastActiveTime { get; set; } = DateTime.UtcNow;

    // Events
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;
    public event EventHandler<TransportDataEventArgs>? DataReceived;
    public event EventHandler<AuthenticationChangedEventArgs>? AuthenticationChanged;

    // Methods
    public Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Dispose()
    {
        // Cleanup
    }
}

/// <summary>
/// 测试用的服务端传输实现
/// </summary>
public class TestServerTransport : IServerTransport
{
    public TestServerTransport(string connectionId)
    {
        ConnectionId = connectionId;
    }

    public string ConnectionId { get; }
    public ConnectionState State => ConnectionState.Connected;
    public System.Net.EndPoint RemoteEndPoint => new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 12345);
    public System.Net.EndPoint LocalEndPoint => new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 8080);
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;
    public DateTime LastActivityAt => DateTime.UtcNow;
    public TransportType Type => TransportType.Tcp;
    public bool IsConnected => true;

    public event EventHandler<TransportStateEventArgs>? StateChanged;
    public event EventHandler<TransportDataEventArgs>? DataReceived;

    public Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Dispose()
    {
        // Cleanup
    }
}

/// <summary>
/// 测试用的客户端会话管理器
/// </summary>
public class TestClientSessionManager : IClientSessionManager
{
    public Task<TResult> InvokeHubMethodAsync<THub, TResult>(IClientSession session, string methodName, object?[] args, CancellationToken cancellationToken)
        where THub : class, IPulseHub
    {
        // 模拟实现，返回默认值
        return Task.FromResult(default(TResult)!);
    }

    public Task InvokeHubMethodAsync<THub>(IClientSession session, string methodName, object?[] args, CancellationToken cancellationToken)
        where THub : class, IPulseHub
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// 测试用的认证上下文
/// </summary>
public class TestAuthenticationContext : IAuthenticationContext
{
    public TestAuthenticationContext(string name)
    {
        Name = name;
        IsAuthenticated = true;
    }

    public string? Name { get; }
    public bool IsAuthenticated { get; }
    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();

    public void Clear()
    {
        Properties.Clear();
    }
}

/// <summary>
/// 简单的断言帮助类
/// </summary>
public static class Assert
{
    public static void NotNull(object? value)
    {
        if (value == null) throw new Exception("Value should not be null");
    }

    public static void NotEmpty(string? value)
    {
        if (string.IsNullOrEmpty(value)) throw new Exception("String should not be null or empty");
    }

    public static void True(bool condition)
    {
        if (!condition) throw new Exception("Condition should be true");
    }

    public static void False(bool condition)
    {
        if (condition) throw new Exception("Condition should be false");
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected: {expected}, Actual: {actual}");
    }

    public static void IsAssignableFrom<T>(object value)
    {
        if (value is not T) throw new Exception($"Value is not assignable from {typeof(T).Name}");
    }

    public static void IsType<T>(object value)
    {
        if (value.GetType() != typeof(T)) throw new Exception($"Expected type {typeof(T).Name}, got {value.GetType().Name}");
    }

    public static void Single<T>(IEnumerable<T> collection)
    {
        if (collection.Count() != 1) throw new Exception($"Expected single item, got {collection.Count()}");
    }

    public static void Contains<T>(T item, IEnumerable<T> collection)
    {
        if (!collection.Contains(item)) throw new Exception($"Collection does not contain {item}");
    }

    public static void Same(object expected, object actual)
    {
        if (!ReferenceEquals(expected, actual)) throw new Exception("Objects are not the same reference");
    }
}

#endregion