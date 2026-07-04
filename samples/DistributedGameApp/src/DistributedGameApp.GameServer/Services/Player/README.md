# PlayerService 架构说明

## 📁 文件结构

```
Services/Player/
├── PlayerService.cs              # 主文件：基类、状态、生命周期
├── PlayerService.Player.cs       # partial: IPlayerHub 实现
├── PlayerHub.cs                  # 无状态 Hub（RPC 入口点）
├── PlayerServiceRegistration.cs  # DI 注册扩展方法
└── README.md                     # 本文档
```

## 🏗️ 架构设计

```
┌─────────────────────────────────────────────────────────────────────┐
│                         请求处理流程                                │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│   Client Request                                                    │
│        ↓                                                            │
│   ┌─────────────────────────────────────────┐                       │
│   │  PlayerHub (无状态)                      │                       │
│   │  - 参数验证                              │                       │
│   │  - 权限检查                              │                       │
│   │  - 路由到 PlayerService                 │                       │
│   └─────────────────────────────────────────┘                       │
│        ↓                                                            │
│   IContextualServiceAccessor<PlayerService>.GetCurrentAsync()       │
│        ↓                                                            │
│   ┌─────────────────────────────────────────┐                       │
│   │  PlayerService (有状态)                  │                       │
│   │  - 玩家数据                              │                       │
│   │  - 专属消息队列                          │                       │
│   │  - 业务逻辑                              │                       │
│   └─────────────────────────────────────────┘                       │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

## 🎯 核心特性

### PlayerService（有状态服务）

- **生命周期**: OnDemand（按需创建）
- **实例范围**: MultiInstance（每个玩家一个实例）
- **调度模式**: DedicatedQueue（专属队列，保证消息顺序）
- **空闲超时**: 10分钟无活动后回收

```csharp
[PulseService(
    StartupType = ServiceStartupType.OnDemand,
    InstanceScope = ServiceInstanceScope.MultiInstance,
    SchedulingMode = ServiceSchedulingMode.DedicatedQueue)]
public partial class PlayerService : PulseServiceBase
{
    // 玩家状态（只在队列线程中访问，无需加锁）
    private Character? _currentCharacter;
    private Position _position;
}
```

### PlayerHub（无状态 Hub）

- **职责**: 接收 RPC 请求，路由到 PlayerService
- **特点**: 无状态，可并发处理

```csharp
public class PlayerHub : IPlayerHub
{
    private readonly IContextualServiceAccessor<PlayerService> _playerService;

    public async Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        // 自动从上下文获取 PlayerId，在 Service 队列中执行
        return await _playerService.ExecuteCurrentAsync(
            service => service.GetPlayerInfoAsync());
    }
}
```

## 📝 注册方式

```csharp
// Program.cs
services.AddPlayerServices();
```

这一行代码会注册：
- `PlayerService` - 有状态服务
- `PlayerHub` - 无状态 Hub
- `IServiceAccessor<PlayerService>` - 服务访问器
- `IPlayerHub → PlayerService` 映射

## 🔄 与旧架构对比

| 特性 | 旧架构 (GameHub) | 新架构 (PlayerService + PlayerHub) |
|------|------------------|-----------------------------------|
| Hub 职责 | 既是 Hub 又是 Service | 仅做 RPC 入口 |
| 状态管理 | 在 Hub 中管理 | 在 Service 中管理 |
| 线程安全 | 手动管理 | 队列自动保证 |
| 代码组织 | 单文件 | partial class 分割 |
| 测试性 | 难以测试 | 易于单元测试 |

## 🧪 测试示例

```csharp
[Fact]
public async Task PlayerService_Should_Return_PlayerInfo()
{
    // Arrange
    var service = new PlayerService("player-123", logger, repository);
    await service.StartAsync();

    // Act - 直接测试 Service，无需 Mock Hub
    var info = await service.EnqueueAsync(() => service.GetPlayerInfoAsync());

    // Assert
    Assert.NotNull(info);
    Assert.Equal("player-123", info.PlayerId);
}
```

