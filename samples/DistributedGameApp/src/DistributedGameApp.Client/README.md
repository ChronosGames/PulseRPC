# DistributedGameApp Client

基于 PulseRPC 的分布式游戏客户端示例程序。

## 🌟 新功能特性（V2.0）

该客户端现在支持高级的多服务器连接和管理功能：

### 1. **多服务器连接管理**
   - 同时连接到多个 GameServer 实例
   - 动态连接到 BattleServer 进行战斗
   - 灵活的服务器切换功能
   - 完整的连接生命周期管理

### 2. **服务器类型支持**
   - **GameServer**：游戏主服务器（角色管理、匹配请求）
   - **BattleServer**：战斗服务器（实时战斗、技能释放）
   - 自动识别和管理不同类型的服务器

### 3. **完整的战斗功能**
   - 发起匹配请求
   - 接收匹配通知并连接到 BattleServer
   - 执行战斗动作（攻击、技能）
   - 实时接收战斗事件

### 4. **客户端连接管理**
   - 使用 `PulseClientBuilder` 创建和配置客户端
   - 支持 TCP/KCP 传输协议
   - 通过 `ServerConnectionManager` 管理多个连接

### 5. **服务调用**
   - 通过源生成器自动生成的代理调用服务器方法
   - 支持异步调用模式
   - 类型安全的 RPC 调用

### 6. **事件接收**
   - 实现接收器接口处理服务器推送事件
   - 支持多种事件类型（玩家、聊天室、战斗、游戏）
   - 实时消息通知

### 7. **交互式命令行**
   - 提供完整的命令行界面
   - 支持所有游戏功能操作
   - 彩色控制台输出

## 构建

```bash
cd samples/DistributedGameApp/src/DistributedGameApp.Client
dotnet build
```

## 运行

### 交互模式

```bash
# 连接到默认服务器 (localhost:8080)
dotnet run

# 连接到指定服务器
dotnet run <host> <port>
```

### 🆕 场景模式

运行自动化场景，无需手动输入命令：

```bash
# 运行完整战斗流程场景
dotnet run -- --scenario fullbattle <账号> <角色名> <职业> [主机] [端口]

# 示例
dotnet run -- --scenario fullbattle player1 Warrior1 Warrior
dotnet run -- --scenario fullbattle player2 Mage1 Mage localhost 8080

# 查看可用场景
dotnet run -- --scenario
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

### 🆕 服务器管理
- `servers` / `listservers` - 列出所有已连接的服务器
- `switch <服务器ID>` - 切换到指定服务器
- `connectgame <服务器ID> <名称> <主机> <端口>` - 连接到额外的 GameServer
- `connectbattle <战斗ID> <主机> <端口>` - 连接到 BattleServer

### 玩家操作
- `info` / `status` - 显示客户端状态（包括当前服务器信息）
- `player` / `playerinfo` - 获取玩家信息（已废弃）
- `move <x> <y> <z>` - 移动到指定位置（已废弃）

### 聊天室
- `join <房间ID> <玩家名称>` - 加入聊天室（已废弃）
- `say` / `chat <消息>` - 发送聊天消息（已废弃）

### 匹配与战斗
- `match <模式>` - 开始匹配
  - 模式: OneVsOne, ThreeVsThree, FiveVsFive
- `battle <战斗ID>` - 加入战斗
- `ready` - 战斗准备
- 🆕 `attack <目标角色ID>` - 攻击目标
- 🆕 `skill <技能ID> <目标角色ID>` - 使用技能
- 🆕 `leave` / `leavebattle` - 离开战斗

### 🆕 场景模式
- `scenario fullbattle <账号> <角色名> <职业>` - 运行完整战斗流程场景
  - 自动执行登录、匹配、战斗、离开的完整流程
  - 职业: Warrior, Mage, Archer, Assassin, Priest

### 其他
- `help` / `h` - 显示帮助
- `exit` / `quit` - 退出程序

## 架构说明

### 核心组件

#### ServerConnectionManager
连接管理器，负责：
- 管理多个服务器连接（GameServer 和 BattleServer）
- 追踪当前活动的服务器
- 提供服务器切换功能
- 处理连接生命周期

#### DistributedGameClient
主客户端类，负责：
- 协调 ServerConnectionManager
- 提供高级 API 封装
- 处理客户端状态
- 封装业务逻辑

#### ServerConnection
服务器连接信息类，包含：
- 服务器标识和类型
- PulseClient 实例
- Hub 代理（GameHub 或 BattleHub）
- 连接状态和元数据

#### GameEventHandler
事件处理器，实现：
- `IPlayerReceiver` - 玩家事件（已废弃）
- `IChatRoomReceiver` - 聊天室事件（已废弃）
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

### 多服务器连接流程

```
1. 初始化客户端
   ↓
2. 连接到 GameServer1 (默认)
   ↓
3. 登录并创建角色
   ↓
4. [可选] 连接到更多 GameServer (负载均衡)
   ↓
5. 发起匹配请求
   ↓
6. 接收匹配通知（包含 BattleServer 地址）
   ↓
7. 连接到 BattleServer
   ↓
8. 加入战斗并执行战斗动作
   ↓
9. 战斗结束，切换回 GameServer
   ↓
10. 关闭客户端，清理所有连接
```

### 服务器切换示例

```
初始状态：
  - GameServer01 (当前) ← 登录、角色管理
  
连接更多服务器：
  - GameServer01 (当前)
  - GameServer02 ← 添加
  - GameServer03 ← 添加
  
切换到 GameServer02：
  - GameServer01
  - GameServer02 (当前) ← 切换
  - GameServer03
  
匹配成功后连接 BattleServer：
  - GameServer01
  - GameServer02
  - GameServer03
  - BattleServer-battle-123 (当前) ← 自动切换
  
战斗结束后：
  - GameServer01
  - GameServer02 (当前) ← 手动切换回
  - GameServer03
  - BattleServer-battle-123 ← 保持连接
```

## 注意事项

1. **服务器连接**
   - 确保服务器已启动并监听指定端口
   - 默认连接 `localhost:8080`
   - 支持同时连接多个服务器

2. **源生成器**
   - 需要引用 `PulseRPC.Client.SourceGenerator` 作为 Analyzer
   - 修改接口后需要重新编译才能生成代理代码

3. **协议一致性**
   - 客户端和服务器必须使用相同的协议定义（Shared 项目）
   - 协议号由源生成器自动生成并保持一致

4. **服务器切换**
   - 切换服务器时，需要确保目标服务器已连接
   - 切换后，当前上下文会切换到目标服务器
   - BattleServer 连接后会自动切换为当前服务器

5. **战斗流程**
   - 匹配成功后，需要手动连接到 BattleServer
   - 连接 BattleServer 时需要提供战斗ID、主机和端口
   - 战斗结束后可以切换回 GameServer

## 使用场景

### 场景1：多个 GameServer 负载均衡
详见：[Scenarios/SCENARIO1_MultipleGameServers.md](Scenarios/SCENARIO1_MultipleGameServers.md)

演示如何连接到多个 GameServer 并在它们之间切换，实现客户端侧的负载均衡。

### 场景2：完整的匹配和战斗流程
详见：[Scenarios/SCENARIO2_MatchingAndBattle.md](Scenarios/SCENARIO2_MatchingAndBattle.md)

演示从 GameServer 发起匹配，连接到 BattleServer 进行战斗，战斗结束后返回 GameServer 的完整流程。

### 场景3：压力测试
详见：[Scenarios/SCENARIO3_StressTest.md](Scenarios/SCENARIO3_StressTest.md)

提供压力测试脚本和方法，测试系统在高并发情况下的表现。

### 🆕 场景4：完整战斗流程自动化
详见：[Scenarios/SCENARIO4_FullBattleFlow.md](Scenarios/SCENARIO4_FullBattleFlow.md)

**自动化场景**，从登录到战斗结束的完整流程，无需手动输入命令。适用于：
- 功能验证和端到端测试
- 性能基准测试
- 自动化回归测试
- 快速演示系统功能

**快速开始**:
```bash
# 启动 GameServer 和 BattleServer（不同终端）
cd ../DistributedGameApp.GameServer && dotnet run
cd ../DistributedGameApp.BattleServer && dotnet run

# 启动两个客户端进行自动化测试
cd ../DistributedGameApp.Client
dotnet run -- --scenario fullbattle player1 Warrior1 Warrior
dotnet run -- --scenario fullbattle player2 Mage1 Mage
```

## 快速开始示例

### 示例1：连接到单个 GameServer

```bash
# 启动 GameServer
cd ../DistributedGameApp.GameServer
dotnet run

# 启动客户端（新终端）
cd ../DistributedGameApp.Client
dotnet run

# 在客户端中执行
> login testuser pass123
> create MyHero Warrior Male
> info
```

### 示例2：连接到多个 GameServer

```bash
# 启动3个 GameServer（不同终端）
dotnet run -- --tcp-port=8080  # GameServer1
dotnet run -- --tcp-port=8082  # GameServer2
dotnet run -- --tcp-port=8084  # GameServer3

# 启动客户端
dotnet run localhost 8080

# 在客户端中执行
> login testuser pass123
> servers
> connectgame GameServer02 GS2 localhost 8082
> connectgame GameServer03 GS3 localhost 8084
> servers
> switch GameServer02
> info
```

### 示例3：完整的战斗流程（手动）

```bash
# 启动所有服务器（不同终端）
cd ../DistributedGameApp.GameServer
dotnet run

cd ../DistributedGameApp.BattleServer
dotnet run

cd ../DistributedGameApp.BackendServer
dotnet run

# 启动两个客户端
cd ../DistributedGameApp.Client
dotnet run  # 客户端1

dotnet run  # 客户端2（新终端）

# 客户端1
> login player1 pass123
> create Warrior1 Warrior Male
> match OneVsOne
# 等待匹配...
# [匹配] 找到对手! 战斗ID: battle-xxx, 服务器: localhost:8100
> connectbattle battle-xxx localhost 8100
> battle battle-xxx
> ready
> attack char-002
> skill fireball char-002

# 客户端2（同时操作）
> login player2 pass456
> create Mage1 Mage Female
> match OneVsOne
# 等待匹配...
> connectbattle battle-xxx localhost 8100
> battle battle-xxx
> ready
> skill icebolt char-001
```

### 🆕 示例4：完整的战斗流程（自动化）

```bash
# 启动所有服务器（不同终端）
cd ../DistributedGameApp.GameServer
dotnet run

cd ../DistributedGameApp.BattleServer
dotnet run

# 启动两个客户端，自动执行完整流程
cd ../DistributedGameApp.Client
dotnet run -- --scenario fullbattle player1 Warrior1 Warrior &
dotnet run -- --scenario fullbattle player2 Mage1 Mage &

# 等待场景执行完成
wait

# 查看输出：
# ========================================
# 开始执行完整战斗流程场景
# 玩家账号: player1
# ========================================
# [步骤1] 登录到 GameServer
#   ✓ 登录成功
# [步骤2] 创建角色
#   ✓ 角色创建成功
# [步骤3] 请求匹配
#   ✓ 匹配请求已提交
# [步骤4] 等待匹配成功通知...
#   ✓ 匹配成功!
# [步骤5] 连接到 BattleServer
#   ✓ 连接成功
# [步骤6] 加入战斗
#   ✓ 成功加入战斗
# [步骤7] 战斗准备
#   ✓ 已准备就绪
# [步骤8] 执行战斗动作
#   攻击 #1: 成功! 伤害: 18
#   攻击 #2: 成功! 伤害: 20
#   ...
# [步骤9] 等待战斗结束...
#   ✓ 战斗已结束
# [步骤10] 离开 BattleServer
#   ✓ 已断开 BattleServer 连接
# ========================================
# 完整战斗流程场景执行完成
# ========================================
# 场景执行成功!
```

## 相关项目

- `DistributedGameApp.GameServer` - 游戏网关服务器
- `DistributedGameApp.BattleServer` - 战斗服务器
- `DistributedGameApp.BackendServer` - 后台服务器
- `DistributedGameApp.Shared` - 共享协议定义
- `PulseRPC.Client` - PulseRPC 客户端库
- `PulseRPC.Client.SourceGenerator` - 源代码生成器

## 故障排除

### 连接失败
- 检查服务器是否已启动
- 验证端口号是否正确
- 检查防火墙设置

### 切换服务器失败
- 确认目标服务器已连接（使用 `servers` 命令查看）
- 检查服务器ID是否正确

### 战斗相关错误
- 确保 BattleServer 已启动
- 验证战斗ID是否正确
- 检查是否已成功连接到 BattleServer

## 许可证

与 PulseRPC 项目相同
