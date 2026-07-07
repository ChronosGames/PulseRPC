# 集群与路由

PulseRPC 集群能力围绕 `IPulseRouter`、`IClusterMembership`、`INodeEndpointResolver`、`INodeLink` 和 `IPulseBackplane` 组织。目标是让 Hub/Actor 调用可以在单节点、本地多实例和多节点之间保持统一寻址模型。

## 层级模型

- L0：本进程服务实例，直接调度。
- L1：本节点多个实例或多连接之间的路由。
- L2：多节点 Actor 属主选择、单一激活和节点间调用。
- L3：可选状态迁移，通过 `IActorStateSnapshot` 和 `IActorStateTransport` 保留内存态。

## 成员发现

核心抽象在 `PulseRPC.Infrastructure`，后端实现包括：

- 静态成员配置
- Consul
- Etcd
- Kubernetes

启用动态发现时，后端扩展通常应在 `AddPulseClustering(...)` 之后调用，以覆盖默认静态注册。

## 认证边界

节点间连接使用独立的节点认证机制，例如共享密钥或证书认证。它不同于业务客户端认证，不应混用业务用户 token。

## 可靠性边界

集群路由解决“消息发往哪里”，不自动提供业务强一致。需要 exactly-once 效果的业务仍需业务幂等、持久化、去重窗口和补偿策略。

## 相关文档

- [架构总览](architecture.md)
- [Actor 模型](actor-model.md)
- [部署指南](../guides/deployment.md)
- [参考手册](../reference/index.md)

