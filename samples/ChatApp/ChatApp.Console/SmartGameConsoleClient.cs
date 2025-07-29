using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Routing;
using PulseRPC.SmartConnection;
using ChatApp;
using PulseRPC.Transport;
using ServiceDiscoveryType = PulseRPC.ServiceDiscoveryType;

namespace ChatApp.Console;

/// <summary>
/// 智能游戏控制台客户端 - 展示PulseRPC智能连接功能
/// </summary>
public class SmartGameConsoleClient
{
    private readonly ILogger<SmartGameConsoleClient> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private ISmartPulseRpcClient? _smartClient;
    private IChatHub? _chatService;
    private IPlayerHub? _playerService;
    private IBattleService? _battleService;
    private IMultiInstanceServiceManager<IChatHub>? _chatManager;
    private IMultiInstanceServiceManager<IBattleService>? _battleManager;
    private ISubscriptionToken? _chatSubscription;
    private ISubscriptionToken? _playerSubscription;
    private ISubscriptionToken? _battleSubscription;
    private CancellationTokenSource? _cts;
    private string _currentUser = "";
    private bool _isConnected;

    public SmartGameConsoleClient(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SmartGameConsoleClient>();
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("🚀 PulseRPC 智能连接演示客户端启动");

        try
        {
            await InitializeSmartClientAsync();
            await ShowMainMenuAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "客户端运行出错");
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private async Task InitializeSmartClientAsync()
    {
        _logger.LogInformation("📡 初始化智能客户端...");

        _cts = new CancellationTokenSource();

        // 创建智能客户端，展示完整的配置选项
        _smartClient = PulseRpcClientFactory.CreateSmartBuilder()
            .WithLogger(_loggerFactory)
            .WithServiceDiscovery(config =>
            {
                config.Type = ServiceDiscoveryType.Static;
                config.Cache.Enabled = true;
                config.Cache.Expiration = TimeSpan.FromMinutes(5);
                config.RefreshInterval = TimeSpan.FromSeconds(30);

                // 配置聊天服务集群
                config.StaticEndpoints["ChatService"] = new ServiceEndpoint
                {
                    Host = "localhost",
                    Port = 8000,
                    Transport = TransportType.Tcp
                };

                // 配置玩家服务
                config.StaticEndpoints["PlayerService"] = new ServiceEndpoint
                {
                    Host = "localhost",
                    Port = 8000,
                    Transport = TransportType.Tcp
                };

                // 配置战斗服务集群（多实例）
                config.StaticEndpoints["BattleService-1"] = new ServiceEndpoint
                {
                    Host = "localhost",
                    Port = 8002,
                    Transport = TransportType.Kcp
                };

                config.StaticEndpoints["BattleService-2"] = new ServiceEndpoint
                {
                    Host = "localhost",
                    Port = 8003,
                    Transport = TransportType.Kcp
                };
            })
            .WithServiceRouting<IChatHub>(config =>
            {
                config.DefaultStrategy = ServiceRoutingStrategy.RoundRobin;
                config.Failover.EnableFailover = true;
                config.Failover.MaxRetries = 3;
                config.HealthCheck.Enabled = true;
                config.HealthCheck.Interval = TimeSpan.FromSeconds(30);
            })
            .WithServiceRouting<IBattleService>(config =>
            {
                config.DefaultStrategy = ServiceRoutingStrategy.ConsistentHashing;
                config.Failover.EnableFailover = true;
                config.Failover.MaxRetries = 2;
                config.HealthCheck.Enabled = true;
            })
            .AddStaticService("ChatService", "localhost", 8000)
            .AddStaticService("PlayerService", "localhost", 8000)
            .AddStaticService("BattleService", "localhost", 8002, TransportType.Kcp)
            .Build();

        _logger.LogInformation("✅ 智能客户端初始化完成");
    }

    private async Task ShowMainMenuAsync()
    {
        while (!_cts!.Token.IsCancellationRequested)
        {
            System.Console.Clear();
            System.Console.WriteLine("🎮 PulseRPC 智能连接演示");
            System.Console.WriteLine("========================");
            System.Console.WriteLine($"状态: {(_isConnected ? "🟢 已连接" : "🔴 未连接")}");
            System.Console.WriteLine($"用户: {_currentUser}");
            System.Console.WriteLine();

            if (!_isConnected)
            {
                System.Console.WriteLine("1. 🔗 智能连接服务");
                System.Console.WriteLine("2. 📊 查看连接统计");
                System.Console.WriteLine("3. 🔧 配置路由策略");
            }
            else
            {
                System.Console.WriteLine("1. 💬 发送聊天消息");
                System.Console.WriteLine("2. 🎯 加入战斗房间");
                System.Console.WriteLine("3. 📡 广播消息到所有实例");
                System.Console.WriteLine("4. 🔄 聚合查询");
                System.Console.WriteLine("5. 📊 查看连接统计");
                System.Console.WriteLine("6. 🧹 清理空闲连接");
                System.Console.WriteLine("7. 🔌 断开连接");
            }

            System.Console.WriteLine("0. ❌ 退出");
            System.Console.WriteLine();
            System.Console.Write("请选择操作: ");

            var input = System.Console.ReadLine();

            try
            {
                if (!_isConnected)
                {
                    switch (input)
                    {
                        case "1":
                            await ConnectToServicesAsync();
                            break;
                        case "2":
                            await ShowConnectionStatisticsAsync();
                            break;
                        case "3":
                            await ConfigureRoutingStrategiesAsync();
                            break;
                        case "0":
                            return;
                    }
                }
                else
                {
                    switch (input)
                    {
                        case "1":
                            await SendChatMessageAsync();
                            break;
                        case "2":
                            await JoinBattleRoomAsync();
                            break;
                        case "3":
                            await BroadcastMessageAsync();
                            break;
                        case "4":
                            await AggregateQueryAsync();
                            break;
                        case "5":
                            await ShowConnectionStatisticsAsync();
                            break;
                        case "6":
                            await CleanupIdleConnectionsAsync();
                            break;
                        case "7":
                            await DisconnectAsync();
                            break;
                        case "0":
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "操作执行失败");
                System.Console.WriteLine($"❌ 操作失败: {ex.Message}");
                System.Console.WriteLine("按任意键继续...");
                System.Console.ReadKey();
            }
        }
    }

    private async Task ConnectToServicesAsync()
    {
        System.Console.Write("请输入用户名: ");
        _currentUser = System.Console.ReadLine() ?? "Player";

        _logger.LogInformation("🔗 开始智能连接服务...");

        try
        {
            // 演示不同的服务获取方式

            // 1. 基于用户ID的路由上下文
            var userRoutingContext = RoutingContext.ByUserId(_currentUser);
            _chatService = await _smartClient!.GetServiceAsync<IChatHub>("ChatService", userRoutingContext);
            _logger.LogInformation("✅ 聊天服务连接成功 (用户路由)");

            // 2. 默认服务获取
            _playerService = await _smartClient.GetServiceAsync<IPlayerHub>();
            _logger.LogInformation("✅ 玩家服务连接成功 (默认)");

            // 3. 特定实例连接
            _battleService = await _smartClient.GetServiceAsync<IBattleService>("BattleService", "BattleService-1");
            _logger.LogInformation("✅ 战斗服务连接成功 (特定实例)");

            // 4. 获取多实例管理器
            _chatManager = await _smartClient.GetMultiInstanceServiceAsync<IChatHub>("ChatService");
            _battleManager = await _smartClient.GetMultiInstanceServiceAsync<IBattleService>("BattleService");
            _logger.LogInformation("✅ 多实例管理器获取成功");

            // 注册事件监听器
            await RegisterEventListenersAsync();

            // 登录服务
            var loginResult = await _playerService.LoginAsync(new LoginRequest { Username = _currentUser });
            var chatJoinResult = await _chatService.JoinAsync(new JoinRequest { UserName = _currentUser });

            if (loginResult.Success && chatJoinResult)
            {
                _isConnected = true;
                _logger.LogInformation("🎉 所有服务连接完成！");
                System.Console.WriteLine("🎉 智能连接成功！按任意键继续...");
                System.Console.ReadKey();
            }
            else
            {
                throw new Exception("服务登录失败");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "智能连接失败");
            throw;
        }
    }

    private async Task RegisterEventListenersAsync()
    {
        // 注册聊天事件监听器 - 使用用户亲和性
        var chatContext = RoutingContext.ByUserId(_currentUser);
        _chatSubscription = await _smartClient!.RegisterEventListenerAsync(
            new SmartChatEventHandler(_logger), "ChatService", chatContext);

        // 注册玩家事件监听器 - 使用默认路由
        _playerSubscription = await _smartClient.RegisterEventListenerAsync(
            new SmartPlayerEventHandler(_logger), "PlayerService");

        // 注册战斗事件监听器 - 使用房间亲和性
        var battleContext = RoutingContext.ByKey("default-room");
        _battleSubscription = await _smartClient.RegisterEventListenerAsync(
            new SmartBattleEventHandler(_logger), "BattleService", battleContext);

        _logger.LogInformation("✅ 事件监听器注册完成");
    }

    private async Task SendChatMessageAsync()
    {
        System.Console.Write("请输入消息: ");
        var message = System.Console.ReadLine();

        if (!string.IsNullOrEmpty(message))
        {
            var success = await _chatService!.SendMessageAsync(message);
            System.Console.WriteLine(success ? "✅ 消息发送成功" : "❌ 消息发送失败");
        }
    }

    private async Task JoinBattleRoomAsync()
    {
        System.Console.Write("请输入战斗房间ID (留空自动生成): ");
        var roomInput = System.Console.ReadLine();
        var roomId = string.IsNullOrEmpty(roomInput) ? $"room_{DateTime.Now:yyyyMMdd_HHmmss}" : roomInput;

        // 使用房间ID进行一致性哈希路由
        var battleContext = RoutingContext.ByBattleRoom(roomId);
        var battleService = await _smartClient!.GetServiceAsync<IBattleService>("BattleService", battleContext);

        var joinResult = await battleService.JoinBattleAsync(new JoinBattleRequest
        {
            PlayerId = _currentUser,
            RoomId = roomId
        });

        if (joinResult.Success)
        {
            System.Console.WriteLine($"✅ 成功加入战斗房间: {roomId}");
            System.Console.WriteLine($"   路由到实例: {joinResult.InstanceId}");
        }
        else
        {
            System.Console.WriteLine($"❌ 加入战斗房间失败: {joinResult.ErrorMessage}");
        }

        System.Console.WriteLine("按任意键继续...");
        System.Console.ReadKey();
    }

    private async Task BroadcastMessageAsync()
    {
        System.Console.Write("请输入广播消息: ");
        var message = System.Console.ReadLine();

        if (!string.IsNullOrEmpty(message))
        {
            _logger.LogInformation("📡 开始广播消息到所有聊天服务实例...");

            var broadcastResult = await _chatManager!.BroadcastAsync(async chat =>
                await chat.SendGlobalAnnouncementAsync($"📢 全局广播: {message}"));

            System.Console.WriteLine($"📡 广播结果:");
            System.Console.WriteLine($"   总实例数: {broadcastResult.TotalCount}");
            System.Console.WriteLine($"   成功数: {broadcastResult.SuccessCount}");
            System.Console.WriteLine($"   失败数: {broadcastResult.FailureCount}");
            System.Console.WriteLine($"   成功率: {broadcastResult.SuccessRate:P1}");

            if (broadcastResult.FailureCount > 0)
            {
                System.Console.WriteLine("   失败详情:");
                foreach (var failure in broadcastResult.GetFailureExceptions())
                {
                    System.Console.WriteLine($"     - {failure.Message}");
                }
            }
        }

        System.Console.WriteLine("按任意键继续...");
        System.Console.ReadKey();
    }

    private async Task AggregateQueryAsync()
    {
        _logger.LogInformation("🔍 执行聚合查询 - 统计所有战斗服务实例的在线玩家数...");

        var totalOnlinePlayers = await _battleManager!.AggregateAsync(
            async battle => await battle.GetOnlinePlayerCountAsync(),
            results => results.Sum());

        System.Console.WriteLine($"📊 聚合查询结果:");
        System.Console.WriteLine($"   全服在线玩家总数: {totalOnlinePlayers}");

        // 获取每个实例的详细信息
        var detailResults = await _battleManager.BroadcastAsync(async battle =>
            new
            {
                OnlineCount = await battle.GetOnlinePlayerCountAsync(),
                ServerInfo = await battle.GetServerInfoAsync()
            });

        System.Console.WriteLine($"   实例详情:");
        var instanceIndex = 1;
        foreach (var result in detailResults.GetSuccessResults())
        {
            System.Console.WriteLine($"     实例{instanceIndex}: {result.OnlineCount} 人在线, 服务器: {result.ServerInfo}");
            instanceIndex++;
        }

        System.Console.WriteLine("按任意键继续...");
        System.Console.ReadKey();
    }

    private async Task ShowConnectionStatisticsAsync()
    {
        var stats = await _smartClient!.GetConnectionStatisticsAsync();

        System.Console.WriteLine("📊 连接统计信息:");
        System.Console.WriteLine($"   总连接数: {stats.TotalConnections}");
        System.Console.WriteLine($"   活跃连接: {stats.ActiveConnections}");
        System.Console.WriteLine($"   空闲连接: {stats.IdleConnections}");
        System.Console.WriteLine($"   失败连接: {stats.FailedConnections}");
        System.Console.WriteLine($"   统计时间: {stats.Timestamp:yyyy-MM-dd HH:mm:ss}");

        if (stats.ServiceStatistics.Count > 0)
        {
            System.Console.WriteLine("   服务统计:");
            foreach (var serviceStat in stats.ServiceStatistics)
            {
                var stat = serviceStat.Value;
                System.Console.WriteLine($"     {serviceStat.Key}:");
                System.Console.WriteLine($"       连接数: {stat.ConnectionCount}");
                System.Console.WriteLine($"       实例数: {stat.InstanceCount}");
                System.Console.WriteLine($"       健康实例: {stat.HealthyInstanceCount}");
                System.Console.WriteLine($"       总请求: {stat.TotalRequests}");
                System.Console.WriteLine($"       成功请求: {stat.SuccessfulRequests}");
                System.Console.WriteLine($"       平均响应时间: {stat.AverageResponseTime.TotalMilliseconds:F2}ms");
            }
        }

        System.Console.WriteLine("按任意键继续...");
        System.Console.ReadKey();
    }

    private async Task CleanupIdleConnectionsAsync()
    {
        System.Console.Write("请输入最大空闲时间(秒，默认300): ");
        var input = System.Console.ReadLine();
        var maxAge = int.TryParse(input, out var seconds) && seconds > 0 ?
            TimeSpan.FromSeconds(seconds) : TimeSpan.FromMinutes(5);

        var cleanedCount = await _smartClient!.CleanupIdleConnectionsAsync(maxAge);

        System.Console.WriteLine($"🧹 清理完成，移除了 {cleanedCount} 个空闲连接");
        System.Console.WriteLine("按任意键继续...");
        System.Console.ReadKey();
    }

    private async Task ConfigureRoutingStrategiesAsync()
    {
        System.Console.WriteLine("🔧 配置路由策略");
        System.Console.WriteLine("1. 聊天服务 - 轮询");
        System.Console.WriteLine("2. 聊天服务 - 一致性哈希");
        System.Console.WriteLine("3. 战斗服务 - 亲和性优先");
        System.Console.WriteLine("4. 战斗服务 - 最少连接");
        System.Console.Write("请选择: ");

        var choice = System.Console.ReadLine();

        switch (choice)
        {
            case "1":
                _smartClient!.ConfigureServiceRouting<IChatHub>(config =>
                {
                    config.DefaultStrategy = ServiceRoutingStrategy.RoundRobin;
                });
                System.Console.WriteLine("✅ 聊天服务路由策略设置为轮询");
                break;

            case "2":
                _smartClient!.ConfigureServiceRouting<IChatHub>(config =>
                {
                    config.DefaultStrategy = ServiceRoutingStrategy.ConsistentHashing;
                });
                System.Console.WriteLine("✅ 聊天服务路由策略设置为一致性哈希");
                break;

            case "3":
                _smartClient!.ConfigureServiceRouting<IBattleService>(config =>
                {
                    config.DefaultStrategy = ServiceRoutingStrategy.AffinityFirst;
                });
                System.Console.WriteLine("✅ 战斗服务路由策略设置为亲和性优先");
                break;

            case "4":
                _smartClient!.ConfigureServiceRouting<IBattleService>(config =>
                {
                    config.DefaultStrategy = ServiceRoutingStrategy.LeastConnections;
                });
                System.Console.WriteLine("✅ 战斗服务路由策略设置为最少连接");
                break;
        }

        System.Console.WriteLine("按任意键继续...");
        System.Console.ReadKey();
    }

    private async Task DisconnectAsync()
    {
        _logger.LogInformation("🔌 开始断开连接...");

        try
        {
            if (_chatService != null)
                await _chatService.LeaveAsync();

            _chatSubscription?.Dispose();
            _playerSubscription?.Dispose();
            _battleSubscription?.Dispose();

            await _smartClient!.DisconnectAsync();

            _isConnected = false;
            _logger.LogInformation("✅ 所有连接已断开");
            System.Console.WriteLine("✅ 断开连接成功，按任意键继续...");
            System.Console.ReadKey();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "断开连接失败");
            throw;
        }
    }

    private async Task CleanupAsync()
    {
        _cts?.Cancel();
        _chatSubscription?.Dispose();
        _playerSubscription?.Dispose();
        _battleSubscription?.Dispose();
        _smartClient?.Dispose();
        _cts?.Dispose();
    }
}

// 事件处理器实现
public class SmartChatEventHandler : IChatHubReceiver
{
    private readonly ILogger _logger;

    public SmartChatEventHandler(ILogger logger)
    {
        _logger = logger;
    }

    public void OnJoin(string name)
    {
        _logger.LogInformation("💬 用户加入聊天: {Name}", name);
    }

    public void OnLeave(string name)
    {
        _logger.LogInformation("💬 用户离开聊天: {Name}", name);
    }

    public void OnSendMessage(MessageResponse message)
    {
        _logger.LogInformation("💬 收到消息: {User} - {Message}", message.UserName, message.Message);
    }

    public Task<string> HelloAsync(string name, int age)
    {
        return Task.FromResult($"Hello {name}, you are {age} years old!");
    }
}

public class SmartPlayerEventHandler : IPlayerLoginEvents
{
    private readonly ILogger _logger;

    public SmartPlayerEventHandler(ILogger logger)
    {
        _logger = logger;
    }

    public void OnPlayerJoined(PlayerJoinedEvent eventData)
    {
        _logger.LogInformation("🟢 玩家上线: {PlayerId} at {Position}", eventData.PlayerId, eventData.Position);
    }

    public void OnPlayerLeft(PlayerLeftEvent eventData)
    {
        _logger.LogInformation("🔴 玩家下线: {PlayerId}", eventData.PlayerId);
    }
}

public class SmartBattleEventHandler : IBattleReceiver
{
    private readonly ILogger _logger;

    public SmartBattleEventHandler(ILogger logger)
    {
        _logger = logger;
    }

    public void OnBattleStarted(string roomId)
    {
        _logger.LogInformation("⚔️ 战斗开始: {RoomId}", roomId);
    }

    public void OnBattleEnded(string roomId, string winner)
    {
        _logger.LogInformation("🏆 战斗结束: {RoomId}, 胜者: {Winner}", roomId, winner);
    }

    public void OnPlayerJoinedBattle(string playerId, string roomId)
    {
        _logger.LogInformation("⚔️ 玩家加入战斗: {PlayerId} -> {RoomId}", playerId, roomId);
    }
}

// 补充的接口定义
public interface IBattleService : IPulseService
{
    Task<JoinBattleResult> JoinBattleAsync(JoinBattleRequest request);
    Task<bool> LeaveBattleAsync(string roomId);
    Task<int> GetOnlinePlayerCountAsync();
    Task<string> GetServerInfoAsync();
    Task<bool> SendGlobalAnnouncementAsync(string message);
}

public interface IBattleReceiver : IPulseEventHandler
{
    void OnBattleStarted(string roomId);
    void OnBattleEnded(string roomId, string winner);
    void OnPlayerJoinedBattle(string playerId, string roomId);
}

public class JoinBattleRequest
{
    public string PlayerId { get; set; } = "";
    public string RoomId { get; set; } = "";
}

public class JoinBattleResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string InstanceId { get; set; } = "";
}
