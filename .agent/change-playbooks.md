# 修改剧本

每次修改先判断类型，再按对应剧本执行。不要把历史文档中的旧 API 当成当前事实。

## 公共 API 修改

必读：

- `src/PulseRPC.Abstractions/PublicAPI.*.txt`
- 受影响项目的 `PublicAPI.*.txt`
- `docs/reference/index.md`
- `.agent/public-api-policy.md`

步骤：

1. 明确新增、废弃、破坏还是内部重构。
2. 更新 XML 注释和 PublicAPI 文件。
3. 如果废弃，添加 `[Obsolete]` 和迁移路径。
4. 更新用户文档和迁移指南。
5. 运行 Client/Server/SourceGenerator 相关测试。

## Source Generator 修改

必读：

- `.agent/skills/pulserpc-source-generator/SKILL.md`
- `src/PulseRPC.Client.SourceGenerator`
- `src/PulseRPC.Server.SourceGenerator`
- `tests/PulseRPC.SourceGenerator.Tests`

步骤：

1. 用测试描述生成前后的契约。
2. 保持客户端生成代码兼容 C# 9.0。
3. 检查协议号重载、一致性和错误消息。
4. 运行 SourceGenerator.Tests，再按影响范围运行 Client/Server tests。

## 传输层修改

必读：

- `src/PulseRPC.Shared`
- `src/PulseRPC.Abstractions/Transport`
- `docs/concepts/transport-model.md`

步骤：

1. 明确修改 TCP、KCP、批处理、缓冲池还是协议常量。
2. 增加单元测试覆盖边界、取消、异常和数据完整性。
3. 运行 Client.Tests、Server.Tests。
4. 如果影响性能，运行 BenchmarkApp smoke test。

## 集群/路由修改

必读：

- `src/PulseRPC.Server/Clustering`
- `src/PulseRPC.Abstractions/Clustering`
- `src/PulseRPC.Infrastructure*`
- `docs/concepts/clustering-and-routing.md`

步骤：

1. 明确是成员发现、节点认证、Actor 属主、Backplane 还是 Gateway。
2. 覆盖单节点和多节点语义测试。
3. 不把业务认证和节点认证混用。
4. 更新部署和参考文档。

## 文档修改

必读：

- `.agent/doc-style.md`
- `docs/index.md`

步骤：

1. 判断文档类型：教程、指南、概念、参考或归档。
2. 当前事实进入当前目录，历史资料进入 `docs/archive/`。
3. 修改链接后运行链接搜索。
4. 示例代码必须可编译，除非明确标注为伪代码。

