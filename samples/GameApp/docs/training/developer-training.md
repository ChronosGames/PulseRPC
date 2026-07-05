# GameApp 开发者培训手册

> 文档状态：培训草案。当前 GameApp 源码以 `samples/GameApp/src` 的 `net9.0` 项目和现有 `Program.cs` 为准；文中部分代码块属于教学示例，未必对应当前可直接编译的 API。

## 培训概述

本培训手册旨在帮助开发者快速掌握 GameApp 的开发技能，包括系统架构理解、开发环境搭建、核心组件开发和最佳实践。

## 培训目标

完成本培训后，开发者将能够：

- ✅ 理解 GameApp 的整体架构和技术栈
- ✅ 搭建完整的开发环境
- ✅ 开发 AuthServer HTTP API
- ✅ 开发 GameServer/BattleServer PulseRPC 服务
- ✅ 开发 Unity 客户端
- ✅ 进行系统测试和性能优化
- ✅ 部署和运维 GameApp 系统

## 培训课程安排

### 第一天：基础架构 (8小时)

#### 上午：系统架构概览 (4小时)

**1. GameApp 简介 (1小时)**
- 项目背景和目标
- 技术选型和架构决策
- 微服务架构优势

**2. 核心组件介绍 (2小时)**
- AuthServer: 认证和授权服务
- GameServer: 游戏逻辑服务
- BattleServer: 战斗服务
- 数据存储: MongoDB + Redis
- 服务发现: Consul

**3. 通信协议详解 (1小时)**
- HTTP REST API (AuthServer)
- PulseRPC + MemoryPack (GameServer/BattleServer)
- TCP vs KCP 通道选择

#### 下午：开发环境搭建 (4小时)

**1. 环境准备 (1小时)**
```bash
# 安装必要工具
# .NET 9 SDK
winget install Microsoft.DotNet.SDK.9

# Docker Desktop
winget install Docker.DockerDesktop

# Visual Studio 2022 或 JetBrains Rider
winget install Microsoft.VisualStudio.2022.Community

# Unity Hub
winget install UnityTechnologies.UnityHub
```

**2. 项目结构解读 (1小时)**
```
GameApp/
├── src/                          # 源代码目录
│   ├── GameApp.Shared/          # 共享模型和接口
│   ├── GameApp.Infrastructure/  # 基础设施服务
│   ├── GameApp.AuthServer/      # 认证服务
│   ├── GameApp.GameServer/      # 游戏服务
│   └── GameApp.BattleServer/    # 战斗服务
├── tests/                       # 测试项目
├── client/                      # Unity 客户端
├── docs/                        # 文档
├── docker/                      # Docker 配置
└── scripts/                     # 部署脚本
```

**3. 基础设施服务启动 (1小时)**
```bash
# 启动开发环境
cd docker
docker-compose up -d mongodb-dev redis-dev consul-dev

# 验证服务状态
docker-compose ps
curl http://localhost:8500/ui/  # Consul UI
```

**4. 第一个 API 调试 (1小时)**
```bash
# 启动 AuthServer
cd src/GameApp.AuthServer
dotnet run

# 测试健康检查
curl http://localhost:5000/api/auth/health

# 测试用户注册
curl -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"testuser","email":"test@example.com","password":"password123","confirmPassword":"password123"}'
```

---

### 第二天：后端服务开发 (8小时)

#### 上午：AuthServer 开发实战 (4小时)

**1. 控制器开发 (1.5小时)**

创建新的API控制器：
```csharp
[ApiController]
[Route("api/[controller]")]
public class PlayerController : ControllerBase
{
    private readonly IPlayerService _playerService;
    private readonly IStructuredLogger _logger;

    public PlayerController(IPlayerService playerService, IStructuredLogger logger)
    {
        _playerService = playerService;
        _logger = logger;
    }

    [HttpGet("{playerId}")]
    [Authorize]
    public async Task<ActionResult<PlayerResponse>> GetPlayer(string playerId)
    {
        try
        {
            _logger.LogInfo("获取玩家信息", new { playerId });

            var player = await _playerService.GetPlayerAsync(playerId);
            if (player == null)
            {
                return NotFound(new { message = "玩家不存在" });
            }

            return Ok(new PlayerResponse
            {
                Success = true,
                Data = player
            });
        }
        catch (Exception ex)
        {
            _logger.LogError("获取玩家信息失败", ex, new { playerId });
            return StatusCode(500, new { message = "服务器内部错误" });
        }
    }
}
```

**2. 服务层开发 (1.5小时)**

实现业务逻辑服务：
```csharp
public class PlayerService : IPlayerService
{
    private readonly IMongoDatabase _database;
    private readonly IDatabase _redis;
    private readonly IStructuredLogger _logger;

    public async Task<Player> GetPlayerAsync(string playerId)
    {
        // 先从缓存获取
        var cachedPlayer = await GetPlayerFromCacheAsync(playerId);
        if (cachedPlayer != null)
        {
            _logger.LogInfo("从缓存获取玩家数据", new { playerId });
            return cachedPlayer;
        }

        // 从数据库获取
        var collection = _database.GetCollection<Player>("players");
        var player = await collection.Find(p => p.Id == playerId).FirstOrDefaultAsync();

        if (player != null)
        {
            // 写入缓存
            await SetPlayerCacheAsync(playerId, player, TimeSpan.FromMinutes(30));
            _logger.LogInfo("从数据库获取玩家数据", new { playerId });
        }

        return player;
    }

    private async Task<Player> GetPlayerFromCacheAsync(string playerId)
    {
        var key = $"player:{playerId}";
        var value = await _redis.StringGetAsync(key);

        if (value.HasValue)
        {
            return JsonSerializer.Deserialize<Player>(value);
        }

        return null;
    }
}
```

**3. 中间件开发 (1小时)**

创建自定义中间件：
```csharp
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IStructuredLogger _logger;

    public RequestLoggingMiddleware(RequestDelegate next, IStructuredLogger logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        // 记录请求开始
        _logger.LogInfo("HTTP请求开始", new
        {
            method = context.Request.Method,
            path = context.Request.Path,
            userAgent = context.Request.Headers.UserAgent.ToString()
        });

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            // 记录请求完成
            _logger.LogPerformance(
                $"{context.Request.Method} {context.Request.Path}",
                stopwatch.Elapsed,
                context.Response.StatusCode < 400
            );
        }
    }
}
```

#### 下午：PulseRPC 服务开发 (4小时)

**1. 服务接口定义 (1小时)**

定义 PulseRPC 服务接口：
```csharp
// 服务接口
[Channel("TcpChannel")]
public interface ICustomService : IPulseService
{
    Task<GetDataResponse> GetDataAsync(GetDataRequest request);
    Task<UpdateDataResponse> UpdateDataAsync(UpdateDataRequest request);
}

// 事件接口
public interface ICustomEvents : IPulseEventHandler
{
    Task OnDataUpdatedAsync(DataUpdatedEvent eventData);
    Task OnSystemNotificationAsync(SystemNotificationEvent eventData);
}

// DTO 定义
[MemoryPackable]
public partial class GetDataRequest
{
    public string DataId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class GetDataResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public CustomData? Data { get; set; }
}

[MemoryPackable]
public partial class DataUpdatedEvent
{
    public string DataId { get; set; } = string.Empty;
    public string UpdateType { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
```

**2. 服务实现 (1.5小时)**

实现 PulseRPC 服务：
```csharp
public class CustomServiceImpl : ICustomService
{
    private readonly IMongoDatabase _database;
    private readonly IEventPublisher _eventPublisher;
    private readonly IStructuredLogger _logger;

    public CustomServiceImpl(
        IMongoDatabase database,
        IEventPublisher eventPublisher,
        IStructuredLogger logger)
    {
        _database = database;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<GetDataResponse> GetDataAsync(GetDataRequest request)
    {
        try
        {
            _logger.LogInfo("获取数据请求", new { request.DataId, request.UserId });

            // 验证请求参数
            if (string.IsNullOrEmpty(request.DataId))
            {
                return new GetDataResponse
                {
                    Success = false,
                    Message = "数据ID不能为空"
                };
            }

            // 从数据库获取数据
            var collection = _database.GetCollection<CustomData>("custom_data");
            var data = await collection.Find(d => d.Id == request.DataId).FirstOrDefaultAsync();

            if (data == null)
            {
                return new GetDataResponse
                {
                    Success = false,
                    Message = "数据不存在"
                };
            }

            // 权限检查
            if (data.OwnerId != request.UserId)
            {
                _logger.LogSecurity(SecurityEventType.AccessDenied,
                    "用户尝试访问无权限的数据",
                    new { request.DataId, request.UserId, data.OwnerId });

                return new GetDataResponse
                {
                    Success = false,
                    Message = "权限不足"
                };
            }

            return new GetDataResponse
            {
                Success = true,
                Message = "获取成功",
                Data = data
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("获取数据失败", ex, new { request.DataId, request.UserId });

            return new GetDataResponse
            {
                Success = false,
                Message = "服务器内部错误"
            };
        }
    }

    public async Task<UpdateDataResponse> UpdateDataAsync(UpdateDataRequest request)
    {
        try
        {
            // 实现更新逻辑...
            var collection = _database.GetCollection<CustomData>("custom_data");
            var filter = Builders<CustomData>.Filter.Eq(d => d.Id, request.DataId);
            var update = Builders<CustomData>.Update
                .Set(d => d.Value, request.NewValue)
                .Set(d => d.UpdatedAt, DateTime.UtcNow);

            var result = await collection.UpdateOneAsync(filter, update);

            if (result.ModifiedCount > 0)
            {
                // 发布更新事件
                await _eventPublisher.PublishEventAsync(new DataUpdatedEvent
                {
                    DataId = request.DataId,
                    UpdateType = "value_changed",
                    UpdatedAt = DateTime.UtcNow
                });

                return new UpdateDataResponse
                {
                    Success = true,
                    Message = "更新成功"
                };
            }

            return new UpdateDataResponse
            {
                Success = false,
                Message = "没有数据被更新"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("更新数据失败", ex, new { request.DataId });

            return new UpdateDataResponse
            {
                Success = false,
                Message = "服务器内部错误"
            };
        }
    }
}
```

**3. 事件发布器 (1小时)**

实现事件发布功能：
```csharp
public class CustomEventPublisher
{
    private readonly IEventPublisher _eventPublisher;
    private readonly IStructuredLogger _logger;

    public CustomEventPublisher(IEventPublisher eventPublisher, IStructuredLogger logger)
    {
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task NotifyDataUpdate(string dataId, string updateType)
    {
        try
        {
            var eventData = new DataUpdatedEvent
            {
                DataId = dataId,
                UpdateType = updateType,
                UpdatedAt = DateTime.UtcNow
            };

            await _eventPublisher.PublishEventAsync(eventData);

            _logger.LogInfo("发布数据更新事件", new { dataId, updateType });
        }
        catch (Exception ex)
        {
            _logger.LogError("发布事件失败", ex, new { dataId, updateType });
        }
    }

    public async Task NotifySystemMessage(string message, string messageType)
    {
        try
        {
            var eventData = new SystemNotificationEvent
            {
                Message = message,
                MessageType = messageType,
                Timestamp = DateTime.UtcNow
            };

            await _eventPublisher.PublishEventAsync(eventData);

            _logger.LogInfo("发布系统通知", new { message, messageType });
        }
        catch (Exception ex)
        {
            _logger.LogError("发布系统通知失败", ex, new { message, messageType });
        }
    }
}
```

**4. 服务注册和配置 (0.5小时)**

在 Program.cs 中注册服务：
```csharp
// 注册 PulseRPC 服务器
services.AddPulseRpcServer(builder =>
{
    builder.AddTcp("TcpChannel", gameServerOptions.TcpPort);
    builder.AddKcp("KcpChannel", gameServerOptions.KcpPort);
});

// 注册自定义服务
services.AddSingleton<ICustomService, CustomServiceImpl>();
services.AddSingleton<CustomEventPublisher>();

// 注册基础设施服务
services.AddGameAppInfrastructure(configuration);
```

---

### 第三天：Unity 客户端开发 (8小时)

#### 上午：Unity 项目搭建 (4小时)

**1. Unity 项目创建 (1小时)**

创建 Unity 项目并配置：
```csharp
// 1. 创建新的 Unity 2022.3 LTS 项目
// 2. 导入必要的包
// - Unity Addressables
// - Unity NetCode (可选)
// - TextMeshPro

// 3. 项目结构设置
Assets/
├── Scripts/
│   ├── Network/        # 网络通信
│   ├── Managers/       # 管理器
│   ├── UI/            # UI控制器
│   ├── Models/        # 数据模型
│   └── Utils/         # 工具类
├── Prefabs/           # 预制体
├── Materials/         # 材质
├── Textures/          # 贴图
└── Scenes/            # 场景
```

**2. 网络客户端框架 (2小时)**

创建网络客户端基础框架：
```csharp
// AuthClient.cs - HTTP API 客户端
public class AuthClient : MonoBehaviour
{
    private const string BASE_URL = "http://localhost:5000/api/auth";

    [SerializeField] private GameConfig gameConfig;

    public async Task<ApiResponse<LoginResult>> LoginAsync(string username, string password)
    {
        var request = new LoginRequest
        {
            username = username,
            password = password,
            rememberMe = true
        };

        try
        {
            string json = JsonUtility.ToJson(request);
            byte[] data = System.Text.Encoding.UTF8.GetBytes(json);

            using var www = UnityWebRequest.PostWwwForm($"{BASE_URL}/login", "");
            www.uploadHandler = new UploadHandlerRaw(data);
            www.SetRequestHeader("Content-Type", "application/json");
            www.timeout = gameConfig.connectionTimeout;

            await www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<ApiResponse<LoginResult>>(www.downloadHandler.text);

                if (response.success)
                {
                    // 保存令牌
                    PlayerPrefs.SetString("AccessToken", response.data.accessToken);
                    PlayerPrefs.SetString("RefreshToken", response.data.refreshToken);
                    PlayerPrefs.Save();
                }

                return response;
            }
            else
            {
                Debug.LogError($"登录请求失败: {www.error}");
                return new ApiResponse<LoginResult>
                {
                    success = false,
                    message = www.error
                };
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"登录异常: {ex.Message}");
            return new ApiResponse<LoginResult>
            {
                success = false,
                message = ex.Message
            };
        }
    }
}

// GameClient.cs - PulseRPC 客户端
public class GameClient : MonoBehaviour
{
    private IPulseClient pulseClient;
    private IPlayerService playerService;
    private IWorldService worldService;

    [SerializeField] private GameConfig gameConfig;

    public async Task<bool> ConnectAsync()
    {
        try
        {
            pulseClient = new PulseClient();

            // 连接到 GameServer
            await pulseClient.ConnectAsync(
                gameConfig.gameServerHost,
                gameConfig.gameServerTcpPort
            );

            // 获取服务代理
            playerService = pulseClient.GetService<IPlayerService>();
            worldService = pulseClient.GetService<IWorldService>();

            // 注册事件处理器
            pulseClient.RegisterEventHandler<IPlayerEvents>(new PlayerEventHandler());
            pulseClient.RegisterEventHandler<IWorldEvents>(new WorldEventHandler());

            Debug.Log("成功连接到游戏服务器");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"连接游戏服务器失败: {ex.Message}");
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (pulseClient != null)
        {
            await pulseClient.DisconnectAsync();
            pulseClient = null;
        }
    }
}
```

**3. 事件处理系统 (1小时)**

实现客户端事件处理：
```csharp
// PlayerEventHandler.cs
public class PlayerEventHandler : MonoBehaviour, IPlayerEvents
{
    public async Task OnPlayerLevelUpAsync(PlayerLevelUpEvent eventData)
    {
        Debug.Log($"玩家升级到 {eventData.NewLevel} 级！");

        // 更新UI
        var uiManager = FindObjectOfType<UIManager>();
        uiManager?.UpdatePlayerLevel(eventData.NewLevel);

        // 播放特效
        var effectManager = FindObjectOfType<EffectManager>();
        effectManager?.PlayLevelUpEffect();

        // 显示升级提示
        var notificationManager = FindObjectOfType<NotificationManager>();
        notificationManager?.ShowLevelUpNotification(eventData.NewLevel);
    }

    public async Task OnPlayerInventoryUpdatedAsync(PlayerInventoryUpdatedEvent eventData)
    {
        Debug.Log("玩家背包已更新");

        // 更新背包UI
        var inventoryUI = FindObjectOfType<InventoryUI>();
        inventoryUI?.UpdateInventory(eventData.UpdatedItems);
    }
}

// WorldEventHandler.cs
public class WorldEventHandler : MonoBehaviour, IWorldEvents
{
    public async Task OnPlayerJoinedAsync(PlayerJoinedEvent eventData)
    {
        Debug.Log($"玩家 {eventData.PlayerName} 加入了游戏");

        // 在世界中生成玩家
        var worldManager = FindObjectOfType<WorldManager>();
        worldManager?.SpawnPlayer(eventData.PlayerId, eventData.Position);
    }

    public async Task OnWorldChatAsync(WorldChatEvent eventData)
    {
        Debug.Log($"[世界频道] {eventData.PlayerName}: {eventData.Message}");

        // 显示聊天消息
        var chatUI = FindObjectOfType<ChatUI>();
        chatUI?.AddChatMessage(eventData);
    }
}
```

#### 下午：UI 系统开发 (4小时)

**1. 登录界面 (1小时)**

创建登录界面控制器：
```csharp
public class LoginUI : MonoBehaviour
{
    [Header("UI Components")]
    [SerializeField] private InputField usernameInput;
    [SerializeField] private InputField passwordInput;
    [SerializeField] private Button loginButton;
    [SerializeField] private Button registerButton;
    [SerializeField] private Text statusText;
    [SerializeField] private GameObject loadingPanel;

    [Header("Dependencies")]
    [SerializeField] private AuthClient authClient;

    private void Start()
    {
        loginButton.onClick.AddListener(OnLoginButtonClicked);
        registerButton.onClick.AddListener(OnRegisterButtonClicked);

        // 自动填充保存的用户名
        if (PlayerPrefs.HasKey("SavedUsername"))
        {
            usernameInput.text = PlayerPrefs.GetString("SavedUsername");
        }
    }

    private async void OnLoginButtonClicked()
    {
        if (string.IsNullOrWhiteSpace(usernameInput.text) ||
            string.IsNullOrWhiteSpace(passwordInput.text))
        {
            ShowStatus("请输入用户名和密码", Color.red);
            return;
        }

        SetLoginButtonEnabled(false);
        loadingPanel.SetActive(true);
        ShowStatus("正在登录...", Color.yellow);

        try
        {
            var result = await authClient.LoginAsync(usernameInput.text, passwordInput.text);

            if (result.success)
            {
                ShowStatus("登录成功！", Color.green);

                // 保存用户名
                PlayerPrefs.SetString("SavedUsername", usernameInput.text);
                PlayerPrefs.Save();

                // 跳转到游戏场景
                await LoadGameScene();
            }
            else
            {
                ShowStatus($"登录失败: {result.message}", Color.red);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"登录异常: {ex.Message}", Color.red);
        }
        finally
        {
            SetLoginButtonEnabled(true);
            loadingPanel.SetActive(false);
        }
    }

    private void ShowStatus(string message, Color color)
    {
        statusText.text = message;
        statusText.color = color;
    }

    private void SetLoginButtonEnabled(bool enabled)
    {
        loginButton.interactable = enabled;
        usernameInput.interactable = enabled;
        passwordInput.interactable = enabled;
    }

    private async Task LoadGameScene()
    {
        var asyncOperation = SceneManager.LoadSceneAsync("GameScene");

        while (!asyncOperation.isDone)
        {
            await Task.Delay(100);
        }
    }
}
```

**2. 游戏主界面 (1.5小时)**

创建游戏主界面控制器：
```csharp
public class GameUI : MonoBehaviour
{
    [Header("Player Info")]
    [SerializeField] private Text playerNameText;
    [SerializeField] private Text playerLevelText;
    [SerializeField] private Slider experienceSlider;
    [SerializeField] private Text experienceText;

    [Header("Status")]
    [SerializeField] private Text healthText;
    [SerializeField] private Text manaText;
    [SerializeField] private Slider healthSlider;
    [SerializeField] private Slider manaSlider;

    [Header("Chat")]
    [SerializeField] private ScrollRect chatScrollRect;
    [SerializeField] private Text chatContent;
    [SerializeField] private InputField chatInput;
    [SerializeField] private Button sendButton;

    [Header("Buttons")]
    [SerializeField] private Button inventoryButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button logoutButton;

    private GameClient gameClient;
    private StringBuilder chatHistory = new StringBuilder();

    private void Start()
    {
        gameClient = FindObjectOfType<GameClient>();

        sendButton.onClick.AddListener(OnSendChatClicked);
        inventoryButton.onClick.AddListener(OnInventoryClicked);
        settingsButton.onClick.AddListener(OnSettingsClicked);
        logoutButton.onClick.AddListener(OnLogoutClicked);

        chatInput.onEndEdit.AddListener(OnChatInputEndEdit);

        // 初始化玩家数据
        InitializePlayerData();
    }

    private async void InitializePlayerData()
    {
        try
        {
            var playerInfo = await gameClient.GetPlayerInfoAsync();
            if (playerInfo.Success)
            {
                UpdatePlayerInfo(playerInfo.Data);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"获取玩家信息失败: {ex.Message}");
        }
    }

    public void UpdatePlayerInfo(PlayerInfo playerInfo)
    {
        playerNameText.text = playerInfo.CharacterName;
        playerLevelText.text = $"Lv.{playerInfo.Level}";

        // 更新经验条
        var expProgress = (float)playerInfo.Experience / playerInfo.ExperienceToNextLevel;
        experienceSlider.value = expProgress;
        experienceText.text = $"{playerInfo.Experience}/{playerInfo.ExperienceToNextLevel}";

        // 更新状态条
        healthSlider.value = (float)playerInfo.CurrentHealth / playerInfo.MaxHealth;
        healthText.text = $"{playerInfo.CurrentHealth}/{playerInfo.MaxHealth}";

        manaSlider.value = (float)playerInfo.CurrentMana / playerInfo.MaxMana;
        manaText.text = $"{playerInfo.CurrentMana}/{playerInfo.MaxMana}";
    }

    public void AddChatMessage(WorldChatEvent chatEvent)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var message = $"[{timestamp}] {chatEvent.PlayerName}: {chatEvent.Message}\n";

        chatHistory.Append(message);
        chatContent.text = chatHistory.ToString();

        // 自动滚动到底部
        Canvas.ForceUpdateCanvases();
        chatScrollRect.verticalNormalizedPosition = 0f;
    }

    private async void OnSendChatClicked()
    {
        await SendChatMessage();
    }

    private async void OnChatInputEndEdit(string text)
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            await SendChatMessage();
        }
    }

    private async Task SendChatMessage()
    {
        var message = chatInput.text.Trim();
        if (string.IsNullOrEmpty(message))
            return;

        try
        {
            var result = await gameClient.SendWorldChatAsync(message);
            if (result.Success)
            {
                chatInput.text = "";
            }
            else
            {
                Debug.LogWarning($"发送消息失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"发送消息异常: {ex.Message}");
        }
    }
}
```

**3. 背包系统 (1小时)**

创建背包界面：
```csharp
public class InventoryUI : MonoBehaviour
{
    [Header("UI Components")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private Transform itemsContainer;
    [SerializeField] private GameObject itemSlotPrefab;
    [SerializeField] private Button closeButton;

    [Header("Item Info")]
    [SerializeField] private GameObject itemInfoPanel;
    [SerializeField] private Text itemNameText;
    [SerializeField] private Text itemDescriptionText;
    [SerializeField] private Image itemIcon;
    [SerializeField] private Button useButton;
    [SerializeField] private Button dropButton;

    private List<ItemSlot> itemSlots = new List<ItemSlot>();
    private GameClient gameClient;
    private ItemData selectedItem;

    private void Start()
    {
        gameClient = FindObjectOfType<GameClient>();

        closeButton.onClick.AddListener(CloseInventory);
        useButton.onClick.AddListener(OnUseItemClicked);
        dropButton.onClick.AddListener(OnDropItemClicked);

        inventoryPanel.SetActive(false);
        itemInfoPanel.SetActive(false);

        // 创建背包格子
        CreateItemSlots(30); // 30格背包
    }

    private void CreateItemSlots(int slotCount)
    {
        for (int i = 0; i < slotCount; i++)
        {
            var slotObject = Instantiate(itemSlotPrefab, itemsContainer);
            var itemSlot = slotObject.GetComponent<ItemSlot>();

            itemSlot.SlotIndex = i;
            itemSlot.OnItemClicked += OnItemSlotClicked;

            itemSlots.Add(itemSlot);
        }
    }

    public void ShowInventory()
    {
        inventoryPanel.SetActive(true);
        LoadInventoryData();
    }

    public void CloseInventory()
    {
        inventoryPanel.SetActive(false);
        itemInfoPanel.SetActive(false);
    }

    private async void LoadInventoryData()
    {
        try
        {
            var result = await gameClient.GetInventoryAsync();
            if (result.Success)
            {
                UpdateInventory(result.Data.Items);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"加载背包数据失败: {ex.Message}");
        }
    }

    public void UpdateInventory(List<ItemData> items)
    {
        // 清空所有格子
        foreach (var slot in itemSlots)
        {
            slot.SetItem(null);
        }

        // 填充物品
        for (int i = 0; i < items.Count && i < itemSlots.Count; i++)
        {
            itemSlots[i].SetItem(items[i]);
        }
    }

    private void OnItemSlotClicked(ItemSlot slot)
    {
        if (slot.ItemData != null)
        {
            selectedItem = slot.ItemData;
            ShowItemInfo(selectedItem);
        }
        else
        {
            itemInfoPanel.SetActive(false);
        }
    }

    private void ShowItemInfo(ItemData item)
    {
        itemInfoPanel.SetActive(true);

        itemNameText.text = item.Name;
        itemDescriptionText.text = item.Description;
        // itemIcon.sprite = ResourceManager.LoadItemIcon(item.IconId);

        // 根据物品类型显示不同按钮
        useButton.gameObject.SetActive(item.Type == ItemType.Consumable);
        dropButton.gameObject.SetActive(true);
    }

    private async void OnUseItemClicked()
    {
        if (selectedItem == null) return;

        try
        {
            var result = await gameClient.UseItemAsync(selectedItem.Id);
            if (result.Success)
            {
                Debug.Log($"使用物品: {selectedItem.Name}");
                LoadInventoryData(); // 刷新背包
            }
            else
            {
                Debug.LogWarning($"使用物品失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"使用物品异常: {ex.Message}");
        }
    }

    private async void OnDropItemClicked()
    {
        if (selectedItem == null) return;

        try
        {
            var result = await gameClient.DropItemAsync(selectedItem.Id, 1);
            if (result.Success)
            {
                Debug.Log($"丢弃物品: {selectedItem.Name}");
                LoadInventoryData(); // 刷新背包
                itemInfoPanel.SetActive(false);
            }
            else
            {
                Debug.LogWarning($"丢弃物品失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"丢弃物品异常: {ex.Message}");
        }
    }
}

// ItemSlot.cs - 背包格子组件
public class ItemSlot : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image itemIcon;
    [SerializeField] private Text itemCountText;
    [SerializeField] private GameObject emptySlot;

    public int SlotIndex { get; set; }
    public ItemData ItemData { get; private set; }

    public event System.Action<ItemSlot> OnItemClicked;

    public void SetItem(ItemData itemData)
    {
        ItemData = itemData;

        if (itemData != null)
        {
            emptySlot.SetActive(false);
            itemIcon.gameObject.SetActive(true);
            itemCountText.gameObject.SetActive(true);

            // itemIcon.sprite = ResourceManager.LoadItemIcon(itemData.IconId);
            itemCountText.text = itemData.Count > 1 ? itemData.Count.ToString() : "";
        }
        else
        {
            emptySlot.SetActive(true);
            itemIcon.gameObject.SetActive(false);
            itemCountText.gameObject.SetActive(false);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OnItemClicked?.Invoke(this);
    }
}
```

**4. 性能监控界面 (0.5小时)**

创建性能监控显示：
```csharp
public class PerformanceMonitor : MonoBehaviour
{
    [Header("UI Components")]
    [SerializeField] private Text fpsText;
    [SerializeField] private Text latencyText;
    [SerializeField] private Text memoryText;
    [SerializeField] private GameObject performancePanel;

    [Header("Settings")]
    [SerializeField] private bool showPerformance = true;
    [SerializeField] private float updateInterval = 1.0f;

    private float fps;
    private float latency;
    private float memory;
    private float lastUpdateTime;
    private GameClient gameClient;

    private void Start()
    {
        gameClient = FindObjectOfType<GameClient>();
        performancePanel.SetActive(showPerformance);

        InvokeRepeating(nameof(MeasureLatency), 1f, 5f);
    }

    private void Update()
    {
        if (!showPerformance) return;

        // 计算FPS
        fps = 1.0f / Time.deltaTime;

        // 计算内存使用
        memory = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemory(false) / (1024f * 1024f);

        // 更新UI
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            UpdateUI();
            lastUpdateTime = Time.time;
        }
    }

    private void UpdateUI()
    {
        fpsText.text = $"FPS: {fps:F1}";
        latencyText.text = latency > 0 ? $"延迟: {latency:F0}ms" : "延迟: --ms";
        memoryText.text = $"内存: {memory:F1}MB";
    }

    private async void MeasureLatency()
    {
        if (gameClient == null) return;

        var startTime = DateTime.UtcNow;

        try
        {
            await gameClient.PingAsync();
            latency = (float)(DateTime.UtcNow - startTime).TotalMilliseconds;
        }
        catch
        {
            latency = -1; // 连接失败
        }
    }

    public void TogglePerformanceDisplay()
    {
        showPerformance = !showPerformance;
        performancePanel.SetActive(showPerformance);
    }
}
```

---

### 第四天：测试和优化 (8小时)

#### 上午：单元测试和集成测试 (4小时)

**1. 单元测试编写 (2小时)**

为服务层编写单元测试：
```csharp
[TestClass]
public class PlayerServiceTests
{
    private PlayerService _playerService;
    private Mock<IMongoDatabase> _mockDatabase;
    private Mock<IDatabase> _mockRedis;
    private Mock<IStructuredLogger> _mockLogger;

    [TestInitialize]
    public void Setup()
    {
        _mockDatabase = new Mock<IMongoDatabase>();
        _mockRedis = new Mock<IDatabase>();
        _mockLogger = new Mock<IStructuredLogger>();

        _playerService = new PlayerService(
            _mockDatabase.Object,
            _mockRedis.Object,
            _mockLogger.Object
        );
    }

    [TestMethod]
    public async Task GetPlayerAsync_WhenPlayerExists_ReturnsPlayer()
    {
        // Arrange
        var playerId = "player123";
        var expectedPlayer = new Player
        {
            Id = playerId,
            CharacterName = "TestPlayer",
            Level = 5
        };

        var mockCollection = new Mock<IMongoCollection<Player>>();
        var mockCursor = new Mock<IAsyncCursor<Player>>();

        mockCursor.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>()))
                  .Returns(true)
                  .Returns(false);
        mockCursor.SetupGet(c => c.Current)
                  .Returns(new[] { expectedPlayer });

        mockCollection.Setup(c => c.FindAsync(
            It.IsAny<FilterDefinition<Player>>(),
            It.IsAny<FindOptions<Player>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockCursor.Object);

        _mockDatabase.Setup(db => db.GetCollection<Player>("players", null))
                    .Returns(mockCollection.Object);

        // Act
        var result = await _playerService.GetPlayerAsync(playerId);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(expectedPlayer.Id, result.Id);
        Assert.AreEqual(expectedPlayer.CharacterName, result.CharacterName);
        Assert.AreEqual(expectedPlayer.Level, result.Level);
    }

    [TestMethod]
    public async Task GetPlayerAsync_WhenPlayerNotExists_ReturnsNull()
    {
        // Arrange
        var playerId = "nonexistent";

        var mockCollection = new Mock<IMongoCollection<Player>>();
        var mockCursor = new Mock<IAsyncCursor<Player>>();

        mockCursor.Setup(c => c.MoveNext(It.IsAny<CancellationToken>()))
                  .Returns(false);

        mockCollection.Setup(c => c.FindAsync(
            It.IsAny<FilterDefinition<Player>>(),
            It.IsAny<FindOptions<Player>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockCursor.Object);

        _mockDatabase.Setup(db => db.GetCollection<Player>("players", null))
                    .Returns(mockCollection.Object);

        // Act
        var result = await _playerService.GetPlayerAsync(playerId);

        // Assert
        Assert.IsNull(result);
    }
}
```

**2. 集成测试编写 (2小时)**

编写端到端集成测试：
```csharp
[TestClass]
public class AuthControllerIntegrationTests
{
    private WebApplicationFactory<Program> _factory;
    private HttpClient _client;

    [TestInitialize]
    public void Setup()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new[]
                    {
                        new KeyValuePair<string, string?>("UseInMemoryDatabase", "true"),
                        new KeyValuePair<string, string?>("JwtOptions:SecretKey", "test_secret_key_for_integration_tests")
                    });
                });
            });

        _client = _factory.CreateClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [TestMethod]
    public async Task RegisterAndLogin_Success()
    {
        // 1. 注册用户
        var registerRequest = new
        {
            username = "testuser",
            email = "test@example.com",
            password = "password123",
            confirmPassword = "password123"
        };

        var registerJson = JsonSerializer.Serialize(registerRequest);
        var registerContent = new StringContent(registerJson, Encoding.UTF8, "application/json");

        var registerResponse = await _client.PostAsync("/api/auth/register", registerContent);

        Assert.AreEqual(HttpStatusCode.OK, registerResponse.StatusCode);

        // 2. 登录用户
        var loginRequest = new
        {
            username = "testuser",
            password = "password123"
        };

        var loginJson = JsonSerializer.Serialize(loginRequest);
        var loginContent = new StringContent(loginJson, Encoding.UTF8, "application/json");

        var loginResponse = await _client.PostAsync("/api/auth/login", loginContent);

        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginResult = await loginResponse.Content.ReadAsStringAsync();
        var loginData = JsonSerializer.Deserialize<JsonElement>(loginResult);

        Assert.IsTrue(loginData.GetProperty("success").GetBoolean());
        Assert.IsTrue(loginData.GetProperty("accessToken").GetString()?.Length > 0);
    }

    [TestMethod]
    public async Task GetProfile_WithValidToken_ReturnsUserInfo()
    {
        // 1. 先注册并登录获取token
        var token = await RegisterAndGetTokenAsync();

        // 2. 使用token获取用户信息
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/auth/profile");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var userData = JsonSerializer.Deserialize<JsonElement>(content);

        Assert.IsTrue(userData.GetProperty("success").GetBoolean());
        Assert.AreEqual("testuser", userData.GetProperty("user").GetProperty("username").GetString());
    }

    private async Task<string> RegisterAndGetTokenAsync()
    {
        // 注册用户
        var registerRequest = new
        {
            username = "testuser",
            email = "test@example.com",
            password = "password123",
            confirmPassword = "password123"
        };

        var registerJson = JsonSerializer.Serialize(registerRequest);
        var registerContent = new StringContent(registerJson, Encoding.UTF8, "application/json");
        await _client.PostAsync("/api/auth/register", registerContent);

        // 登录获取token
        var loginRequest = new
        {
            username = "testuser",
            password = "password123"
        };

        var loginJson = JsonSerializer.Serialize(loginRequest);
        var loginContent = new StringContent(loginJson, Encoding.UTF8, "application/json");
        var loginResponse = await _client.PostAsync("/api/auth/login", loginContent);

        var loginResult = await loginResponse.Content.ReadAsStringAsync();
        var loginData = JsonSerializer.Deserialize<JsonElement>(loginResult);

        return loginData.GetProperty("accessToken").GetString()!;
    }
}
```

#### 下午：性能测试和优化 (4小时)

**1. 性能测试编写 (2小时)**

使用 NBomber 编写性能测试：
```csharp
[TestClass]
public class PerformanceTests
{
    [TestMethod]
    public void AuthServer_LoginLoad_Test()
    {
        var scenario = Scenario.Create("login_scenario", async context =>
        {
            var httpClient = new HttpClient();

            var request = new
            {
                username = $"user{context.ScenarioInfo.CurrentConcurrentCopies}",
                password = "password123"
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync("http://localhost:5000/api/auth/login", content);

            return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
        })
        .WithLoadSimulations(
            Simulation.InjectPerSec(rate: 10, during: TimeSpan.FromSeconds(30)),
            Simulation.KeepConstant(copies: 50, during: TimeSpan.FromMinutes(2))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();

        // 验证性能指标
        var scnStats = stats.AllScenarios.First();
        Assert.IsTrue(scnStats.Ok.Response.Mean < 500); // 平均响应时间小于500ms
        Assert.IsTrue(scnStats.AllFailCount == 0); // 无失败请求
    }

    [TestMethod]
    public void GameServer_ConcurrentConnections_Test()
    {
        var scenario = Scenario.Create("gameserver_connection", async context =>
        {
            try
            {
                var client = new PulseClient();
                await client.ConnectAsync("127.0.0.1", 7000);

                var playerService = client.GetService<IPlayerService>();
                var result = await playerService.PingAsync();

                await client.DisconnectAsync();

                return Response.Ok();
            }
            catch
            {
                return Response.Fail();
            }
        })
        .WithLoadSimulations(
            Simulation.InjectPerSec(rate: 5, during: TimeSpan.FromSeconds(60))
        );

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();

        var scnStats = stats.AllScenarios.First();
        Assert.IsTrue(scnStats.Ok.Response.Mean < 100); // 连接响应时间小于100ms
        Assert.IsTrue(scnStats.AllFailCount < scnStats.AllOkCount * 0.01); // 失败率小于1%
    }
}
```

**2. 性能分析和优化 (2小时)**

性能优化重点：

```csharp
// 1. 数据库查询优化
public class OptimizedPlayerService : IPlayerService
{
    public async Task<Player> GetPlayerAsync(string playerId)
    {
        // 使用投影只获取需要的字段
        var projection = Builders<Player>.Projection
            .Include(p => p.Id)
            .Include(p => p.CharacterName)
            .Include(p => p.Level)
            .Include(p => p.Experience);

        var player = await _collection
            .Find(p => p.Id == playerId)
            .Project<Player>(projection)
            .FirstOrDefaultAsync();

        return player;
    }

    public async Task<List<Player>> GetPlayersInRangeAsync(Vector3 position, float range)
    {
        // 使用地理空间索引优化位置查询
        var geoFilter = Builders<Player>.Filter.Near(
            p => p.Position,
            position.x, position.y,
            maxDistance: range
        );

        return await _collection
            .Find(geoFilter)
            .Limit(50)
            .ToListAsync();
    }
}

// 2. 缓存优化
public class CacheOptimizedService
{
    private readonly IMemoryCache _memoryCache;
    private readonly IDatabase _redis;

    public async Task<T> GetWithCacheAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan expiry)
    {
        // 先查内存缓存
        if (_memoryCache.TryGetValue(key, out T cachedValue))
        {
            return cachedValue;
        }

        // 再查Redis缓存
        var redisValue = await _redis.StringGetAsync(key);
        if (redisValue.HasValue)
        {
            var value = JsonSerializer.Deserialize<T>(redisValue);
            _memoryCache.Set(key, value, TimeSpan.FromMinutes(5));
            return value;
        }

        // 最后查数据库
        var result = await factory();
        if (result != null)
        {
            // 写入缓存
            var json = JsonSerializer.Serialize(result);
            await _redis.StringSetAsync(key, json, expiry);
            _memoryCache.Set(key, result, TimeSpan.FromMinutes(5));
        }

        return result;
    }
}

// 3. 连接池优化
public class OptimizedGameServer
{
    private readonly ObjectPool<PulseRpcConnection> _connectionPool;

    public OptimizedGameServer()
    {
        var poolPolicy = new DefaultPooledObjectPolicy<PulseRpcConnection>();
        _connectionPool = new DefaultObjectPool<PulseRpcConnection>(poolPolicy, 100);
    }

    public async Task<T> ExecuteWithConnectionAsync<T>(Func<PulseRpcConnection, Task<T>> operation)
    {
        var connection = _connectionPool.Get();
        try
        {
            return await operation(connection);
        }
        finally
        {
            _connectionPool.Return(connection);
        }
    }
}
```

---

### 第五天：部署和运维 (8小时)

#### 上午：Docker 容器化 (4小时)

**1. Dockerfile 优化 (2小时)**

创建优化的 Dockerfile：
```dockerfile
# Multi-stage build for AuthServer
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["src/GameApp.AuthServer/GameApp.AuthServer.csproj", "src/GameApp.AuthServer/"]
COPY ["src/GameApp.Shared/GameApp.Shared.csproj", "src/GameApp.Shared/"]
COPY ["src/GameApp.Infrastructure/GameApp.Infrastructure.csproj", "src/GameApp.Infrastructure/"]
COPY ["Directory.Packages.props", "./"]
COPY ["Directory.Build.props", "./"]

RUN dotnet restore "src/GameApp.AuthServer/GameApp.AuthServer.csproj"

# Copy source code and build
COPY . .
WORKDIR "/src/src/GameApp.AuthServer"
RUN dotnet build "GameApp.AuthServer.csproj" -c Release -o /app/build --no-restore

# Publish
FROM build AS publish
RUN dotnet publish "GameApp.AuthServer.csproj" -c Release -o /app/publish --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser

# Copy published app
COPY --from=publish /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
  CMD curl -f http://localhost:5000/api/auth/health || exit 1

EXPOSE 5000
ENTRYPOINT ["dotnet", "GameApp.AuthServer.dll"]
```

**2. Docker Compose 生产配置 (2小时)**

创建生产级 docker-compose 配置：
```yaml
version: '3.8'

services:
  # Load Balancer
  nginx:
    image: nginx:1.24-alpine
    container_name: gameapp-nginx
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx/nginx.conf:/etc/nginx/nginx.conf:ro
      - ./nginx/ssl:/etc/nginx/ssl:ro
      - nginx_logs:/var/log/nginx
    depends_on:
      - authserver-1
      - authserver-2
    networks:
      - gameapp-network
    restart: unless-stopped
    deploy:
      resources:
        limits:
          memory: 256M
          cpus: '0.5'

  # AuthServer instances
  authserver-1:
    image: gameapp/authserver:${VERSION:-latest}
    container_name: gameapp-authserver-1
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__MongoDB=${MONGODB_CONNECTION_STRING}
      - ConnectionStrings__Redis=${REDIS_CONNECTION_STRING}
      - JwtOptions__SecretKey=${JWT_SECRET_KEY}
      - Consul__Host=consul
    volumes:
      - authserver_logs:/app/logs
    depends_on:
      - mongodb-primary
      - redis-master
      - consul
    networks:
      - gameapp-network
    restart: unless-stopped
    deploy:
      resources:
        limits:
          memory: 1G
          cpus: '1.0'
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:5000/api/auth/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 60s

  authserver-2:
    image: gameapp/authserver:${VERSION:-latest}
    container_name: gameapp-authserver-2
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__MongoDB=${MONGODB_CONNECTION_STRING}
      - ConnectionStrings__Redis=${REDIS_CONNECTION_STRING}
      - JwtOptions__SecretKey=${JWT_SECRET_KEY}
      - Consul__Host=consul
    volumes:
      - authserver_logs:/app/logs
    depends_on:
      - mongodb-primary
      - redis-master
      - consul
    networks:
      - gameapp-network
    restart: unless-stopped
    deploy:
      resources:
        limits:
          memory: 1G
          cpus: '1.0'

  # MongoDB Replica Set
  mongodb-primary:
    image: mongo:7.0
    container_name: gameapp-mongodb-primary
    command: mongod --replSet gameapp-replica --bind_ip_all
    environment:
      MONGO_INITDB_ROOT_USERNAME: ${MONGODB_ROOT_USERNAME}
      MONGO_INITDB_ROOT_PASSWORD: ${MONGODB_ROOT_PASSWORD}
    volumes:
      - mongodb_primary_data:/data/db
      - mongodb_logs:/var/log/mongodb
    networks:
      - gameapp-network
    restart: unless-stopped
    deploy:
      resources:
        limits:
          memory: 2G
          cpus: '2.0'

  mongodb-secondary:
    image: mongo:7.0
    container_name: gameapp-mongodb-secondary
    command: mongod --replSet gameapp-replica --bind_ip_all
    volumes:
      - mongodb_secondary_data:/data/db
    networks:
      - gameapp-network
    restart: unless-stopped
    deploy:
      resources:
        limits:
          memory: 1G
          cpus: '1.0'

  # Redis Cluster
  redis-master:
    image: redis:7.0-alpine
    container_name: gameapp-redis-master
    command: redis-server --requirepass ${REDIS_PASSWORD} --appendonly yes
    volumes:
      - redis_master_data:/data
    networks:
      - gameapp-network
    restart: unless-stopped
    deploy:
      resources:
        limits:
          memory: 512M
          cpus: '0.5'

  redis-slave:
    image: redis:7.0-alpine
    container_name: gameapp-redis-slave
    command: redis-server --requirepass ${REDIS_PASSWORD} --slaveof redis-master 6379 --masterauth ${REDIS_PASSWORD}
    volumes:
      - redis_slave_data:/data
    depends_on:
      - redis-master
    networks:
      - gameapp-network
    restart: unless-stopped

  # Service Discovery
  consul:
    image: hashicorp/consul:1.15
    container_name: gameapp-consul
    command: >
      consul agent
      -server
      -bootstrap-expect=1
      -ui
      -client=0.0.0.0
      -data-dir=/consul/data
      -config-dir=/consul/config
    volumes:
      - consul_data:/consul/data
      - ./consul:/consul/config:ro
    networks:
      - gameapp-network
    restart: unless-stopped
    deploy:
      resources:
        limits:
          memory: 256M
          cpus: '0.5'

  # Monitoring
  prometheus:
    image: prom/prometheus:v2.45.0
    container_name: gameapp-prometheus
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      - '--storage.tsdb.path=/prometheus'
      - '--web.console.libraries=/etc/prometheus/console_libraries'
      - '--web.console.templates=/etc/prometheus/consoles'
      - '--web.enable-lifecycle'
    volumes:
      - ./monitoring/prometheus.yml:/etc/prometheus/prometheus.yml:ro
      - prometheus_data:/prometheus
    networks:
      - gameapp-network
    restart: unless-stopped

  grafana:
    image: grafana/grafana:10.0.0
    container_name: gameapp-grafana
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=${GRAFANA_ADMIN_PASSWORD}
    volumes:
      - grafana_data:/var/lib/grafana
      - ./monitoring/grafana/dashboards:/etc/grafana/provisioning/dashboards:ro
      - ./monitoring/grafana/datasources:/etc/grafana/provisioning/datasources:ro
    depends_on:
      - prometheus
    networks:
      - gameapp-network
    restart: unless-stopped

volumes:
  mongodb_primary_data:
  mongodb_secondary_data:
  mongodb_logs:
  redis_master_data:
  redis_slave_data:
  consul_data:
  prometheus_data:
  grafana_data:
  authserver_logs:
  nginx_logs:

networks:
  gameapp-network:
    driver: bridge
    ipam:
      config:
        - subnet: 172.20.0.0/16
```

#### 下午：监控和运维 (4小时)

**1. 监控配置 (2小时)**

配置 Prometheus 和 Grafana：

```yaml
# monitoring/prometheus.yml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

rule_files:
  - "rules/*.yml"

scrape_configs:
  - job_name: 'gameapp-authserver'
    static_configs:
      - targets: ['authserver-1:5000', 'authserver-2:5000']
    metrics_path: '/metrics'
    scrape_interval: 10s

  - job_name: 'gameapp-gameserver'
    static_configs:
      - targets: ['gameserver-1:7000', 'gameserver-2:7000']
    metrics_path: '/metrics'
    scrape_interval: 10s

  - job_name: 'mongodb'
    static_configs:
      - targets: ['mongodb-exporter:9216']

  - job_name: 'redis'
    static_configs:
      - targets: ['redis-exporter:9121']

  - job_name: 'nginx'
    static_configs:
      - targets: ['nginx-exporter:9113']

alerting:
  alertmanagers:
    - static_configs:
        - targets:
          - alertmanager:9093
```

**2. 日志聚合 (1小时)**

配置 ELK Stack：
```yaml
# docker-compose.elk.yml
version: '3.8'

services:
  elasticsearch:
    image: docker.elastic.co/elasticsearch/elasticsearch:8.8.0
    container_name: gameapp-elasticsearch
    environment:
      - discovery.type=single-node
      - xpack.security.enabled=false
      - "ES_JAVA_OPTS=-Xms1g -Xmx1g"
    volumes:
      - elasticsearch_data:/usr/share/elasticsearch/data
    networks:
      - elk-network
    restart: unless-stopped

  logstash:
    image: docker.elastic.co/logstash/logstash:8.8.0
    container_name: gameapp-logstash
    volumes:
      - ./elk/logstash/config/logstash.yml:/usr/share/logstash/config/logstash.yml:ro
      - ./elk/logstash/pipeline:/usr/share/logstash/pipeline:ro
    depends_on:
      - elasticsearch
    networks:
      - elk-network
    restart: unless-stopped

  kibana:
    image: docker.elastic.co/kibana/kibana:8.8.0
    container_name: gameapp-kibana
    environment:
      ELASTICSEARCH_HOSTS: http://elasticsearch:9200
    ports:
      - "5601:5601"
    depends_on:
      - elasticsearch
    networks:
      - elk-network
    restart: unless-stopped

  filebeat:
    image: docker.elastic.co/beats/filebeat:8.8.0
    container_name: gameapp-filebeat
    user: root
    volumes:
      - ./elk/filebeat/filebeat.yml:/usr/share/filebeat/filebeat.yml:ro
      - /var/lib/docker/containers:/var/lib/docker/containers:ro
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - authserver_logs:/var/log/authserver:ro
    depends_on:
      - logstash
    networks:
      - elk-network
    restart: unless-stopped

volumes:
  elasticsearch_data:

networks:
  elk-network:
    external: true
```

**3. 自动化部署脚本 (1小时)**

创建部署自动化脚本：
```bash
#!/bin/bash
# deploy.sh - 生产环境部署脚本

set -e

# 配置
PROJECT_NAME="gameapp"
REGISTRY_URL="registry.example.com"
VERSION="${1:-latest}"
ENVIRONMENT="${2:-production}"

# 颜色输出
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

log() {
    echo -e "${GREEN}[$(date +'%Y-%m-%d %H:%M:%S')] $1${NC}"
}

warn() {
    echo -e "${YELLOW}[$(date +'%Y-%m-%d %H:%M:%S')] WARNING: $1${NC}"
}

error() {
    echo -e "${RED}[$(date +'%Y-%m-%d %H:%M:%S')] ERROR: $1${NC}"
    exit 1
}

# 检查依赖
check_dependencies() {
    log "检查部署环境..."

    command -v docker >/dev/null 2>&1 || error "Docker 未安装"
    command -v docker-compose >/dev/null 2>&1 || error "Docker Compose 未安装"

    # 检查磁盘空间
    DISK_USAGE=$(df / | tail -1 | awk '{print $5}' | sed 's/%//')
    if [ "$DISK_USAGE" -gt 80 ]; then
        warn "磁盘使用率超过80%: ${DISK_USAGE}%"
    fi

    # 检查内存
    FREE_MEM=$(free -m | awk 'NR==2{print $7}')
    if [ "$FREE_MEM" -lt 1024 ]; then
        warn "可用内存不足1GB: ${FREE_MEM}MB"
    fi

    log "环境检查完成"
}

# 备份当前版本
backup_current_version() {
    log "备份当前版本..."

    if [ -f docker-compose.yml ]; then
        cp docker-compose.yml docker-compose.yml.backup.$(date +%Y%m%d_%H%M%S)
        log "配置文件已备份"
    fi

    # 备份数据库
    docker exec gameapp-mongodb-primary mongodump \
        --username admin \
        --password "${MONGODB_ROOT_PASSWORD}" \
        --authenticationDatabase admin \
        --out /backup/$(date +%Y%m%d_%H%M%S) || warn "数据库备份失败"

    log "备份完成"
}

# 拉取最新镜像
pull_images() {
    log "拉取最新镜像..."

    docker pull "${REGISTRY_URL}/${PROJECT_NAME}/authserver:${VERSION}"
    docker pull "${REGISTRY_URL}/${PROJECT_NAME}/gameserver:${VERSION}"
    docker pull "${REGISTRY_URL}/${PROJECT_NAME}/battleserver:${VERSION}"

    log "镜像拉取完成"
}

# 更新配置
update_configuration() {
    log "更新配置文件..."

    # 生成新的配置文件
    export VERSION
    envsubst < docker-compose.prod.template.yml > docker-compose.yml

    # 验证配置
    docker-compose config > /dev/null || error "配置文件验证失败"

    log "配置更新完成"
}

# 滚动更新服务
rolling_update() {
    log "开始滚动更新..."

    # 更新 AuthServer
    for instance in authserver-1 authserver-2; do
        log "更新 ${instance}..."

        docker-compose stop ${instance}
        docker-compose rm -f ${instance}
        docker-compose up -d ${instance}

        # 等待健康检查通过
        for i in {1..30}; do
            if docker-compose ps ${instance} | grep -q "healthy"; then
                log "${instance} 更新成功"
                break
            fi

            if [ $i -eq 30 ]; then
                error "${instance} 健康检查失败"
            fi

            sleep 5
        done
    done

    log "滚动更新完成"
}

# 验证部署
verify_deployment() {
    log "验证部署状态..."

    # 检查服务状态
    docker-compose ps

    # 健康检查
    for service in authserver-1 authserver-2; do
        if ! docker-compose ps ${service} | grep -q "healthy"; then
            error "${service} 不健康"
        fi
    done

    # API测试
    curl -f http://localhost/api/auth/health || error "API健康检查失败"

    log "部署验证完成"
}

# 主流程
main() {
    log "开始部署 GameApp ${VERSION} 到 ${ENVIRONMENT} 环境"

    check_dependencies
    backup_current_version
    pull_images
    update_configuration
    rolling_update
    verify_deployment

    log "部署完成！"
    log "版本: ${VERSION}"
    log "环境: ${ENVIRONMENT}"
    log "访问地址: http://localhost"
}

# 错误处理
trap 'error "部署过程中发生错误，请检查日志"' ERR

# 执行主流程
main "$@"
```

## 培训总结

### 核心技能掌握

完成本培训后，开发者应具备：

1. **架构理解**: 深入理解微服务架构和 GameApp 技术栈
2. **开发技能**: 熟练开发 HTTP API 和 PulseRPC 服务
3. **客户端开发**: Unity 客户端网络通信和UI开发
4. **测试能力**: 编写单元测试、集成测试和性能测试
5. **运维技能**: Docker 容器化、监控配置和自动化部署

### 最佳实践

1. **代码质量**: 遵循编码规范，编写可测试的代码
2. **安全意识**: 实施安全编码实践，保护用户数据
3. **性能优化**: 关注性能指标，持续优化系统
4. **监控运维**: 建立完善的监控体系，确保系统稳定
5. **文档维护**: 及时更新文档，促进团队协作

### 持续学习

1. **技术更新**: 关注 .NET、Unity 和相关技术的最新发展
2. **社区参与**: 参与开源社区，分享经验和学习
3. **实践项目**: 通过实际项目加深理解和应用
4. **团队协作**: 与团队成员分享知识，共同成长

---

**恭喜完成 GameApp 开发者培训！** 🎉

现在您已具备开发和维护 GameApp 系统的核心技能。继续实践和学习，不断提升技术水平！
