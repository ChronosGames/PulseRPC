# 迁移指南

本文记录当前推荐迁移方向。历史设计文档中的 `BaseService`、`ConcurrentServiceBase`、旧 ServiceDiscovery 包名和旧 Receiver 模型不再作为新代码入口。

## 迁移到统一 IPulseHub

1. 把服务契约收敛到继承 `IPulseHub` 的接口。
2. 服务端实现使用当前 `PulseServiceBase` / 服务注册 API。
3. 客户端通过 Source Generator 生成代理调用。
4. 服务端推送接口按当前 Hub/Receiver 生成器约定整理。

## 迁移服务发现

旧独立 `PulseRPC.ServiceDiscovery`、`PulseRPC.LoadBalancing` 命名空间不作为当前入口。新代码优先使用：

- `PulseRPC.Infrastructure`
- `PulseRPC.Infrastructure.Consul`
- `PulseRPC.Infrastructure.Etcd`
- `PulseRPC.Infrastructure.Kubernetes`

## 迁移历史示例

`samples/README.md` 已标记历史探索示例。迁移这些示例前，先确认当前公共 API，并补最小构建测试。

## 迁移到严格 Hub 路由和 node wire v2

- 重新生成客户端代理；新代理要求通道实现 `IHubAddressedClientChannel` 并始终发送 canonical Hub，不再静默回退为空 Hub 调用。
- 手写 `IClientChannel.InvokeRawAsync` / `SendCommandAsync` 调用无法提供 Hub，现已标记为 `[Obsolete]` 并给出迁移诊断。需要进入严格网络入口的代码应改用 `IHubAddressedClientChannel.InvokeHubRawAsync` / `SendHubCommandAsync`。
- 所有集群节点先部署支持能力协商的版本，再保持 `ClusterNodeWireOptions.AllowLegacyActorProtocol=false`。如滚动升级必须短期开启 legacy，应接受该窗口没有 claims 传播与 lease fencing，并在升级完成后关闭。
- 多节点环境注册共享 `IActorLeaseStore`；默认进程内实现不再被多成员拓扑接受。
- 显式设置 `TcpNodeTransportOptions.SecurityMode`。生产选择 `ExternalMutualTls` 前必须先让节点端口实际处于 mTLS 保护层之后；本机测试才可使用 `InsecureDevelopment`。
- wire v2 的 Send 现在等待远端执行 ACK。容量规划应重新测量延迟/吞吐；跨进程 exactly-once 仍需持久 inbox 或业务幂等，不能只依赖进程内去重窗口。

## 相关文档

- [RPC 模型](../concepts/rpc-model.md)
- [Source Generator 模型](../concepts/source-generation.md)
- [测试指南](testing.md)
- [历史归档](../archive/)
