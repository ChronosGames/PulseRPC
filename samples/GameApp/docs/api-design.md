# GameApp API 设计文档

## API 架构概述

GameApp 采用混合 API 架构，结合 HTTP REST API 和 PulseRPC 协议，为不同的业务场景提供最适合的通信方式。

```mermaid
graph TB
    Client[Unity 客户端]

    subgraph "HTTP APIs"
        Auth[AuthServer<br/>认证服务]
        Zone[ZoneServer<br/>区服服务]
    end

    subgraph "PulseRPC Services"
        Game[GameServer<br/>游戏服务]
        Battle[BattleServer<br/>战斗服务]
    end

    Client -->|HTTP REST| Auth
    Client -->|HTTP REST| Zone
    Client -->|PulseRPC TCP| Game
    Client -->|PulseRPC KCP| Battle

    Game --> Auth
    Battle --> Auth

    style Auth fill:#fff3e0
    style Zone fill:#fff3e0
    style Game fill:#e8f5e8
    style Battle fill:#fce4ec
```

## HTTP REST API 设计

### 1. AuthServer API

#### 基础信息
- **Base URL**: `https://auth.gameapp.com/api`
- **认证方式**: JWT Bearer Token
- **内容类型**: `application/json`
- **字符编码**: `UTF-8`

#### 1.1 用户认证接口

**POST /auth/register - 用户注册**

请求体:
```json
{
  "username": "player001",
  "email": "player001@example.com",
  "password": "SecurePassword123!",
  "confirmPassword": "SecurePassword123!",
  "inviteCode": "ABC123",
  "agreementAccepted": true
}
```

响应体:
```json
{
  "success": true,
  "message": "注册成功",
  "data": {
    "userId": 12345,
    "username": "player001",
    "email": "player001@example.com",
    "registrationTime": "2024-01-15T10:00:00Z",
    "emailVerificationRequired": true
  },
  "errors": null
}
```

**POST /auth/login - 用户登录**

请求体:
```json
{
  "username": "player001",
  "password": "SecurePassword123!",
  "deviceId": "unity_device_001",
  "deviceInfo": {
    "platform": "Windows",
    "version": "10.0.19042",
    "unityVersion": "2022.3.10f1"
  }
}
```

响应体:
```json
{
  "success": true,
  "message": "登录成功",
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "rt_1234567890abcdef",
    "expiresIn": 3600,
    "tokenType": "Bearer",
    "user": {
      "userId": 12345,
      "username": "player001",
      "profile": {
        "nickname": "玩家001",
        "avatar": "avatar_001.jpg",
        "level": 25,
        "vipLevel": 2
      }
    },
    "gameTicket": "gt_abcdef1234567890",
    "gameTicketExpiresIn": 300
  },
  "errors": null
}
```

**POST /auth/logout - 用户登出**

请求头:
```http
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

响应体:
```json
{
  "success": true,
  "message": "登出成功",
  "data": null,
  "errors": null
}
```

**POST /auth/refresh - 刷新Token**

请求体:
```json
{
  "refreshToken": "rt_1234567890abcdef"
}
```

响应体:
```json
{
  "success": true,
  "message": "Token刷新成功",
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "expiresIn": 3600,
    "tokenType": "Bearer"
  },
  "errors": null
}
```

**GET /auth/verify - 验证Token**

请求头:
```http
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

响应体:
```json
{
  "success": true,
  "message": "Token有效",
  "data": {
    "userId": 12345,
    "username": "player001",
    "expiresAt": "2024-01-15T11:00:00Z",
    "permissions": ["game_access", "chat_access"]
  },
  "errors": null
}
```

#### 1.2 游戏票据接口

**POST /auth/game-ticket - 生成游戏票据**

请求头:
```http
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

请求体:
```json
{
  "zoneId": "zone001",
  "serverId": "gameserver_001"
}
```

响应体:
```json
{
  "success": true,
  "message": "游戏票据生成成功",
  "data": {
    "gameTicket": "gt_abcdef1234567890",
    "expiresIn": 300,
    "zoneId": "zone001",
    "serverId": "gameserver_001",
    "permissions": ["world_access", "battle_access"]
  },
  "errors": null
}
```

**POST /auth/verify-ticket - 验证游戏票据**

请求体:
```json
{
  "gameTicket": "gt_abcdef1234567890",
  "serverId": "gameserver_001"
}
```

响应体:
```json
{
  "success": true,
  "message": "票据验证成功",
  "data": {
    "userId": 12345,
    "username": "player001",
    "zoneId": "zone001",
    "permissions": ["world_access", "battle_access"],
    "playerInfo": {
      "playerId": 67890,
      "characterName": "勇敢的战士",
      "level": 45,
      "class": "warrior"
    }
  },
  "errors": null
}
```

#### 1.3 区服管理接口

**GET /zones - 获取区服列表**

响应体:
```json
{
  "success": true,
  "message": "获取区服列表成功",
  "data": {
    "zones": [
      {
        "zoneId": "zone001",
        "name": "华夏一区",
        "status": "online",
        "playerCount": 2500,
        "maxPlayers": 5000,
        "loadLevel": "medium",
        "recommendation": true,
        "newPlayerAllowed": true,
        "description": "推荐新手服务器",
        "features": ["pvp", "guild_war", "world_boss"]
      }
    ],
    "totalCount": 10,
    "onlineCount": 8
  },
  "errors": null
}
```

**GET /zones/{zoneId} - 获取区服详情**

响应体:
```json
{
  "success": true,
  "message": "获取区服详情成功",
  "data": {
    "zoneId": "zone001",
    "name": "华夏一区",
    "status": "online",
    "playerCount": 2500,
    "maxPlayers": 5000,
    "loadLevel": "medium",
    "serverList": [
      {
        "serverId": "gameserver_001",
        "type": "game",
        "address": "game1.zone001.gameapp.com:7000",
        "status": "online",
        "playerCount": 800
      },
      {
        "serverId": "battleserver_001",
        "type": "battle",
        "address": "battle1.zone001.gameapp.com:7001",
        "status": "online",
        "activeBattles": 25
      }
    ],
    "events": [
      {
        "eventId": "double_exp",
        "name": "双倍经验活动",
        "startTime": "2024-01-15T00:00:00Z",
        "endTime": "2024-01-17T23:59:59Z"
      }
    ],
    "maintenance": {
      "scheduled": false,
      "nextMaintenance": "2024-01-20T02:00:00Z",
      "estimatedDuration": 7200
    }
  },
  "errors": null
}
```

**POST /zones/select - 选择区服**

请求头:
```http
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

请求体:
```json
{
  "zoneId": "zone001"
}
```

响应体:
```json
{
  "success": true,
  "message": "区服选择成功",
  "data": {
    "zoneId": "zone001",
    "gameTicket": "gt_abcdef1234567890",
    "expiresIn": 300,
    "gameServers": [
      {
        "serverId": "gameserver_001",
        "address": "game1.zone001.gameapp.com:7000",
        "priority": 1
      }
    ],
    "battleServers": [
      {
        "serverId": "battleserver_001",
        "address": "battle1.zone001.gameapp.com:7001",
        "priority": 1
      }
    ]
  },
  "errors": null
}
```

#### 1.4 错误响应格式

```json
{
  "success": false,
  "message": "操作失败",
  "data": null,
  "errors": [
    {
      "code": "INVALID_CREDENTIALS",
      "message": "用户名或密码错误",
      "field": "password",
      "details": {}
    }
  ],
  "requestId": "req_1234567890",
  "timestamp": "2024-01-15T10:30:00Z"
}
```

**常见错误代码**:
- `INVALID_CREDENTIALS`: 无效凭据
- `USER_NOT_FOUND`: 用户不存在
- `EMAIL_ALREADY_EXISTS`: 邮箱已存在
- `USERNAME_ALREADY_EXISTS`: 用户名已存在
- `TOKEN_EXPIRED`: Token已过期
- `TOKEN_INVALID`: Token无效
- `ZONE_NOT_FOUND`: 区服不存在
- `ZONE_MAINTENANCE`: 区服维护中
- `RATE_LIMIT_EXCEEDED`: 请求频率超限

## PulseRPC API 设计

### 2. GameServer RPC 服务

#### 2.1 玩家服务 (IPlayerService)

```csharp
[Channel("TcpChannel")]
public interface IPlayerService : IPulseService
{
    /// <summary>
    /// 玩家登录游戏服务器
    /// </summary>
    ValueTask<LoginResponse> LoginAsync(LoginRequest request);

    /// <summary>
    /// 获取玩家信息
    /// </summary>
    ValueTask<PlayerInfo> GetPlayerInfoAsync(PlayerInfoRequest request);

    /// <summary>
    /// 更新玩家位置
    /// </summary>
    [Channel("KcpChannel")]
    ValueTask UpdatePositionAsync(UpdatePositionRequest request);

    /// <summary>
    /// 玩家登出
    /// </summary>
    ValueTask LogoutAsync(LogoutRequest request);

    /// <summary>
    /// 获取玩家统计信息
    /// </summary>
    ValueTask<PlayerStatistics> GetStatisticsAsync(GetStatisticsRequest request);
}
```

**数据传输对象**:

```csharp
[MemoryPackable]
public partial class LoginRequest
{
    public string GameTicket { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public ClientInfo ClientInfo { get; set; }
}

[MemoryPackable]
public partial class LoginResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string Message { get; set; }
    [Key(2)] public string SessionId { get; set; }
    [Key(3)] public PlayerInfo PlayerInfo { get; set; }
    [Key(4)] public WorldInfo WorldInfo { get; set; }
    [Key(5)] public ServerInfo ServerInfo { get; set; }
}

[MemoryPackable]
public partial class PlayerInfo
{
    public int PlayerId { get; set; }
    public int UserId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public int Level { get; set; }
    public long Experience { get; set; }
    public PlayerAttributes Attributes { get; set; }
    public PlayerStatus Status { get; set; }
    public PlayerPosition Position { get; set; }
    public PlayerInventory Inventory { get; set; }
    public PlayerEquipment Equipment { get; set; }
}

[MemoryPackable]
public partial class PlayerPosition
{
    public string WorldId { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Rotation { get; set; }
    public DateTime LastUpdate { get; set; }
}
```

#### 2.2 世界服务 (IWorldService)

```csharp
[Channel("TcpChannel")]
public interface IWorldService : IPulseService
{
    /// <summary>
    /// 加入世界
    /// </summary>
    ValueTask<JoinWorldResponse> JoinWorldAsync(JoinWorldRequest request);

    /// <summary>
    /// 离开世界
    /// </summary>
    ValueTask LeaveWorldAsync(LeaveWorldRequest request);

    /// <summary>
    /// 获取世界状态
    /// </summary>
    ValueTask<WorldState> GetWorldStateAsync(GetWorldStateRequest request);

    /// <summary>
    /// 世界聊天
    /// </summary>
    ValueTask SendWorldChatAsync(WorldChatRequest request);

    /// <summary>
    /// 获取附近玩家
    /// </summary>
    ValueTask<NearbyPlayersResponse> GetNearbyPlayersAsync(NearbyPlayersRequest request);
}

/// <summary>
/// 世界事件推送接口
/// </summary>
[Channel("TcpChannel")]
public interface IWorldEvents : IPulseEventHandler
{
    /// <summary>
    /// 世界状态更新
    /// </summary>
    void OnWorldUpdate(WorldUpdateEvent eventData);

    /// <summary>
    /// 玩家加入世界
    /// </summary>
    void OnPlayerJoined(PlayerJoinedEvent eventData);

    /// <summary>
    /// 玩家离开世界
    /// </summary>
    void OnPlayerLeft(PlayerLeftEvent eventData);

    /// <summary>
    /// 玩家移动更新
    /// </summary>
    [Channel("KcpChannel")]
    void OnPlayerMoved(PlayerMovedEvent eventData);
}
```

**数据传输对象**:

```csharp
[MemoryPackable]
public partial class WorldState
{
    [Key(0)] public string WorldId { get; set; }
    [Key(1)] public string Name { get; set; }
    [Key(2)] public int CurrentPlayers { get; set; }
    [Key(3)] public int MaxPlayers { get; set; }
    [Key(4)] public string Status { get; set; }
    [Key(5)] public List<WorldEvent> ActiveEvents { get; set; }
    [Key(6)] public WeatherInfo Weather { get; set; }
    [Key(7)] public Dictionary<string, object> Properties { get; set; }
}

[MemoryPackable]
public partial class WorldUpdate
{
    [Key(0)] public string UpdateType { get; set; }
    [Key(1)] public string WorldId { get; set; }
    [Key(2)] public int PlayerId { get; set; }
    [Key(3)] public Dictionary<string, object> Data { get; set; }
    [Key(4)] public DateTime Timestamp { get; set; }
}

[MemoryPackable]
public partial class WorldChatRequest
{
    [Key(0)] public int PlayerId { get; set; }
    [Key(1)] public string Message { get; set; }
    [Key(2)] public string ChatType { get; set; } // world, zone, guild
    [Key(3)] public Dictionary<string, object> Metadata { get; set; }
}
```

#### 2.3 背包服务 (IInventoryService)

```csharp
[Channel("TcpChannel")]
public interface IInventoryService : IPulseService
{
    /// <summary>
    /// 获取背包信息
    /// </summary>
    ValueTask<InventoryInfo> GetInventoryAsync(GetInventoryRequest request);

    /// <summary>
    /// 使用道具
    /// </summary>
    ValueTask<UseItemResponse> UseItemAsync(UseItemRequest request);

    /// <summary>
    /// 移动道具
    /// </summary>
    ValueTask MoveItemAsync(MoveItemRequest request);

    /// <summary>
    /// 出售道具
    /// </summary>
    ValueTask<SellItemResponse> SellItemAsync(SellItemRequest request);

    /// <summary>
    /// 交易道具
    /// </summary>
    ValueTask<TradeResponse> TradeItemAsync(TradeItemRequest request);
}
```

### 3. BattleServer RPC 服务

#### 3.1 战斗服务 (IBattleService)

```csharp
[Channel("KcpChannel")]
public interface IBattleService : IPulseService
{
    /// <summary>
    /// 加入战斗
    /// </summary>
    ValueTask<JoinBattleResponse> JoinBattleAsync(JoinBattleRequest request);

    /// <summary>
    /// 离开战斗
    /// </summary>
    ValueTask LeaveBattleAsync(LeaveBattleRequest request);

    /// <summary>
    /// 使用技能
    /// </summary>
    ValueTask<SkillResult> UseSkillAsync(UseSkillRequest request);

    /// <summary>
    /// 获取战斗状态
    /// </summary>
    [Channel("TcpChannel")]
    ValueTask<BattleState> GetBattleStateAsync(GetBattleStateRequest request);
}

/// <summary>
/// 战斗事件推送接口
/// </summary>
[Channel("KcpChannel")]
public interface IBattleEvents : IPulseEventHandler
{
    /// <summary>
    /// 战斗状态更新
    /// </summary>
    void OnBattleStateUpdate(BattleStateUpdateEvent eventData);

    /// <summary>
    /// 技能释放事件
    /// </summary>
    void OnSkillUsed(SkillUsedEvent eventData);

    /// <summary>
    /// 伤害事件
    /// </summary>
    void OnDamageDealt(DamageDealtEvent eventData);

    /// <summary>
    /// 玩家战败事件
    /// </summary>
    void OnPlayerDefeated(PlayerDefeatedEvent eventData);
}
```

**数据传输对象**:

```csharp
[MemoryPackable]
public partial class JoinBattleRequest
{
    [Key(0)] public int PlayerId { get; set; }
    [Key(1)] public string BattleType { get; set; } // pvp, pve, guild_war
    [Key(2)] public Dictionary<string, object> Parameters { get; set; }
}

[MemoryPackable]
public partial class BattleCommand
{
    [Key(0)] public string CommandType { get; set; }
    [Key(1)] public int PlayerId { get; set; }
    [Key(2)] public int TargetId { get; set; }
    [Key(3)] public string SkillId { get; set; }
    [Key(4)] public Vector3 Position { get; set; }
    [Key(5)] public Dictionary<string, object> Parameters { get; set; }
    [Key(6)] public DateTime Timestamp { get; set; }
}

[MemoryPackable]
public partial class BattleEvent
{
    [Key(0)] public string EventType { get; set; }
    [Key(1)] public string BattleId { get; set; }
    [Key(2)] public int SourcePlayerId { get; set; }
    [Key(3)] public int TargetPlayerId { get; set; }
    [Key(4)] public SkillResult SkillResult { get; set; }
    [Key(5)] public Dictionary<string, object> Data { get; set; }
    [Key(6)] public DateTime Timestamp { get; set; }
}

[MemoryPackable]
public partial class SkillResult
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string SkillId { get; set; }
    [Key(2)] public int Damage { get; set; }
    [Key(3)] public List<string> Effects { get; set; }
    [Key(4)] public Dictionary<string, object> Properties { get; set; }
}
```

#### 3.2 技能服务 (ISkillService)

```csharp
[Channel("TcpChannel")]
public interface ISkillService : IPulseService
{
    /// <summary>
    /// 学习技能
    /// </summary>
    ValueTask<LearnSkillResponse> LearnSkillAsync(LearnSkillRequest request);

    /// <summary>
    /// 升级技能
    /// </summary>
    ValueTask<UpgradeSkillResponse> UpgradeSkillAsync(UpgradeSkillRequest request);

    /// <summary>
    /// 获取技能列表
    /// </summary>
    ValueTask<SkillListResponse> GetSkillListAsync(GetSkillListRequest request);

    /// <summary>
    /// 重置技能点
    /// </summary>
    ValueTask<ResetSkillsResponse> ResetSkillsAsync(ResetSkillsRequest request);
}
```

## 客户端 API 调用示例

### 1. Unity HTTP 客户端示例

```csharp
public class AuthClient
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://auth.gameapp.com/api";

    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        var request = new LoginRequest
        {
            Username = username,
            Password = password,
            DeviceId = SystemInfo.deviceUniqueIdentifier,
            DeviceInfo = new DeviceInfo
            {
                Platform = Application.platform.ToString(),
                Version = SystemInfo.operatingSystem,
                UnityVersion = Application.unityVersion
            }
        };

        var json = JsonUtility.ToJson(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{BaseUrl}/auth/login", content);
        var responseJson = await response.Content.ReadAsStringAsync();

        return JsonUtility.FromJson<ApiResponse<LoginData>>(responseJson);
    }

    public async Task<ZoneListResponse> GetZoneListAsync()
    {
        var response = await _httpClient.GetAsync($"{BaseUrl}/zones");
        var responseJson = await response.Content.ReadAsStringAsync();

        return JsonUtility.FromJson<ApiResponse<ZoneListData>>(responseJson);
    }
}
```

### 2. Unity PulseRPC 客户端示例

```csharp
public class GameClient
{
    private IPulseClient _pulseClient;
    private IPlayerService _playerService;
    private IWorldService _worldService;
    private IWorldEvents _worldEvents;

    public async Task<bool> ConnectToGameServerAsync(string address, string gameTicket)
    {
        // 创建 PulseRPC 客户端
        _pulseClient = PulseClientBuilder.Create()
            .AddTcp("TcpChannel", address, 7000)
            .AddKcp("KcpChannel", address, 7001)
            .WithTimeout(TimeSpan.FromSeconds(30))
            .Build();

        await _pulseClient.ConnectAsync();

        // 获取服务代理
        _playerService = await _pulseClient.GetServiceAsync<IPlayerService>();
        _worldService = await _pulseClient.GetServiceAsync<IWorldService>();

        // 注册事件监听器
        _worldEvents = new WorldEventsImpl();
        await _pulseClient.RegisterEventListenerAsync<IWorldEvents>(_worldEvents);

        var loginRequest = new LoginRequest
        {
            GameTicket = gameTicket,
            DeviceId = SystemInfo.deviceUniqueIdentifier,
            ClientInfo = new ClientInfo
            {
                Version = Application.version,
                Platform = Application.platform.ToString()
            }
        };

        var response = await _playerService.LoginAsync(loginRequest);
        return response.Success;
    }

    public async Task UpdatePositionAsync(Vector3 position, float rotation)
    {
        var request = new UpdatePositionRequest
        {
            PlayerId = GameManager.Instance.PlayerId,
            Position = new PlayerPosition
            {
                X = position.x,
                Y = position.y,
                Z = position.z,
                Rotation = rotation,
                LastUpdate = DateTime.UtcNow
            }
        };

        await _playerService.UpdatePositionAsync(request);
    }

        // 事件监听器实现
    private class WorldEventsImpl : IWorldEvents
    {
        public void OnWorldUpdate(WorldUpdateEvent eventData)
        {
            // 处理世界更新事件
            Debug.Log($"World updated: {eventData.UpdateType}");
        }

        public void OnPlayerJoined(PlayerJoinedEvent eventData)
        {
            // 处理玩家加入事件
            Debug.Log($"Player joined: {eventData.PlayerName}");
        }

        public void OnPlayerLeft(PlayerLeftEvent eventData)
        {
            // 处理玩家离开事件
            Debug.Log($"Player left: {eventData.PlayerId}");
        }

        public void OnPlayerMoved(PlayerMovedEvent eventData)
        {
            // 处理玩家移动事件
            var position = new Vector3(eventData.X, eventData.Y, eventData.Z);
            // 更新玩家位置
        }
    }
}
```

### 3. Unity 战斗客户端示例

```csharp
public class BattleClient
{
    private IPulseClient _pulseClient;
    private IBattleService _battleService;
    private IBattleEvents _battleEvents;

    public async Task<bool> ConnectToBattleServerAsync(string address)
    {
        // 创建 PulseRPC 客户端
        _pulseClient = PulseClientBuilder.Create()
            .AddKcp("KcpChannel", address, 7001)
            .AddTcp("TcpChannel", address, 7000)
            .WithTimeout(TimeSpan.FromSeconds(10))
            .Build();

        await _pulseClient.ConnectAsync();

        // 获取战斗服务
        _battleService = await _pulseClient.GetServiceAsync<IBattleService>();

        // 注册战斗事件监听器
        _battleEvents = new BattleEventsImpl();
        await _pulseClient.RegisterEventListenerAsync<IBattleEvents>(_battleEvents);

        return true;
    }

    public async Task<bool> JoinBattleAsync(string battleType)
    {
        var request = new JoinBattleRequest
        {
            PlayerId = GameManager.Instance.PlayerId,
            BattleType = battleType,
            Parameters = new Dictionary<string, object>()
        };

        var response = await _battleService.JoinBattleAsync(request);
        return response.Success;
    }

    public async Task UseSkillAsync(string skillId, int targetId, Vector3 position)
    {
        var request = new UseSkillRequest
        {
            PlayerId = GameManager.Instance.PlayerId,
            SkillId = skillId,
            TargetId = targetId,
            Position = position,
            Timestamp = DateTime.UtcNow
        };

        var result = await _battleService.UseSkillAsync(request);
        if (!result.Success)
        {
            Debug.LogError($"Skill use failed: {result.ErrorMessage}");
        }
    }

    // 战斗事件监听器实现
    private class BattleEventsImpl : IBattleEvents
    {
        public void OnBattleStateUpdate(BattleStateUpdateEvent eventData)
        {
            // 处理战斗状态更新
            Debug.Log($"Battle state updated: {eventData.BattleId}");
        }

        public void OnSkillUsed(SkillUsedEvent eventData)
        {
            // 处理技能使用事件
            Debug.Log($"Skill used: {eventData.SkillId} by {eventData.PlayerId}");
        }

        public void OnDamageDealt(DamageDealtEvent eventData)
        {
            // 处理伤害事件
            Debug.Log($"Damage dealt: {eventData.Damage} to {eventData.TargetId}");
        }

        public void OnPlayerDefeated(PlayerDefeatedEvent eventData)
        {
            // 处理玩家战败事件
            Debug.Log($"Player defeated: {eventData.PlayerId}");
        }
    }
}
```

## API 版本管理

### 1. 版本策略

- **URL 版本控制**: `/api/v1/auth/login`, `/api/v2/auth/login`
- **向后兼容**: 保持旧版本 API 至少 6 个月
- **废弃通知**: 在响应头中添加废弃警告

### 2. 版本映射

```http
# HTTP API
GET /api/v1/zones          # 版本 1.0
GET /api/v2/zones          # 版本 2.0 (新增字段)

# PulseRPC 版本通过 ServiceContract 属性控制
[ServiceContract(Version = "1.0")]
public interface IPlayerService_V1 : IService<IPlayerService_V1>

[ServiceContract(Version = "2.0")]
public interface IPlayerService_V2 : IService<IPlayerService_V2>
```

## API 安全设计

### 1. 认证安全

- JWT Token 使用 RS256 签名算法
- Token 有效期为 1 小时，支持刷新机制
- 敏感操作需要二次验证

### 2. 传输安全

- 强制使用 HTTPS/TLS 1.3
- PulseRPC 连接启用 TLS 加密
- 敏感数据字段加密传输

### 3. 接口安全

- API 限流：每用户每分钟最多 60 次请求
- 参数验证：严格的输入验证和清理
- SQL 注入防护：使用参数化查询
- XSS 防护：输出编码和 CSP 头

---

本 API 设计确保了系统的安全性、可扩展性和易用性，为游戏客户端和服务端提供了完整的通信接口。
