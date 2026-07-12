# IPulseHub 统一架构使用指南

## 概述

从本版本开始，PulseRPC 统一使用 `IPulseHub` 作为所有远程接口的标记接口，通过 `ChannelAttribute` 和 `AuthorizeAttribute` 来声明式地配置提供者来源和认证方式。

旧 `IPulseReceiver` 已从公共 API 硬移除。旧源码应改为 `[Channel("CLIENT")] : IPulseHub`；客户端与服务端 Source Generator 都会针对旧继承写法给出迁移诊断和 CodeFix。

## 核心概念

### 1. IPulseHub - 统一标记接口

所有远程调用接口（无论是客户端调用服务器，还是服务器推送客户端）都继承 `IPulseHub`。

```csharp
public interface IPulseHub
{
    // 所有远程服务都应继承此接口
}
```

### 2. ChannelAttribute - 指定提供者来源

使用 `ChannelAttribute` 标记接口由哪个角色提供：

```csharp
[Channel("CLIENT")]          // 客户端实现（服务器推送给客户端）
[Channel("GameServer")]      // 游戏服务器提供
[Channel("BattleServer")]    // 战斗服务器提供
[Channel("BackendServer")]   // 后台服务器提供
[Channel("CustomService")]   // 自定义服务名称
```

### 3. AuthorizeAttribute - 声明认证要求

使用 `AuthorizeAttribute` 和 `RoleTypes` 常量来控制访问权限：

```csharp
// 使用预定义角色
[Authorize(Role = RoleTypes.External)]  // 外部玩家认证
[Authorize(Role = RoleTypes.Internal)]  // 服务器间内部调用
[Authorize(Role = RoleTypes.GM)]        // GM 管理员
[Authorize(Role = RoleTypes.System)]    // 系统级调用

// 使用自定义角色
[Authorize(Role = "CustomAdminRole")]

// 允许匿名访问
[AllowAnonymous]
```

### 4. RoleTypes - 预定义角色常量

```csharp
public static class RoleTypes
{
    public const string External = "External";    // 外部玩家
    public const string Internal = "Internal";    // 服务器间调用
    public const string GM = "GM";                // GM管理员
    public const string System = "System";        // 系统调用
    public const string Anonymous = "Anonymous";  // 匿名访问
}
```

**扩展性**：`RoleTypes` 使用字符串常量而非枚举，允许用户定义自己的角色类型，不受框架限制。

## 使用场景

### 场景 1: 客户端调用服务器接口

**游戏服务器提供的玩家接口**：

```csharp
/// <summary>
/// 游戏服务 Hub - 由 GameServer 提供，客户端调用
/// </summary>
[Channel("GameServer")]
[Authorize(Role = RoleTypes.External)]  // 接口级别认证
public interface IGameHub : IPulseHub
{
    /// <summary>
    /// 登录接口 - 允许匿名访问
    /// </summary>
    [AllowAnonymous]  // 方法级别覆盖接口认证
    Task<LoginResponse> LoginAsync(LoginRequest request);

    /// <summary>
    /// 获取角色信息 - 需要玩家认证（继承接口级别认证）
    /// </summary>
    Task<CharacterInfo> GetCharacterAsync(string characterId);

    /// <summary>
    /// GM 操作 - 需要 GM 权限
    /// </summary>
    [Authorize(Role = RoleTypes.GM)]  // 方法级别认证，覆盖接口认证
    Task<bool> KickPlayerAsync(string playerId, string reason);
}
```

**关键点**：
- `[Channel("GameServer")]` 表示这个接口由 GameServer 实现
- `[Authorize(Role = RoleTypes.External)]` 表示默认需要玩家认证
- `[AllowAnonymous]` 允许特定方法匿名访问（如登录）
- 方法级别的 `[Authorize]` 可以覆盖接口级别的认证设置

### 场景 2: 服务器推送客户端

**服务器向客户端推送消息的接口**：

```csharp
/// <summary>
/// 游戏事件接收器 - 客户端实现，服务器推送
/// </summary>
[Channel("CLIENT")]
public interface IGameReceiver : IPulseHub
{
    /// <summary>
    /// 匹配成功通知
    /// </summary>
    Task OnMatchFoundAsync(MatchFoundNotification notification);

    /// <summary>
    /// 玩家被踢出通知
    /// </summary>
    Task OnKickedAsync(string reason);

    /// <summary>
    /// 系统公告 - 无需认证也可以接收
    /// </summary>
    [AllowAnonymous]
    Task OnSystemAnnouncementAsync(string announcement);
}
```

**关键点**：
- `[Channel("CLIENT")]` 表示这个接口由客户端实现
- 服务器可以通过此接口向客户端推送消息
- 可以灵活配置哪些推送需要认证，哪些不需要

### 场景 3: 服务器之间的调用

**GameServer 调用 BattleServer**：

```csharp
/// <summary>
/// 战斗服务器接口 - 提供给其他服务器调用
/// </summary>
[Channel("BattleServer")]
[Authorize(Role = RoleTypes.Internal)]  // 只允许服务器间调用
public interface IBattleHub : IPulseHub
{
    /// <summary>
    /// 创建战斗房间
    /// </summary>
    Task<BattleRoom> CreateBattleRoomAsync(CreateBattleRoomRequest request);

    /// <summary>
    /// 结束战斗
    /// </summary>
    Task<BattleResult> EndBattleAsync(string battleId);

    /// <summary>
    /// 系统调度战斗（需要系统级权限）
    /// </summary>
    [Authorize(Role = RoleTypes.System)]
    Task<bool> ScheduleBattleAsync(ScheduleBattleRequest request);
}
```

**关键点**：
- `[Channel("BattleServer")]` 表示由 BattleServer 提供
- `[Authorize(Role = RoleTypes.Internal)]` 限制只有服务器可以调用
- 可以进一步细化到系统级别的权限控制

### 场景 4: 混合场景 - 客户端和服务器都可调用

**聊天服务 - 客户端和其他服务器都可以调用**：

```csharp
/// <summary>
/// 聊天服务接口
/// </summary>
[Channel("ChatServer")]
public interface IChatHub : IPulseHub
{
    /// <summary>
    /// 发送消息 - 玩家调用
    /// </summary>
    [Authorize(Role = RoleTypes.External)]
    Task<bool> SendMessageAsync(string channelId, string content);

    /// <summary>
    /// 系统广播 - 服务器调用
    /// </summary>
    [Authorize(Role = RoleTypes.Internal)]
    Task<bool> BroadcastSystemMessageAsync(string content);

    /// <summary>
    /// GM 禁言操作
    /// </summary>
    [Authorize(Role = RoleTypes.GM)]
    Task<bool> MutePlayerAsync(string playerId, int durationMinutes);
}
```

**关键点**：
- 接口不设置统一认证，每个方法根据调用者设置不同的认证要求
- 支持多种角色类型访问同一个服务

### 场景 5: 自定义角色类型

**使用自定义角色扩展框架**：

```csharp
// 定义自己的角色常量
public static class CustomRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Moderator = "Moderator";
    public const string VIP = "VIP";
}

/// <summary>
/// VIP 服务接口
/// </summary>
[Channel("VIPServer")]
[Authorize(Role = CustomRoles.VIP)]
public interface IVIPHub : IPulseHub
{
    /// <summary>
    /// VIP 专属功能
    /// </summary>
    Task<VIPReward> GetDailyVIPRewardAsync();

    /// <summary>
    /// 超级管理员操作
    /// </summary>
    [Authorize(Role = CustomRoles.SuperAdmin)]
    Task<bool> ResetServerDataAsync();
}
```

**关键点**：
- 不受 `RoleTypes` 预定义常量限制
- 可以定义任意自定义角色字符串
- 框架完全支持扩展

## 迁移指南

### 迁移旧 Receiver 契约

`IPulseReceiver` 不再存在于运行时程序集，旧源码不能依赖二进制兼容层。保留接口名和方法签名，只把契约改为当前方向声明：

```csharp
[Channel("CLIENT")]
public interface IGameReceiver : IPulseHub
{
    Task OnMatchFoundAsync(MatchFoundNotification notification);
}
```

Source Generator 的迁移分析器会在旧继承语法上报告诊断，并可用 CodeFix 完成这项机械改写；迁移后重新生成客户端 Dispatcher 与服务端推送代理。

## 配置与线程调度

### ServiceName 配置

`ChannelAttribute` 还支持配置 `ServiceName` 用于线程调度：

```csharp
[Channel("GameServer", ServiceName = "PlayerService")]
public interface IPlayerHub : IPulseHub
{
    Task<PlayerInfo> GetPlayerInfoAsync(string playerId);
}
```

**用途**：
- 相同 `ServiceName` 的服务实例会在同一线程内按序执行
- 用于保证线程安全和状态一致性
- ServiceName 线程调度规范曾记录在 `specs/001-channelattribute-servicename-ipulsehub/spec.md`，该旧 spec 当前仓库未提供。

## 最佳实践

### 1. 接口命名约定

```csharp
IXxxHub      - 服务器提供的接口（客户端或其他服务器调用）
IXxxReceiver - 客户端实现的接口（服务器推送，建议迁移到 Hub 命名）
```

### 2. 认证策略

- **接口级别**: 设置默认认证要求
- **方法级别**: 覆盖接口认证，实现细粒度控制
- **显式标记**: 即使允许匿名，也显式标记 `[AllowAnonymous]`

### 3. 通道命名

```csharp
"CLIENT"         - 客户端实现的接口
"GameServer"     - 游戏服务器
"BattleServer"   - 战斗服务器
"BackendServer"  - 后台服务器
```

建议使用清晰、一致的命名约定。

### 4. 角色设计

```csharp
// 推荐：使用常量类管理角色
public static class GameRoles
{
    public const string Player = RoleTypes.External;
    public const string Admin = RoleTypes.GM;
    public const string ServerInternal = RoleTypes.Internal;

    // 自定义角色
    public const string TrialPlayer = "TrialPlayer";
    public const string PremiumPlayer = "PremiumPlayer";
}
```

## 代码生成器支持

### 客户端生成器

客户端代码生成器会自动识别 `ChannelAttribute` 和 `AuthorizeAttribute`，生成相应的代理代码。

### 服务端生成器

服务端代码生成器会根据认证特性生成认证检查逻辑。

## 示例项目

完整的示例代码请参考：
- [DistributedGameApp](../../samples/DistributedGameApp/)
- [ChatApp](../../samples/ChatApp/)

## 总结

统一使用 `IPulseHub` 后的优势：

1. **架构统一**: 所有远程接口使用同一个标记接口
2. **配置清晰**: 通过特性声明式配置，一目了然
3. **灵活扩展**: 支持自定义角色类型，不受框架限制
4. **向后兼容**: 现有代码无需立即修改
5. **类型安全**: 编译时检查，减少运行时错误

---

**相关文档**：
- ChannelAttribute ServiceName 规范 - 旧 spec 当前仓库未提供
- [认证与授权指南](../guides/authentication.md)
- [服务间通信指南](../archive/historical-design-notes/cross-service-communication-guide.md)
