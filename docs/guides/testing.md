# 测试指南

测试范围应跟修改风险匹配。协议、生成器、公共 API、传输层和集群路由都需要比普通样例文档更严格的验证。

## 常用命令

```bash
dotnet build PulseRPC.sln
dotnet test tests/PulseRPC.Client.Tests/PulseRPC.Client.Tests.csproj
dotnet test tests/PulseRPC.Server.Tests/PulseRPC.Server.Tests.csproj
dotnet test tests/PulseRPC.SourceGenerator.Tests/PulseRPC.SourceGenerator.Tests.csproj
dotnet test tests/PulseRPC.Infrastructure.Tests/PulseRPC.Infrastructure.Tests.csproj
```

Discovery/membership 稳定性门禁要求 Infrastructure.Tests 连续运行 20 次全部通过。Release CI 还会以 `--warnaserror` 构建解决方案、维护中样例和九个运行时 NuGet 包，并检查每个包的 PublicAPI 基线与 README。`run-tests` job 会启动带健康检查的真实 Redis 服务，因此 Redis Actor lease 竞争、CAS 续租/释放和 TTL 接管测试在 CI 中不会跳过。

Redis backplane 测试可能依赖容器环境：

```bash
dotnet test tests/PulseRPC.Backplane.Redis.Tests/PulseRPC.Backplane.Redis.Tests.csproj
```

## 修改类型与测试

| 修改类型 | 最小验证 |
| --- | --- |
| 文档链接、导航 | 链接搜索 + `dotnet build PulseRPC.sln` |
| 公共契约/API | build + Client/Server tests + PublicAPI 文件检查 |
| Source Generator | SourceGenerator.Tests + 相关 Client/Server tests |
| 传输层 | Client.Tests + Server.Tests + BenchmarkApp smoke test |
| 集群/路由 | Server.Tests clustering 分组 + Infrastructure.Tests |
| 样例 | 对应样例 build/run smoke test |

## 测试原则

- 修 Bug 先写能失败的测试。
- 新增校验先写异常输入测试。
- 重构必须保持现有测试通过。
- 不要用全仓搜索替代行为测试。
