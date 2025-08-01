# 🎮 GameApp - 分布式游戏系统

[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![Unity](https://img.shields.io/badge/Unity-2022.3+-green.svg)](https://unity.com/)
[![PulseRPC](https://img.shields.io/badge/PulseRPC-Latest-orange.svg)](https://github.com/ChronosGames/PulseRPC)
[![License](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

GameApp 是一个基于 **PulseRPC** 框架的现代分布式游戏系统，展示了完整的游戏登录、认证、游戏世界和战斗系统的实现。

## 🚀 项目特色

- 🏗️ **微服务架构**: AuthServer + GameServer + BattleServer
- 🔥 **高性能通信**: 基于 PulseRPC 的 TCP/KCP 双通道
- ⚡ **高效序列化**: 使用 MemoryPack 优化数据传输
- 🎯 **Unity 支持**: 完整的 Unity 客户端实现
- 🐳 **容器化部署**: Docker + Docker Compose 开发环境
- 📊 **服务发现**: 基于 Consul 的动态服务注册
- 🔒 **安全认证**: JWT + GameTicket 双重认证机制

## 📋 系统架构

```mermaid
graph TB
    Client[Unity 客户端]
    LB[负载均衡器]
    Auth[AuthServer<br/>HTTP + JSON]
    Game[GameServer<br/>PulseRPC]
    Battle[BattleServer<br/>PulseRPC]

    MongoDB[(MongoDB)]
    Redis[(Redis)]
    Consul[Consul]

    Client -->|HTTP REST| LB
    Client -->|PulseRPC| LB
    LB --> Auth
    LB --> Game
    LB --> Battle

    Auth --> MongoDB
    Auth --> Redis
    Auth --> Consul
    Game --> MongoDB
    Game --> Redis
    Game --> Consul
    Battle --> MongoDB
    Battle --> Redis
    Battle --> Consul
```

### 核心组件

| 组件 | 技术栈 | 职责 |
|------|--------|------|
| **AuthServer** | ASP.NET Core + HTTP + JSON | 用户认证、区服管理、JWT Token |
| **GameServer** | PulseRPC + TCP/KCP | 游戏逻辑、玩家管理、世界同步 |
| **BattleServer** | PulseRPC + KCP | 实时战斗、技能系统、状态同步 |
| **Unity Client** | Unity 2022.3+ + PulseRPC | 游戏客户端、UI、网络通信 |

### 基础设施

| 服务 | 版本 | 用途 |
|------|------|------|
| **MongoDB** | 7.0 | 主数据库 - 用户数据、游戏数据 |
| **Redis** | 7.0 | 缓存 - 会话管理、实时数据 |
| **Consul** | 1.15 | 服务发现 - 动态注册、健康检查 |

## 🎯 核心功能

### 🔐 认证系统
- ✅ 用户注册/登录
- ✅ JWT Token 管理
- ✅ GameTicket 游戏票据
- ✅ 区服选择和负载均衡

### 🌍 游戏世界
- ✅ 角色创建与管理
- ✅ 世界状态同步
- ✅ 玩家位置更新
- ✅ 实时事件推送

### ⚔️ 战斗系统
- ✅ 实时战斗逻辑
- ✅ 技能系统
- ✅ 伤害计算
- ✅ 战斗状态同步

### 🎒 游戏系统
- ✅ 背包系统
- ✅ 装备系统
- ✅ 技能系统
- ✅ 社交系统

## 🚀 快速开始

### 环境要求

- **开发环境**: Windows 10/11, macOS, Linux
- **.NET 8 SDK**: [下载地址](https://dotnet.microsoft.com/download)
- **Docker Desktop**: [下载地址](https://www.docker.com/products/docker-desktop)
- **Unity 2022.3+**: [下载地址](https://unity.com/download)

### 1. 克隆项目

```bash
git clone https://github.com/your-org/GameApp.git
cd GameApp/samples/GameApp
```

### 2. 启动开发环境

```bash
# 方式1: 启动完整的游戏服务 (推荐)
./scripts/start-gameserver.sh

# 方式2: 分步启动基础设施服务
./scripts/start-dev.sh

# 查看服务状态
docker-compose -f docker/docker-compose.yml ps
```

### 3. 访问服务

| 服务 | 地址 | 说明 |
|------|------|------|
| **AuthServer** | http://localhost:8080 | 认证服务 HTTP API |
| **GameServer TCP** | tcp://localhost:7000 | 游戏服务器 TCP 通道 |
| **GameServer KCP** | udp://localhost:7001 | 游戏服务器 KCP 通道 |
| **Consul UI** | http://localhost:8500 | 服务发现管理界面 |
| **MongoDB** | mongodb://admin:dev_password@localhost:27017/gameapp_dev | 数据库连接 |
| **Redis** | redis://localhost:6379 | 缓存服务 (密码: dev_password) |

### 4. 构建和运行服务

```bash
# 构建解决方案
dotnet build GameApp.sln

# 运行 AuthServer
cd src/GameApp.AuthServer
dotnet run

# 在新终端运行 GameServer
cd src/GameApp.GameServer
dotnet run

# 在新终端运行 BattleServer (待实现)
# cd src/GameApp.BattleServer
# dotnet run
```

### 5. Unity 客户端

1. 使用 Unity 打开 `client/GameApp.Unity` 项目
2. 运行 LoginScene 场景
3. 进行登录测试

## 📖 文档

| 文档 | 描述 |
|------|------|
| [架构设计](docs/architecture.md) | 系统架构和技术选型 |
| [数据库设计](docs/database-design.md) | MongoDB 数据模型设计 |
| [API 设计](docs/api-design.md) | HTTP API 和 PulseRPC 接口 |
| [开发计划](docs/development-plan.md) | 详细开发时间表 |
| [部署计划](docs/deployment-plan.md) | 生产环境部署指南 |
| [进度报告](PROGRESS.md) | 当前开发进度 |

## 🛠️ 开发工作流

### 分支策略
- `main` - 主分支，稳定版本
- `develop` - 开发分支，功能集成
- `feature/*` - 功能分支
- `hotfix/*` - 热修复分支

### 开发流程
1. 从 `develop` 创建 `feature/*` 分支
2. 完成功能开发和测试
3. 提交 Pull Request 到 `develop`
4. 代码评审通过后合并
5. 定期从 `develop` 合并到 `main`

### 代码规范
- 遵循 .NET 编码规范
- 使用 EditorConfig 统一代码格式
- 必须编写单元测试
- API 必须有完整文档

## 🧪 测试

### 单元测试
```bash
# 运行所有测试
dotnet test

# 运行特定项目测试
dotnet test src/GameApp.AuthServer.Tests/
```

### 集成测试
```bash
# 启动测试环境
./scripts/start-dev.sh

# 运行集成测试
dotnet test tests/GameApp.IntegrationTests/
```

### Unity 测试
1. 在 Unity 中打开 Test Runner
2. 运行 PlayMode 和 EditMode 测试

## 📊 性能指标

### 目标性能
- **并发用户**: 1000+ 同时在线
- **API 响应时间**: < 200ms
- **战斗延迟**: < 50ms (KCP)
- **系统可用性**: 99.9%

### 监控指标
- 请求响应时间
- 并发连接数
- 内存使用率
- 数据库性能

## 🔧 故障排查

### 常见问题

#### Docker 服务启动失败
```bash
# 检查 Docker 状态
docker info

# 重置开发环境
./scripts/reset-dev.sh
```

#### 数据库连接失败
```bash
# 检查 MongoDB 状态
docker-compose -f docker/docker-compose.yml logs mongodb-dev

# 重新初始化数据库
docker-compose -f docker/docker-compose.yml restart mongodb-dev
```

#### Unity 连接失败
1. 检查服务器是否启动
2. 确认防火墙设置
3. 查看 Unity Console 错误日志

## 🤝 贡献指南

我们欢迎任何形式的贡献！

### 贡献方式
1. 🐛 **Bug 报告**: 提交 Issue 描述问题
2. 💡 **功能建议**: 提交 Feature Request
3. 🔧 **代码贡献**: 提交 Pull Request
4. 📚 **文档改进**: 完善项目文档

### 提交规范
- 使用清晰的提交信息
- 遵循代码规范
- 添加必要的测试
- 更新相关文档

## 📄 License

本项目使用 [MIT License](LICENSE) 开源协议。

## 🙏 致谢

- [PulseRPC](https://github.com/ChronosGames/PulseRPC) - 高性能 RPC 框架
- [MemoryPack](https://github.com/Cysharp/MemoryPack) - 高效序列化库
- Unity Technologies - 游戏引擎支持

## 📞 联系我们

- **项目主页**: https://github.com/your-org/GameApp
- **问题反馈**: https://github.com/your-org/GameApp/issues
- **开发团队**: gameapp-dev@example.com

---

**⭐ 如果这个项目对你有帮助，请给我们一个 Star！**
