using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Routing;
using PulseRPC.SmartConnection;
using ChatApp;

/// <summary>
/// 智能聊天组件 - 展示PulseRPC智能连接功能
/// </summary>
public class SmartChatComponent : MonoBehaviour
{
    [Header("智能连接配置")]
    [SerializeField] private string _primaryChatServer = "localhost";
    [SerializeField] private int _primaryChatPort = 8000;
    [SerializeField] private string _secondaryChatServer = "localhost";
    [SerializeField] private int _secondaryChatPort = 8001;
    [SerializeField] private string _battleServer = "localhost";
    [SerializeField] private int _battlePort = 8002;

    [Header("UI 组件")]
    [SerializeField] private InputField _usernameInput;
    [SerializeField] private InputField _messageInput;
    [SerializeField] private Text _chatDisplay;
    [SerializeField] private Text _statusText;
    [SerializeField] private Button _connectButton;
    [SerializeField] private Button _sendMessageButton;
    [SerializeField] private Button _joinBattleButton;
    [SerializeField] private Button _broadcastButton;
    [SerializeField] private Dropdown _routingStrategyDropdown;

    private ISmartPulseRpcClient _smartClient;
    private IChatHub _chatService;
    private IBattleService _battleService;
    private IMultiInstanceServiceManager<IChatHub> _chatManager;
    private ISubscriptionToken _chatSubscription;
    private ISubscriptionToken _battleSubscription;
    private CancellationTokenSource _cts;
    private string _currentUser;
    private bool _isConnected;

    private void Start()
    {
        InitializeUI();
        InitializeSmartClient();
    }

    private void InitializeUI()
    {
        _connectButton.onClick.AddListener(OnConnectClick);
        _sendMessageButton.onClick.AddListener(OnSendMessageClick);
        _joinBattleButton.onClick.AddListener(OnJoinBattleClick);
        _broadcastButton.onClick.AddListener(OnBroadcastClick);

        // 设置路由策略下拉菜单
        _routingStrategyDropdown.ClearOptions();
        _routingStrategyDropdown.AddOptions(new[]
        {
            "轮询 (Round Robin)",
            "一致性哈希 (Consistent Hash)",
            "最少连接 (Least Connections)",
            "亲和性优先 (Affinity First)"
        });

        _sendMessageButton.interactable = false;
        _joinBattleButton.interactable = false;
        _broadcastButton.interactable = false;

        UpdateStatus("智能客户端就绪，点击连接开始");
    }

    private void InitializeSmartClient()
    {
        try
        {
            _cts = new CancellationTokenSource();

            // 创建智能客户端
            _smartClient = PulseRpcClientFactory.CreateSmartBuilder()
                .WithServiceDiscovery(config =>
                {
                    config.Type = ServiceDiscoveryType.Static;
                    
                    // 配置聊天服务的多个实例
                    config.StaticEndpoints["ChatService-1"] = new ServiceEndpoint
                    {
                        Host = _primaryChatServer,
                        Port = _primaryChatPort,
                        Transport = TransportType.Tcp
                    };
                    
                    config.StaticEndpoints["ChatService-2"] = new ServiceEndpoint
                    {
                        Host = _secondaryChatServer,
                        Port = _secondaryChatPort,
                        Transport = TransportType.Tcp
                    };
                    
                    // 配置战斗服务
                    config.StaticEndpoints["BattleService"] = new ServiceEndpoint
                    {
                        Host = _battleServer,
                        Port = _battlePort,
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
                    config.DefaultStrategy = ServiceRoutingStrategy.AffinityFirst;
                    config.Failover.EnableFailover = true;
                })
                .Build();

            UpdateStatus("智能客户端初始化完成");
        }
        catch (Exception ex)
        {
            UpdateStatus($"智能客户端初始化失败: {ex.Message}");
        }
    }

    private async void OnConnectClick()
    {
        if (_isConnected)
        {
            await DisconnectAsync();
            return;
        }

        try
        {
            _connectButton.interactable = false;
            UpdateStatus("正在连接智能服务...");

            // 更新路由策略
            var selectedStrategy = _routingStrategyDropdown.value switch
            {
                0 => ServiceRoutingStrategy.RoundRobin,
                1 => ServiceRoutingStrategy.ConsistentHashing,
                2 => ServiceRoutingStrategy.LeastConnections,
                3 => ServiceRoutingStrategy.AffinityFirst,
                _ => ServiceRoutingStrategy.RoundRobin
            };

            _smartClient.ConfigureServiceRouting<IChatHub>(config =>
            {
                config.DefaultStrategy = selectedStrategy;
            });

            // 获取聊天服务 - 智能路由
            var routingContext = RoutingContext.ByUserId(_usernameInput.text);
            _chatService = await _smartClient.GetServiceAsync<IChatHub>("ChatService", routingContext);

            // 获取战斗服务 - 特定实例
            _battleService = await _smartClient.GetServiceAsync<IBattleService>("BattleService");

            // 获取多实例管理器
            _chatManager = await _smartClient.GetMultiInstanceServiceAsync<IChatHub>("ChatService");

            // 注册事件监听器
            await RegisterEventListenersAsync();

            // 加入聊天
            _currentUser = _usernameInput.text;
            var joinResult = await _chatService.JoinAsync(new JoinRequest { Username = _currentUser });

            if (joinResult)
            {
                _isConnected = true;
                _connectButton.GetComponentInChildren<Text>().text = "断开连接";
                _sendMessageButton.interactable = true;
                _joinBattleButton.interactable = true;
                _broadcastButton.interactable = true;
                UpdateStatus($"✅ 已连接 - 路由策略: {selectedStrategy}");
                
                // 显示连接统计
                await ShowConnectionStatisticsAsync();
            }
            else
            {
                UpdateStatus("❌ 加入聊天失败");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"❌ 连接失败: {ex.Message}");
        }
        finally
        {
            _connectButton.interactable = true;
        }
    }

    private async Task RegisterEventListenersAsync()
    {
        try
        {
            // 注册聊天事件监听器 - 使用用户亲和性路由
            var chatRoutingContext = RoutingContext.ByUserId(_currentUser);
            _chatSubscription = await _smartClient.RegisterEventListenerAsync(
                new ChatEventHandler(this), "ChatService", chatRoutingContext);

            // 注册战斗事件监听器 - 使用特定实例
            _battleSubscription = await _smartClient.RegisterEventListenerAsync(
                new BattleEventHandler(this), "BattleService");

            UpdateStatus("事件监听器注册完成");
        }
        catch (Exception ex)
        {
            UpdateStatus($"事件监听器注册失败: {ex.Message}");
        }
    }

    private async void OnSendMessageClick()
    {
        if (string.IsNullOrEmpty(_messageInput.text)) return;

        try
        {
            var success = await _chatService.SendMessageAsync(_messageInput.text);
            if (success)
            {
                _messageInput.text = "";
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"发送消息失败: {ex.Message}");
        }
    }

    private async void OnJoinBattleClick()
    {
        try
        {
            // 使用房间ID路由到特定战斗服务实例
            var battleRoomId = "room_" + UnityEngine.Random.Range(1000, 9999);
            var battleRoutingContext = RoutingContext.ByBattleRoom(battleRoomId);
            
            var battleService = await _smartClient.GetServiceAsync<IBattleService>(
                "BattleService", battleRoutingContext);

            var joinResult = await battleService.JoinBattleAsync(new JoinBattleRequest
            {
                PlayerId = _currentUser,
                RoomId = battleRoomId
            });

            if (joinResult.Success)
            {
                AddChatMessage($"✨ 成功加入战斗房间: {battleRoomId}");
                AddChatMessage($"   实例: {joinResult.InstanceId}");
            }
            else
            {
                AddChatMessage($"❌ 加入战斗失败: {joinResult.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            AddChatMessage($"❌ 战斗服务错误: {ex.Message}");
        }
    }

    private async void OnBroadcastClick()
    {
        try
        {
            UpdateStatus("正在执行广播消息...");

            // 使用多实例管理器广播消息到所有聊天服务实例
            var broadcastResult = await _chatManager.BroadcastAsync(async chat =>
                await chat.SendGlobalAnnouncementAsync($"📢 来自 {_currentUser} 的全局广播！"));

            AddChatMessage($"📡 广播完成:");
            AddChatMessage($"   成功: {broadcastResult.SuccessCount}/{broadcastResult.TotalCount}");
            AddChatMessage($"   成功率: {broadcastResult.SuccessRate:P1}");

            // 显示失败的实例
            foreach (var failure in broadcastResult.GetFailureExceptions())
            {
                AddChatMessage($"   失败: {failure.Message}");
            }
        }
        catch (Exception ex)
        {
            AddChatMessage($"❌ 广播失败: {ex.Message}");
        }
    }

    private async Task ShowConnectionStatisticsAsync()
    {
        try
        {
            var stats = await _smartClient.GetConnectionStatisticsAsync();
            
            AddChatMessage($"📊 连接统计:");
            AddChatMessage($"   总连接数: {stats.TotalConnections}");
            AddChatMessage($"   活跃连接: {stats.ActiveConnections}");
            AddChatMessage($"   空闲连接: {stats.IdleConnections}");
            
            foreach (var serviceStat in stats.ServiceStatistics)
            {
                AddChatMessage($"   {serviceStat.Key}: {serviceStat.Value.ConnectionCount} 连接");
            }
        }
        catch (Exception ex)
        {
            AddChatMessage($"获取统计信息失败: {ex.Message}");
        }
    }

    private async Task DisconnectAsync()
    {
        try
        {
            UpdateStatus("正在断开连接...");

            if (_chatService != null)
            {
                await _chatService.LeaveAsync();
            }

            _chatSubscription?.Dispose();
            _battleSubscription?.Dispose();
            
            // 清理空闲连接
            var cleanedConnections = await _smartClient.CleanupIdleConnectionsAsync();
            AddChatMessage($"🧹 已清理 {cleanedConnections} 个空闲连接");

            await _smartClient.DisconnectAsync();

            _isConnected = false;
            _connectButton.GetComponentInChildren<Text>().text = "智能连接";
            _sendMessageButton.interactable = false;
            _joinBattleButton.interactable = false;
            _broadcastButton.interactable = false;
            
            UpdateStatus("已断开连接");
        }
        catch (Exception ex)
        {
            UpdateStatus($"断开连接失败: {ex.Message}");
        }
    }

    public void OnChatMessage(string user, string message)
    {
        AddChatMessage($"{user}: {message}");
    }

    public void OnUserJoined(string user)
    {
        AddChatMessage($"🟢 {user} 加入了聊天");
    }

    public void OnUserLeft(string user)
    {
        AddChatMessage($"🔴 {user} 离开了聊天");
    }

    public void OnBattleEvent(string eventType, string message)
    {
        AddChatMessage($"⚔️ 战斗事件 [{eventType}]: {message}");
    }

    private void AddChatMessage(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        _chatDisplay.text += $"[{timestamp}] {message}\n";
        
        // 保持聊天显示在合理长度
        var lines = _chatDisplay.text.Split('\n');
        if (lines.Length > 20)
        {
            _chatDisplay.text = string.Join("\n", lines, lines.Length - 20, 20);
        }
    }

    private void UpdateStatus(string status)
    {
        _statusText.text = status;
        Debug.Log($"[SmartChatComponent] {status}");
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        _chatSubscription?.Dispose();
        _battleSubscription?.Dispose();
        _smartClient?.Dispose();
    }
}

/// <summary>
/// 聊天事件处理器
/// </summary>
public class ChatEventHandler : IChatHubReceiver
{
    private readonly SmartChatComponent _component;

    public ChatEventHandler(SmartChatComponent component)
    {
        _component = component;
    }

    public void OnJoin(string name)
    {
        _component.OnUserJoined(name);
    }

    public void OnLeave(string name)
    {
        _component.OnUserLeft(name);
    }

    public void OnSendMessage(MessageResponse message)
    {
        _component.OnChatMessage(message.Username, message.Message);
    }

    public Task<string> HelloAsync(string name, int age)
    {
        return Task.FromResult($"Hello {name}, you are {age} years old!");
    }
}

/// <summary>
/// 战斗事件处理器
/// </summary>
public class BattleEventHandler : IBattleReceiver
{
    private readonly SmartChatComponent _component;

    public BattleEventHandler(SmartChatComponent component)
    {
        _component = component;
    }

    public void OnBattleStarted(string roomId)
    {
        _component.OnBattleEvent("Battle Started", $"Room {roomId}");
    }

    public void OnBattleEnded(string roomId, string winner)
    {
        _component.OnBattleEvent("Battle Ended", $"Room {roomId}, Winner: {winner}");
    }

    public void OnPlayerJoinedBattle(string playerId, string roomId)
    {
        _component.OnBattleEvent("Player Joined", $"{playerId} joined {roomId}");
    }
}

// 示例战斗服务接口
public interface IBattleService : IPulseService
{
    Task<JoinBattleResult> JoinBattleAsync(JoinBattleRequest request);
    Task<bool> LeaveBattleAsync(string roomId);
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