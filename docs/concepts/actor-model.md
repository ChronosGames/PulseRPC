# Actor 模型

PulseRPC 的 Actor 模型建立在统一 `IPulseHub` 契约和服务端 `PulseServiceBase` 运行时之上。它面向游戏服务器中常见的玩家、房间、公会、战斗实例等“带身份的长生命周期对象”。

## 核心对象

- Hub 契约：业务接口，继承 `IPulseHub`。
- Service/Actor 实现：服务端实例，通常继承 `PulseServiceBase` 并实现 Hub 契约。
- ServiceId：Actor 实例身份，例如玩家 ID、房间 ID 或战斗 ID。
- ServiceName：Actor 类型或逻辑服务名，用于路由和注册。
- `PulseContext`：当前调用上下文，包含连接、调用者、认证等信息。

## 调用语义

默认投递模式是 `DeliveryMode.AtMostOnce`，适合实时位置同步、聊天提示、房间状态广播等低延迟场景。涉及扣费、发奖、库存变更等强一致业务时，业务层必须设计幂等键、去重和持久化事务。

## 状态边界

Actor 内存状态只在当前进程内可靠。集群 L2 路由负责单一激活和属主选择；L3 状态迁移需要 Actor 实现 `IActorStateSnapshot`，并由业务或运维流程显式触发 `ActorMigrationCoordinator`。

## 并发边界

Actor 的目标是把同一逻辑实例的消息串行化或按策略调度，降低显式锁的使用。不要在 Actor 内部绕过调度队列直接修改共享状态；需要并发 IO 时优先使用当前 `PulseServiceBase` 和 `ServiceExecutionOptions` 支持的执行模型。

## 相关文档

- [RPC 模型](rpc-model.md)
- [服务端运行时](server-runtime.md)
- [集群与路由](clustering-and-routing.md)
- [Actor 服务开发](../guides/actor-services.md)

