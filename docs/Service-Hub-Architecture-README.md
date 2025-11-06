# PulseRPC.Server 服务架构文档索引

## 概述

本系列文档定义了 PulseRPC.Server 中有状态服务（IPulseService）与无状态通信契约（IPulseHub）的关系架构，提供了生产级的解决方案。

**核心方案**：Service as State Container + ServiceFactory

**设计日期**：2025-01-10
**状态**：生产就绪 (Production Ready)

---

## 核心问题

PulseRPC.Server 存在以下设计挑战：

- **IPulseHub**：期望是无状态的通信契约（类似 gRPC Service Definition）
- **IPulseService**：期望是有状态的服务实例（类似 Actor）
- **需求**：多个不同的 Hub 接口需要共享同一个 Service 实例的状态
- **目标**：在生产环境内优雅地设计这种关系

---

## 解决方案

### 方案 1：Service as State Container + ServiceFactory

#### 核心思想

```
┌──────────────────────────────────────────────────────────────┐
│                    IPulseService                             │
│                  (有状态容器)                                  │
│  • 存储业务状态                                                │
│  • 实现业务逻辑                                                │
│  • ServiceName + ServiceId                                   │
└───────────────┬──────────────────────────────────────────────┘
                │
                │ 依赖注入
                │
    ┌───────────┴───────────┬───────────────────────┐
    │                       │                       │
    ▼                       ▼                       ▼
┌─────────────┐      ┌─────────────┐      ┌─────────────┐
│ Hub 1       │      │ Hub 2       │      │ Hub 3       │
│ (用户权限)   │      │ (管理员权限) │      │ (查询接口)   │
└─────────────┘      └─────────────┘      └─────────────┘
```

#### 关键组件

- **IPulseService**：有状态的服务实例，负责业务逻辑和状态管理
- **IPulseHub**：无状态的通信契约，定义客户端可调用的 RPC 方法
- **IPulseServiceFactory**：管理 Service 实例的生命周期（创建、缓存、销毁）
- **ThreadAffinityManager**：保证同一 ServiceId 的请求在同一线程执行（无需加锁）

---

## 文档结构

本系列文档包含以下部分：

### 1. [架构设计文档](Service-Hub-Architecture-Design.md)

**目标读者**：架构师、技术负责人

**内容**：
- 完整的架构设计
- 组件关系图
- 实现模式（基本模式、读写分离模式、简化模式）
- 线程安全保证
- 生命周期管理
- 配置与调优
- 监控与可观测性
- 故障处理

**关键章节**：
- 第 2 节：设计方案详解
- 第 3 节：实现模式
- 第 4 节：线程安全保证
- 第 5 节：生命周期管理

### 2. [ServiceFactory 设计文档](ServiceFactory-Design.md)

**目标读者**：框架开发者、核心贡献者

**内容**：
- IPulseServiceFactory 接口设计
- IServiceLifecycle 接口设计
- PulseServiceFactory 默认实现
- 配置选项详解
- 关键实现细节（创建流程、LRU 驱逐、健康检查）
- DI 注册扩展
- 性能考虑
- 监控指标

**关键章节**：
- 第 2 节：接口设计
- 第 4 节：关键实现细节
- 第 5 节：DI 注册扩展

### 3. [ServiceFactory 实现示例](ServiceFactory-Implementation-Example.cs)

**目标读者**：框架开发者

**内容**：
- IPulseServiceFactory 完整实现
- IServiceLifecycle 接口定义
- PulseServiceFactory 完整代码
- 配置选项类
- 异常类型定义
- 指标接口
- DI 扩展方法

**特点**：
- 可直接集成到项目中
- 包含完整的错误处理
- 包含详细的日志记录
- 包含指标收集

### 4. [最佳实践指南](Service-Hub-Best-Practices.md)

**目标读者**：应用开发者、服务实现者

**内容**：
- ServiceId 命名规范
- Service 状态管理
- Hub 设计模式
- 生命周期钩子使用
- DI 注册模式
- 错误处理
- 性能优化
- 测试策略
- 常见场景实现
- 检查清单

**特点**：
- 大量示例代码
- 明确的 ✅ 推荐 和 ❌ 错误 对比
- 实用的检查清单
- 常见场景的完整实现

### 5. [完整示例代码](Service-Hub-Complete-Example.cs)

**目标读者**：所有开发者

**内容**：
- **场景 1：聊天室系统**
  - ChatRoomService（状态容器）
  - ChatRoomUserHub（用户接口）
  - ChatRoomAdminHub（管理员接口）
  - 持久化接口和实现

- **场景 2：游戏房间系统**
  - GameRoomService（游戏状态管理）
  - GameRoomHub（玩家接口）
  - 完整的游戏流程

- **场景 3：分布式缓存系统**
  - CacheShardService（缓存分片）
  - DistributedCacheHub（缓存接口）
  - 分片路由逻辑

**特点**：
- 真实场景的完整实现
- 包含领域模型、Service、Hub、DI 注册
- 可直接运行的示例

---

## 快速开始

### 1. 理解核心概念

阅读 [架构设计文档](Service-Hub-Architecture-Design.md) 的第 1-2 节，理解：
- IPulseService 和 IPulseHub 的职责
- ServiceFactory 的作用
- 线程安全保证

### 2. 查看实现示例

查看 [完整示例代码](Service-Hub-Complete-Example.cs) 中的聊天室示例，理解：
- 如何定义 Service
- 如何定义多个 Hub
- 如何通过 Factory 共享状态

### 3. 学习最佳实践

阅读 [最佳实践指南](Service-Hub-Best-Practices.md)，掌握：
- ServiceId 命名规范
- 状态管理原则
- Hub 设计模式
- 生命周期钩子使用

### 4. 实现你的服务

参考 [完整示例代码](Service-Hub-Complete-Example.cs)，实现你的业务场景：
1. 定义 Service（状态容器）
2. 定义 Hub（通信契约）
3. 注册 ServiceFactory
4. 编写单元测试

---

## 核心设计原则

### ✅ DO（推荐做法）

1. **Service 负责状态**
   - 所有可变状态放在 Service 中
   - 使用实例字段（非静态）
   - 无需手动加锁（线程亲和性保证）

2. **Hub 负责契约**
   - Hub 是无状态的
   - 通过 Factory 获取 Service
   - 只做参数验证和转发

3. **Factory 负责生命周期**
   - 使用 ServiceFactory 管理实例
   - 实现生命周期钩子（OnActivate/OnDeactivate）
   - 配置合理的超时和缓存策略

4. **ServiceId 唯一性**
   - 格式：`{ServiceName}:{BusinessId}`
   - 示例：`ChatRoom:room-123`

### ❌ DON'T（避免做法）

1. ❌ 在 Hub 中存储状态
2. ❌ 在 Service 中使用静态字段
3. ❌ 在 Service 中手动加锁
4. ❌ 所有实例使用相同的 ServiceId
5. ❌ 在生命周期钩子中抛出异常

---

## 关键指标

### 性能指标

- **缓存命中率**：>95% （说明 Factory 缓存有效）
- **平均实例存活时间**：根据业务调整
- **驱逐次数**：<1% 总创建次数（说明缓存大小合理）

### 配置建议

| 场景 | WorkerThreadCount | MaxCachedInstances | IdleTimeout |
|------|-------------------|-------------------|-------------|
| 低流量 | 2-4 | 1000 | 2 分钟 |
| 中流量 | CPU 核数 | 5000 | 5 分钟 |
| 高流量 | CPU 核数 | 10000+ | 10 分钟 |
| 内存敏感 | CPU 核数 | 5000 | 2 分钟 |

---

## 常见场景

### 1. 聊天室
- Service：ChatRoomService（管理消息、参与者、封禁名单）
- Hub：ChatRoomUserHub（用户接口）、ChatRoomAdminHub（管理员接口）
- ServiceId：`ChatRoom:{roomId}`

### 2. 游戏房间
- Service：GameRoomService（管理玩家、游戏状态、回合）
- Hub：GameRoomHub（玩家接口）
- ServiceId：`GameRoom:{gameId}`

### 3. 购物车
- Service：ShoppingCartService（管理购物车项）
- Hub：ShoppingCartHub（用户接口）
- ServiceId：`ShoppingCart:{userId}`

### 4. 分布式缓存
- Service：CacheShardService（管理缓存分片）
- Hub：DistributedCacheHub（缓存接口）
- ServiceId：`CacheShard:{shardId}`

---

## 测试建议

### 单元测试
- 测试 Service 的业务逻辑
- 测试 Hub 的参数验证
- Mock Factory 和依赖

### 集成测试
- 测试多个 Hub 共享 Service 状态
- 测试生命周期钩子
- 测试并发场景

### 性能测试
- 测试缓存命中率
- 测试并发性能
- 测试内存占用

---

## 常见问题（FAQ）

### Q1: 为什么不在 Hub 中存储状态？
**A**: Hub 是单例，存储状态会导致所有客户端共享状态，违反隔离原则。状态应该在 Service 中，按 ServiceId 隔离。

### Q2: 为什么不需要在 Service 中加锁？
**A**: ThreadAffinityManager 保证同一 ServiceId 的请求在同一线程执行，因此 Service 内部访问是串行的，无需加锁。

### Q3: 如何处理跨 Service 通信？
**A**: 通过 Factory 获取另一个 Service 实例，使用异步调用。PulseRPC 会自动切换到对应的线程。

### Q4: Factory 缓存会占用多少内存？
**A**: 每个 Entry 约 64 字节 + Service 实例大小。10,000 实例约占 640 KB（不含 Service）。

### Q5: 健康检查失败后会怎样？
**A**: 实例会被自动移除，下次访问时重新创建。确保在 `OnActivateAsync` 中实现幂等性。

---

## 版本历史

- **v1.0** (2025-01-10) - 初始设计
  - Service as State Container 方案
  - ServiceFactory 实现
  - 完整文档和示例

---

## 贡献

欢迎贡献改进建议和示例代码！

- **问题反馈**：https://github.com/yourorg/PulseRPC/issues
- **Pull Request**：https://github.com/yourorg/PulseRPC/pulls
- **讨论区**：https://github.com/yourorg/PulseRPC/discussions

---

## 相关资源

### 内部文档
- [PulseRPC.Server 架构分析报告](PulseRPC-Server-Architecture-Analysis-Report.md)
- [PulseRPC.Server DI 接口设计](PulseRPC-Server-DI-Interface-Design.md)
- [并发服务安全指南](Concurrent-Service-Safety-Guide.md)

### 外部参考
- [Orleans Actor 模型](https://learn.microsoft.com/en-us/dotnet/orleans/)
- [Akka.NET Actor 系统](https://getakka.net/)
- [Proto.Actor](https://proto.actor/)

---

**文档维护者**：PulseRPC Team
**最后更新**：2025-01-10
**许可证**：MIT
