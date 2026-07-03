# 10 · Astra 与 PulseRPC 上游改动清单 · Astra 存废评估

> 本文承接 [09-契约即接口·HubActor 统一模型演进方案](09-契约即接口·HubActor统一模型演进方案.md)，
> 回答两个工程问题：
> 1. 要落地 doc 09 的 HubActor 统一模型，**需要上游 Astra 库与 PulseRPC 库分别做哪些改动**（本仓库无法自行完成、必须由上游处理的部分）。
> 2. **Astra 是否还有必要存在**——把它承担的能力全部并入 PulseRPC 是否可行。
>
> 全文结论基于对两库**真实源码 / 程序集公开 API** 的核对（Astra 源码位于 `E:\Projects\Astra`，
> PulseRPC 位于 <https://github.com/ChronosGames/PulseRPC>）。凡标注**「现状」**为核对所得事实，
> **「目标态」**为 doc 09 所需，**「建议」**为本文的专业判断，**「待确认」**为需与上游对齐的推断。

## 1. 目的与范围

doc 09 提出「实现 `IPulseHub` 即 Actor、Gateway 退化为纯中转」的目标态。本仓库（CrossServerMMO）
只是这两个库的**消费方**，其中相当一部分能力缺口只能由上游库提供。本文把这些缺口**从业务代码中剥离**，
整理成两份可直接交给上游维护者的改动清单，并据此评估 Astra 的存废。

范围**不含**本仓库内可自行完成的改造（那些属于 doc 09 的落地任务），只列**必须上游处理**的项。

## 2. 关键前置发现（含对既有文档的事实校正）

在开清单前，核对源码得到三条影响全局判断的事实，其中两条**修正了 doc 08 / README 的表述**。

### 2.1 Astra 已内置「契约即接口」代码生成器（但未打包、未被本仓库使用）

**现状**：Astra 源码含 `Astra.CodeGen`（`src/Astra.CodeGen`），其能力与 doc 09 「契约即接口」几乎一致：

- `[ActorContract]` 标注接口 → 生成**每方法一个消息 record** + 强类型客户端门面 `{Name}Ref`（转发到
  `IActorRef.TellAsync` / `AskAsync`，`Of(system, key)` 按 key 寻址）。
- `[ActorDispatch]` 标注 `partial` Actor 类 → 生成 `OnReceiveAsync` 分发（消除手写 `switch`）。
- `[Tell]` / `[Ask]` / `[Message(Name=…)]` 修饰方法；`ValueTask` ⇒ Tell、`ValueTask<T>` ⇒ Ask。
- MSBuild 开关 `AstraContractMemoryPack=true` 时给消息 record 贴 `[MemoryPackable]`，以便过 PulseRPC。

**但**：`Astra.CodeGen` **未打进** 本仓库的本地包（`packages/` 只有 `Astra.Abstractions/Core/Cluster/
Diagnostics/Hosting/Persistence.*/Remote.*/Tick/TestKit`，无 `Astra.CodeGen`）；且本仓库 Actor
（`TeamActor`/`PlayerActor` 等）**仍全部手写 `switch`**，未采用该生成器。

**影响**：doc 09 的「契约即接口」在 Astra 侧**并非从零开始**——已有一套现成实现，但它绑定的是
**Astra 自己的 `[ActorContract]` + 生成 record** 模型，而非 `IPulseHub` 模型（见 §2.3 的两套生成器问题）。

### 2.2【校正】Astra 的「PulseRPC 传输」其实是自研裸 TCP，并非 PulseRPC 库

**现状**：`Astra.Remote.PulseRPC`（`services.UsePulseRpcTransport()` 注册的 `PulseRpcTransport`）是一段
**自研的、4 字节大端长度前缀 + `TcpListener`/`TcpClient`** 的实现，其 `.csproj` **只**引用
`Astra.Remote.Abstractions` 与 `Microsoft.Extensions.*`，**完全没有引用任何 `PulseRPC.*` 包**。它只是
「扮演 PulseRPC 角色（play the PulseRPC role）」，名字借用而已。

**校正**：doc 08 / README 中「两条 PulseRPC 平面同源（客户端接入与节点间 remoting 用同一套 PulseRPC）」
的表述**不准确**。真实情况是：

| 平面 | 实际传输实现 | 是否为 PulseRPC 库 |
| --- | --- | --- |
| 客户端接入（Client ↔ Gateway） | `PulseRPC.Server`（真库：`IPulseHub`/源生成器/`IHubContext`） | **是** |
| 节点间 Astra remoting（Node ↔ Node） | `Astra.Remote.PulseRPC` 自研裸 TCP（长度前缀帧） | **否** |

二者仅在「长度前缀 TCP + MemoryPack」这一**风格**上相似，是**两套独立实现**。这条事实使「融合」不再是
「本来就同源、顺水推舟」，而是「**存在重复的传输栈，需要合并**」——这正是本文存废评估的核心动因之一。

### 2.3 PulseRPC 远不止「线上 RPC」，已具备大量 Actor 式运行时能力

**现状**（据公开 API 核对）：PulseRPC 服务端已内置以下与 Actor 运行时**高度重叠**的能力：

- **按 key 的实例激活/回收**：`IPulseHubFactory<THub, TService>.GetOrCreateAsync(serviceId)` / `TryGet` /
  `RemoveAsync(serviceId)` / `GetActiveServiceIds()`——`serviceId` 即等价于 Actor key（注：接口为**双泛型参数**
  `<THub, TService>`，Hub 与 Service 一对一绑定）。
- **单实例串行执行（邮箱）**：`ServiceScenario.Actor`、`ServiceExecutionOptions.Actor`、
  `ServiceSchedulingMode.DedicatedQueue`/`ThreadAffinity`、`MaxConcurrency`、`QueueCapacity`、
  `ServiceBackpressureMode`（`Block`/`DropOldest`/`ThrowException`）、`UnifiedPulseServiceBase.EnqueueAsync(work)`、
  `AffinityAwareScheduler.ScheduleAsync(sessionId, handler)`。**待确认**：据这些 API 判断，PulseRPC 已能对同一
  `serviceId` 串行处理（等价 Astra 单实体单线程邮箱 + `MailboxFullMode` 背压），实现语义需与上游确认。
- **声明式注解**：`[PulseService(Scenario, InstanceScope, SchedulingMode, MaxConcurrency, QueueCapacity,`
  `BackpressureMode, IdleTimeoutSeconds, …)]`——即 doc 09 想要的注解体系雏形。
- **生命周期/被动回收**：`OnStartingAsync`/`OnStoppingAsync`、`ServiceLifecycleState`、`IdleDuration`/
  `LastAccessTime`（空闲 passivation）、`ServiceLifecycleOptions` 预设含 **`GameRoom`/`ChatRoom`**。
- **服务端→客户端推送**：`IHubContext<TReceiver>`/`IHubClients<TReceiver>`（`Single`/`Group`/`User`/`All`）、
  `IGroupManager`、`IUserConnectionMapping`。
- **协议号路由**：`IServiceRoutingTable.RouteByProtocolIdAsync(ushort protocolId, data)`（已按数值方法号分发）。
- **接入侧完备件**：多传输（TCP/KCP）、鉴权（`IAuthenticationService`/`IJwtTokenService`/`IAuthorizationService`）、
  服务发现（**核心库仅内置 `Static`；`Consul`/`Etcd`/`Dns` 目前只是 `ServiceDiscoveryType` 枚举 + `ServiceDiscoveryOptions`
  配置占位，真正的 provider 实现仅存在于 `samples/`，且无 `Kubernetes`——枚举里对应项是 `Custom`**）、
  负载均衡（含一致性哈希）、源生成器（客户端免反射，兼容 Unity）。

**PulseRPC 明确缺失**（源码/API 中无对应类型，均为 Astra 独有）：

- **跨节点单激活归属**：租约/epoch/fencing（Astra.Cluster：`IActorOwnershipStore`、`ClusterNode.ValidateDelivery`、
  `StaleEpochException`）。PulseRPC 的发现+LB 只把请求路由到「某个」实例，**不保证「player 123 全集群仅一个
  owner」**。
- **持久化**：快照/日志/状态版本乐观并发（Astra.Persistence：`IActorSnapshotStore`/`IActorJournal`/
  `PersistentActorBase<T>`/`ActorStateVersionConflictException`）。PulseRPC 无。
- **可持久提醒（durable reminders）**：`IActorReminderStore`/`ReminderService`（重启后存活的定时消息）。PulseRPC 无。
- **固定帧 Tick**：`Astra.Tick`（`OnTickAsync(tick, deltaTime)`、`MaxInputsPerTick`、超帧度量）。PulseRPC 的
  `HighFrequency` 只是调度档位，不是帧循环。

### 2.4 能力矩阵（谁提供什么）

| 能力 | Astra | PulseRPC | 说明 |
| --- | --- | --- | --- |
| 线上传输（TCP/KCP） | 自研裸 TCP（`Astra.Remote.PulseRPC`） | ✅ 一等（多传输） | **重复**：两套 TCP 栈 |
| 源生成器（分发+存根+序列化） | ✅ `Astra.CodeGen`（record 模型，未打包） | ✅ 面向 `IPulseHub`（本仓库已用） | **重复**：两套生成器 |
| 单实体串行邮箱 + 背压 | ✅ `MailboxOptions`/`MailboxFullMode` | ✅ `Actor`/`DedicatedQueue`/`MaxConcurrency`（待确认） | **重复** |
| 按 key 激活 + 空闲回收 | ✅ 虚拟 Actor + `PassivationOptions` | ✅ `IPulseHubFactory`+`IdleTimeout` | **重复** |
| 声明式「Actor 类型」注解 | ✳️ 基类/接口表达（`PersistentActorBase`/`ITickActor`） | ✅ `[PulseService(...)]` | 语义可对齐 |
| 服务端→客户端推送 | ❌（本仓库在 Gateway 层自建） | ✅ `IHubContext`/`IHubClients` | PulseRPC 更强 |
| 鉴权/分组/用户映射/发现/LB | ❌ | ✅ | PulseRPC 独有 |
| **跨节点单激活 + 租约 + epoch** | ✅ `Astra.Cluster` | ❌ | **Astra 独有（命门）** |
| **持久化（快照/日志/版本）** | ✅ `Astra.Persistence` | ❌ | **Astra 独有** |
| **可持久 reminders** | ✅ `ReminderService` | ❌ | **Astra 独有** |
| **固定帧 Tick** | ✅ `Astra.Tick` | ❌ | **Astra 独有** |
| 统一集群寻址（`ActorId`=system/type/key，位置透明） | ✅ | ✳️ 有 serviceId，但非统一集群地址空间 | Astra 更强 |

一句话：**重叠集中在「单机执行 + 分发 + 传输 + 接入 + 推送」这一层（PulseRPC 甚至已内置 `Actor` 档位）；
不重叠的恰是「把 RPC 服务网格变成分布式有状态游戏后端」的那部分——归属、持久化、reminder、tick，全在 Astra。**

## 3. 供 PulseRPC 上游的改动清单

> 目标态：PulseRPC 成为 doc 09「接口即 Actor」的执行/分发/传输/接入底座。以下为需上游处理的项。

| 编号 | 改动 | 现状 | 需要上游做什么 | 阻塞的 doc 09 能力 | 优先级 |
| --- | --- | --- | --- | --- | --- |
| P-1 | 通用 `IPulseHub` 生成器产物三件套 | 已能面向 `IPulseHub` 生成分发+存根+MemoryPack（生成器以「派生自 `IPulseHub` 的接口」为唯一入口，产出客户端存根 `ServiceProxyGenerator` + 服务端 `RoutingTableGenerator` + `ResponseSerializerGenerator`；注：doc 早期草稿引用的 `IGatewayHub` 属**消费方** CrossServerMMO 定义，不在 PulseRPC 仓库内） | 明确/文档化：任意 `IPulseHub` 派生接口都产出「服务端分发 + 客户端存根 + 序列化」，且产物可被服务端按 `serviceId`（=Actor key）路由到实例 | §3/§4 契约即接口 | 高 |
| P-2 | 稳定方法号（MethodId/protocolId）+ 清单 + 漂移校验 | `RouteByProtocolIdAsync(ushort)` 已按数值号分发；**协议号已由生成器确定性分配**（`ProtocolIdGenerator` 用 FNV-1a 哈希方法签名），但**冲突时用线性探测（+1）可能致号漂移**，且**无随代码提交的清单文件、无 CI 校验** | ①冲突稳定化（去掉线性探测的漂移风险）；②将方法↔`ushort` 映射导出为随代码提交的清单文件；③提供 CI 校验「同一方法跨提交号不漂移」（对应 doc 09 §11-Q2 建议） | §4.5 信封寻址 | 高 |
| P-3 | 方法级重入开关 | `MaxConcurrency` 为**实例级** | 增加方法级「只读可重入」标注（默认串行；仅对无副作用查询放开并发），或等价机制（对应 doc 09 §11-Q5 建议） | §4.2 `[Reentrant]` | 中 |<br>**✅ 已实施**：新增 `PulseRPC.ReentrantAttribute`（方法级）；`UnifiedPulseServiceBase` 邮箱改为读写协调（读者并发、写者独占排空在途读者、读写不重叠、写者 FIFO 不饥饿，仅 DedicatedQueue/Actor 模式生效）；新增 `EnqueueReadAsync`/`EnqueueAsync(work, reentrant)` 与 `ServiceAccessor.ExecuteReadAsync`；源生成器读取 `[Reentrant]` 并在 `ServiceRoutingTable.Statistics` 暴露静态 `IsReentrant(ushort)`（非破坏性，未改 `IServiceRoutingTable`）；含单元测试。 |
| P-4 | 服务端→客户端**反向 Ask（Call）** | `IPulseReceiver`/`IHubContext` 仅单向 push（`Task` 返回、无返回值语义） | 支持服务端发起、等客户端应答的请求-响应：`RequestId` 关联、超时、**断线兜底**（对应 doc 08 §6.4 / doc 09 构建6） | 服务端 Ask 客户端 | 中 |
| P-5 | 只读信封头的**地址中转** API | 目标实例 key 需进入处理管线才可知 | 暴露「只读头（Hub/Key/MethodId）→ 转发到目标节点/实例、不反序列化 body」的中转原语，供 Gateway 作纯中转（doc 09 §5.1） | §5 Gateway 纯中转 | 高 |
| P-6 | 客户端可见性门闸原语 | 有 `IAuthorization*`，但无「接口/facet 级 client-facing 白名单」的生成器+强制链 | 支持：facet 级 `client-facing` 标注 → 生成器产出白名单 → 中转层编译期强制（doc 09 §5.2；把「协议框架」与「业务鉴权」分离） | §5.2 可见性门闸 | 中 |
| P-7 | 跨节点**单激活归属**（若走「全并入 PulseRPC」） | 只有发现+LB（含一致性哈希），路由到「某实例」 | 引入 ownership/lease/epoch，保证同一 `serviceId` 全集群唯一 owner + fencing | 有状态实体权威性 | 高（仅 Option A 需要） |
| P-8 | **持久化**（快照/日志/版本乐观并发，若走「全并入」） | 无 | 引入等价 `Astra.Persistence` 的抽象与实现 | 有状态 Actor | 高（仅 Option A 需要） |
| P-9 | **可持久 reminders**（若走「全并入」） | 无（仅连接/调度层 timeout） | 引入重启存活的定时消息 | 定时/延迟业务 | 中（仅 Option A 需要） |
| P-10 | **固定帧 Tick**（若走「全并入」） | 有 `HighFrequency` 调度档，但非帧循环 | 引入 `OnTick(tick, delta)` 帧模型 | Scene 模拟 | 中（仅 Option A 需要） |

> P-1..P-6 无论选哪种最终形态都需要；**P-7..P-10 仅在「取消 Astra、全并入 PulseRPC」（§6 Option A）时才需要**，
> 是本评估的分水岭。

## 4. 供 Astra 上游的改动清单

> 目标态：Astra 要么与 PulseRPC 对齐并复用其底座（推荐），要么补齐 doc 09 所需的注解与接入点。

| 编号 | 改动 | 现状 | 需要上游做什么 | 阻塞的 doc 09 能力 | 优先级 |
| --- | --- | --- | --- | --- | --- |
| A-1 | 打包 / 或退役 `Astra.CodeGen` | 生成器存在但未打包，本仓库无法使用 | 二选一：**打包发布** `Astra.CodeGen`；或**退役**它、统一改用 PulseRPC 生成器（见 §6 推荐） | 契约即接口 | 高 |
| A-2 | 分发接入点对齐 | Astra 生成 `{Name}Ref` 门面 + **自有 record** 模型（`ValueTask`/`ValueTask<T>`） | 与 `IPulseHub` 模型统一：对齐 `Task` vs `ValueTask`、record-based vs protocolId-based；暴露「把方法调用投递进邮箱」的稳定 hook（doc 09 §11-Q1：代理是语法糖，真实路径仍入邮箱） | 契约即接口 | 高 |
| A-3 | 「Actor 关注点」注解化 | 用基类表达（`ActorBase`/`PersistentActorBase<T>`/`ITickActor`）+ 外部注入 `IPlacementStrategy` | 增加声明式注解（`[Stateful]`/`[Stateless]`/`[Placement]`/`[Tick]`…）并由 hosting 读取装配，把「选基类/手注册放置」降级为「打注解」（doc 09 §4.2） | §4.2 注解体系 | 中 |
| A-4 | **退役自研裸 TCP 传输**，复用 PulseRPC | `Astra.Remote.PulseRPC` 是自研 TCP（§2.2） | 让节点间 remoting 真正跑在 PulseRPC 传输上，消除「两套 TCP 栈」；同步修正文档「同源」表述 | 传输单一化 | 高 |
| A-5 | 内建 explicit-node 放置 + 边缘代理 Actor | 只给 `IPlacementStrategy` 接口，explicit-node 由本仓库 `KeyedPlacementStrategy` 自实现；无「连接即 Actor」概念 | 一等支持 explicit-node 放置（key 编码目标节点，如 `{gatewayId}:{playerId}`）与 ClientActor 边缘桥；或明确此职责交给 PulseRPC 的 `IHubContext` | §4.4 ClientActor / §4.5 放置 | 中 |
| A-6 | 集群寻址与 PulseRPC serviceId 对齐 | `ActorId`=system/type/key 与 PulseRPC `serviceId` 是两套地址 | 定义 `ActorId ⇄ serviceId` 的确定映射，使 Astra 的归属/持久化能挂到 PulseRPC 托管的实例上 | 两库拼装 | 高（Option C 前提） |

## 5. 改动后的职责边界（目标态）

无论选 Option A 还是 C，边界都应收敛为**一处执行、一处传输、一份契约**：

```text
契约（IPulseHub 接口 + 方法号清单）
        │  ← 唯一真源：客户端存根 / 服务端分发 / 中转目录 三态合一
        ▼
PulseRPC：传输(TCP/KCP) · 源生成器分发 · 单实例串行执行(Actor 档) · 背压 · 空闲回收 · 服务端推送 · 鉴权/分组
        │  ← serviceId = Actor key
        ▼
Astra（仅在 Option C 保留）：跨节点单激活归属+租约+epoch · 持久化(快照/日志/版本) · durable reminders · 固定帧 Tick · 统一 ActorId 寻址
```

## 6. 评估：Astra 是否可以不存在（全在 PulseRPC 内支持）？

把三种候选形态摆开对比：

| 维度 | Option A：取消 Astra，全并入 PulseRPC | Option B：维持现状（两套并行） | **Option C：PulseRPC 作底座 + Astra 瘦身为分布式有状态层（推荐）** |
| --- | --- | --- | --- |
| 需上游做什么 | PulseRPC 需补 P-1..P-10（含 ownership/持久化/reminder/tick 四大块） | 基本不动，但重复不消除 | PulseRPC 补 P-1..P-6；Astra 做 A-1..A-6（退役重复件、对齐 serviceId） |
| 重复消除 | 彻底（只剩 PulseRPC） | 无（两套 mailbox/传输/codegen 长期并存） | 彻底（执行/传输/codegen 归 PulseRPC；Astra 只留不重叠能力） |
| 把 Actor 复杂度强加给通用 RPC 用户 | **是**（PulseRPC 非游戏用户被迫背负归属/持久化/tick） | 否 | 否（分布式有状态能力是**可选上层**，不侵入 PulseRPC 内核） |
| MMO 权威性/一致性风险 | **最高**（单激活/持久化要在 PulseRPC 里重造，是命门） | 低（Astra 成熟能力保留） | 低（沿用 Astra 成熟的 cluster/persistence） |
| 工作量与周期 | 最大（相当于让 PulseRPC 长成完整 Actor 框架） | 最小（但不解决问题） | 中（主要是「删重复 + 拼接层」） |
| 与 doc 09「契约单一化」目标 | 满足 | **不满足** | 满足 |

**结论（建议）**：**不建议直接取消 Astra（Option A）。** 理由：

1. **PulseRPC 缺的恰是命门**。取消 Astra 意味着要在 PulseRPC 内**重造**跨节点单激活归属（租约/epoch/fencing）、
   持久化（快照/日志/版本乐观并发）、durable reminders、固定帧 Tick（P-7..P-10）。这四块正是保证「player 123
   全集群唯一权威写者、崩溃可恢复」的基础——在通用 RPC 库里重做，风险与成本都最高。
2. **会污染 PulseRPC 的定位**。PulseRPC 面向「Unity 游戏 + 微服务」的通用 RPC 用户；把游戏专有的有状态
   actor 语义压进其内核，会让大量非游戏用户背负不需要的复杂度。
3. **重复其实出在「执行/传输/分发」层，而非「有状态」层**。真正该消除的是 §2.4 里标「重复」的四行
   （两套 TCP、两套生成器、两套邮箱、两套激活）——这靠 Option C 即可解决，无需牺牲 Astra 的独有能力。

**推荐 Option C：让 PulseRPC 成为唯一的「执行 + 分发 + 传输 + 接入 + 推送」底座，Astra 瘦身为其之上的
「分布式有状态层」。** 即：

- 退役 `Astra.Remote.PulseRPC` 自研 TCP（A-4）、退役或让位 `Astra.CodeGen`（A-1），Astra actor 的
  「单实例串行执行」直接由 PulseRPC 的 `Actor`/`DedicatedQueue` 档承载。
- 一个 Astra actor = **一个受 PulseRPC 托管、`serviceId = ActorId`、串行执行的 hub 实例**，再叠加 Astra 提供的
  **归属（Astra.Cluster）+ 持久化（Astra.Persistence）+ reminder + tick（Astra.Tick）**。
- 净效果不是「取消 Astra」，而是「**Astra 去掉与 PulseRPC 重复的运行时，只保留 PulseRPC 不具备的分布式
  有状态能力**」——保留 `Astra.Cluster`/`Astra.Persistence`/`Astra.Tick`，收缩 `Astra.Core`（邮箱）/
  `Astra.Remote`（传输）/`Astra.CodeGen`（分发）。

> 何时才考虑 Option A：仅当 PulseRPC 上游**明确愿意**成为一个完整 Actor 框架（接纳 P-7..P-10），
> 且团队接受「一个库通吃」的耦合代价时。否则 Option C 在「消除重复」与「保住命门」之间取得最佳平衡。

## 7. 上游改动汇总（按是否阻塞推荐路径）

| 归属库 | 推荐路径（Option C）必需 | 仅 Option A 需要 |
| --- | --- | --- |
| PulseRPC | P-1 通用 `IPulseHub` 生成器；P-2 稳定方法号+清单；P-4 反向 Ask；P-5 头部地址中转；P-6 可见性门闸（P-3 重入为增强） | P-7 单激活归属；P-8 持久化；P-9 reminders；P-10 Tick |
| Astra | A-1 退役/让位 CodeGen；A-2 分发接入点对齐；A-4 退役自研 TCP；A-6 `ActorId⇄serviceId` 对齐（A-3 注解化、A-5 边缘代理为增强） | —（Option A 下 Astra 整体退役） |

## 8. 风险与依赖

- **对上游节奏的强依赖**：P-4（反向 Ask）、P-5（头部中转）、P-6（可见性门闸）若上游不排期，doc 09 的
  「Gateway 纯中转 + 服务端 Ask 客户端」无法完整落地。建议先与 PulseRPC 维护者确认这三项的可行性与排期。
- **`Actor` 档语义待确认**：§2.3 中「PulseRPC 已按 `serviceId` 串行执行」是据 API 名称推断（`ServiceScenario.Actor`/
  `DedicatedQueue`/`MaxConcurrency`/`AffinityAwareScheduler`）。**落地前须用 samples/基准确认**其并发与顺序保证，
  否则 doc 09「方法调用天然进单线程邮箱」的前提不成立。
- **`ActorId ⇄ serviceId` 映射一致性（A-6）**：Option C 的拼接点。映射不一致会导致「归属层认为在 A 节点、
  执行层实例在 B 节点」的脑裂，须有单一真源。
- **文档一致性**：§2.2 的校正需回填到 doc 08 与 README（「两条平面同源」应改为「两套独立传输、风格相近」），
  避免后续设计沿用错误前提。

## 9. 待定问题与建议

1. **PulseRPC 是否愿意接纳有状态 actor 能力（P-7..P-10）？** 建议：先只推动 P-1..P-6，把 Option C 落地；
   P-7..P-10 作为「若上游有意向再议」的长期选项，不阻塞主线。
2. **`Astra.CodeGen` 退役还是并行？** 建议：**退役**，统一到 PulseRPC 生成器，避免两套生成器长期割裂
   （与 doc 09「契约单一化」一致）；`[ActorContract]` 模型的语法糖（`{Name}Ref` 门面）可作为迁移期
   的兼容外观由 PulseRPC 生成器等价产出。
3. **Astra 独有能力挂载方式？** 建议：以「PulseRPC hub 实例 + Astra 提供的归属/持久化/tick 装饰」组合，
   而非在 Astra 内复制 PulseRPC 的执行栈——即 Astra 提供的是**能力（capabilities）**而非**运行时（runtime）**。
4. **谁来定义 `serviceId` 命名规范？** 建议：由本仓库（消费方）统一定义 `ActorId → serviceId` 编码
   （如 `{system}/{type}/{key}`），并把 explicit-node 前缀（`{gatewayId}:{playerId}`）纳入该规范，作为 A-6 的输入。

## 相关文档

| 文档 | 内容 |
| --- | --- |
| [09-契约即接口·HubActor 统一模型演进方案](09-契约即接口·HubActor统一模型演进方案.md) | 本文所服务的目标态：`IPulseHub` 即 Actor、Gateway 纯中转、注解体系、契约单一化、一次性整体切换 |
| [08-Actor 运行时与 PulseRPC 融合·ClientActor 演进](08-Actor运行时与PulseRPC融合·ClientActor演进.md) | 前置论证：Tell/Ask↔Event/Call 语义等价、ClientActor 边缘桥（其「两条平面同源」表述由本文 §2.2 校正） |
| [01-技术选型与基础设施](01-技术选型与基础设施.md) | Astra + PulseRPC + MemoryPack 选型（本文重估两者的职责边界） |
| [02-项目结构与核心模块](02-项目结构与核心模块.md) | 分层依赖方向；本文改动后的 PulseRPC/Astra 职责落点 |
| [03-服务状态与线程模型](03-服务状态与线程模型.md) | 状态分级与单实体单线程模型（`[Stateful]`/`Actor` 档与单激活归属的依据）|
| [07-网关·会话·目录服务详细设计](07-网关·会话·目录服务详细设计.md) | Gateway/Session/Directory 现状（P-5 头部中转、P-6 可见性门闸的改造对象）|
