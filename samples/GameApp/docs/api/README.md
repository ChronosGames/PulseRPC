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
- [区服 API](zone-api.md) - 区服列表、选择、状态
- [性能监控 API](monitoring-api.md) - 系统监控、告警管理

### GameServer API
- [玩家服务 API](player-service-api.md) - 玩家信息、等级、装备
- [世界服务 API](world-service-api.md) - 世界交互、聊天、组队
- [事件推送 API](game-events-api.md) - 服务器推送事件

### BattleServer API
- [战斗服务 API](battle-service-api.md) - 战斗匹配、房间管理
- [技能服务 API](skill-service-api.md) - 技能释放、冷却管理
- [战斗事件 API](battle-events-api.md) - 实时战斗事件

## 错误码参考
- [通用错误码](error-codes.md) - 系统级错误码定义
- [业务错误码](business-errors.md) - 业务逻辑错误码

## SDK 和工具
- [Unity SDK](../client/unity-sdk.md) - Unity客户端SDK使用指南
- [测试工具](../tools/testing-tools.md) - API测试工具和脚本
- [性能测试](../tools/performance-testing.md) - 压力测试和性能监控

## 变更日志
- [API变更日志](changelog.md) - API版本变更记录
- [升级指南](migration-guide.md) - API升级和迁移指南
