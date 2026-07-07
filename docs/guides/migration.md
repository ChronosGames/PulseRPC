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

## 相关文档

- [RPC 模型](../concepts/rpc-model.md)
- [Source Generator 模型](../concepts/source-generation.md)
- [测试指南](testing.md)
- [历史归档](../archive/)

