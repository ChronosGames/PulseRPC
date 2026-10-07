# Game server production readiness execution record

This records work in progress; it is not a production support or capacity guarantee.

- Starting point: `33b81a24f4d877157b60c2b970aa2014a3d6198c` (seven hardening stages completed).
- Branch: `codex/game-server-production-readiness`; keep the previous PR separate.
- Local .NET 10 and Docker are unavailable. Build and integration validation run remotely.
- Ask for a dedicated remote runner/environment and workload/SLO targets; continue independent work while waiting.
- The selected general-game workload is the existing PostgreSQL purchase flow, with Redis Streams for actual broker delivery.
- Never mark dedicated cross-host capacity or a continuous 24-hour soak complete from hosted short CI runs.

| Stage | Deliverable | State |
| --- | --- | --- |
| 1 | Reproducible environment, workload profiles and explicit SLO evidence | In progress |
| 2 | Corrected shared benchmark, traces and attributed Actor regressions | In progress: found synchronous worker enumeration bias; new harness needs remote validation |
| 3 | Durable purchase/outbox/inbox through a real broker and crash-window verification | Pending |
| 4 | Session replacement, reconnect, expiry and authorization verification | Pending |
| 5 | Operational metrics and fault/rotation scenarios | Pending |
| 6 | Capacity curves and a continuous 24-hour soak on controlled hardware | Pending external environment and business targets |
| 7 | Mixed versions, drain, rollback and final candidate evidence | Pending |

Keep API and C# 9/Unity compatibility. Each completed stage requires actual execution evidence.
Use final-commit CI results; store workload and environment hashes with all performance data.
For remote Git writes use normalized index blobs, verify tree identity, update refs with expected head,
then fetch/fast-forward locally. GitHub app writes are available; local Git credentials previously returned 403.
