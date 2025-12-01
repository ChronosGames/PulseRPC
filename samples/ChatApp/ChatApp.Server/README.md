# ChatApp 新架构示例

## 📁 目录结构

```
NewArchitecture/
├── README.md                    # 本文档
├── Contracts/
│   └── IChatRoomHub.cs          # Hub 接口定义（RPC 契约）
├── Services/
│   ├── ChatRoomService.cs       # 有状态服务（继承 UnifiedPulseServiceBase）
│   └── ChatRoomHub.cs           # 无状态 Hub（Singleton）
├── Authentication/
│   └── ChatAuthenticationHandler.cs  # 认证处理
└── Registration/
    └── ChatServiceRegistration.cs    # DI 注册
```

## 🏗️ 架构设计

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              新架构流程                                      │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────────────────────┐                                    │
│  │  IChatRoomHub (接口契约)             │  ← 定义 RPC 方法签名               │
│  │  - JoinRoomAsync()                  │                                    │
│  │  - SendMessageAsync()               │                                    │
│  │  - LeaveRoomAsync()                 │                                    │
│  └─────────────────────────────────────┘                                    │
│                    │                                                         │
│         ┌─────────┴─────────┐                                                │
│         ↓                   ↓                                                │
│  ┌──────────────┐    ┌──────────────────┐                                   │
│  │ ChatRoomHub  │    │ ChatRoomService  │                                   │
│  │ (无状态)     │ →  │ (有状态)         │                                   │
│  │ Singleton    │    │ 按 RoomId 缓存   │                                   │
│  │              │    │                  │                                   │
│  │ 路由 + 验证  │    │ 房间成员         │                                   │
│  │              │    │ 消息历史         │                                   │
│  │              │    │ 专属消息队列     │                                   │
│  └──────────────┘    └──────────────────┘                                   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## 🔄 请求流程

```
1. 客户端连接 → 服务器创建 Connection
2. 客户端调用 LoginAsync(token)
   → 验证 token，提取 userId
   → connection.SetAuthentication(userId)
3. 客户端调用 JoinRoomAsync("room-1")
   → ChatRoomHub.JoinRoomAsync()
   → 获取 ChatRoomService("room-1")
   → 在 Service 队列中执行加入逻辑
4. 后续调用 SendMessageAsync("hello")
   → 框架设置 RequestContext.Current.UserId
   → ChatRoomHub 自动找到对应的 ChatRoomService
   → 在 Service 队列中顺序执行
```

## ✅ 关键特性

- **Hub 无状态**：注册为 Singleton，全局复用
- **Service 有状态**：按 RoomId 缓存，每个房间一个实例
- **线程安全**：Service 使用专属队列，无需加锁
- **认证隔离**：RequestContext 携带当前用户信息

