# GameApp Unity 战斗客户端

🎮 **Unity战斗客户端现已完成！**

## 🆕 新增战斗系统组件

### 核心文件
- **`Network/BattleClient.cs`**: 与 BattleServer 的 PulseRPC 通信
- **`Network/BattleEventsImpl.cs`**: 战斗事件监听器实现
- **`Managers/BattleManager.cs`**: 统一管理战斗逻辑
- **`Managers/BattleSceneController.cs`**: 3D战斗场景管理
- **`Managers/GameLauncher.cs`**: 游戏启动和初始化管理器
- **`UI/BattleUIController.cs`**: 战斗界面控制器
- **`Utils/GameConfig.cs`**: 可配置的游戏设置

## ✨ 战斗功能特性

- ✅ **实时战斗**: KCP 低延迟通信，支持 PVP/PVE 模式
- ✅ **技能系统**: 技能学习、使用、冷却管理
- ✅ **伤害系统**: 实时伤害显示和特效
- ✅ **移动系统**: 点击移动和位置同步
- ✅ **事件系统**: 服务器推送战斗事件的实时处理
- ✅ **UI系统**: 完整的战斗界面和状态显示
- ✅ **3D场景**: 玩家对象、特效、伤害数字显示

## 🌐 网络架构

```
Unity Client
    ├── AuthClient (HTTP) → AuthServer:8080
    ├── GameClient (PulseRPC) → GameServer:9000/9001
    └── 🆕 BattleClient (PulseRPC) → BattleServer:8000/8001
```

**BattleServer 通信协议**:
- **KCP 通道**: 优先用于低延迟战斗 (技能使用、移动)
- **TCP 通道**: 用于可靠传输 (技能学习、战斗信息查询)

## 🚀 快速使用指南

### 1. 设置 GameConfig
1. 在 Unity 中创建 GameConfig ScriptableObject
2. 配置 BattleServer 地址: `localhost:8000/8001`
3. 设置玩家参数和战斗设置

### 2. 场景设置
1. 添加 GameLauncher 到场景中
2. 添加 BattleManager 组件
3. 设置 BattleUIController 和相关UI组件
4. 配置 BattleSceneController 和3D场景元素

### 3. 测试战斗功能
```csharp
// 加入PVP战斗
await battleManager.JoinBattleAsync("pvp");

// 使用技能
await battleManager.UseSkillAsync(skillId, targetPosition);

// 移动玩家
await battleManager.MoveToBattlePositionAsync(newPosition);

// 离开战斗
await battleManager.LeaveBattleAsync();
```

## 🎯 主要组件说明

### BattleClient
- 连接 BattleServer (KCP + TCP 双通道)
- 处理技能使用、移动、战斗状态
- 监听实时战斗事件

### BattleManager
- 统一的战斗逻辑管理接口
- 自动处理连接状态和错误
- 提供简单易用的API调用

### BattleSceneController
- 管理3D战斗场景中的玩家对象
- 播放技能特效和伤害数字
- 处理玩家移动和视觉反馈

### BattleUIController
- 完整的战斗UI界面
- 实时状态显示和按钮控制
- 战斗日志和聊天功能

### GameLauncher
- 统一的游戏初始化管理
- 自动连接所有服务器
- 场景切换和状态管理

## 🎮 测试功能

各组件提供了 Context Menu 测试功能：

**BattleManager**:
- Test Join PVP Battle
- Test Leave Battle
- Test Use Skill

**GameLauncher**:
- Test Authentication
- Test Battle Scene
- Show Connection Status

## ⚙️ 配置选项

### GameConfig 设置
- Battle Server 地址和端口
- 技能冷却时间: 1.0秒
- 移动速度: 5.0 单位/秒
- 血量/法力值上限

### 环境变量支持
- `GAMEAPP_BATTLE_SERVER_ADDRESS`
- `GAMEAPP_BATTLE_SERVER_TCP_PORT`
- `GAMEAPP_BATTLE_SERVER_KCP_PORT`

## 🔧 开发提示

### 添加新技能
1. 在 IBattleService.cs 中定义技能数据
2. 在 BattleSceneController 中添加特效
3. 在 BattleUIController 中更新按钮

### 性能优化
- 战斗使用 KCP 减少延迟
- 合理使用对象池管理特效
- 监控网络连接状态

### 调试技巧
- 查看 Unity Console 战斗日志
- 使用 Context Menu 快速测试
- 监控 GameConfig.ShowDebugInfo

## 🎊 完成状态

**Unity 战斗客户端开发完成！** 现在您拥有一个功能完整的实时战斗系统，支持：

- 🎯 低延迟 PVP/PVE 战斗
- ⚔️ 技能学习和使用系统
- 💥 实时伤害和特效显示
- 🏃 玩家移动和位置同步
- 📱 完整的战斗UI界面
- 🎮 3D战斗场景管理

您可以立即开始测试和自定义您的战斗系统！
