# 部署指南

PulseRPC 服务端可以作为普通 .NET Worker/Host 进程部署。游戏后端通常按服务器角色拆分，例如 LoginServer、GameServer、BattleServer、BackendServer 或 Gateway 节点。

## 单节点部署

1. 使用 `AddPulseServer(...)` 注册传输。
2. 注册业务 Hub/Service。
3. 暴露 TCP 或 KCP 端口。
4. 配置日志、健康检查和进程守护。

## 多节点部署

1. 为每个节点配置稳定 `LocalNodeId`。
2. 调用 `AddPulseClustering(...)`。
3. 选择静态成员或 Consul/Etcd/Kubernetes 发现后端。
4. 配置节点间认证。
5. 针对 Actor 属主变化设计状态持久化或 L3 迁移。

## Kubernetes 注意事项

- Pod 名作为节点 ID 时，重建会改变节点身份。
- 需要保留内存态的 Actor 应接入 L3 迁移或持久化。
- 对公网客户端暴露端口时区分外部入口和内部节点通信。

## 相关示例

- [DistributedGameApp](../../samples/DistributedGameApp/)
- [DistributedGameApp deploy](../../samples/DistributedGameApp/deploy/README.md)

## 相关文档

- [集群与路由](../concepts/clustering-and-routing.md)
- [性能指南](performance.md)
- [参考手册](../reference/index.md)

