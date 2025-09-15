using PulseRPC.Client.Redesign;
using PulseRPC.Transport;

namespace PulseRPC.Client.Redesign.Examples;

/// <summary>
/// 新设计的使用示例
/// </summary>
public class UsageExamples
{
    /// <summary>
    /// 示例1：基本游戏客户端设置
    /// </summary>
    public async Task BasicGameClientExample()
    {
        // 构建客户端
        var client = new PulseRPCClientBuilder()
            .AddGameServerSet("production") // 添加标准游戏服务器集
            .WithBattleOptimizations()      // 配置战斗优化
            .WithConnectionPooling()        // 配置连接池
            .Build();

        // 初始化（连接核心服务器）
        await client.InitializeAsync();

        // 获取核心服务
        var loginConnection = client.Connections.GetConnection("core-login-service");
        var loginService = await loginConnection!.GetServiceAsync<ILoginService>();
        
        // 登录
        var loginResult = await loginService.LoginAsync("username", "password");
        
        // 连接到游戏世界
        var worldConnection = client.Connections.GetConnection("core-game-world-service");
        var worldService = await worldConnection!.GetServiceAsync<IGameWorldService>();
        
        // 停止客户端
        await client.StopAsync();
    }

    /// <summary>
    /// 示例2：动态战斗服连接
    /// </summary>
    public async Task DynamicBattleServerExample(IPulseRPCClient client)
    {
        // 从匹配服务获取战斗服信息
        var matchConnection = client.Connections.GetConnection("core-match-service");
        var matchService = await matchConnection!.GetServiceAsync<IMatchService>();
        var battleInfo = await matchService.FindBattleAsync();

        // 连接到战斗服
        var battleConnection = await client.ConnectToBattleServerAsync(
            battleInfo.BattleId,
            battleInfo.ServerHost,
            battleInfo.ServerPort);

        try
        {
            // 获取战斗服务
            var battleService = await battleConnection.GetServiceAsync<IBattleService>();
            
            // 注册战斗事件
            var battleHandler = new BattleEventHandler();
            await battleConnection.RegisterEventListenerAsync<IBattleEventHandler>(battleHandler);
            
            // 加入战斗
            await battleService.JoinBattleAsync(battleInfo.BattleId);
            
            // 战斗逻辑...
            
        }
        finally
        {
            // 离开战斗（自动断开连接）
            await client.LeaveBattleAsync(battleInfo.BattleId);
        }
    }

    /// <summary>
    /// 示例3：地图切换
    /// </summary>
    public async Task MapSwitchingExample(IPulseRPCClient client)
    {
        // 连接到初始地图
        var mapConnection = await client.ConnectToMapServerAsync("map-001");
        var mapService = await mapConnection.GetServiceAsync<IMapService>();
        
        // 在地图上游戏...
        
        // 切换到新地图（自动断开旧连接）
        var newMapConnection = await client.SwitchMapAsync("map-001", "map-002");
        var newMapService = await newMapConnection.GetServiceAsync<IMapService>();
    }

    /// <summary>
    /// 示例4：临时连接模式
    /// </summary>
    public async Task TemporaryConnectionExample(IPulseRPCClient client)
    {
        // 使用临时连接执行快速操作
        await client.WithTemporaryConnectionAsync(
            new ConnectionConfig
            {
                Name = "temp-stats",
                ServiceName = "statistics-service",
                Transport = TransportType.Tcp,
                Lifetime = ConnectionLifetime.Transient
            },
            async connection =>
            {
                var statsService = await connection.GetServiceAsync<IStatisticsService>();
                await statsService.RecordEventAsync("player_action", "jump");
            });
        // 连接自动清理
    }

    /// <summary>
    /// 示例5：连接状态监控
    /// </summary>
    public async Task ConnectionMonitoringExample(IPulseRPCClient client)
    {
        // 监控连接状态变化
        client.Connections.ConnectionStateChanged += (sender, args) =>
        {
            Console.WriteLine($"Connection {args.ConnectionName}: {args.OldState} -> {args.NewState}");
            
            if (args.NewState == ConnectionState.Failed)
            {
                Console.WriteLine($"Connection failed: {args.Exception?.Message}");
            }
        };

        // 获取连接统计
        var allConnections = client.Connections.GetAllConnections();
        var battleConnections = client.Connections.GetConnectionsByTag("type", "battle");
        
        Console.WriteLine($"Total connections: {allConnections.Count}");
        Console.WriteLine($"Battle connections: {battleConnections.Count}");

        // 清理空闲连接
        var cleanedCount = await client.Connections.CleanupIdleConnectionsAsync(TimeSpan.FromMinutes(5));
        Console.WriteLine($"Cleaned up {cleanedCount} idle connections");
    }

    /// <summary>
    /// 示例6：Unity集成
    /// </summary>
    public class UnityGameClient : UnityEngine.MonoBehaviour
    {
        private IPulseRPCClient? _client;
        private IConnectionContext? _currentBattleConnection;

        async void Start()
        {
            // 初始化客户端
            _client = new PulseRPCClientBuilder()
                .AddDevelopmentServers("game.server.com", 8000)
                .WithBattleOptimizations()
                .Build();

            await _client.InitializeAsync();

            // 监控连接状态
            _client.Connections.ConnectionStateChanged += OnConnectionStateChanged;
        }

        public async Task JoinBattleAsync(string battleId, string host, int port)
        {
            if (_client == null) return;

            // 连接战斗服
            _currentBattleConnection = await _client.ConnectToBattleServerAsync(battleId, host, port);
            
            // 监控战斗连接状态
            _currentBattleConnection.StateChanged += OnBattleConnectionStateChanged;
            
            var battleService = await _currentBattleConnection.GetServiceAsync<IBattleService>();
            await battleService.JoinBattleAsync(battleId);
        }

        public async Task LeaveBattleAsync()
        {
            if (_client == null || _currentBattleConnection == null) return;

            var battleId = _currentBattleConnection.Config.Tags["battleId"];
            await _client.LeaveBattleAsync(battleId);
            _currentBattleConnection = null;
        }

        private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
        {
            UnityEngine.Debug.Log($"Connection {e.ConnectionName}: {e.OldState} -> {e.NewState}");
        }

        private void OnBattleConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
        {
            if (e.NewState == ConnectionState.Failed)
            {
                UnityEngine.Debug.LogError($"Battle connection failed: {e.Exception?.Message}");
                // 处理战斗连接失败
            }
        }

        void OnDestroy()
        {
            _client?.Dispose();
        }
    }
}

// 示例服务接口
public interface ILoginService : IPulseService
{
    Task<LoginResult> LoginAsync(string username, string password);
}

public interface IBattleService : IPulseService
{
    Task JoinBattleAsync(string battleId);
    Task LeaveBattleAsync();
}

public interface IMapService : IPulseService
{
    Task EnterMapAsync(string mapId);
    Task LeaveMapAsync();
}

public interface IStatisticsService : IPulseService
{
    Task RecordEventAsync(string eventType, string data);
}

public interface IMatchService : IPulseService
{
    Task<BattleInfo> FindBattleAsync();
}

// 示例数据类型
public record LoginResult(bool Success, string Token);
public record BattleInfo(string BattleId, string ServerHost, int ServerPort);

// 示例事件处理器
public interface IBattleEventHandler : IPulseEventHandler
{
    Task OnBattleStarted();
    Task OnBattleEnded();
    Task OnPlayerJoined(string playerId);
}

public class BattleEventHandler : IBattleEventHandler
{
    public Task OnBattleStarted() => Task.CompletedTask;
    public Task OnBattleEnded() => Task.CompletedTask;
    public Task OnPlayerJoined(string playerId) => Task.CompletedTask;
}