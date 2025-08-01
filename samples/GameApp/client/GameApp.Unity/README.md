# GameApp Unity 客户端

这是 GameApp 的 Unity 游戏客户端项目。

## 项目结构

```
GameApp.Unity/
├── Assets/
│   ├── Scripts/                 # C# 脚本文件
│   │   ├── Network/            # 网络通信相关
│   │   │   ├── AuthClient.cs   # 认证客户端
│   │   │   ├── GameClient.cs   # 游戏客户端
│   │   │   ├── BattleClient.cs # 战斗客户端
│   │   │   └── NetworkManager.cs # 网络管理器
│   │   ├── UI/                 # 用户界面
│   │   │   ├── LoginUI.cs      # 登录界面
│   │   │   ├── GameUI.cs       # 游戏界面
│   │   │   ├── BattleUI.cs     # 战斗界面
│   │   │   └── MessageUI.cs    # 消息界面
│   │   ├── Game/               # 游戏逻辑
│   │   │   ├── Player.cs       # 玩家逻辑
│   │   │   ├── World.cs        # 世界逻辑
│   │   │   └── Battle.cs       # 战斗逻辑
│   │   ├── Managers/           # 管理器
│   │   │   ├── GameManager.cs  # 游戏管理器
│   │   │   ├── UIManager.cs    # UI 管理器
│   │   │   └── AudioManager.cs # 音频管理器
│   │   └── Utils/              # 工具类
│   │       ├── JsonHelper.cs   # JSON 工具
│   │       └── ConfigHelper.cs # 配置工具
│   ├── Scenes/                 # 场景文件
│   │   ├── LoginScene.unity    # 登录场景
│   │   ├── GameScene.unity     # 游戏场景
│   │   └── BattleScene.unity   # 战斗场景
│   ├── Prefabs/                # 预制件
│   │   ├── UI/                 # UI 预制件
│   │   ├── Characters/         # 角色预制件
│   │   └── Effects/            # 特效预制件
│   ├── Resources/              # 资源文件
│   └── StreamingAssets/        # 流数据资源
├── Packages/                   # 包管理
│   └── manifest.json           # 包清单
└── ProjectSettings/            # 项目设置
```

## 依赖包

- **PulseRPC.Client.Unity**: PulseRPC Unity 客户端
- **MemoryPack**: 序列化库
- **UniTask**: 异步编程支持
- **UniRx**: 响应式编程

## 开发说明

### 1. 网络通信

客户端使用 PulseRPC 与服务器进行通信：

```csharp
// 创建 PulseRPC 客户端
_pulseClient = PulseClientBuilder.Create()
    .AddTcp("TcpChannel", serverAddress, 7000)
    .AddKcp("KcpChannel", serverAddress, 7001)
    .Build();

// 获取服务代理
_playerService = await _pulseClient.GetServiceAsync<IPlayerService>();

// 注册事件监听器
_playerEvents = new PlayerEventsImpl();
await _pulseClient.RegisterEventListenerAsync<IPlayerEvents>(_playerEvents);
```

### 2. 认证流程

1. **HTTP 登录**: 使用 HTTP API 进行用户认证
2. **获取 GameTicket**: 登录成功后获取游戏票据
3. **连接游戏服务器**: 使用 GameTicket 连接 GameServer
4. **事件订阅**: 订阅游戏事件推送

### 3. 场景管理

- **LoginScene**: 用户登录和区服选择
- **GameScene**: 主游戏世界
- **BattleScene**: 战斗场景

### 4. 状态管理

使用单例模式的 GameManager 管理全局游戏状态：

```csharp
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public int PlayerId { get; set; }
    public string CurrentWorldId { get; set; }
    public PlayerInfo PlayerInfo { get; set; }

    // 游戏状态管理
    public GameState CurrentState { get; set; }
}
```

## 构建说明

### 开发构建

1. 确保服务端正在运行
2. 在 Unity 中打开项目
3. 运行 LoginScene 进行测试

### 发布构建

1. 配置构建设置
2. 选择目标平台
3. 构建并发布

## 配置文件

客户端配置文件位于 `StreamingAssets/config.json`：

```json
{
  "serverConfig": {
    "authServerUrl": "https://auth.gameapp.com",
    "gameServerAddress": "game.gameapp.com",
    "battleServerAddress": "battle.gameapp.com"
  },
  "clientConfig": {
    "version": "1.0.0",
    "platform": "PC",
    "enableLogging": true,
    "logLevel": "Info"
  }
}
```

## 测试说明

### 单元测试

位于 `Assets/Tests/` 目录，使用 Unity Test Runner 运行。

### 集成测试

需要启动完整的服务端环境进行测试。

## 注意事项

1. **网络异常处理**: 客户端需要处理网络断线、重连等异常情况
2. **资源管理**: 合理管理内存和资源，避免内存泄漏
3. **性能优化**: 注意帧率和内存使用，特别是在移动设备上
4. **安全性**: 不要在客户端存储敏感信息，所有重要逻辑在服务端验证

## 联系方式

如有问题请联系开发团队或查看项目文档。
