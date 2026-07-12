# Public API 策略

PulseRPC 使用 Roslyn PublicApiAnalyzer 管理公开 API。维护者不能只让代码编译通过，还需要维护 API 文件和迁移语义。

## 文件位置

所有运行时 NuGet 包都必须同时维护 `PublicAPI.Shipped.txt` 与 `PublicAPI.Unshipped.txt`：

- 核心包：`PulseRPC.Abstractions`、`PulseRPC.Shared`、`PulseRPC.Client`、`PulseRPC.Server`
- 基础设施包：`PulseRPC.Infrastructure`、`PulseRPC.Infrastructure.Consul`、`PulseRPC.Infrastructure.Etcd`、`PulseRPC.Infrastructure.Kubernetes`
- backplane：`PulseRPC.Backplane.Redis`

单目标框架包的文件位于 `src/<PackageName>/`。`PulseRPC.Abstractions`、`PulseRPC.Shared`和 `PulseRPC.Client` 同时面向 `netstandard2.1` 和 `net10.0`，必须分别在 `PublicAPI/<TargetFramework>/` 下维护基线；两个 TFM 不得共用基线，因为依赖项的生成代码可能导致真实 ABI 差异。CI 会校验九个包的全部十二组 TFM 基线及打包 README。

当前已发布兼容基线必须位于对应 TFM 的 `PublicAPI.Shipped.txt`，不能只创建空文件或把全量签名长期留在 `Unshipped`。新增 API 先进入同 TFM 的 `Unshipped`；发布版本时，经兼容性审阅后再提升到 `Shipped`。删除、改签名或改变基线属性会由 PublicApiAnalyzer 在构建期报告，从而保护源码和二进制调用签名。

## 新增 API

1. 添加 XML 注释。
2. 更新 `PublicAPI.Unshipped.txt`。
3. 在文档中说明使用方式或标记为高级 API。
4. 添加最小测试。

## 废弃 API

1. 添加 `[Obsolete("迁移说明", false)]`。
2. 保留兼容行为，除非明确做 major breaking change。
3. 在 `docs/guides/migration.md` 说明替代路径。
4. 不在同一次修改中顺手删除大量相邻旧代码。

## 删除 API

只有满足以下条件才可删除：

- 用户明确要求或版本策略允许破坏性变更。
- 已有替代 API。
- 文档和示例已迁移。
- 测试覆盖新路径。

## 常见风险

- 添加可选参数可能触发兼容性警告。
- 重命名类型会影响 Source Generator 输出和用户契约。
- 把内部类型设为 public 会扩大维护面。
- 删除 `[Obsolete]` API 可能破坏下游包。
