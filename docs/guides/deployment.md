# 部署指南

PulseRPC 服务端可以作为普通 .NET Worker/Host 进程部署。游戏后端通常按服务器角色拆分，例如 LoginServer、GameServer、BattleServer、BackendServer 或 Gateway 节点。

## 单节点部署

1. 使用 `AddPulseServer(...)` 注册传输。
2. 注册业务 Hub/Service。
3. 暴露 TCP 或 KCP 端口。
4. 配置日志、健康检查和进程守护。

## 多节点部署

1. 为每个节点配置稳定 `LocalNodeId`。
2. 调用 `AddPulseClustering(...)`；它默认注册内置 `TcpNodeTransport`。
3. 选择静态成员或 Consul/Etcd/Kubernetes 发现后端，并保证成员端点指向可接受 PulseRPC TCP 的节点监听端口。
4. 注册共享 `IConnectionMultiplexer` 并调用 `AddRedisActorLeases(...)`，或提供等价的 CAS + TTL `IActorLeaseStore`。多成员拓扑默认拒绝进程内租约。
5. 配置节点认证；生产优先使用 `UseCertificateNodeAuthentication(...)`。
6. 用私网、防火墙以及 mTLS service mesh/TLS 终止层保护节点端口。内置 TCP 节点传输不自行加密线路。
7. 针对 Actor 属主变化设计幂等、状态持久化或 L3 迁移。

最小 Redis 租约装配如下；Redis 连接应作为单例并配置生产重连、超时和故障转移策略：

```csharp
services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConfiguration));

services.AddPulseClustering(
    topology =>
    {
        topology.LocalNodeId = nodeId;
        topology.Members.AddRange(clusterMembers);
    },
    auth => auth.SharedSecret = nodeSharedSecret);

services.AddRedisActorLeases(options =>
{
    options.KeyPrefix = $"pulserpc:{environmentName}:{clusterName}";
});

services.Configure<TcpNodeTransportOptions>(options =>
{
    // 运维必须确保该节点端口确实位于双向 TLS 的 mesh/sidecar/终止层之后。
    options.SecurityMode = NodeTransportSecurityMode.ExternalMutualTls;
});
```

`TcpNodeTransportOptions.SecurityMode` 未设置时节点传输拒绝构造；`InsecureDevelopment` 只允许 loopback 测试。`ClusterNodeWireOptions.AllowLegacyActorProtocol` 默认关闭。只有滚动升级窗口可以短期开启；所有节点升级到 wire v2 后应立即关闭。`ActorLeaseHeartbeatOptions.Interval` 必须明显短于 `LeaseActorDirectoryOptions.LeaseDuration`，并为 Redis 抖动保留余量。

## Kubernetes 注意事项

- Pod 名作为节点 ID 时，重建会改变节点身份。
- 需要保留内存态的 Actor 应接入 L3 迁移或持久化。
- 对公网客户端暴露端口时区分外部入口和内部节点通信。
- NetworkPolicy/安全组应只允许集群成员访问节点控制 Hub；不要把“证书签名凭据”等同于 TLS 加密。

## 相关示例

- [DistributedGameApp](../../samples/DistributedGameApp/)
- [DistributedGameApp deploy](../../samples/DistributedGameApp/deploy/README.md)

## 相关文档

- [集群与路由](../concepts/clustering-and-routing.md)
- [性能指南](performance.md)
- [参考手册](../reference/index.md)
