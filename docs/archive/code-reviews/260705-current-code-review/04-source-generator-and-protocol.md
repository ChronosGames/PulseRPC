# 04 Source Generator 与协议设计评审

## 已做对的部分

### 协议号生成思路一致

证据：

- 客户端生成器使用 FNV-1a，基于接口全名、方法名、参数类型构造签名。
- 服务端生成器 `GenerateProtocolIdInternal` 使用 `ProtocolIdHelper.BuildMethodSignature`，注释明确与客户端保持一致。
- 双端都在冲突时报告诊断，并通过 `[Protocol(0xXXXX)]` 手工修正。

价值：

- 去字符串路由是正确方向。
- 签名纯哈希比运行时字符串查表更可控。
- 冲突显式报错比静默线性探测更安全，因为客户端和服务端必须完全一致。

后续重点不是推翻协议号，而是把生成器边界、生成类型命名和运行时注册做稳。

## 问题 D1：服务端生成器目标框架不适合作为通用 analyzer

证据：

- 客户端生成器：`src/PulseRPC.Client.SourceGenerator/PulseRPC.Client.SourceGenerator.csproj:4` 为 `netstandard2.0`。
- 服务端生成器：`src/PulseRPC.Server.SourceGenerator/PulseRPC.Server.SourceGenerator.csproj:35` 为 `net10.0`。
- 服务端包把 net10.0 生成器 dll 打到 `analyzers/dotnet/cs`：`src/PulseRPC.Server/PulseRPC.Server.csproj:42`。

影响：

- Roslyn analyzer/source generator 通常应以 `netstandard2.0` 面向更广构建宿主。
- `net10.0` analyzer 可能在 IDE、旧 SDK、Unity/CI 构建宿主中加载失败或行为不一致。

建议：

- P1：把服务端生成器降到 `netstandard2.0`，与客户端一致。
- 若确实需要 net10 API，应隔离到运行时代码，不放 analyzer。
- 为生成器添加独立加载测试，覆盖 `dotnet build`、IDE design-time build、package 引用场景。

## 问题 D2：生成类型与运行时库类型冲突

证据：

- `dotnet build PulseRPC.sln --no-restore --verbosity minimal` 出现 `CS0436=56`。
- 典型警告：生成的 `ServiceRoutingTable.g.cs`、`GatewayFrontHub.Proxy.g.cs`、`GatewayRelayHub.Proxy.g.cs`、`ClusterInternalHub.Proxy.g.cs` 与 `PulseRPC.Server` 中导入类型冲突。

影响：

- 编译虽通过，但用户项目中出现大量 warning。
- 类型解析依赖编译器冲突选择，长期不可维护。
- 这说明运行时库中可能包含了本应只在用户项目生成的 proxy/routing 类型。

根因：

- 生成器在样例/benchmark 项目中生成的类型命名空间与 `PulseRPC.Server` 已编译进包的类型重叠。
- 运行时库、内部 hub、生成 proxy 之间边界不清。

建议：

- P0/P1：检查 `PulseRPC.Server` 包中是否包含生成产物或手写同名 proxy；不应让用户项目生成类型与包内类型同名。
- 生成类型命名加入用户程序集维度或放入 `PulseRPC.Generated.<AssemblyName>`。
- 对内部 hub proxy 使用 internal/private namespace，避免污染公共命名空间。

## 问题 D3：生成器命名空间不统一

证据：

- 客户端主生成器使用 `namespace PulseRPC.Generator`：`src/PulseRPC.Client.SourceGenerator/ServiceProxyGenerator.cs:12`。
- 客户端项目 RootNamespace 是 `PulseRPC.Client.SourceGenerator`。
- 服务端生成器使用 `PulseRPC.Server.SourceGenerator`。

影响：

- 日志、诊断、代码修复、生成输出路径难以统一。
- 用户看到 `PulseRPC.Generator` 无法判断是客户端还是服务端。

建议：

- 新增代码统一到 `PulseRPC.Client.SourceGenerator.*`。
- 旧 namespace 内类型保留兼容，逐步迁移。

## 问题 D4：序列化可用性检查不闭合

证据：

- 服务端主生成器中仍有 `IsMemoryPackable = false // TODO: 检查是否可序列化`：`src/PulseRPC.Server.SourceGenerator/PulseRPCSourceGenerator.cs:963`。
- 服务端 analyzer 中另有 `IsMemoryPackable` 检查逻辑，但主生成模型没有完全复用该结果。
- 响应序列化依赖生成的 `IResponseSerializerRegistry`，运行时找不到 serializer 时会失败。

影响：

- 编译期可能没有完整阻止不可序列化的请求/响应类型。
- 问题延迟到运行时，表现为响应序列化失败或客户端超时。

建议：

- P1：把 analyzer 的 MemoryPack 可序列化判断提升为生成模型的一部分。
- P1：启动时验证 routing table 与 response serializer registry 完整匹配。
- P1：不可序列化类型必须是编译错误，而不是运行时错误。

## 问题 D5：协议号 0 的兼容语义混乱

证据：

- `MessageDispatcher.DispatchAsync` 遇到 `ProtocolId == 0` 直接抛异常：`src/PulseRPC.Server/Processing/Engine/MessageDispatcher.cs:158-161`。
- `ResponseProcessor` 和 `ServiceCallContext` 中仍有注释表达 “0 表示使用方法名路径”。

影响：

- 代码注释、错误消息、兼容设计不一致。
- 后续维护者可能以为字符串方法名路径仍可用。

建议：

- P0：统一协议语义：若字符串路由已移除，删除“0 表示方法名路径”注释。
- P1：若要兼容旧客户端，应在 dispatcher 明确 legacy path，并单独测试。

## 问题 D6：生成器扫描与全局注册需要更明确边界

观察：

- 服务端生成器会扫描用户项目程序集和语法接口。
- 运行时依赖 `ServiceRoutingTableRegistry.Instance` 与 `ResponseSerializerRegistry.Instance`。
- `PulseServerServiceCollectionExtensions` 在 DI 注册时读取静态 registry。

影响：

- 用户项目中只要引用某些接口，就可能生成额外类型。
- 静态注册失败时，运行时才发现缺少 routing table 或 serializer。

建议：

- P1：生成器输出 manifest，列出生成了哪些 service、protocol ID、serializer。
- P1：`AddPulseServer` 启动时校验 manifest，而不是只读静态单例。
- P2：提供 `dotnet` 或 analyzer diagnostic，帮助用户定位“没有生成 routing table”的原因。

## 问题 D7：客户端扩展生成器仍有 TODO

证据：

- `src/PulseRPC.Client.SourceGenerator/Generators/PulseClientExtensionsGenerator.cs:292` 有 `TODO: 实现通用连接获取逻辑`。

影响：

- 生成的客户端扩展 API 可能暴露未完成行为。

建议：

- P1：要么完成通用连接获取逻辑，要么不生成该扩展。
- 生成代码中不要输出 TODO 给用户项目，用户无法判断这是框架问题还是自己代码问题。

## 推荐协议/生成器治理规则

1. 协议号生成必须是双端共享测试验证的纯函数。
2. 生成器不能生成与运行时库 public 类型同名同命名空间的类型。
3. 生成器目标框架优先 `netstandard2.0`。
4. 编译期能发现的问题不延迟到运行时：协议冲突、不可序列化、缺少 response serializer、重复 receiver handler。
5. 生成器输出必须可诊断：生成 manifest、protocol map、serializer map。

