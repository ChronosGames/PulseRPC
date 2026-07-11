# Actor 服务开发

Actor 服务适合承载玩家、房间、战斗、公会等有身份、有状态、需要串行化处理的游戏逻辑。

## 推荐模型

1. 在共享契约项目定义 Hub 接口。
2. 服务端实现同一个接口并继承 `PulseServiceBase`，使用 `ServiceScenario.Actor` 或 `ServiceExecutionOptions.Actor`。
3. 用 `ServiceName` 表示 Actor 类型，用业务 key 作为 `ServiceId`。
4. 通过 `AddPulseService<T>((sp, key) => ...)` 注册按 key 创建实例的工厂。
5. 通过生成的 keyed 路由、`IServiceAccessor<T>.ExecuteAsync` / `ExecuteReadAsync` 或显式 `EnqueueAsync` 访问 Actor，避免直接调用实例方法绕过 mailbox。

Gateway keyed 路由会按 Hub 接口短名解析服务。例如 `IPlayerHub` 使用 `PlayerHub`，Actor 的 `[PulseService(DisplayName = "PlayerHub")]` 和基类 `serviceType` 都应与它一致。完整代码见[经 Gateway 调用 Actor](gateway-actors.md)。

## 状态设计

- 在 `OnStartingAsync` 从业务仓储恢复状态；钩子成功后 Actor 才进入运行态。
- 在 `OnStoppingAsync` 保存正常停止且 mailbox 在等待窗口内排空后的状态，但不要把它当作无条件恢复保证：处理超时会取消队列，进程异常退出时该钩子也可能不执行。
- 关键业务状态应在业务方法中持久化到数据库、事务日志或事件流，并使用版本号、compare-and-set 和幂等键处理重试。
- 框架当前没有通用 durable state store；仓储模型、schema 和恢复策略属于业务边界。
- 需要跨节点迁移内存态时，实现 `IActorStateSnapshot`。
- `IActorStateSnapshot` 服务于显式迁移，不等同于持久化，也不会在集群启动后自动迁移所有 Actor。

## 并发与生命周期

- 生成的 keyed 路由在实例为 `PulseServiceBase` 时把普通方法提交为独占 mailbox 工作；调用完成或异常后才向上游返回。
- `[Reentrant]` 方法作为只读工作执行：读者之间可并发，但不会与写者重叠。只在不会修改 Actor 状态时使用。
- 正常停止 Actor 时，运行时先停止接收新工作，最多等待 30 秒处理已接收的 mailbox，再调用 `OnStoppingAsync`；超时后会取消处理循环。
- 同一 Actor 内部避免裸 `Task.Run`、计时器回调或直接 DI 调用修改状态；这些路径会绕过 mailbox。
- 外部 IO 应设置超时和取消。
- 扣费、发奖、库存等操作必须业务幂等。
- 高并发广播应优先走框架提供的扇出和路由能力。

## 测试清单

- 同一 key 的并发写是否保持预期顺序。
- `[Reentrant]` 读是否从不与写重叠。
- 业务异常是否返回调用方，且不会让 mailbox 永久停止。
- 停止时已接受命令是否完成后再保存状态。
- 重建同一 key 的 Actor 是否能从仓储恢复。
- 重试、重复命令和进程崩溃是否满足业务幂等与恢复要求。

## 相关文档

- [Actor 模型](../concepts/actor-model.md)
- [经 Gateway 调用 Actor](gateway-actors.md)
- [集群与路由](../concepts/clustering-and-routing.md)
- [最佳实践](best-practices.md)
- [测试指南](testing.md)
