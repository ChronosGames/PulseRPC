# 02 接口契约与命名语义评审

## 总体判断

当前最主要的接口设计问题不是“缺少接口”，而是“接口承诺过多、实现兑现不足”。这会直接伤害用户信任：调用方按公开 API 编程，但实际行为可能是空实现、忽略参数、退化策略或运行时异常。

本轮建议优先遵循一个标准：

> 公开 API 只表达已经实现、已测试、可解释的行为。未实现功能要么 internal，要么显式 `[Obsolete]`，要么在文档中标为实验。

## 问题 B1：`IPulseClientBuilder` 与 `PulseClient` 参数未完全生效

证据：

- `PulseClientBuilder` 保存 `_authenticationProvider`、`_connectionPoolOptions`、`_retryPolicy`、`_loadBalancingOptions`：`src/PulseRPC.Client/Configuration/PulseClientBuilder.cs:16-20`。
- `Build()` 把这些参数传给 `PulseClient`：`PulseClientBuilder.cs:136-140`。
- `PulseClient` 构造函数接收 `authenticationProvider`、`connectionPoolOptions`、`loadBalancingOptions`、`retryPolicy`：`src/PulseRPC.Client/PulseClient.cs:63-67`。
- `PulseClient` 只保存 `_retryPolicy`，并且当前调用路径没有使用它：`PulseClient.cs:21`、`:71`。
- `CreateLoadBalancer` 接收 `options` 但直接 `new ConnectionLoadBalancer(strategy, logger)`：`PulseClient.cs:335-341`。

影响：

- 用户配置了鉴权、连接池、重试、负载均衡参数后，可能误以为已经生效。
- 生产环境问题会表现为“配置无效”，难以排查。
- API 一旦发布，后续修复会改变既有行为，需要兼容说明。

根因：

- Builder API 先行设计，运行时实现未同步闭合。
- 配置对象缺少端到端测试验证“配置 -> 行为”的链路。

建议：

1. P0：对未生效配置补测试，明确哪些行为应生效。
2. P0：短期在 XML 注释和运行时日志中标明未实现项，避免误用。
3. P1：真正接入认证握手、连接池、重试策略、负载均衡 options。
4. P1：若短期不做连接池，应移除或 `[Obsolete]` 对应 builder 方法。

## 问题 B2：客户端服务发现契约未闭合

证据：

- `ConnectionManager.ConnectAsync` 支持 `ConnectionDescriptor`，descriptor 可表达 service-name 场景。
- `ResolveEndpointAsync` 只返回 `descriptor.Endpoint`：`src/PulseRPC.Client/ConnectionManager.cs:229-232`。
- 当 descriptor 只有服务名没有 endpoint 时，验证可以通过，但连接阶段会抛 `无法解析连接端点`。

影响：

- “按服务连接”的 API 语义与实际“按端点连接”冲突。
- 用户无法从类型系统判断哪些 descriptor 可用。

根因：

- 服务发现抽象在 Infrastructure/Cluster 一侧实现，但客户端连接管理没有注入 resolver。

建议：

- 把 descriptor 分成 `EndpointConnectionDescriptor` 和 `ServiceConnectionDescriptor`，避免一个类型承载互斥语义。
- `ConnectionManager` 注入 `IServiceEndpointResolver`，服务名 descriptor 必须通过 resolver。
- 若服务发现暂不支持客户端，禁用相关 factory 或在创建 descriptor 时抛出明确异常。

## 问题 B3：客户端生命周期事件与参数语义不准确

证据：

- `PulseClient.StopAsync` 先把 `_state` 设置为 `Stopping`，再读取 `previousState = _state`：`src/PulseRPC.Client/PulseClient.cs:156-167`。
- `DisconnectAsync(string connectionId, bool graceful = true, ...)` 接收 `graceful`，但只调用 `_connectionManager.DisconnectAsync`：`PulseClient.cs:259-263`。
- 批量断开也向下传 `graceful`，但最终未生效：`PulseClient.cs:269-280`。

影响：

- `StateChanged` 的 `PreviousState` 会错误地显示为 `Stopping`，破坏监控和状态机测试。
- `graceful=false` 与 `graceful=true` 行为一致，API 参数误导调用方。

根因：

- 生命周期实现没有把事件参数和 public 参数纳入测试。

建议：

- P0：修复 `previousState` 捕获顺序，补事件测试。
- P1：定义 graceful 语义：等待 pending request、发送 close frame、立即 abort 三者如何区分。
- 未实现前将 `graceful` 标注为保留参数或移除重载。

## 问题 B4：客户端负载均衡策略语义不成立

证据：

- `WeightedRoundRobin` 当前退化为普通轮询：`src/PulseRPC.Client/Configuration/ConnectionLoadBalancer.cs:136-141`。
- `ConsistentHash` 使用 `hint.ToString() + DateTime.UtcNow.Ticks`：`ConnectionLoadBalancer.cs:147-153`。
- `GetConnectionWeight` 仍是 TODO：`ConnectionLoadBalancer.cs:191-194`。

影响：

- 加权轮询没有权重。
- 一致性哈希因为混入时间戳，不具备一致性，也无法稳定 sticky。
- 命名与行为相反，用户按策略选择时会得到错误预期。

根因：

- 策略枚举先于算法落地。
- `LoadBalancingHint` 没有携带稳定 key。

建议：

- P0：把未实现策略从推荐路径移除，或运行时抛 `NotSupportedException`，不要静默退化。
- P1：为 `ConsistentHash` 明确输入 key，例如 `LoadBalancingContext.StickyKey`。
- P1：连接权重来源统一为 descriptor/options/tag，且有默认值。

## 问题 B5：`IPulseServer` 公开了未实现能力

证据：

- `IPulseServer` 公开 `BroadcastAsync(data, filter)`：`src/PulseRPC.Server/IPulseServer.cs:65`。
- `PulseServer.BroadcastAsync` 忽略 `filter`：`src/PulseRPC.Server/PulseServer.cs:388-390`。
- `IPulseServer.GetChannel/GetAllChannels` 公开 `ITransportChannel`：`IPulseServer.cs:82-88`。
- `PulseServer.GetChannel` 返回 null，`GetAllChannels` 返回空数组：`PulseServer.cs:406-420`。
- `GetRegisteredServices` 返回空：`PulseServer.cs:354-358`。
- `GetPerformanceMetrics` 多项为 0/TODO：`PulseServer.cs:363-375`。

影响：

- 管理面 API 无法真实反映服务端状态。
- 用户拿到 `IPulseServer` 后会认为可广播过滤、可查通道、可查服务、可取性能指标。
- 这些 API 如果已经发布，后续改成真实行为时可能改变监控和业务逻辑。

根因：

- 旧设计保留了管理能力，但新的 `IServerChannel` / `ITransportChannel` 适配未完成。
- 服务路由改为源生成器后，没有同步设计运行时可查询的 service metadata。

建议：

- P0：`BroadcastAsync` 要么实现 filter，要么移除/废弃 filter 参数。
- P0：修复 `GetChannel/GetAllChannels`，或从接口移出，改为内部 API。
- P1：生成器输出 service metadata 表，支撑 `GetRegisteredServices`。
- P1：指标只暴露真实数据；未实现的字段不要返回 0，应使用 nullable 或单独 capability。

## 问题 B6：服务端构建器传输配置被丢弃

证据：

- `PulseServerBuilder.AddTcpTransport` 把配置加入 `_transports`：`src/PulseRPC.Server/Extensions/PulseServerServiceCollectionExtensions.cs:264-270`。
- `AddKcpTransport` 同理：`PulseServerServiceCollectionExtensions.cs:273-279`。
- `Build()` 调用 `Services.AddPulseServer(...)` 但没有把 `_transports` 写入 options：`PulseServerServiceCollectionExtensions.cs:288-294`。
- `options.Transports.AddRange(_transports)` 只存在于注释代码：`PulseServerServiceCollectionExtensions.cs:296-304`。

影响：

- 用户通过 builder 添加端口后，服务端实际不监听对应传输。
- 这是高优先级 API bug，因为配置表面成功、运行时静默失效。

根因：

- 重构 `AddPulseServer` 后遗留旧实现注释，没有补回传输配置桥接。

建议：

- P0：补测试复现：builder 添加 TCP 后 `IOptions<PulseServerOptions>.Value.Transports` 包含该配置。
- P0：修复 `Build()` 传输配置写入。
- P1：删除注释掉的大段旧实现，避免误导。

## 问题 B7：服务端连接事件强转错误

证据：

- `PulseServer.ProcessNewConnectionAsync` 调用 `_channelManager.AddChannel(e.Transport)`：`src/PulseRPC.Server/PulseServer.cs:276`。
- 下一行把 `e.Transport as IServerChannel` 传给 `ClientConnectedEventArgs`，失败则抛异常：`PulseServer.cs:278-279`。
- `e.Transport` 是 `IServerTransport`，真正的 `IServerChannel` 是 `AddChannel` 创建/返回的对象。

影响：

- 新连接可能已被 channel manager 注册，但随后事件触发失败并进入异常分支关闭 transport。
- 连接建立路径不稳定，且错误发生在接入核心路径。

根因：

- 传输对象和通道对象职责分离后，事件参数没有同步调整。

建议：

- P0：让 `AddChannel` 返回值赋给局部变量，并传给事件。
- P0：补连接接入单元测试，验证事件收到的是 `IServerChannel`。

## 命名语义问题

| 问题 | 证据 | 影响 | 建议 |
| --- | --- | --- | --- |
| `IServiceHandler` 重名 | `PulseRPC.Channels.IServiceHandler`、`PulseRPC.Server.Services.IServiceHandler`、`MessageDispatcher` 内部 handler 概念 | 用户和维护者无法快速判断 handler 处理的是 transport request、service invocation 还是 dispatcher task | 改为 `ITransportRequestHandler`、`IServiceMethodInvoker`、`IInvocationDispatcherHandler` |
| `ValidationResult` 重名 | `PulseRPC.Client` 与 `PulseRPC.Authentication` 都有不同语义的 `ValidationResult` | using 后二义性，错误处理含义不清 | 使用 `ConnectionValidationResult`、`AuthenticationValidationResult` |
| `MessagePriority` 重复 | Abstractions 属性中的消息优先级与 Server scheduling 中的优先级 | 用户不知道是否同一套优先级会贯通调度 | 保留一套公共 priority，服务端内部用 adapter 映射 |
| `ConnectionState`/`ExtendedConnectionState`/`ServerConnectionState` | 多处状态枚举 | 生命周期事件与健康检查状态难以统一 | 明确分层：transport state、client channel state、server connection state |
| Hub/Service/Actor 术语交叉 | `IPulseHub`、`IPulseService`、`PulseServiceBase`、`PulseHubBase`、Actor routing、ServiceKey/ServiceId | 文档和 API 让用户难判断“我要实现哪个接口” | 定义术语表：Hub 是 RPC contract，Service 是 server runtime instance，Actor 是 key-addressed service instance |

## API 治理问题

证据：

- 只有 `PulseRPC.Abstractions` 和 `PulseRPC.Client` 有 `PublicAPI.*.txt`。
- `PulseRPC.Server`、`PulseRPC.Shared` 有大量 public 类型，但没有 PublicAPI 文件。
- `PulseRPC.Abstractions/PublicAPI.Unshipped.txt` 已很大，`PulseRPC.Client/PublicAPI.Unshipped.txt` 仍接近空文件，说明治理不均衡。

影响：

- 公开 surface 的兼容性保护不完整。
- Server/Shared 的 public 类型可能无意中成为稳定承诺。

建议：

- P1：所有准备发布的包启用同等 API 基线。
- P1：先把不该公开的类型 internal 化，再生成 PublicAPI。
- P2：每次 PR 改 public API 时必须说明：新增、破坏、废弃、迁移路径。

