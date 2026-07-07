# PulseRPC Documentation Maintenance Skill

Use this skill when reorganizing, adding, or deleting documentation.

## Rules

1. User-facing docs live under `docs/`.
2. AI Agent docs live under `.agent/`.
3. Historical material lives under `docs/archive/`.
4. Update `docs/index.md` when adding current docs.
5. Update README links when moving public entry points.
6. Run link searches for old paths.

## Link checks

Search for moved legacy paths:

```bash
rg -n "docs/使用指南|docs/架构设计与分析|docs/待办计划|docs/性能相关|docs/设计提案与实现记录|docs/ai"
```

Search for archive leaks in current docs:

```bash
rg -n "BaseService|ConcurrentServiceBase|PulseRPC.ServiceDiscovery|PulseRPC.LoadBalancing" docs/getting-started docs/concepts docs/guides docs/reference
```

