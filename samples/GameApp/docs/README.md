# GameApp 游戏登录系统开发计划

## 项目概述

GameApp 是一个基于 PulseRPC 框架的分布式游戏系统，实现了完整的游戏登录、认证、游戏世界和战斗系统。项目架构采用微服务设计，支持高并发、高可用的游戏服务。

## 系统架构

### 客户端
- **Unity 游戏客户端**：基于 Unity 引擎的游戏客户端，支持多平台部署

### 服务端架构
- **AuthServer**：认证服务器，使用 HTTP + JSON 提供用户认证、注册、JWT Token 管理
- **GameServer**：游戏逻辑服务器，使用 PulseRPC 协议处理游戏业务逻辑
- **BattleServer**：战斗服务器，使用 PulseRPC 协议处理实时战斗逻辑

### 基础设施
- **MongoDB**：主数据库，存储用户数据、游戏数据、角色数据等
- **Redis**：缓存和会话管理，用于存储临时数据、缓存热点数据
- **Consul**：服务注册与发现，支持服务动态伸缩和负载均衡

## 核心功能

### 1. 用户认证系统
- 用户注册/登录
- JWT Token 管理
- 游戏票据 (GameTicket) 验证
- 多重认证支持

### 2. 游戏世界系统
- 角色创建与管理
- 游戏世界状态同步
- 玩家数据持久化
- 实时消息推送

### 3. 战斗系统
- 实时战斗逻辑
- 技能系统
- 伤害计算
- 战斗结果统计

### 4. 服务发现与负载均衡
- 基于 Consul 的服务注册
- 动态负载均衡
- 健康检查
- 故障转移

## 技术栈

### 后端技术
- **.NET 8**：服务端开发框架
- **PulseRPC**：高性能 RPC 通信框架
- **ASP.NET Core**：HTTP API 服务
- **MongoDB Driver**：数据库访问
- **StackExchange.Redis**：Redis 客户端
- **Consul.NET**：服务发现客户端

### 前端技术
- **Unity 2022.3+**：游戏引擎
- **PulseRPC.Client.Unity**：Unity RPC 客户端
- **UniTask**：异步编程支持
- **UniRx**：响应式编程

### 基础设施
- **Docker**：容器化部署
- **Kubernetes**：容器编排（可选）
- **MongoDB**：文档数据库
- **Redis**：内存数据库
- **Consul**：服务网格

## 开发阶段

### 第一阶段：基础架构搭建
1. 项目结构初始化
2. 基础设施环境搭建（MongoDB、Redis、Consul）
3. 服务端基础框架搭建
4. Unity 客户端基础框架搭建

### 第二阶段：认证系统开发
1. AuthServer HTTP API 开发
2. JWT Token 管理
3. 用户注册/登录功能
4. Unity 登录界面开发

### 第三阶段：游戏系统开发
1. GameServer PulseRPC 服务开发
2. 角色系统开发
3. 游戏世界基础功能
4. Unity 游戏界面开发

### 第四阶段：战斗系统开发
1. BattleServer PulseRPC 服务开发
2. 实时战斗逻辑
3. 技能系统
4. Unity 战斗界面开发

### 第五阶段：集成测试与优化
1. 系统集成测试
2. 性能优化
3. 错误处理完善
4. 部署脚本准备

## 项目结构

```
samples/GameApp/
├── docs/                          # 项目文档
│   ├── README.md                   # 项目概述（本文件）
│   ├── architecture.md             # 架构设计文档
│   ├── database-design.md          # 数据库设计
│   ├── api-design.md               # API 设计文档
│   ├── development-plan.md         # 详细开发计划
│   ├── deployment-plan.md          # 部署计划
│   └── game_login_flowchart.mermaid # 登录流程图
├── src/
│   ├── GameApp.AuthServer/         # 认证服务器
│   ├── GameApp.GameServer/         # 游戏逻辑服务器
│   ├── GameApp.BattleServer/       # 战斗服务器
│   ├── GameApp.Shared/             # 共享代码库
│   └── GameApp.Infrastructure/     # 基础设施代码
├── client/
│   └── GameApp.Unity/              # Unity 客户端项目
├── scripts/
│   ├── deploy/                     # 部署脚本
│   ├── db/                         # 数据库脚本
│   └── tools/                      # 开发工具
├── docker/                         # Docker 配置
├── k8s/                           # Kubernetes 配置
└── tests/                         # 测试项目
```

## 开发环境要求

### 开发工具
- Visual Studio 2022 或 JetBrains Rider
- Unity 2022.3 LTS+
- Docker Desktop
- MongoDB Compass
- Redis CLI

### 运行环境
- .NET 8 SDK
- MongoDB 7.0+
- Redis 7.0+
- Consul 1.15+

## 快速开始

### 1. 环境准备
```bash
# 启动基础设施服务
docker-compose up -d mongodb redis consul

# 验证服务状态
docker-compose ps
```

### 2. 后端服务启动
```bash
# 启动认证服务器
cd src/GameApp.AuthServer
dotnet run

# 启动游戏服务器
cd src/GameApp.GameServer
dotnet run

# 启动战斗服务器
cd src/GameApp.BattleServer
dotnet run
```

### 3. Unity 客户端
1. 使用 Unity 打开 `client/GameApp.Unity` 项目
2. 运行登录场景进行测试

## 相关文档

- [架构设计](architecture.md) - 详细的系统架构设计
- [数据库设计](database-design.md) - 数据库结构和数据模型
- [API 设计](api-design.md) - HTTP API 和 RPC 接口设计
- [开发计划](development-plan.md) - 详细的开发任务和时间安排
- [部署计划](deployment-plan.md) - 生产环境部署指南
- [登录流程图](game_login_flowchart.mermaid) - 完整的登录流程图

## 联系方式

如有问题或建议，请通过以下方式联系：
- 项目 Issues
- 开发团队内部沟通渠道

---

**注意**：本项目基于 [ChatApp 示例](../../ChatApp/README.md) 进行扩展开发，继承了 PulseRPC 框架的设计理念和最佳实践。
