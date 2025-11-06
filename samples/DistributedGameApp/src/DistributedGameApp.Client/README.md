# DistributedGameApp Client

基于 PulseRPC 的分布式游戏客户端示例程序。

## 功能特性

该客户端演示了以下 PulseRPC 功能：

1. **客户端连接管理**
   - 使用 `PulseClientBuilder` 创建和配置客户端
   - 支持 TCP/KCP 传输协议
   - 自动连接重连和心跳

2. **服务调用**
   - 通过源生成器自动生成的代理调用服务器方法
   - 支持异步调用模式
   - 类型安全的 RPC 调用

3. **事件接收**
   - 实现接收器接口处理服务器推送事件
   - 支持多种事件类型（玩家、聊天室、战斗、游戏）
   - 实时消息通知

4. **交互式命令行**
   - 提供完整的命令行界面
   - 支持所有游戏功能操作
   - 彩色控制台输出

## 构建

```bash
cd samples/DistributedGameApp/src/DistributedGameApp.Client
dotnet build
```

## 运行

```bash
# 连接到默认服务器 (localhost:8080)
dotnet run

# 连接到指定服务器
dotnet run <host> <port>
```

## 使用示例

### 1. 账号管理

```
# 登录账号
> login myaccount mypassword

# 创建角色
> create MyCharacter Warrior Male
```

### 2. 玩家操作

```
# 查看状态
> info

# 获取玩家信息
> player

# 移动到指定位置
> move 100 0 200
```

### 3. 聊天室

```
# 加入聊天室
> join room001 PlayerName

# 发送消息
> say Hello, World!
```

### 4. 匹配与战斗

```
# 开始匹配
> match OneVsOne

# 加入战斗
> battle battle-id-123

# 准备就绪
> ready
```

## 命令列表

### 账号管理
- `login <账号> <密码>` - 登录账号
- `create <名称> <职业> <性别>` - 创建角色
  - 职业: Warrior, Mage, Archer, Assassin, Priest
  - 性别: Male, Female

### 玩家操作
- `info` / `status` - 显示客户端状态
- `player` / `playerinfo` - 获取玩家信息
- `move <x> <y> <z>` - 移动到指定位置

### 聊天室
- `join <房间ID> <玩家名称>` - 加入聊天室
- `say` / `chat <消息>` - 发送聊天消息

### 匹配与战斗
- `match <模式>` - 开始匹配
  - 模式: OneVsOne, ThreeVsThree, FiveVsFive
- `battle <战斗ID>` - 加入战斗
- `ready` - 战斗准备

### 其他
- `help` / `h` - 显示帮助
- `exit` / `quit` - 退出程序

## 架构说明

### 核心组件

#### DistributedGameClient
主客户端类，负责：
- 管理与服务器的连接
- 提供高级 API 封装
- 处理客户端状态
- 协调各个服务代理

#### GameEventHandler
事件处理器，实现：
- `IPlayerReceiver` - 玩家事件
- `IChatRoomReceiver` - 聊天室事件
- `IBattleReceiver` - 战斗事件
- `IGameReceiver` - 游戏事件

### 源代码生成

客户端使用 PulseRPC 源生成器自动生成：
- 服务代理类（如 `IPlayerHubProxy`）
- 事件监听器注册代码
- 扩展方法 `GetServiceAsync<T>` 和 `RegisterEventListenerAsync<T>`

这些都通过 `[PulseClientGeneration]` 特性触发：

```csharp
[PulseClientGeneration(typeof(IPlayerHub))]
[PulseClientGeneration(typeof(IChatRoomHub))]
[PulseClientGeneration(typeof(IBattleHub))]
[PulseClientGeneration(typeof(IGameHub))]
public class DistributedGameClient
{
    // ...
}
```

### 事件流

1. 客户端初始化 → 连接到服务器
2. 注册事件监听器 → 准备接收服务器推送
3. 获取服务代理 → 准备调用服务器方法
4. 执行命令 → 调用服务器方法或接收事件
5. 关闭客户端 → 清理资源

## 注意事项

1. **服务器连接**
   - 确保服务器已启动并监听指定端口
   - 默认连接 `localhost:8080`

2. **源生成器**
   - 需要引用 `PulseRPC.Client.SourceGenerator` 作为 Analyzer
   - 修改接口后需要重新编译才能生成代理代码

3. **协议一致性**
   - 客户端和服务器必须使用相同的协议定义（Shared 项目）
   - 协议号由源生成器自动生成并保持一致

## 相关项目

- `DistributedGameApp.Server` - 游戏服务器
- `DistributedGameApp.Shared` - 共享协议定义
- `PulseRPC.Client` - PulseRPC 客户端库
- `PulseRPC.Client.SourceGenerator` - 源代码生成器

## 许可证

与 PulseRPC 项目相同
