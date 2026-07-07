# Public API 策略

PulseRPC 使用 Roslyn PublicApiAnalyzer 管理公开 API。维护者不能只让代码编译通过，还需要维护 API 文件和迁移语义。

## 文件位置

- `src/PulseRPC.Abstractions/PublicAPI.Shipped.txt`
- `src/PulseRPC.Abstractions/PublicAPI.Unshipped.txt`
- `src/PulseRPC.Client/PublicAPI.Shipped.txt`
- `src/PulseRPC.Client/PublicAPI.Unshipped.txt`

其他包如果启用 PublicAPI analyzer，应按同样规则维护。

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

