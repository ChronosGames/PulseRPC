# 游戏服务端专项验收

`samples/GameServer` 展示玩家经 TLS Gateway 调用有状态 Actor、节点之间真实 mTLS、Redis placement 与 PostgreSQL 持久化的组合。首次上手仍使用 [HelloRPC](../../samples/HelloRPC/README.md)。本示例用于验证部署与故障语义，不是完整的账号、经济或战斗系统。

## 运行环境

验收需要 Linux、.NET SDK 10、Python 3、OpenSSL、stunnel4，以及两个专用测试容器：Redis 7 和 PostgreSQL 17。脚本会暂停 Redis 容器和节点进程，只能对隔离的测试环境执行。无需在开发者本机安装 SDK：提交 PR 后，`Game server validation` workflow 在 GitHub Actions 中完成这些步骤，保留日志、TRX、故障结果和性能 JSON。

有可用环境时，从仓库根目录执行：

```bash
export GAME_REDIS='localhost:6379,abortConnect=false,connectTimeout=1000,syncTimeout=1000,asyncTimeout=1000'
export GAME_POSTGRES='Host=localhost;Username=game;Password=acceptance-only;Database=game;Timeout=5;Command Timeout=5'
export GAME_REDIS_CONTAINER='<专用 Redis 测试容器 ID>'
export GAME_JWT_KEY=$(openssl rand -base64 32)
dotnet build samples/GameServer/GameServer.Host/GameServer.Host.csproj -c Release --warnaserror
dotnet build samples/GameServer/GameServer.LegacyClient/GameServer.LegacyClient.csproj -c Release --warnaserror
python3 scripts/verify-game-cluster.py
```

账号和密码仅用于隔离验收环境。脚本生成临时 CA/节点证书，运行 Gateway 与两个独立 Game 进程，随机选择 loopback 端口；证书私钥不写入构建产物。退出时清理进程并恢复暂停的 Redis。结果位于 `artifacts/game-server/cluster/results.json`，失败时不能将已有部分结果视为全部通过。

## 身份和传输边界

玩家 → 本地测试 TLS 客户端代理 → Gateway TLS 入口 → Gateway；节点 → 本节点出站 TLS 代理 → 对端 mTLS 入口 → 对端节点。应用 TCP 端口仅监听 loopback。`ExternalMutualTls` 是对实际部署的声明，本身不会加密线路。生产跨主机部署需要把应用与代理放在受保护的网络命名空间，限制明文端口可达性，并配置证书轮换与撤销。

公网 TLS 入口不要求玩家客户端证书；玩家用 JWT 登录，Gateway 验证签名、算法、issuer、audience 和有效期，将已验证身份绑定到连接。登录契约返回 `Task<bool>`，让调用方等待服务端确认并接收错误；框架中的无返回值 `Task` 是单向命令，不适合承担登录成功确认。客户端只应持有登录服务签发的令牌。示例客户端持有临时签名密钥，是为了模拟测试身份提供者；生产应拆分签发与验证权限。

`UserOwnedActorInvocationPolicy` 在 placement 和激活前限制 `Actor key == UserId`、身份期限和显式方法白名单。`PlayerService` 在业务入口再次验证身份。房间成员与租户授权需要业务策略。节点证书凭据与玩家身份分开验证，不能用玩家 JWT 代替节点认证。示例同时配置 `CertificateNodeAuthenticatorOptions.AllowedNodeIds`，只允许三个成员身份；TLS 的主体限制与应用层限制必须一致，避免同 CA 签发的非成员证书通过公网认证协议绕过内部 TLS 入口。此集合在鉴权器创建时快照；默认为空，保留既有 CA/指纹信任行为。

## 持久化与投递语义

Redis 决定路由属主，PostgreSQL 独立签发单调递增的写入代次。每次激活取得新的 owner UUID 和 generation；数据库时间决定有效期。续租、释放和资产写入都比较 owner、generation 与有效期。旧进程恢复后，即使忽略取消信号，也无法凭旧代次修改余额。

每次购买由客户端生成稳定 `OperationId`。服务端定价，在同一事务中锁定玩家行、验证代次、扣款、增加背包、写入收据及 outbox。并发重试返回原收据；相同 ID 配不同参数被拒绝。收据和资产均在数据库中，进程退出不会丢失。实际业务需要制定收据保留期、参数指纹规则和分区/归档策略。

示例 outbox 消费器模拟“消费成功但生产方未收到 ACK”，通过 inbox 唯一键抑制重复。它没有接入外部消息代理；接入时须将业务消费副作用与 inbox 记录放在同一消费事务中，再确认消息。框架的内存去重和 `ExactlyOnce` 投递选项不提供跨进程资产事务保证。

`PlayerService` 的数据库续租失败会停止自身，并使用 `RemoveServiceIfSameAsync(this)` 清理该激活，避免误删同地址的新实例。Redis 租约守卫独立控制 mailbox、Tick 和请求取消。应用仍须在所有外部写入端实施代次检查；取消令牌不能撤销已提交事务。

## 契约演进

显式协议号 `0x7100`–`0x7103` 保持稳定。DTO 使用 MemoryPack VersionTolerant 与显式字段序号，新增字段追加序号，已有序号不复用。独立 `GameServer.LegacyContracts` 只包含 V1 的余额和背包字段；C# 9 客户端使用旧方法名与相同协议号访问 V2 服务端。

契约项目同时面向 netstandard2.1 和 net10.0。MemoryPack 在现代 .NET 和 netstandard 中采用不同的生成接口，不能把仅为 netstandard 编译的 DTO 程序集直接加载到现代运行时；参见 [MemoryPack 官方说明](https://github.com/Cysharp/MemoryPack#target-framework-dependency)。Unity 使用适用目标的契约构建与 C# 9 生成代理；本专项测试的旧客户端运行在 .NET 10，Unity 导入与 TCP 实测由原有 Build workflow 另行覆盖。

## 故障与性能结果

脚本检查真实 TLS 拒绝、匿名/越权/过期身份、数据库并发去重与旧代次拒绝、旧协议客户端、杀死属主、暂停后恢复旧属主、Redis 中断后的 Tick 停止与恢复，以及数据库行锁下的请求突发与 SERVER_BUSY 恢复、正常/大载荷/热点 Actor 和重新建连。每项必须实际完成后才计入结果。CI 另设属主宕机接管小于 20 秒、三种 Echo 工作负载 P99 小于 250 ms 的宽松回归门禁；这些不是生产 SLO。示例的失败节点隔离期为 20 秒，长于 9 秒 placement 租约及 RPC 超时，避免租约到期时重新选中故障节点。

验收显式将负载客户端、服务端监听和节点连接的 TCP 收发缓冲设为 64 KiB；节点选项 `TcpNodeTransportOptions.RecvBufferSize/SendBufferSize` 默认仍为 8 KiB。增加缓冲会增加每条连接的内存预算，应与实际载荷和并发共同调优。

集群负载输出吞吐、P50/P95/P99、客户端 RSS，以及负载前后的各节点 RSS。它是固定并发闭环 Echo 工作负载，不能推导数据库写入吞吐或生产 CCU。`scripts/benchmark-game-server.sh <baseline-ref> full` 在同一 runner 上运行基线与候选版本的五轮比较；CI 的比较报告记录延迟和分配，不把共享 runner 的抖动作为硬性容量门禁。

生产容量仍需使用目标硬件、网络、认证、真实业务比例和峰值到达率，在指定 P99、错误率、内存与恢复时间 SLO 下测量；报告始终标记 `capacity_certified: false`。本验收为可重复的正确性和回归证据，不宣称已获生产容量认证。

相关配置见 [部署指南](deployment.md)、[性能指南](performance.md) 和 [迁移指南](migration.md)。

## 已执行的验收记录

实现提交 `ac40755` 的 [Actions 验收](https://github.com/ChronosGames/PulseRPC/actions/runs/37512688617) 三个任务全部通过。完整原始数据保存在 [验收 JSON](../reference/game-server-validation-results.json)，避免仅依赖有保留期限的 CI 附件。记录包含实际执行的 PR merge commit、SDK（本次为 10.0.401）、运行时和主机信息；仓库 `global.json` 允许 SDK roll-forward，复现实验时应使用记录中的版本。

- Debug/Release 构建通过；644 项测试全部通过且无跳过：Client 80、Server 460、SourceGenerator 61、Infrastructure 18、Redis 25。
- 三个独立进程通过实际 mTLS 通信；四项 TLS 拒绝检查及五项应用层身份检查通过。
- 20 个并发数据库重试只产生一次扣款；旧代次写入、续租、释放和过期代次恢复检查通过；旧版 C# 9 客户端成功读取 V2 响应。
- 杀死属主后约 7.87 秒恢复；进程暂停场景约 15.11 秒完成接管及恢复检查（含固定 11 秒暂停和 3 秒恢复等待）；Redis 恢复后约 0.37 秒可用。暂停 Redis 期间数据库证明 Actor 已停止续租。
- 256 个突发请求中 32 个成功、224 个收到 `SERVER_BUSY`，同一连接随后恢复；20 次新建会话保持一致状态。

每项 Echo 负载均为 16 个连接、1000 次请求；不是饱和容量测试：

| 场景 | 吞吐（请求/秒） | P50 / P95 / P99（ms） | 失败 |
| --- | ---: | --- | ---: |
| 普通 Actor，128 B | 1929 | 7.07 / 13.63 / 17.95 | 0 |
| 普通 Actor，4 KiB | 1801 | 7.48 / 13.06 / 16.68 | 0 |
| 热点 Actor，128 B | 2712 | 5.50 / 9.01 / 10.70 | 0 |

三节点负载后 RSS 约 121–131 MiB，完整前后值见 JSON；此短测试不能证明长期无泄漏。4 KiB 载荷最初在 8 KiB socket 缓冲下 P99 约 1042 ms，调整示例各端缓冲后两轮分别为 12.7 和 16.7 ms。这是特定配置的实测改进，不能外推到所有硬件、业务和连接规模。

同 runner 的五轮基线比较也保留了不利结果：传输与 mailbox 吞吐变化分别约 -0.26% 和 -0.04%；热点查询吞吐约 -12.87%；Actor 创建/删除吞吐约 -11.40%、P95 约 +29.61%，分配约 +0.64%。这些短微基准受共享 runner 波动影响，额外安全检查的成本也需进一步定位。未据此宣称全面加速；生产采用前应结合实际生命周期频率复测。
