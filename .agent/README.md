# PulseRPC Agent 文档

`.agent/` 只放给 AI Agent、自动化脚本和维护者执行任务时使用的操作文档。面向用户的产品文档放在 `docs/`。

## 必读顺序

1. [仓库地图](repo-map.md)
2. [修改剧本](change-playbooks.md)
3. [测试矩阵](testing-matrix.md)
4. [Public API 策略](public-api-policy.md)
5. [文档风格](doc-style.md)

## Skill 草案

- [通用代码修改](skills/pulserpc-code-change/SKILL.md)
- [Source Generator 修改](skills/pulserpc-source-generator/SKILL.md)
- [文档维护](skills/pulserpc-doc-maintenance/SKILL.md)
- [发布检查](skills/pulserpc-release-check/SKILL.md)

## 基本原则

- 先读当前源码，再相信历史文档。
- 不删除公开兼容 API，除非任务明确要求破坏性变更并给出迁移策略。
- 修改协议、生成器、路由、传输层时必须补测试。
- 文档移动后必须修链接。
- 历史资料进入 `docs/archive/`，不要混入当前使用指南。

