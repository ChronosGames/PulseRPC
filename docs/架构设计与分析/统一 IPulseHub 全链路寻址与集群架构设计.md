# 统一 IPulseHub 全链路寻址与集群架构设计（设计评审稿）

> 状态：**设计定稿（Decisions Locked，2026-07-04）** —— 本文档只做设计，不涉及任何代码改动；§15.2 的 8 项关键决策已拍板（见 §0）。
> 目标读者：架构评审、核心维护者。
> 关联文档：
> - [IPulseHub 统一架构使用指南](./IPulseHub%20统一架构使用指南.md)（愿景态使用指南）
> - [服务器间通信设计文档 - 全双工对等架构](./服务器间通信设计文档%20-%20全双工对等架构.md)（全双工对等 RPC 愿景）
> - [PulseRPC.Server 架构设计深度分析](./PulseRPC.Server%20架构设计深度分析.md)

---

## 0. 决策定稿（2026-07-04）

以下 8 项关键决策已拍板，正文各章已据此改写为"已定稿"。本表为速查，细节见对应章节。

| # | 决策项 | 定稿 | 对应章节 |
|---|---|---|---|
| 1 | 消歧覆盖特性 | **C2：`[PulseHub(Provide = true, Consume = true)]`（单特性双布尔）** | §5.2-C |
| 2 | 协议号空间 | **P1：按 Hub 接口隔离** | §11.2 |
| 3 | Backplane 扩散模型 | **抽象层同时支持 X+Y；默认实现走 X（pub/sub），并一步到位同时实现 Y（全局目录）** | §9.2 |
| 4 | Actor 一致性级别 | **L2：目录 + 租约（single-activation）** | §8.2 / §8.3 |
| 5 | 投递保证 | **三级可选：至多一次 / 至少一次 / 精确一次（按方法或调用选择）** | §10.3 |
| 6 | `IPulseReceiver` 处置 | **M2：硬移除（直接删除，不保留 `[Obsolete]` 兼容）** | §13.1 |
| 7 | 服务发现 | **先静态配置成员**（动态发现推迟到 P8） | §8.1 / §14-P8 |
| 8 | 节点互信鉴权 | **首版共享密钥（预共享 token/HMAC），生产用 mTLS，抽象为 `INodeAuthenticator`** | §12 |

> ⚠️ 决策 6（M2 硬移除）是**破坏性变更**：需配合大版本号跃迁与迁移指南；决策 4（L2）与 5（精确一次）显著增加实现复杂度，路线图（§14）已相应前移/加重。

---

## 1. 目的与范围

### 1.1 需求来源

在 `PulseService`（Actor）实现类中，希望**直接用统一的 `IPulseHub` 代理**向以下三类目标发送消息，取代当前注入 `IHubContext<TReceiver>` 的写法：

1. **远程客户端**（含向"自己这个 Hub 对应的接收方"广播）；
2. **同进程内的其它 Actor**；
3. **跨节点（经 Gateway 桥接）的其它 Actor / 远程客户端**。

并**移除 `IPulseReceiver`**，全框架只保留一个标记接口 `IPulseHub`；重新设计
`PulseRPC.Client.SourceGenerator` 与 `PulseRPC.Server.SourceGenerator`，制定配套规则，让生成器识别每个
`IPulseHub` 接口需要生成 **ClientStub / ServerSkeleton / 两者都要**。

### 1.2 本次范围（已与需求方确认）

- **最大范围**：客户端↔服务端 + 同进程 Actor↔Actor + **跨节点集群 Actor↔Actor 远程调用**，一步到位统一寻址模型；
- **预留 Gateway**：需要一个 Gateway 组件把远程客户端桥接进 Actor 网格，使 Actor↔Actor 端到端打通全链路；
- 本轮交付物 = **本设计文档**，供评审；评审通过后再进入实现阶段。

### 1.3 不在本文档范围

- 具体的编译期语法树遍历实现细节；
- 具体 backplane 后端（Redis/NATS 等）的选型 PoC；
- 性能基准测试方案（后续单独文档）。

---

## 2. 现状核实（基于源码，务必对齐）

> 下列结论均通过阅读源码核实，是本设计的事实基线。**若基线判断有误，请评审时首先纠正。**

### 2.1 已具备（可复用的资产）

| 能力 | 位置 | 说明 |
|---|---|---|
| **寻址三元组已在线协议中** | `MessageHeader`（`ServiceName`=Hub / `ServiceKey`=实例键 / `ProtocolId`=方法号）| Actor 实例键 = `"ServiceName:BusinessId"` 中的 `BusinessId` |
| **零拷贝仅头部中转原语** | `EnvelopeRelay` + `ReadOnlyEnvelopeHeader` | 支持只解析头、不反序列化 body 转发；`WithKey`/`WithHub` 改写目标 |
| **运行时方向语义** | `CallSourceType{InternalService, ExternalUser, SystemTimer, AdminConsole}`、`ClientFacingGate` | 已能区分内部/外部调用；`[ClientFacing]` 白名单编译期生成 |
| **协议号哈希（纯函数）** | 两个生成器的 `ProtocolIdGenerator`（FNV-1a 32→16bit）| 客户端/服务端各自独立编译，靠"相同签名→相同哈希"保证一致 |
| **全双工对等 RPC 愿景** | `TransportChannelBase`（`GetHubAsync<T>` + `RegisterHub<T>`）| 单连接双向调用的设计已在文档中，部分接口已存在 |
| **本地 Actor 运行时** | `PulseServiceManager`（`"Type:Id"` → 实例）、`IServiceAccessor<T>`、邮箱串行/可重入读 | Actor 生命周期、Tick、串行一致性均已实现 |

### 2.2 缺失 / 未接线（本设计要补齐的核心）

| 缺口 | 现状证据 | 影响 |
|---|---|---|
| **Gateway 未接线** | 全仓无"我是 Gateway / node→node 转发表 / 远程 actor 寻址"实现，`EnvelopeRelay` 无调用者 | 远程客户端无法桥接进 Actor 网格 |
| **服务发现是空壳** | `Infrastructure`、`Infrastructure.Consul/.Etcd/.Kubernetes` 四个项目**只有 bin/obj 无 .cs**；`IServiceDiscovery`、`IMultiInstanceServiceManager<T>` 仅接口无实现 | 无法定位"某 Actor 在哪个节点" |
| **Actor 实例是节点本地** | `PulseServiceManager._instances` = 进程内 `ConcurrentDictionary`，无跨节点查找/转发，无 placement/directory | 跨节点 Actor↔Actor 不可达 |
| **路由不按 ServiceKey 选实例** | 服务端路由表用 `serviceProvider.GetService(typeof(接口))` 取**单例无状态 Hub**；keyed-actor 由 Hub 内手动 `IServiceAccessor.GetAsync(serviceId)` 选择 | 框架层没有"按(Hub,Key)自动路由到属主 Actor/节点" |
| **广播/分组/连接注册全进程内** ⚠️ | `GroupManager`、`UserConnectionMapping`、`ServerChannelManager` 均为 `ConcurrentDictionary`，只覆盖连到本节点的连接 | **多节点/Gateway 下 `All/Group/User/Except` fan-out 会静默漏掉其它节点成员** |
| **多跳回执缺字段** | `MessageHeader` 只有 `MessageId` 关联，**无 `SourceNodeId`/`ReplyTo`**；假设"响应沿同一连接单跳返回" | client→gateway→backend actor 多跳的请求/响应（含 reverse-Ask）回执路径无法建模 |

### 2.3 结论：本工作 = 小头（统一标记）+ 大头（分布式 Actor 网格）

- **小头（可控）**：统一 `IPulseHub`、移除 `IPulseReceiver`、改两个生成器按规则产出 Stub/Skeleton。
- **大头（近乎从零）**：跨节点集群 Actor↔Actor + Gateway 桥接 + 跨节点广播，本质是**在"单节点 RPC + 本地 Actor 缓存"之上新建一套分布式 Actor 运行时**（可类比 Orleans / Proto.Actor / Akka.Cluster）。需新建：Actor placement/目录、节点间转发、**分布式 backplane**、多跳回执关联。

### 2.4 与两份既有设计文档的关系

- 《IPulseHub 统一架构使用指南》描述了**目标使用姿态**（全部统一 `IPulseHub` + `[Channel]` + `[Authorize(Role)]`），但**代码未落地**：实际 `IPulseReceiver` 未继承 `IPulseHub`，生成器仍按双标记分支。→ 本文档把它从"使用指南"补全为"可实现的架构规格 + 消歧规则 + 生成器设计"。
- 《服务器间通信设计文档 - 全双工对等架构》给出了 **server↔server 全双工对等** 的连接层设计，但其"集群"仅为**朴素遍历连接池广播**，且把"服务发现"列为后续扩展，**未解决跨节点广播/分组、Gateway 桥接远程客户端、多跳回执**。→ 本文档在其连接层之上补齐寻址/backplane/gateway/回执。

---

## 3. 目标架构总览

### 3.1 一句话模型

> **一切远程可调用契约都是 `IPulseHub`；一切"发送"都是"对一个地址（Address）发起一次协议号调用"。**
> 地址统一抽象为 `PulseAddress`，由框架把它解析为"本地 Actor / 本地连接 / 本地连接集合（fan-out）/ 远程节点（经 Gateway 或直连）"，调用方代理与被调方骨架都由源生成器产出。

### 3.2 分层视图

```
┌─────────────────────────────────────────────────────────────────┐
│  契约层 (Shared)：interface IXxxHub : IPulseHub                    │
│  + [Channel(provider)] / [Authorize(Role)] / [ClientFacing] 声明   │
└─────────────────────────────────────────────────────────────────┘
        │ 源生成器（按消歧规则产出）
        ▼
┌──────────────────────┐        ┌──────────────────────────────────┐
│ 调用方代理 (Proxy)     │        │ 被调方骨架 (Skeleton/Invoker)      │
│  - ClientStub(单连接)  │        │  - ServerSkeleton(协议号→方法)     │
│  - Fan-out 选择器      │        │  - ClientDispatcher(客户端接收)    │
│  - Actor 代理(按 Key)  │        │                                   │
└──────────────────────┘        └──────────────────────────────────┘
        │ 统一寻址：IPulseRouter.Send/Ask(PulseAddress, protocolId, body)
        ▼
┌─────────────────────────────────────────────────────────────────┐
│  路由与传输层                                                       │
│   本地 Actor 邮箱 │ 本地连接 │ Fan-out(+Backplane) │ 远程节点/Gateway │
└─────────────────────────────────────────────────────────────────┘
        │
        ▼
┌────────────┐   node↔node   ┌────────────┐   桥接远程客户端   ┌────────┐
│  Backend A │◀────────────▶│  Backend B │◀────────────────│ Gateway │◀── 客户端
└────────────┘               └────────────┘                  └────────┘
       └──────── Backplane（分组/用户映射/广播的跨节点扩散）────────┘
```

### 3.3 三种"调用方代理"的目标拓扑（关键）

统一后，"调用方代理"不是一种，而是**按地址种类**产出三种形态（生成器需分别支持）：

| 拓扑 | 地址种类 | 典型场景 | 语义 |
|---|---|---|---|
| **单连接** | `Connection(connId)` | 客户端→服务端；服务端→单个客户端 | 请求/响应或单向 |
| **Fan-out** | `All / Group(g) / User(u) / Except(...)` | 服务端→一批客户端（广播/组播）| 单向为主；跨节点需 backplane |
| **Actor 寻址** | `Actor(hub, key)` | Actor→Actor（本地或跨节点）| 请求/响应或单向；跨节点需 placement + 转发 |

---

## 4. 统一寻址模型（PulseAddress）

### 4.1 线上寻址三元组（复用现有 `MessageHeader`，新增回执字段）

现有：`ServiceName(Hub)` + `ServiceKey(实例键)` + `ProtocolId(方法号)`。

**新增（本设计提议）**，用于多跳与回执：

| 字段 | 用途 | 兼容性 |
|---|---|---|
| `SourceNodeId`（string）| 发起节点标识，供多跳回执寻径 | MemoryPack 尾部新增，旧端反序列化为空 → 退化为单跳 |
| `ReplyTo`（string，可选）| 显式回执地址（node/connection/actor），覆盖"沿原连接返回"默认 | 同上 |
| `CorrelationId`（复用 `MessageId`）| 跨跳请求/响应关联 | 已存在 |
| `Ttl`/`HopLimit`（byte，可选）| 防止转发环路 | 尾部新增，默认 0=不限（或设默认上限） |

> 设计原则：**保持 `MessageId` 作为端到端关联键不变**；`SourceNodeId + ReplyTo` 让中间节点/Gateway 知道"响应该发回哪里"，从而支持 `client → gateway → backendA(actor) → backendB(actor)` 的多跳请求/响应。

### 4.2 运行时地址抽象（新增 `PulseAddress`）

```csharp
// 提议：PulseRPC.Abstractions 新增
public readonly struct PulseAddress
{
    public AddressKind Kind { get; }        // Connection | AllClients | Group | User | Except | Actor | Node
    public string Hub { get; }              // 目标 Hub（ServiceName）
    public string Key { get; }              // Actor 实例键 / groupName / userId / connectionId
    public string? NodeId { get; }          // 可选：显式目标节点（否则由 placement 解析）
    // 便捷工厂：Connection(connId) / Actor(hub,key) / Group(g) / User(u) / All(hub) / Except(...)
}
```

- **调用方代理只产出 `PulseAddress` + `protocolId` + 序列化后的 body**，交给统一路由 `IPulseRouter`；
- `IPulseRouter` 负责把 `PulseAddress` 解析成实际投递动作（见 §7）。

### 4.3 地址解析优先级

1. `NodeId` 显式指定 → 直接走该节点（本地或 node↔node 转发）；
2. `Kind == Actor` 且无 `NodeId` → 查 **Actor 目录/placement**（§8）得到属主节点：本地则入邮箱，远程则转发；
3. `Kind ∈ {AllClients, Group, User, Except}` → 本地连接直投 + **Backplane 扩散**到其它节点（§9）；
4. `Kind == Connection` → 本地有该连接直投；否则查连接注册表（可能是 Gateway 上的虚拟连接，§6）转发。

---

## 5. IPulseHub 统一与方向消歧

### 5.1 统一后契约写法

```csharp
// 客户端实现、服务端推送（原 IPulseReceiver）
[Channel("CLIENT")]
public interface IChatClient : IPulseHub
{
    Task OnMessage(ChatMessage msg);         // 单向推送
    Task<bool> ConfirmKick(string reason);   // 反向 Ask（服务端等客户端应答）
}

// 服务端实现、客户端/其它 Actor 调用（原 IPulseHub）
[Channel("ChatServer")]
[Authorize(Role = RoleTypes.External)]
public interface IChatRoomHub : IPulseHub
{
    Task<JoinResult> JoinAsync(string roomId);
    [Authorize(Role = RoleTypes.Internal)]   // 仅服务器间可调
    Task BroadcastSystemAsync(string text);
}
```

### 5.2 消歧规则（复用既有标记，避免新造 Direction 枚举）

需求方倾向：**复用 `[Channel]` / `RoleTypes.Internal|External` / `[PulseClientGeneration]` / `[ClientFacing]`**。据此定义如下**分层消歧规则**（编译期，生成器可判定）：

#### 规则 A —— "谁提供"由 `[Channel]` 决定（主规则）

- `[Channel("CLIENT")]` ⇒ **客户端提供实现**。
  - 客户端侧生成器：产出 **ClientSkeleton（Dispatcher，被调方）**。
  - 服务端侧生成器：产出 **调用方代理（Fan-out 选择器：All/User/Group/Except/Single）** —— 即原 `ReceiverProxy + HubContext + HubClients` 的统一替代。
- `[Channel("XxxServer")]`（非 CLIENT）⇒ **该服务端角色提供实现**。
  - 提供方节点的服务端生成器：产出 **ServerSkeleton（协议号→实现方法）**。
  - 调用方（客户端 或 其它服务端角色）生成器：产出 **调用方代理**：
    - 客户端项目 ⇒ **ClientStub（单连接）**；
    - 服务端项目（跨服务器调用）⇒ **Actor/Node 代理（按 Key/Node 寻址）**。

#### 规则 B —— "我是哪一侧"由**编译上下文**决定（天然区分）

- `PulseRPC.Server.SourceGenerator` 只在服务端项目运行；`PulseRPC.Client.SourceGenerator` 只在客户端项目运行；
- 客户端侧通过 `[PulseClientGeneration(typeof(MarkerInSharedAsm))]` 指向 Shared 程序集触发扫描。
- ⇒ **同一个契约，在客户端项目里默认生成"调用方 Stub 或 客户端 Skeleton"，在服务端项目里默认生成"服务端 Skeleton 或 Fan-out 调用方代理"**，由 §5.2-A 的 `[Channel]` 值进一步区分。

#### 规则 C —— "双向/Actor 自调用"由 `RoleTypes` + 显式特性覆盖

- 服务端 Actor 接口常常**既被调用（骨架）又要调用同类其它实例（代理）**，即"两者都要"。
  - 默认：一个 `[Channel("XxxServer")]` 接口在**提供方节点**同时生成 **ServerSkeleton + Actor 调用方代理**（因为本节点既实现它、又可能寻址同类其它 Key/Node 实例）。
- 若默认推断不符预期，用**显式覆盖特性**（**定稿：C2**）：

  ```csharp
  [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
  public sealed class PulseHubAttribute : Attribute
  {
      /// <summary>本编译侧是否为该 Hub 生成"被调方骨架"（提供实现）。</summary>
      public bool Provide { get; set; } = true;
      /// <summary>本编译侧是否为该 Hub 生成"调用方代理"（发起调用）。</summary>
      public bool Consume { get; set; } = true;
  }
  ```

  - 语义：`Provide`/`Consume` 是**同一维度的两个属性**，用单特性双布尔承载，最内聚、便于未来扩展（如追加 `Placement`、`SingleActivation`）。
  - 绝大多数接口**无需标注**——靠 §5.2-A（`[Channel]`）+ §5.2-B（编译侧）默认推断即可；`[PulseHub(...)]` 只用于**少数歧义场景**（Actor 自调用、纯 Shared 双向契约）的显式覆盖。默认不写 = 走推断。
  - （已否决 C1：两个独立特性 `[GenerateStub]/[GenerateSkeleton]` 易出现"只标一个"的半配置状态，叠加语义不如布尔直观。）
- `[Authorize(Role=Internal)]` / `[ClientFacing]` **不做方向消歧的唯一依据**，仅作**鉴权门闸**（见 §11），但可作为"是否为该接口生成外部客户端 Stub"的**过滤器**（如整个接口 `Internal` 且无任何 `[ClientFacing]` 方法 ⇒ 客户端项目不生成 Stub）。

> **明确否决**：不用"返回类型编码方向"（`Task<T>`=请求响应、`Task`=推送）。因为框架已用返回类型区分"反向 Ask(`Task<T>`)"与"单向命令(`Task`)"，两者正交于通信方向。

### 5.3 消歧规则决策表（生成器实现依据）

| 契约标注 | 编译侧 | 生成物 |
|---|---|---|
| `[Channel("CLIENT")]` | 客户端 | ClientSkeleton（Dispatcher）|
| `[Channel("CLIENT")]` | 服务端 | Fan-out 调用方代理（All/User/Group/Except/Single）|
| `[Channel("SvrX")]` | 服务端(=SvrX 提供方) | ServerSkeleton（+ 若可自调用则叠加 Actor 调用方代理）|
| `[Channel("SvrX")]` | 服务端(≠SvrX，跨服调用) | Actor/Node 调用方代理 |
| `[Channel("SvrX")]` | 客户端 | ClientStub（单连接），除非全 `Internal` 且无 `[ClientFacing]` |
| 显式 `[PulseHub(Provide, Consume)]` | 任一 | 覆盖上表默认（分别控制是否产出骨架/代理）|

---

## 6. Gateway 设计（桥接远程客户端进入 Actor 网格）

### 6.1 角色定位

Gateway = **一个特殊的 PulseServer**，对外接受客户端连接、对内与 backend 节点建立 node↔node 全双工连接（复用《全双工对等架构》连接层），职责：

1. **终结客户端连接**：完成握手/鉴权，得到外部用户身份（`CallSourceType.ExternalUser`）；
2. **虚拟连接注册**：在 backend 侧把"远程客户端"表示为**虚拟连接（Virtual Connection）**，使 backend 的 Actor 可以像对待本地连接一样对它 `Send/Ask`；
3. **帧中转**：对 Actor 寻址帧/推送帧，用 `EnvelopeRelay` **只读头部零拷贝转发**（必要时 `WithKey/WithHub` 改写），不反序列化 body；
4. **回执寻径**：依据 `SourceNodeId/ReplyTo`（§4.1）把响应/反向 Ask 结果送回正确的客户端连接。

### 6.2 虚拟连接（Virtual Connection）

- backend 节点的 `ServerChannelManager` 需支持注册**虚拟连接**：`connectionId = gatewayNodeId + originalConnId`，其 `SendAsync` 实为"打包成 node↔node 帧发回 Gateway，由 Gateway 投递给真实客户端"。
- 好处：**backend 上的 Fan-out 选择器无需感知客户端是否远程** —— 对虚拟连接 `Send` 即可，Gateway 负责最后一跳。
- 代价：连接注册表（本地/虚拟）+ 用户映射/分组需**跨节点可见**（见 §9 Backplane），否则 `Group/User` 仍会漏。

### 6.3 Gateway 数据面（零拷贝路径）

```
客户端帧到达 Gateway
  → EnvelopeRelay.TryReadHeader（只读 Hub/Key/MethodId，不解 body）
  → 解析 PulseAddress：
       Actor(hub,key) → 查 Actor 目录得到 backend 节点 → WithKey(规范化) → 转发原始帧
       Connection/自身 Hub → 本地处理
  → 注入 SourceNodeId=GatewayId, ReplyTo=客户端虚拟连接地址
  → node↔node 发送（body 原样拼接，零拷贝）
backend 响应帧
  → 依据 ReplyTo 回到 Gateway
  → Gateway 依据原始 connId 投递给真实客户端
```

### 6.4 Gateway 与安全边界

- Gateway 是**信任边界**：外部客户端只能触达 `[ClientFacing]` 白名单方法（`ClientFacingGate` 在 backend 强制，见 §11）；
- Gateway 转发的 Actor↔Actor 内部帧标记 `CallSourceType.InternalService`，但**必须防止外部客户端伪造 Internal**（Gateway 负责重写/钳制来源标记，backend 信任来自可信 Gateway 的标记）。

---

## 7. 统一路由层（IPulseRouter）

### 7.1 抽象

```csharp
// 提议：PulseRPC.Abstractions / Server
public interface IPulseRouter
{
    // 单向发送
    ValueTask SendAsync(in PulseAddress addr, ushort protocolId, ReadOnlyMemory<byte> body, CancellationToken ct = default);
    // 请求/响应（含 Actor 反向 Ask、跨节点）
    ValueTask<ReadOnlyMemory<byte>> AskAsync(in PulseAddress addr, ushort protocolId, ReadOnlyMemory<byte> body, CancellationToken ct = default);
}
```

- **所有调用方代理（客户端 Stub / Fan-out / Actor 代理）最终都调用 `IPulseRouter`**，把"目标解析"从生成代码里抽出来，生成代码只负责"序列化参数 + 提供 `PulseAddress` + 反序列化结果"。
- 这样使得**同一份生成的 Actor 代理**在"本地/远程"下行为一致（本地直接入邮箱、远程走转发），对业务透明。

### 7.2 与现有路由表的关系

- 入站方向（被调方）：现有 `ServiceRoutingTable`（协议号→方法）保留，但**扩展为按 `(Hub, Key)` 解析目标 Actor 实例**（当前是取 DI 单例，见 §2.2）：
  - 当 `ServiceKey` 非空 ⇒ 通过 `IServiceAccessor<T>/PulseServiceManager` 取/建 keyed Actor 实例，入其邮箱执行；
  - 当 `ServiceKey` 为空 ⇒ 保持现有单例语义（无状态 Hub）。
- 出站方向（调用方）：新增 `IPulseRouter` 作为统一出口。

---

## 8. 集群与 Actor Placement / 目录

### 8.1 需要新增的能力

| 组件 | 职责 |
|---|---|
| **节点标识 `NodeId`** | 每个 backend/gateway 唯一标识 |
| **Actor 目录 `IActorDirectory`** | 解析 `(Hub, Key) → NodeId`（谁拥有该实例）；支持注册/查询/失效 |
| **Placement 策略** | 首次寻址某 `(Hub,Key)` 时决定落在哪个节点（一致性哈希/亲和/随机/加权，复用 `ServiceRoutingStrategy` 枚举）|
| **节点间传输 `INodeLink`** | node↔node 全双工连接（复用《全双工对等架构》连接层）+ 转发 |
| **服务发现 `IServiceDiscovery`** | 节点成员发现（**当前仅接口无实现**，Consul/Etcd/K8s 项目为空壳）。**定稿：首版用静态配置成员列表**（配置文件/环境变量），动态发现推迟到 P8 |

### 8.2 单实例 Actor 的一致性（定稿：L2 目录 + 租约）

- Actor 模型要求**同一 `(Hub,Key)` 全集群唯一属主（single-activation）**，否则串行一致性被破坏。
- **定稿采用 L2：目录 + 租约**：
  - 一致性哈希给出候选属主节点；`IActorDirectory` 登记 `(Hub,Key) → (NodeId, LeaseId, 过期时间)`，**通过租约保证单一激活**；
  - 属主节点持有租约期间独占该 `(Hub,Key)`；租约到期需续租，未续租视为释放；
  - 寻址方先查目录：命中且租约有效 ⇒ 直投属主；未命中 ⇒ 由候选节点竞争激活（CAS 抢租约，失败者转投胜出者）；
  - 节点故障（租约失效/心跳丢失）⇒ 允许在别处**重新激活（reactivation）**；此时**旧实例的内存态丢失**（除非业务自行持久化），需在文档中明确此语义。
- **有意不做 L3**（迁移 + 在途消息接管）：L3 需要状态迁移与消息重定向的复杂协调，本轮不纳入；L2 已能保证"同一时刻单属主"，满足绝大多数 keyed-actor 场景。`IActorDirectory` 仍按可插拔接口设计，为将来 L3 预留。

> **一致性边界（须对用户明示）**：L2 保证"稳定期单属主"；在**节点集变化的窗口期**（故障切换、扩缩容再平衡），可能出现极短时间的"旧属主租约未过期 + 新属主尝试激活"的竞争，靠租约 CAS 收敛。业务若需要跨激活的状态连续性，必须自行持久化状态（Actor 内存态不保证跨激活保留）。

### 8.3 落地方式

- `IActorDirectory` 定义为可插拔接口；
- **首版实现 = 一致性哈希 + 租约目录**，租约存储后端可选：单节点内存（开发）、或分布式后端（生产，与 §9 Backplane / §8.1 服务发现共用基础设施，如 Redis/Etcd）；
- Placement 策略复用 `ServiceRoutingStrategy` 枚举（一致性哈希/亲和/加权等）。

---

## 9. Backplane：跨节点广播 / 分组 / 用户映射（本设计的核心缺口修复）⚠️

### 9.1 问题复述

`GroupManager` / `UserConnectionMapping` / `ServerChannelManager` 均为**进程内**结构。多节点或 Gateway 桥接下：

- `Clients.All` 只广播到本节点连接；
- `Clients.Group("room")` / `User("u")` 只覆盖本节点上的成员；
- **结果：跨节点广播"看起来成功但静默丢消息"**，这是最危险的隐性 bug。

### 9.2 解决方案：可插拔 Backplane

引入 `IPulseBackplane` 抽象（对标 SignalR Redis backplane）：

```csharp
public interface IPulseBackplane
{
    // 把一次 fan-out 意图扩散到其它节点
    ValueTask PublishAsync(PulseAddress fanoutAddr, ushort protocolId, ReadOnlyMemory<byte> body, CancellationToken ct = default);
    // 分组/用户映射的分布式视图（或事件订阅）
    ValueTask AddToGroupAsync(string connectionId, string group, string ownerNodeId, CancellationToken ct = default);
    ValueTask RemoveFromGroupAsync(string connectionId, string group, string ownerNodeId, CancellationToken ct = default);
    // 成员归属查询：group/user → (nodeId, connectionId)[]
    IAsyncEnumerable<(string nodeId, string connectionId)> ResolveAsync(PulseAddress addr, CancellationToken ct = default);
}
```

**两种扩散模型（定稿：抽象层同时支持，一步到位实现两者）：**

- **模型 X｜广播 fan-out（publish/subscribe）**：本节点把 fan-out 意图发布到 backplane topic，各节点订阅后对本地成员投递。简单、无需全局成员表；缺点：每条广播都全网扩散（用 per-group 分片 topic 控制风暴）。
- **模型 Y｜全局成员目录（directory）**：backplane 维护 `group/user → {node, conn}` 全局映射，发送节点先 `ResolveAsync` 再**定向**发到相关节点。省流量；缺点：需维护一致的成员表（成员增删、断线清理）。

**定稿路由策略（两者协同）**：
- `All` / 大范围 `Group` ⇒ 走 **X**（pub/sub 扩散，避免维护巨大成员表）；
- `User(u)` / `Single(c)` / 小范围定向 ⇒ 走 **Y**（先 `ResolveAsync` 定位节点再定投，避免全网风暴）；
- `Group` 可按规模阈值在 X/Y 间自适应（小组用 Y 定向、大组用 X 分片广播）。
- `IPulseBackplane` 抽象**同时暴露 `PublishAsync`（X）与 `ResolveAsync`/成员维护（Y）**，上层由路由层按上表选择。

**默认实现：** `InProcessBackplane`（单节点，无扩散，等价于现状，零依赖）。集群实现（Redis/NATS 等）作为独立包（放进目前空壳的 `Infrastructure.*` 项目族或新建 `PulseRPC.Backplane.*`）。成员表的**断线清理**（连接断开/节点失联时移除其 group/user 归属）是 Y 的正确性关键，须与连接生命周期、§8.2 租约失效联动。

### 9.3 广播语义矩阵（多节点下需明确并测试）

| 选择器 | 单节点 | 多节点（本设计目标）|
|---|---|---|
| `All` | 本节点全认证连接 | 全集群全认证连接（backplane 扩散）|
| `Group(g)` | 本节点组成员 | 全集群组成员（backplane 目录/发布）|
| `User(u)` | 本节点该用户连接 | 全集群该用户所有连接 |
| `Except(c)` | 本节点排除 c | 全集群排除 c（c 可能在别的节点）|
| `Single(c)` | 本节点连接 c | 定位 c 所在节点后定投（含 Gateway 虚拟连接）|

> **测试要求**：必须新增"双节点/三节点 + Gateway"集成测试，断言上表每一格的"跨节点成员不漏、不重复"。

---

## 10. 多跳请求/响应与回执关联

### 10.1 现状假设与问题

现状请求/响应靠 `MessageId` 在**同一条连接**上关联；`client→gateway→backend actor` 是多跳，响应需要"原路返回"或"按 ReplyTo 直达"。

### 10.2 设计

- 每跳保留 `MessageId`（端到端关联键不变）；
- 发起端写入 `SourceNodeId`（自己）与可选 `ReplyTo`；
- 中间节点/Gateway 维护**在途请求表**（`MessageId → 上游连接/回执地址`，带超时/TTL），响应回来时按表回投；
- **Actor 反向 Ask（服务端调客户端 `Task<T>`）跨 Gateway**：backend Actor 通过虚拟连接 `Ask`，帧经 Gateway 到客户端，客户端应答帧带原 `MessageId + ReplyTo` 回到 backend，完成关联。
- **超时与失败**：每跳独立超时（复用 `MessageHeader.TimeoutMs` 相对超时语义），任一跳超时即向上游返回失败；防环用 `HopLimit`。

### 10.3 投递保证（定稿：三级可选，按方法或调用选择）

框架提供**三档投递保证**，通过方法特性（如 `[Delivery(DeliveryMode.AtLeastOnce)]`）或调用参数选择：

| 档位 | 语义 | 实现要点 | 默认适用 |
|---|---|---|---|
| **至多一次（AtMostOnce）** | 发送后不重试，失败即上抛 | 无额外状态；等价现状单节点行为 | **默认档**（实时消息、位置同步等丢弃过期消息无害的场景）|
| **至少一次（AtLeastOnce）** | 失败/超时重试直到确认 | 需 ACK + 重试；接收方可能重复收到 | 关键状态变更（需接收方幂等或容忍重复）|
| **精确一次（ExactlyOnce）** | 有且仅有一次生效 | 至少一次 + **接收方基于 `MessageId` 去重**（有界去重窗口 + TTL）达成"效果幂等" | 扣费、发奖等不可重复的操作 |

**设计要点**：
- `MessageId` 作为全局幂等键（跨跳不变，§4.1）；精确一次的"去重"在 **Actor 侧**进行（每个 `(Hub,Key)` 维护有界去重集，落在其串行邮箱内，天然线程安全）。
- 精确一次是"**有效一次（effectively-once）**"语义（至少一次投递 + 去重），**非分布式事务**；跨越持久化状态的强一致需业务自行保证。
- 去重窗口有界（按时间/条数），超窗口的极晚重复不再去重——需在文档标注该边界。
- 至少一次/精确一次的重试与 ACK 依赖 §10.2 的在途请求表与 §8.2 租约（属主变更时的重投由目录协调）。

> 单节点阶段（§14-P3）仅需实现"至多一次"（现状）；至少一次/精确一次随集群阶段（P6 起）落地，去重窗口与 ACK 机制在 P6/P7 完成。

---

## 11. 协议号一致性（跨客户端/服务端/节点）

### 11.1 现状

- 协议号 = 方法签名的 **FNV-1a 纯哈希**（不做线性探测），客户端/服务端各自独立编译靠"相同签名→相同哈希"保证一致；冲突报错 `PULSE003/004`（服务端）、`PRPC001`（客户端），要求 `[Protocol(0xXXXX)]` 手动区分。
- 目前 **Hub 与 Receiver 是两个独立协议号空间**。

### 11.2 统一后的协议号空间（定稿：P1 按 Hub 接口隔离）

移除 `IPulseReceiver`、统一 `IPulseHub` 后，协议号空间**定稿采用 P1：按 Hub 接口隔离**。

- **P1｜按 Hub 接口隔离**：协议号在**单个 Hub 接口内唯一**即可（服务端本就按 `(ServiceType/Hub, ProtocolId)` 路由；`MessageHeader`/`ReadOnlyEnvelopeHeader` 均带 `Hub` 字段随行）。不同 Hub 之间可复用同一 ushort。→ 冲突面最小、跨程序集稳定。
- **前提**：入站路由必须按 `(Hub, ProtocolId)` 二元组，而非仅 `ProtocolId`（现状服务端已是 `(ServiceType, ProtocolId)`，符合）；Gateway 中转/多跳时 `Hub` 字段必须始终随行（`ReadOnlyEnvelopeHeader.Hub` 已满足）。
- （已否决 P2 全局唯一：ushort 仅 65536 空间，全局唯一在方法总数增大时因生日悖论频繁撞号，逼迫大量手动 `[Protocol]`。P1 把冲突面缩到单接口内的几~几十个方法，冲突概率可忽略。）
- **收益**：比现状"Hub / Receiver 两套独立空间"更简单——统一后仍是"每个 Hub 一套空间"，`[Channel("CLIENT")]` 的推送 Hub 与服务 Hub 一视同仁。

> **签名哈希的跨端一致性前提**：客户端 Stub 与服务端 Skeleton 必须对**同一方法**算出**同一协议号** ⇒ 签名构造函数（接口全名 + 方法名 + 参数类型全名，过滤 `CancellationToken`）必须两侧**完全一致**。统一后要保证两个生成器的 `ProtocolIdHelper.BuildMethodSignature` 规则**逐字节一致**（含泛型 `Task<T>`/`ValueTask<T>` 的处理、命名空间写法、数组/泛型参数格式）。这是回归风险点，需专门测试。

### 11.3 命名碰撞（同一接口同时产出 Stub + Skeleton）

统一后，一个 `[Channel("SvrX")]` 接口在提供方节点可能**同时生成 Skeleton + Actor 调用方代理**。现有命名（`{N}Proxy`/`{N}Stub`/`{N}Dispatcher`/`{N}ReceiverProxy`）需重设，建议：

| 生成物 | 建议类名 | 替代原 |
|---|---|---|
| 客户端调用方（单连接）| `{N}ClientStub` | 原 `{N}Stub` |
| 服务端被调方（协议号→方法）| `{N}ServerInvoker` | 原 `{N}Proxy` |
| 客户端被调方（接收推送）| `{N}ClientDispatcher` | 原 `{N}Dispatcher` |
| 服务端调用方（Fan-out 到客户端）| `{N}ClientProxy` + `{N}HubClients`/`{N}HubContext` | 原 `{N}ReceiverProxy` 等 |
| 服务端调用方（Actor 寻址）| `{N}ActorProxy` | 新增 |

> 文件名同步用 `{Namespace}_{N}.<Role>.g.cs` 消歧，避免与历史生成文件冲突。

---

## 12. 安全与鉴权（跨 Gateway/集群）

- 复用 `CallSourceType`（External/Internal/System/Admin）与 `ClientFacingGate`；
- **Gateway 强制来源钳制**：外部客户端进入的帧一律标记 `ExternalUser`，禁止其自称 `Internal`；backend 仅信任来自可信 Gateway/节点链路的来源标记；
- `[ClientFacing]` 白名单门闸继续在 backend 的协议号路由唯一必经点强制（保持"默认拒绝、拒绝优先"）；
- `[Authorize(Role=Internal)]` 的 Hub 方法：客户端项目**默认不生成 Stub**（§5.2-C 过滤器），编译期即隔离，减小攻击面；
- 跨节点内部调用的鉴权（节点↔节点互信）**定稿**：抽象为 `INodeAuthenticator`，**首版用共享密钥（预共享 token / HMAC）**，**生产用 mTLS**（双向证书，可配合 K8s / 服务网格），两种实现可切换：
  - 首选共享密钥：内网受控通信，实现最简、依赖最少，足以建立节点互信并阻止外部/客户端伪造成内部节点（配合上文 Gateway 来源钳制）；
  - mTLS 作为生产级增强，与 P8 服务发现一起落地（引入证书签发/轮换/管理）；
  - 密钥/证书的分发在部署层解决；密钥轮换需运维配合。升级 mTLS 是增强、不返工。

---

## 13. 兼容与迁移方案

### 13.1 `IPulseReceiver` 的处置（定稿：M2 硬移除）

**定稿采用 M2：硬移除** —— 直接删除 `IPulseReceiver`，要求所有推送契约改为 `[Channel("CLIENT")] IXxxReceiver : IPulseHub`。

- **理由**：一步到位统一到单标记 `IPulseHub`，不背负过渡期的双识别/兼容分支，生成器与运行时逻辑最干净。
- **代价（须显式管理）**：这是**破坏性变更**——依赖 `IPulseReceiver` 的现有代码（含 Unity 客户端等外部使用面）将无法编译，必须改动。因此：
  - **必须配合大版本号跃迁**（当前 1.0.33 → 建议进入 2.0 或明确的破坏性小版本，遵循语义化版本约定）；
  - **必须提供迁移指南 + 编译期引导**：删除 `IPulseReceiver` 后，为降低迁移摩擦，可在**一个过渡小版本**保留一个**编译错误级 Roslyn 分析器/诊断**（如 `PULSE_MIGRATE_RECEIVER`），当检测到 `: IPulseReceiver` 时报错并给出 CodeFix（自动改写为 `[Channel("CLIENT")] : IPulseHub`），帮助用户机械迁移；
  - 生成器**不再保留** "识别旧 Receiver 接口" 的兼容路径。
- （已否决 M1 软移除：虽更平滑，但会长期保留 `[Obsolete]` 兼容分支与双识别逻辑，与"一步到位统一"目标相悖。）

### 13.2 迁移步骤（面向用户）

1. `IXxxReceiver : IPulseReceiver` → `[Channel("CLIENT")] IXxxReceiver : IPulseHub`（接口名可保留；CodeFix 可自动完成）；
2. 服务端注入 `IHubContext<IXxxReceiver>` → 改为注入统一 Fan-out 代理 / 或经 `IPulseRouter`。为降低改动量，可提供**兼容 shim**：保留 `IHubContext<T>` 的**签名**但其实现转调新的 Fan-out 代理 / `IPulseRouter`（`T` 约束从 `IPulseReceiver` 改为 `IPulseHub`）；
3. `PulseHubBase<TReceiver>` / `PulseReceiverHub<TReceiver>` → 统一到基于 `IPulseHub` 的新基类（旧基类可作为薄适配层保留一个过渡版本，或一并要求迁移）。

> 注意：因 `IPulseReceiver` 已删除，上述 shim/基类的泛型约束需改为 `where T : class, IPulseHub`；这本身也是破坏性 API 变更，纳入大版本。

### 13.3 兼容性保护（协议线兼容 ≠ 源码兼容）

- **源码层**：M2 是破坏性变更，不承诺源码向后兼容（见 §13.1，靠版本号 + 迁移工具兜底）。
- **协议线层**：`MessageHeader` 新增字段（`SourceNodeId`/`ReplyTo`/`HopLimit`）全部走 **MemoryPack 尾部新增**，保证**新旧运行时的线格式互操作**（旧端读不到新字段 → 退化为单跳/单节点，行为等同现状）。因此**滚动升级**在协议层是安全的（老节点/老客户端仍能与新节点通信，只是不参与多跳/集群特性）。
- `PublicAPI.Shipped/Unshipped.txt` 需登记删除项（`IPulseReceiver` 及相关 API）与新增项。

---

## 14. 分阶段实施路线图（评审后细化排期）

> 原则：**先小头再大头**；每阶段可独立编译、可回归，且不破坏现有单节点行为。

| 阶段 | 内容 | 产出可验证点 |
|---|---|---|
| **P0 设计定稿** | 本文档 8 项决策已拍板（§0）：消歧 C2、协议号 P1、backplane X+Y、Actor L2、投递三级、IPulseReceiver M2、服务发现静态、节点鉴权共享密钥/mTLS | ✅ 已完成（本文档）|
| **P1 抽象层统一（破坏性）** | 引入 `PulseAddress`/`IPulseRouter`/`IPulseBackplane`(默认 InProcess)/`IActorDirectory`/`INodeAuthenticator` 接口；**删除 `IPulseReceiver`** + 提供迁移分析器与 CodeFix；`IHubContext<T>` 约束改为 `IPulseHub`；`MessageHeader` 加 `SourceNodeId/ReplyTo/HopLimit`（尾部新增）| 单节点行为不变；迁移 CodeFix 生效；单测通过 |
| **P2 生成器重设计** | 两个生成器改为单标记 + §5 消歧规则（含 `[PulseHub(Provide,Consume)]` 覆盖）；统一命名(§11.3)；协议号 P1 按 Hub 隔离；移除旧 Receiver 识别路径 | 生成产物快照测试；**协议号跨端一致性测试** |
| **P3 单节点全链路统一** | 服务端路由按 `(Hub,Key)` 解析 keyed Actor；Fan-out 代理取代 `IHubContext`；本地 Actor↔Actor 走 `IPulseRouter`（本地直投）；投递=至多一次 | ChatApp/JwtAuth 迁移；行为回归 |
| **P4 节点间传输 + 目录(L2 租约)** | `INodeLink`(复用全双工连接层) + 静态成员 + 一致性哈希 + **租约目录(single-activation)**；节点鉴权=共享密钥；跨节点 Actor↔Actor 打通（无 backplane 广播）| 双节点 Actor↔Actor 集成测试；租约抢占/续租/失效测试 |
| **P5 Gateway + 虚拟连接** | Gateway 组件 + `EnvelopeRelay` 转发 + 虚拟连接 + 多跳回执(§10) | 客户端经 Gateway 调 backend Actor，反向 Ask 回执 |
| **P6 分布式 Backplane（X+Y）+ 可靠投递** | Redis/NATS 后端**同时实现 X(pub/sub) 与 Y(全局目录)** 覆盖 `All/Group/User/Except/Single`；实现至少一次(ACK+重试) 与 精确一次(Actor 侧去重) | 多节点+Gateway 广播语义矩阵(§9.3)全绿；投递保证测试 |
| **P7 故障接管增强（L3 不纳入本轮）** ✅ 已实现（首版）| 属主变更时的重投/接管（`ClusterPulseRouter` 链路失败→上报健康→按存活集重建环→重解析属主重试）；`StaticClusterMembership` 失败累计下线 + 隔离半开恢复；去重桶空桶回收（`MessageDeduplicationCache.Sweep`）| ✅ 失败接管/健康下线恢复/去重回收单测通过（`ClusterPulseRouterFailoverTests`/`StaticClusterMembershipTests`/`MessageDeduplicationCacheTests`）；L3 迁移+在途接管留待后续 |
| **P8 服务发现落地** ◐ 部分（成员抽象已就位）| `IClusterMembership` 成员抽象（存活集 + Changed + 健康提示）作为 Consul/Etcd/K8s 插拔点；`StaticClusterMembership` 默认实现；环随存活集重建。**剩余**：填充空壳 `Infrastructure.Consul/.Etcd/.Kubernetes` 动态发现 + 生产级 mTLS `INodeAuthenticator` | 静态成员 + 健康驱动存活集单测通过；动态发现/ mTLS 待后续 |
| **P9 文档与迁移收尾** | 更新 README/两份既有设计文档/CHANGELOG；迁移指南；大版本号跃迁 | 发布评审 |

---

## 15. 风险与未决问题

### 15.1 风险

1. **协议号跨端一致性回归**：两个生成器签名构造必须逐字节一致（§11.2），统一重构极易引入偏差 → 需专门"客户端/服务端同签名→同协议号"测试。
2. **跨节点广播静默丢消息**（§9）：若 P6 未完成就宣称"支持集群"，会造成生产隐性事故 → 在 P6 前，多节点广播必须**显式抛错或告警**，不得静默降级。
3. **单实例 Actor 一致性（L2 租约）**（§8.2）：租约续租/失效窗口期可能出现短暂双激活；节点故障后重新激活会丢失内存态 → 明确投递保证与"跨激活状态不保留"语义；租约 CAS、续租、失效清理必须有测试。
4. **多跳回执/超时/防环**（§10）：在途请求表泄漏、环路 → TTL/HopLimit/超时清理必须有测试。
5. **命名/文件碰撞与增量生成器缓存**（§11.3）：重命名生成物可能与历史 `*.g.cs` 冲突或触发全量重编译 → 迁移期需清理与命名隔离。
6. **性能回归**：`IPulseRouter` 抽象与 keyed-actor 解析引入的间接层，须保持热路径零/低分配（对齐现有 `NetworkBufferPool`/零拷贝路径）。
7. **安全边界**：Gateway 来源钳制不严会导致外部伪造 Internal 越权（§12）。
8. **M2 硬移除的破坏性**（§13.1）：删除 `IPulseReceiver` 及 `IHubContext<T>` 约束变更会破坏现有编译（含 Unity 客户端）→ 必须配合大版本号、迁移分析器/CodeFix、迁移指南；发布沟通到位，避免用户升级即崩。
9. **精确一次去重的正确性与成本**（§10.3）：去重集有界（时间/条数），超窗晚到重复会漏去重；去重集内存与 TTL 需随 Actor 生命周期管理，属主变更时去重状态可能丢失 → 明确"有效一次"边界并测试。

### 15.2 关键决策（已定稿，2026-07-04）

| # | 决策项 | 定稿结论 | 章节 |
|---|---|---|---|
| 1 | 消歧覆盖特性 | **C2 `[PulseHub(Provide, Consume)]`** | §5.2-C |
| 2 | 协议号空间 | **P1 按 Hub 接口隔离** | §11.2 |
| 3 | Backplane 扩散模型 | **X+Y 同时实现**（默认 X，定向用 Y）；默认后端 InProcess | §9.2 |
| 4 | Actor 一致性级别 | **L2 目录 + 租约**（L3 不纳入本轮）| §8.2 |
| 5 | 投递保证 | **三级可选：至多一次 / 至少一次 / 精确一次** | §10.3 |
| 6 | `IPulseReceiver` 处置 | **M2 硬移除**（配合大版本 + 迁移 CodeFix）| §13.1 |
| 7 | 服务发现 | **先静态配置**，动态发现推迟 P8 | §8.1 / §14 |
| 8 | 节点互信鉴权 | **共享密钥首版 + mTLS 生产，抽象 `INodeAuthenticator`** | §12 |

> 决策已锁定，进入实现阶段的门槛（P0）已达成。后续如需调整，走变更评审并同步更新本表与对应章节。

---

## 16. 附录：受影响文件清单（实现阶段参考，非本轮改动）

### 16.1 抽象层 `PulseRPC.Abstractions`
- `IPulseHub.cs`（保留为唯一标记）、`IPulseReceiver.cs`（**M2 删除**）
- `Attributes.cs`（`ChannelAttribute`、`PulseClientGenerationAttribute`、新增 `PulseHubAttribute(Provide, Consume)`；`GenerateSmartHandlerAttribute` 等按需调整）
- `Channels/MessageHeader.cs`（新增 `SourceNodeId/ReplyTo/HopLimit`，MemoryPack 尾部）
- `Messaging/EnvelopeRelay.cs`、`ReadOnlyEnvelopeHeader.cs`（Gateway 复用，可能补充）
- `Routing/*`（`IActorDirectory`+租约、placement、`IServiceDiscovery` 落地）
- 新增：`PulseAddress`、`IPulseRouter`、`IPulseBackplane`（X+Y）、`INodeLink`、`INodeAuthenticator`、`DeliveryMode`/`[Delivery]`
- 迁移分析器/CodeFix：检测 `: IPulseReceiver` → 改写 `[Channel("CLIENT")] : IPulseHub`
- `PublicAPI.Shipped/Unshipped.txt`（登记删除项 + 新增项）

### 16.2 服务端 `PulseRPC.Server`
- `Hubs/*`（`IHubContext`/`IHubClients`/`PulseHubBase`/`PulseReceiverHub` → 统一 Fan-out 代理 + 兼容 shim）
- `Channels/ServerChannelManager.cs`（虚拟连接、跨节点感知）
- `Services/GroupManager.cs`、`UserConnectionMapping.cs`（接 Backplane）
- `Services/Management/PulseServiceManager.cs`、`ServiceAccessor.cs`（按 `(Hub,Key)` 解析 + 远程转发）
- `Processing/*`（`MessageEngine`/`MessageDispatcher`/路由 → 接 `IPulseRouter`、`ReplyTo` 回执）
- `Security/ClientFacingGate.cs`、`Contexts/*`（来源钳制）
- 新增：Gateway 组件、`INodeLink` 实现

### 16.3 源生成器
- `PulseRPC.Server.SourceGenerator`：`PulseRPCSourceGenerator.cs`、`Analyzers/ServiceAnalyzer.cs`、`Generators/*`（ServiceProxy→ServerInvoker、ReceiverProxy→ClientProxy、Routing、ResponseSerializer、ProtocolId）、`Models/*`
- `PulseRPC.Client.SourceGenerator`：`ServiceProxyGenerator.cs`、`Generators/*`（ReceiverDispatcher/ClientChannelGenericExtensions/PulseClientExtensions/SmartEventHandler/ProtocolId）

### 16.4 基础设施（当前空壳，需落地）
- `PulseRPC.Infrastructure`、`Infrastructure.Consul`、`Infrastructure.Etcd`、`Infrastructure.Kubernetes`
- 新增：`PulseRPC.Backplane.*`（Redis/NATS 等，独立包）

### 16.5 示例与测试
- 迁移：`samples/ChatApp`、`samples/JwtAuthentication`、`samples/JsonTranscoding`
- 新增：多节点 + Gateway 集成测试（广播语义矩阵 §9.3、多跳回执 §10、协议号一致性 §11.2）
- 现有：`tests/PulseRPC.Server.Tests/*`（`ServerReverseCallTests`、`ClientFacingGateTests`、`TickAttributeTests` 等）回归

### 16.6 文档
- 更新《IPulseHub 统一架构使用指南》《服务器间通信设计文档 - 全双工对等架构》与本文档保持一致
- README、`docs/变更日志.md`、迁移指南

---

## 17. 术语表

| 术语 | 含义 |
|---|---|
| Hub | `ServiceName`，一个远程可调用契约（`IPulseHub`）|
| Key | `ServiceKey`，Actor 实例键（`"Type:Id"` 的 Id 部分）|
| MethodId | `ProtocolId`，方法的 16 位协议号 |
| Stub | 调用方代理（发起调用）|
| Skeleton / Invoker | 被调方骨架（协议号→实现方法）|
| Dispatcher | 客户端侧接收推送的被调方骨架 |
| Fan-out | 一对多投递（All/Group/User/Except）|
| Backplane | 跨节点扩散广播/分组/用户映射的后端 |
| Placement / Directory | Actor 实例到节点的放置与目录解析 |
| Virtual Connection | backend 上代表"经 Gateway 桥接的远程客户端"的连接对象 |
