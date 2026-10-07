# Game server production readiness execution record

The implementation and hosted regression evidence are ready for review. This is not production capacity certification, and the goal remains incomplete until external acceptance runs finish.

- Started from `33b81a24f4d877157b60c2b970aa2014a3d6198c` (first hardening phase).
- User requested updating main during execution: local/remote main is `d49a053e6b41bf6396536074e43521ea50478710`; merged into this branch with `3c64e2b` and retargeted PR #33 to main.
- Branch: `codex/game-server-production-readiness`; draft PR https://github.com/ChronosGames/PulseRPC/pull/33.
- Local .NET 10 and Docker remain unavailable; do not install SDK 10 locally. Remote SDK 10.0.401 performs builds and integration checks.
- Implementation `e44f3c3`, actual PR merge `6cbdda01`, passed validation run https://github.com/ChronosGames/PulseRPC/actions/runs/37560263340: 645 .NET tests with zero skips, 9 Python evidence tests, 21 cluster checks, five paired benchmark rounds and separate traces.
- Durable evidence: [production results](../docs/reference/game-server-production-results.json); user workflow: [acceptance guide](../docs/guides/game-server-acceptance.md).

| Stage | Delivered and verified | Remaining external acceptance |
| --- | --- | --- |
| 1 | Immutable previous version, pinned remote SDK, bounded workload profiles, environment/config hashes and SLO accounting | Actual dedicated environment and business targets |
| 2 | Fixed synchronous worker starvation, shared schema-v2 harness, raw AB/BA samples, CPU/GC/contention and separate traces; prior 12% regressions not reproduced | Recheck on controlled hardware; no unsupported runtime optimization or capacity claim |
| 3 | PostgreSQL assets/receipt/outbox, real Redis Streams, atomic consumer inbox+notification, SIGKILL after publish and after commit, one side effect | Production retention/dead-letter policy and HA failure testing |
| 4 | Last-login session replacement, transaction session fence, safe logout, reconnect/resync and Room membership revocation | Production issuer/verifier split and application-specific tenant permissions |
| 5 | Bounded readiness, metrics, Redis/PostgreSQL outage recovery, process faults and actual owner leaf/key rotation | Cross-host partitions, HA failover and CA revocation/distribution |
| 6 | Open-loop writes/reads/echo, SQL reconciliation, durable samples, capacity driver smoke, self-hosted 26h workflow, rejection of fake short-soak evidence | Saturation curve and one continuous >=86400s run on the dedicated environment |
| 7 | Actual old/new binary mixing, rollback/upgrade, in-flight purchase drain, unchanged old C#9 client, session flag enabled last | Production rollout acceptance and final release decision after long-run evidence |

A dedicated runner/deployment and workload/SLO details were requested but not supplied. Do not substitute multiple short hosted jobs for a continuous soak or same-host processes for cross-host testing. Do not mark the goal complete based on CI alone.

Keep public API and C#9/Unity compatibility. New `RpcAdmissionException` is documented in Server Unshipped; claims snapshot has a regression test. Core protocol IDs are unchanged. No package has been published and the draft PR has not been merged.

For GitHub writes use normalized staged blobs, compare tree identity, create a commit with the expected parent, update the ref with expected head, then fetch and align the local index without discarding unstaged work. Local Git push credentials previously returned 403.
