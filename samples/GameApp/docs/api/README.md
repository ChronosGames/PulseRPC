# GameApp API 文档

## 概述

GameApp 提供完整的游戏登录和管理 API，包括认证服务、游戏服务和战斗服务。

## 服务架构

```
┌─────────────────┐    ┌──────────────────┐    ┌──────────────────┐
│   AuthServer    │    │   GameServer     │    │  BattleServer    │
│   (HTTP API)    │    │   (PulseRPC)     │    │   (PulseRPC)     │
├─────────────────┤    ├──────────────────┤    ├──────────────────┤
│ • 用户认证      │    │ • 玩家管理       │    │ • 战斗匹配       │
│ • 令牌管理      │    │ • 世界交互       │    │ • 技能系统       │
│ • 区服管理      │    │ • 事件推送       │    │ • 实时战斗       │
│ • 性能监控      │    │ • 负载均衡       │    │ • 战斗统计       │
└─────────────────┘    └──────────────────┘    └──────────────────┘
```

## API 服务列表

### 1. AuthServer (HTTP REST API)
- **端口**: 5000 (HTTP), 5001 (HTTPS)
- **协议**: HTTP + JSON
- **功能**: 用户认证、令牌管理、区服选择

### 2. GameServer (PulseRPC)
- **端口**: 7000 (TCP), 7001 (KCP)
- **协议**: PulseRPC + MemoryPack
- **功能**: 玩家管理、世界交互、事件推送

### 3. BattleServer (PulseRPC)
- **端口**: 8000 (TCP), 8001 (KCP)
- **协议**: PulseRPC + MemoryPack
- **功能**: 战斗匹配、技能系统、实时战斗

## 快速开始

### 认证流程
```http
# 1. 用户注册
POST /api/auth/register
Content-Type: application/json

{
  "username": "player123",
  "password": "password123",
  "email": "player@example.com"
}

# 2. 用户登录
POST /api/auth/login
Content-Type: application/json

{
  "username": "player123",
  "password": "password123"
}

# 响应
{
  "success": true,
  "accessToken": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9...",
  "refreshToken": "refresh_token_here",
  "expiresIn": 3600,
  "user": {
    "id": "user_id",
    "username": "player123",
    "level": 1
  }
}

# 3. 获取游戏票据
POST /api/auth/game-ticket
Authorization: Bearer eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9...

{
  "zoneId": "zone001"
}
```

### PulseRPC 连接
```csharp
// Unity客户端连接示例
var gameClient = new GameClient();
await gameClient.ConnectAsync("127.0.0.1", 7000, 7001);

// 使用游戏票据认证
await gameClient.AuthenticateAsync(gameTicket);

// 调用服务
var playerInfo = await gameClient.PlayerService.GetPlayerInfoAsync();
```

## 详细文档

### AuthServer API
- [认证 API](auth-api.md) - 用户注册、登录、令牌管理
- 区服 API - 当前仓库未提供独立文档
- [性能监控 API](monitoring-api.md) - 系统监控、告警管理

### GameServer API
- 玩家服务 API - 当前仓库未提供独立文档
- 世界服务 API - 当前仓库未提供独立文档
- 事件推送 API - 当前仓库未提供独立文档

### BattleServer API
- 战斗服务 API - 当前仓库未提供独立文档
- 技能服务 API - 当前仓库未提供独立文档
- 战斗事件 API - 当前仓库未提供独立文档

## 错误码参考
- 通用错误码 - 当前仓库未提供独立文档
- 业务错误码 - 当前仓库未提供独立文档

## SDK 和工具
- Unity SDK - 当前仓库未提供独立文档
- 测试工具 - 当前仓库未提供独立文档
- 性能测试 - 当前仓库未提供独立文档

## 变更日志
- API变更日志 - 当前仓库未提供独立文档
- 升级指南 - 当前仓库未提供独立文档
