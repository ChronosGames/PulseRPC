# DistributedGameApp.Client 交互式使用指南

## 完整流程演示

本指南将一步一步演示如何使用交互式命令行完成从登录到战斗的完整游戏流程。

## 前置条件

1. 确保 LoginServer 正在运行（默认 `http://localhost:5000`）
2. 确保至少有一个 GameServer 正在运行并注册到 Consul
3. 确保 BattleServer 正在运行（用于匹配后的战斗）

## 启动客户端

```bash
cd samples/DistributedGameApp/src/DistributedGameApp.Client
dotnet run
```

您将看到如下输出：

```
╔════════════════════════════════════════════════════════╗
║     DistributedGameApp 客户端                          ║
║     基于 PulseRPC 的分布式游戏                         ║
╚════════════════════════════════════════════════════════╝

LoginServer URL: http://localhost:5000
运行模式: interactive

欢迎使用 DistributedGameApp 客户端！
这是一个完整的分布式游戏客户端示例。

=== 命令列表 ===

【认证】
  register <用户名> <密码> <邮箱>  - 注册新账号
  login <用户名/邮箱> <密码>       - 登录

【服务器】
  servers | listservers           - 列出所有可用服务器
  recommend                       - 获取推荐服务器
  connect [索引]                  - 连接到服务器（不提供索引则连接推荐服务器）

【角色管理】
  characters | chars              - 显示角色列表
  create <名称> <职业> <性别>     - 创建新角色
    职业: Warrior, Mage, Archer, Assassin, Priest
    性别: Male, Female
  select <索引>                   - 选择角色
  delete <索引>                   - 删除角色

【匹配与战斗】
  match <模式>                    - 请求匹配
    模式: OneVsOne, ThreeVsThree, FiveVsFive
  ready                           - 战斗准备
  leave | leavebattle             - 离开战斗

【其他】
  status | info                   - 显示客户端状态
  clear | cls                     - 清屏
  help | h | ?                    - 显示帮助
  exit | quit | q                 - 退出程序

>
```

## 完整流程步骤

### 步骤 1: 注册新账号（首次使用）

```
> register testuser password123 test@example.com
```

**预期输出**:
```
正在注册用户: testuser
注册成功: testuser (xxxxx-xxxx-xxxx-xxxx)
```

**说明**: 如果账号已存在，会提示用户名已存在，可以直接跳到步骤 2 登录。

### 步骤 2: 登录

```
> login testuser password123
```

**预期输出**:
```
正在登录: testuser
登录成功: testuser (xxxxx-xxxx-xxxx-xxxx)
```

### 步骤 3: 查看可用的游戏服务器

```
> servers
```

**预期输出**:
```
正在获取游戏服务器列表
成功获取 2 个游戏服务器

可用服务器 (2):
  [1] GameServer-1
      地址: localhost:8080
      负载: 5/1000 (0%)
      状态: Online
  [2] GameServer-2
      地址: localhost:8082
      负载: 3/1000 (0%)
      状态: Online

>
```

### 步骤 4: 获取推荐服务器（可选）

```
> recommend
```

**预期输出**:
```
正在获取推荐的游戏服务器
成功获取推荐服务器: GameServer-1

推荐服务器: GameServer-1
  地址: localhost:8080
  负载: 5/1000 (0%)

>
```

### 步骤 5: 连接到游戏服务器

**方式 1: 连接到推荐服务器**
```
> connect
```

**方式 2: 通过索引连接**
```
> connect 1
```

**预期输出**:
```
正在连接到游戏服务器: GameServer-1 (localhost:8080)
成功连接到游戏服务器: GameServer-1
>
```

### 步骤 6: 查看客户端状态

```
> status
```

**预期输出**:
```
=== 游戏客户端状态 ===
用户: testuser (xxxxx-xxxx-xxxx-xxxx)
已登录: True

当前游戏服务器: GameServer-1
  地址: localhost:8080
  状态: 已连接
  负载: 5/1000 (0%)

已连接服务器数: 1

>
```

### 步骤 7: 查看角色列表

```
> characters
```

**预期输出（如果没有角色）**:
```
获取到 0 个角色

角色列表 (0):

>
```

### 步骤 8: 创建新角色

```
> create MyWarrior Warrior Male
```

**预期输出**:
```
正在创建角色: MyWarrior, 职业: Warrior, 性别: Male
成功创建角色: MyWarrior (char-xxxxx-xxxx-xxxx-xxxx)
>
```

**职业选项**: Warrior, Mage, Archer, Assassin, Priest
**性别选项**: Male, Female

### 步骤 9: 再次查看角色列表

```
> characters
```

**预期输出**:
```
获取到 1 个角色

角色列表 (1):
  [1] MyWarrior
      职业: Warrior  等级: 1
      HP: 100/100  攻击: 15  防御: 10

>
```

### 步骤 10: 选择角色进入游戏

```
> select 1
```

**预期输出**:
```
已选择角色: MyWarrior (等级 1)
>
```

### 步骤 11: 查看完整状态

```
> status
```

**预期输出**:
```
=== 游戏客户端状态 ===
用户: testuser (xxxxx-xxxx-xxxx-xxxx)
已登录: True

当前游戏服务器: GameServer-1
  地址: localhost:8080
  状态: 已连接
  负载: 6/1000 (0%)

当前角色: MyWarrior
  职业: Warrior
  等级: 1
  HP: 100/100
  攻击: 15 | 防御: 10

已连接服务器数: 1

>
```

### 步骤 12: 请求匹配

```
> match OneVsOne
```

**预期输出**:
```
开始匹配: 模式=OneVsOne, 队伍大小=1
匹配请求成功，票据ID: ticket-xxxxx-xxxx-xxxx-xxxx, 预计等待: 30秒
>
```

**匹配模式选项**: OneVsOne, ThreeVsThree, FiveVsFive

### 步骤 13: 等待匹配结果

当匹配成功时，您会看到以下通知（由事件处理器自动输出）:

```
[匹配] 找到对手! 战斗ID: battle-xxxxx-xxxx-xxxx-xxxx
       服务器: localhost:8100
       倒计时: 10秒
>
```

**说明**: 客户端会自动连接到 BattleServer

### 步骤 14: 加入战斗（在自动连接到 BattleServer 后）

```
> join battle-xxxxx-xxxx-xxxx-xxxx
```

**注意**: 使用步骤 13 中收到的实际 battleId

**预期输出**:
```
正在加入战斗: battle-xxxxx-xxxx-xxxx-xxxx
成功加入战斗: battle-xxxxx-xxxx-xxxx-xxxx, 状态: Waiting
>
```

**或者，如果 GameClient 已经自动处理了加入逻辑，直接进行准备**

### 步骤 15: 战斗准备

```
> ready
```

**预期输出**:
```
已准备就绪
>
```

您可能会看到以下事件通知:

```
[战斗] 战斗开始! ID: battle-xxxxx-xxxx-xxxx-xxxx
>
```

### 步骤 16: 战斗中（自动接收事件）

在战斗过程中，您会自动收到各种事件通知，例如:

```
[战斗] 玩家 player2 加入战斗 (队伍2)
>

[战斗] 回合 1 开始
>

[战斗] 动作: Attack, 来自: char-xxxxx
>

[战斗] 动作执行: Attack, 成功: True
>
```

### 步骤 17: 离开战斗

战斗结束后或需要提前离开时:

```
> leave
```

**预期输出**:
```
正在离开战斗...
已离开战斗
>
```

您可能会看到:

```
[战斗] 战斗结束! 胜利者: Team1
>
```

### 步骤 18: 查看最终状态

```
> status
```

**预期输出**:
```
=== 游戏客户端状态 ===
用户: testuser (xxxxx-xxxx-xxxx-xxxx)
已登录: True

当前游戏服务器: GameServer-1
  地址: localhost:8080
  状态: 已连接
  负载: 6/1000 (0%)

当前角色: MyWarrior
  职业: Warrior
  等级: 1
  HP: 100/100
  攻击: 15 | 防御: 10

已连接服务器数: 1

>
```

### 步骤 19: 退出程序

```
> exit
```

**预期输出**:
```
按任意键退出...
```

## 常用命令速查

### 认证流程
```bash
# 注册
> register username password email@example.com

# 登录
> login username password
```

### 服务器操作
```bash
# 查看所有服务器
> servers

# 获取推荐服务器
> recommend

# 连接到推荐服务器
> connect

# 连接到指定服务器（通过索引）
> connect 1
```

### 角色管理
```bash
# 查看角色列表
> characters

# 创建角色
> create HeroName Warrior Male

# 选择角色
> select 1

# 删除角色
> delete 2
```

### 匹配与战斗
```bash
# 开始匹配
> match OneVsOne

# 战斗准备
> ready

# 离开战斗
> leave
```

### 实用命令
```bash
# 查看状态
> status

# 清屏
> clear

# 帮助
> help

# 退出
> exit
```

## 完整流程命令序列

以下是一个完整的命令序列，可以直接按顺序输入：

```
register testuser password123 test@example.com
servers
connect
characters
create MyWarrior Warrior Male
select 1
status
match OneVsOne
# 等待匹配成功通知
ready
# 战斗中...
leave
status
exit
```

## 故障排查

### 问题 1: 登录失败
**现象**: `登录失败: Unauthorized`
**解决**:
- 检查 LoginServer 是否正在运行
- 确认用户名和密码正确
- 如果是首次使用，先使用 `register` 命令注册

### 问题 2: 服务器列表为空
**现象**: `可用服务器 (0):`
**解决**:
- 确认 GameServer 正在运行
- 检查 GameServer 是否成功注册到 Consul
- 访问 Consul UI (`http://localhost:8500`) 确认服务注册

### 问题 3: 连接服务器失败
**现象**: `连接到游戏服务器失败`
**解决**:
- 确认 GameServer 的端口正确（默认 8080）
- 检查防火墙设置
- 确认 GameServer 日志中没有错误

### 问题 4: 角色列表获取失败
**现象**: `未连接到游戏服务器`
**解决**:
- 先使用 `connect` 命令连接到 GameServer
- 使用 `status` 命令确认已连接

### 问题 5: 匹配超时
**现象**: 长时间没有匹配成功通知
**解决**:
- 确认 BattleServer 正在运行
- 检查匹配队列中是否有其他玩家
- 可以开启另一个客户端实例进行匹配测试

### 问题 6: 战斗相关命令失败
**现象**: `未连接到战斗服务器`
**解决**:
- 确保已经通过匹配成功连接到 BattleServer
- 检查是否收到了匹配成功通知
- 使用 `status` 命令确认连接状态

## 高级用法

### 多窗口测试

打开两个终端窗口，分别运行客户端：

**窗口 1**:
```bash
dotnet run
> login player1 password1
> connect
> create Warrior1 Warrior Male
> select 1
> match OneVsOne
```

**窗口 2**:
```bash
dotnet run
> login player2 password2
> connect
> create Mage1 Mage Female
> select 1
> match OneVsOne
```

两个玩家将被匹配到一起进行战斗。

### 测试不同职业

```bash
> create Tank Warrior Male
> create DPS Mage Female
> create Healer Priest Female
> create Ranger Archer Male
> create Ninja Assassin Male
```

### 测试角色删除

```bash
> characters
> delete 2  # 删除第2个角色
> characters  # 确认已删除
```

## 提示与技巧

1. **使用 Tab 补全**: 大多数终端支持 Tab 键补全命令（如果配置了）

2. **查看实时事件**: 所有游戏事件都会实时显示在控制台，注意观察彩色输出

3. **状态检查**: 经常使用 `status` 命令检查当前状态

4. **帮助信息**: 任何时候输入 `help` 查看命令列表

5. **清屏**: 使用 `clear` 命令清理控制台，保持整洁

6. **快速退出**: 使用 `q` 快速退出（等同于 `exit`）

## 总结

通过以上步骤，您已经完成了：
- ✅ 账号注册和登录
- ✅ 选择并连接游戏服务器
- ✅ 创建和选择角色
- ✅ 请求匹配
- ✅ 自动连接到战斗服务器
- ✅ 加入战斗并准备
- ✅ 离开战斗

这是一个完整的分布式游戏客户端流程！🎮

---

**文档版本**: 1.0.0
**最后更新**: 2025-11-18
