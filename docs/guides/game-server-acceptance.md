# 游戏服务端专项验收

`samples/GameServer` 展示玩家经 TLS Gateway 调用有状态 Actor、节点之间真实 mTLS、Redis placement 与 PostgreSQL 持久化的组合。首次上手仍使用 [HelloRPC](../../samples/HelloRPC/README.md)。本示例用于验证部署与故障语义，不是完整的账号、经济或战斗系统。

## 运行环境

验收需要 Linux、.NET SDK 10.0.401、Python 3、OpenSSL、stunnel4，以及专用 Redis 7 和 PostgreSQL 17 容器。脚本会暂停数据库和节点进程，只能对隔离测试环境执行。开发者本机无需安装 SDK 10；PR 的 `Game server validation` workflow 负责远程构建、测试和上传原始结果。

从仓库根目录执行：

```bash
export GAME_REDIS='localhost:6379,abortConnect=false,connectTimeout=1000,syncTimeout=1000,asyncTimeout=1000'
export GAME_POSTGRES='Host=localhost;Username=game;Password=acceptance-only;Database=game;Timeout=5;Command Timeout=5'
export GAME_REDIS_CONTAINER='<专用 Redis 测试容器 ID>'
export GAME_POSTGRES_CONTAINER='<专用 PostgreSQL 测试容器 ID>'
export GAME_JWT_KEY=$(openssl rand -base64 32)
dotnet build samples/GameServer/GameServer.Host/GameServer.Host.csproj -c Release --warnaserror
dotnet build samples/GameServer/GameServer.LegacyClient/GameServer.LegacyClient.csproj -c Release --warnaserror
git worktree add --detach /tmp/pulserpc-previous d49a053e6b41bf6396536074e43521ea50478710
dotnet build /tmp/pulserpc-previous/samples/GameServer/GameServer.Host/GameServer.Host.csproj -c Release --warnaserror
export GAME_PREVIOUS_HOST=/tmp/pulserpc-previous/samples/GameServer/GameServer.Host/bin/Release/net10.0/GameServer.Host.dll
python3 scripts/verify-game-cluster.py
```

凭据仅用于隔离验收环境。脚本生成临时 CA/节点证书，运行 Gateway 与两个独立 Game 进程，随机选择 loopback 端口；私钥不进入构建产物。退出时清理进程并恢复暂停的容器。结果位于 `artifacts/game-server/cluster/results.json`；部分通过记录不能代表整个验收通过。

## 玩家身份、会话与授权

玩家 → 测试 TLS 客户端代理 → Gateway TLS 入口 → Gateway；节点 → 本节点出站 TLS 代理 → 对端 mTLS 入口 → 对端节点。应用 TCP 端口仅监听 loopback。`ExternalMutualTls` 是部署声明，本身不加密线路。跨主机部署须保护应用和代理之间的明文端口，并配置真实证书撤销流程。

公网 TLS 入口不要求玩家客户端证书。Gateway 验证 JWT 签名、算法、issuer、audience 和有效期，将身份绑定到连接。登录和登出返回 `Task<bool>`，让调用方等待确认；框架中无返回值的 `Task` 是单向命令，不适合登录确认。客户端只应持有身份服务签发的令牌。示例客户端持有临时签名密钥以模拟身份服务；生产应拆分签发与验证权限。

每次成功登录会替换 PostgreSQL 中该玩家的 session UUID。Gateway 覆盖 `game_session` claim，拒绝客户端自行指定会话；Gateway 和业务 Actor 都验证当前会话。资产事务对会话行持有共享锁：新登录等待已获准的事务结束，新登录确认后，旧会话排队中的购买不能提交新资产变更。旧连接的延迟登出只删除自身 session，不影响新连接。物理重连必须重新登录并读取状态；重放购买沿用原 `OperationId`。

`UserOwnedActorInvocationPolicy` 在 placement 与激活前限制 `Actor key == UserId` 和显式方法白名单，业务入口再次校验。共享 `RoomHub` 使用数据库成员表授权，并在后端重新检查；验收覆盖两位成员、非成员及成员被移除后的访问。租户、组队邀请和写操作的成员变更事务仍由业务定义。

节点证书和玩家身份独立验证。示例同时设置 `CertificateNodeAuthenticatorOptions.AllowedNodeIds`，只允许三个成员；TLS 主体限制与应用层限制必须一致，避免同 CA 签发的非成员通过公网认证协议取得节点身份。该集合在鉴权器创建时快照，默认空集合保留原有 CA/指纹信任行为。

## 资产事务与真实消息投递

Redis 决定路由属主，PostgreSQL 独立签发单调递增的写入代次。每次激活取得新的 owner UUID 和 generation；数据库时间决定有效期。续租、释放和资产写入均比较 owner、generation 与有效期，旧进程恢复后不能凭旧代次写入。数据库续租失败会通过 `RemoveServiceIfSameAsync(this)` 退休当前激活，避免误删同地址的新实例。Redis 租约守卫独立停止 mailbox、Tick 和请求；取消不能撤销已提交事务。

客户端为购买生成稳定 `OperationId`。服务端定价，在同一事务中锁定会话和玩家行、校验代次、扣款、增加背包、写入收据及 outbox。并发重试返回原收据；相同 ID 配不同参数被拒绝。框架内存去重及 `ExactlyOnce` 选项不能代替此持久化事务。

`PurchaseBroker` 将 SQL outbox 发布到真实 Redis Streams。发布成功不等于投递完成：消费者在同一 PostgreSQL 事务中写 inbox、增加通知副作用计数、标记 outbox 已送达，提交后再 ACK。验收分别在“Redis 已接收、发布事务未提交”和“消费者事务已提交、尚未 ACK”时杀死进程，随后由新进程重投和接管 pending 消息，校验资产与消费副作用各一次。

运行节点时设置 `GAME_PURCHASE_STREAM` 启用后台生产/消费循环。它使用专用 stream、单个 consumer group，每批最多 64 条，stream 达到 4096 条后停止追加，积压留在 SQL；不裁剪未确认消息。未交付 outbox 每两秒可重发，pending 超过一秒可接管，ACK 与删除由同一 Lua 脚本完成。这个示例只支持该专用消费组，不能直接扩展为多个组后仍删除消息。`verify-store` 中旧的 SQL 去重模拟只测试事务，不作为真实 broker 交付证据。

错误事件保留 pending 并记日志，需告警和人工调查；尚未提供自动死信处理。收据、inbox、outbox 和过期会话不会自动归档。生产须根据最大重试窗口制定保留期、分区、积压阈值及修复流程，不能先删除幂等记录再允许旧操作重放。

## 运行监控、排空和升级

设置 `GAME_ADMIN_BASE_PORT` 后，三节点按 gateway/game-a/game-b 顺序使用起始端口及后两个端口；管理监听严格绑定 `127.0.0.1`。跨主机采集应通过受保护的控制通道转发。

| 接口 | 行为 |
| --- | --- |
| `GET /live` | 进程存活 |
| `GET /ready` | Redis、PostgreSQL 可访问且未排空时 200，否则 503 |
| `GET /metrics` | Prometheus 文本：RPC、连接、Actor、队列深度/容量/拒绝、GC、RSS、CPU、SQL outbox 数量和最老积压时间 |
| `POST /drain` | 停止接纳新 dispatcher 请求，通知 placement 排除本节点，等待在途请求并退休 Actor；处理中 202，完成 200，失败 500 |

排空期限为 40 秒；失败后保留不就绪状态并记录错误，不能把超时当作已安全停止。排空后需重启节点才重新接纳请求。示例只演示固定三个节点，不能代替完整编排器。指标不包含玩家 ID 标签，避免无界基数；应为 outbox 积压、队列拒绝、依赖不可用和不就绪持续时间设置业务告警。

升级验收实际构建 `d49a053` 旧版二进制，逐节点回滚和升级，重复同一购买并验证一次扣款；独立 C# 9 旧客户端读取追加字段后的 DTO。旧协议号 `0x7100`–`0x7103` 保持稳定；新增登出 `0x7104` 和 Room `0x7110`–`0x7111`。MemoryPack VersionTolerant 字段只追加序号，参见 [目标框架说明](https://github.com/Cysharp/MemoryPack#target-framework-dependency)。Unity 包、TCP 实测和 iOS IL2CPP 仍由 Build workflow 覆盖。

新会话语义默认 `GAME_SESSION_MODE=required`。混合旧 Gateway/旧节点期间必须显式使用 `legacy-compatible`，它允许旧 Gateway 缺少新增会话 claim；有 claim 时仍校验。所有节点支持新语义后再逐个启用 `required`。兼容阶段不能宣称旧会话写入隔离成立；回滚必须先切回兼容模式。新增 Room/登出方法不能向旧二进制调用，业务功能应待升级完成后启用。

证书测试替换实际属主的叶证书和私钥，通过 [stunnel 的 SIGHUP 重载](https://www.stunnel.org/manual.html)更新代理，再重启应用凭据持有者，验证新连接的证书指纹和实际购买重放。它覆盖同一 CA 下的叶证书轮换，不代表已验证 CA 撤销或跨主机滚动证书分发。

## 故障与性能测试边界

专项脚本覆盖独立进程、实际 mTLS、身份与成员拒绝、broker 崩溃窗口、杀死/暂停属主、Redis 和 PostgreSQL 暂停与恢复、SQL 行锁下的忙碌拒绝、在途购买排空、旧新版混合与回滚、叶证书轮换。每项完成后才计入结果。属主接管小于 20 秒、闭环 Echo P99 小于 250 ms 是宽松 CI 回归限制，不是生产 SLO。节点隔离期为 20 秒，避免 Redis 九秒租约到期时重新选中故障节点。

原有闭环 Echo 负载继续保留。新增 `perf/game-server/ci.json` 使用 20 秒、8 连接、100 次/秒的开环请求：80% Echo、15% 状态读、5% 新购买。每次购买使用新操作 ID，并对账 SQL 余额、背包、收据与 outbox。每连接客户端队列有界；请求独立于响应按时到达，生成器无法接纳的请求记为 dropped，不从成功率分母删除。延迟从计划到达时间计算，同时保留服务耗时和调度延迟；固定直方图使用约 5% 桶宽，JSONL 持续落盘，长测不在内存保留全部样本。

验收各端 TCP 收发缓冲显式设为 64 KiB；框架节点默认仍为 8 KiB。调大缓冲增加每连接内存，需与载荷和并发一起核算。短时吞吐、P99 和 RSS 不能推出数据库饱和容量、生产 CCU 或长期无泄漏。

微基准使用 schema 2：独立并发 worker、启动屏障、固定分配操作量、显式预热、每 worker 计数、原始样本、CPU/GC/锁争用指标。`scripts/benchmark-game-server.sh <baseline-ref> full` 将同一候选测量代码应用到基线运行时，五轮交替 AB/BA 比较；拒绝配置、CPU、运行时不一致或 worker 饥饿的数据。dotnet-trace 在另外的运行中采集，带探针的数据不参加吞吐比较。

第一阶段微基准中，同步完成的热路径可能让懒枚举 worker 被首个 worker 耗尽，不能作为可靠并发回归证据。纠正测量后第一次五轮比较中，热点吞吐约 +1.19%、生命周期吞吐约 -0.25%，先前 -12.87%/-11.40% 未复现；共享 runner 结果仍需专用硬件复核，不能据此承诺加速。

## 容量阶梯与连续 24 小时长稳

`perf/game-server/controlled.template.json` 是待填写的工作负载模板。先提供真实环境标识、独立部署、连接规模、业务比例和 SLO；默认参数不代表推荐生产容量。环境应提前运行三节点、TLS 代理及业务所需的 outbox worker。压测工具会创建独立前缀的合成玩家，直接准备测试数据库，并持有测试身份签名能力，因此只能用于隔离环境。

```bash
# 在已构建 Host、有上述连接环境变量的专用 Linux 压测机执行。
python3 scripts/run-game-capacity.py --profile perf/game-server/controlled.template.json \
  --host 127.0.0.1 --port 25060 --rates 100,200,400 --duration 60 \
  --admin http://127.0.0.1:25070 --admin http://127.0.0.1:25071 --admin http://127.0.0.1:25072
# 独立的一次连续运行；不会拼接若干短测试。
python3 scripts/run-game-capacity.py --profile perf/game-server/controlled.template.json \
  --host 127.0.0.1 --port 25060 --soak
```

脚本拒绝占位环境标识；`--soak` 要求配置和实际连续运行至少 86400 秒。它保存提交、SDK、主机信息、配置哈希、逐阶段原始结果、退出码及可选每五秒节点指标。错误和生成器丢弃均计入失败率；未配置 SLO 时不输出“通过 SLO”的容量上限。中断、缺失到达请求、对账失败或短测冒充长测都不能通过验收。

也可手动运行 `Controlled game server capacity` workflow，选择已有专用 Linux self-hosted runner 标签、工作负载文件、TLS 客户端代理地址和 ladder/soak 模式。它使用 `GAME_ACCEPTANCE_REDIS`、`GAME_ACCEPTANCE_POSTGRES`、`GAME_ACCEPTANCE_JWT_KEY` 三个仓库 secret，最长运行 26 小时，产物保留 90 天。请通过仓库 secret 管理配置凭据，不写入 profile 或日志。

本次尚未提供专用环境及业务 SLO，因此没有运行跨主机网络分区、Redis/PostgreSQL 高可用切换、饱和容量或连续 24 小时长稳。所有报告保留 `capacity_certified: false`；单机多个进程和容器暂停测试不能代替上述生产验收。

相关配置见 [部署指南](deployment.md)、[性能指南](performance.md) 和 [迁移指南](migration.md)。

## 第二阶段已执行记录

实现提交 `e44f3c3` 的 [专项 Actions 验收](https://github.com/ChronosGames/PulseRPC/actions/runs/37560263340) 三个任务全部通过；SDK 10.0.401、运行时 .NET 10.0.12、Ubuntu 24.04.5、4 个逻辑处理器。实际测试的是 PR 合并提交 `6cbdda01`，完整结果及产物校验和保存在[第二阶段验收 JSON](../reference/game-server-production-results.json)。

- 645 项 .NET 测试全部通过，无跳过；另有 9 项 Python 验收数据校验测试通过。
- 21 项集群检查通过，包括真实 broker 两个崩溃窗口、会话与房间授权、数据库故障、在途购买排空、真实旧二进制回滚升级和叶证书轮换。
- 20 秒开环测试：2000 次到达，2000 次成功，零错误/丢弃，99 次新购买、302 次读取；按计划到达计算的 P99 为 6.84 ms，资产对账通过。两档 25/50 次每秒的脚本短测均通过；没有配置生产 SLO，也未完成 24 小时长稳。
- 排空及属主交接约 4.07 秒，期间等待被 SQL 行锁阻塞的已接纳购买提交；购买重放仍只扣一次。PostgreSQL 恢复后约 0.27 秒恢复业务。
- 本次同机五轮微基准：热点吞吐约 -1.97%、生命周期约 -1.23%、mailbox 约 +0.20%、传输约 -0.01%；对应原始样本及 P95/分配/CPU/锁争用值见 JSON。保留这些波动，不将它们换算为生产容量结论。

## 第一阶段历史验收记录

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
