# Game server hardening execution record

This is an agent execution record, not a statement of supported production guarantees.

- Baseline: `5ea04df00e1648eb53effdb0aba762f50cc01fca`.
- Working branch: `codex/game-server-hardening`.
- Local constraint: .NET 10 cannot be installed; Docker daemon is unavailable.
- Validation: GitHub Actions via the connected GitHub app. Repository write permission verified.
- Draft PR: https://github.com/ChronosGames/PulseRPC/pull/32.
- Initial remote commit: `9306c7b361f74d80ba4ba098c2ec05e9269334c4`.
- Initial Actions run: `37493266293`; benchmark passed. Correctness build was blocked by
  existing SourceLink -> Microsoft.Build.Tasks.Git 10.0.102 advisory GHSA-23fw-v26w-5fgq.
  Upgrade SourceLink to patched 10.0.111; preserve warnings-as-errors and rerun.
- Local Git push credentials return HTTP 403; connected GitHub app writes succeed.
  Publish through Git Data API, then fetch and fast-forward the local branch.
- Do not mark tests, capacity, or stages complete without execution evidence.

## Acceptance stages

| Stage | Scope | Status |
| --- | --- | --- |
| 0 | Reproducible baseline and required-suite/Redis execution checks | Passed: baseline run 37494029780; final implementation run 37512688617 has 644 passing tests, no skips, Debug/Release builds and five-round same-runner comparisons |
| 1 | Lease loss, expiry, quiescence, bounded renewal, stale-writer fencing example | Passed: runtime guard tests plus PostgreSQL concurrent replay, stale generation rejection without cancellation, and suspended-owner recovery |
| 2 | Bounded concurrent ingress, Actor ordering, lifecycle/context isolation | Passed: bounded shard/lane tests and generated-client multiplexed RPC through three independent processes |
| 3 | Overload response, budgets, deadline/cancellation, resource recovery | Passed: budget/deadline/reentrant/response regressions; SQL-lock burst accepts 32 and returns 224 SERVER_BUSY responses; same connection recovers |
| 4 | Gateway/internal profiles, resource authorization, actual mTLS | Passed: four TLS negative cases; anonymous, cross-player, expired-token/session and public-endpoint non-member node credential checks |
| 5 | Durable idempotency example, retry contracts, client/protocol compatibility | Passed: PostgreSQL receipt/outbox/inbox and old-writer tests; independent C# 9 V1 client reads V2 DTO using stable protocol IDs |
| 6 | Separate-process cluster, real Redis, faults, load and capacity evidence | Passed CI acceptance: kill/SIGSTOP/Redis outage recovery, hot/large payload loads, latency and recovery gates; production capacity remains explicitly uncertified |

Implementation acceptance run: https://github.com/ChronosGames/PulseRPC/actions/runs/37512688617.
Raw metrics and actual runtime/SDK/merge-commit metadata are retained in
`docs/reference/game-server-validation-results.json`; human-readable results are in
`docs/guides/game-server-acceptance.md`. Subsequent documentation-only commits rerun all CI,
and their checks are linked on PR #32.

## Execution rules

Use the existing code-change, generator and documentation skills under `.agent/skills`.
Preserve public APIs; update Unshipped baselines for additions. Avoid generated client syntax
newer than C# 9. Keep authorization before deserialization and activation. Do not equate
cooperative cancellation with cancellation of database side effects. Retain payload/connection
ownership until execution actually finishes. Scope lease cleanup to the old generation.

Remote validation must run on the final commit. Hosted performance results are regression
evidence; representative production capacity requires a controlled environment. Real game
asset transactions must integrate the fencing/idempotency contract demonstrated by examples.

## Follow-up findings

- Original Build workflow run 37494029763: .NET build/tests, package API validation, samples,
  and iOS IL2CPP passed. Unity clean-UPM job 112374909423 failed before executing Unity:
  its semantic-version step rejects the manifest/tarball modifications deliberately made by
  the workflow. Fix that job's versioning/dirty-worktree configuration and rerun.
- Publish Git Data API blobs from the Git index (`git show :path`), not raw working-tree bytes;
  the local checkout applies line-ending filters. Verify returned tree SHA equals `git write-tree`.
- Run 37501715307: Unity clean UPM import and actual TCP roundtrip passed after the workflow fix.
- Run 37501715306 hit a KCP test timeout; its minimal peer never consumed ACKs, causing
  retransmitted fragment bursts against a deliberately small receive buffer. Complete the
  peer's ACK processing. Do not mask the failure with a retry or skipped test. Validation
  now collects all correctness suites even if an earlier suite fails.
- Run 37502950121 (0ce95f4): Debug/Release, all five correctness suites and baseline/candidate
  smoke benchmark passed. Hosted benchmarks remain regression evidence, not capacity certification.

- Run 37508687232: 637 tests passed without skips (80 Client, 455 Server, 59 generator,
  18 infrastructure, 25 Redis); PostgreSQL verification passed all durable replay/fencing
  checks; actual TLS rejected absent certificate, untrusted issuer, unauthorized subject and
  wrong hostname. Cluster RPC exposed missing runtime-generated Gateway routes.
- Fix the Server analyzer project reference so built-in routing is generated in the runtime
  assembly. Keep those generated helper types internal to avoid C# name collisions with host
  output. Add a compiled runtime routing test and generator visibility regressions.
- Five-round same-runner benchmark in run 37508687232: transport/mailbox throughput roughly
  unchanged; hot lookup throughput -9.1%; Actor lifecycle P95 +15.8% with throughput +5.5%.
  These mixed results are evidence, not a claim of blanket improvement or production capacity.

- Run 37510446616 first passed the complete three-process fault suite and 640 tests. Its 4 KiB
  Echo P99 exceeded one second with 8 KiB socket buffers. Explicit 64 KiB client/listener/node
  buffers brought P99 to 12.7 ms in run 37511467610 and 16.7 ms in run 37512688617; no global
  transport defaults were changed. Node socket budgets are now configurable.
- Align the sample quarantine (20s) with placement lease (9s) and RPC timeout (5s), so a failed
  candidate cannot immediately re-enter placement while handoff waits for its old lease.
  CI now enforces kill recovery <20s and closed-loop Echo P99 <250ms.
- Public Gateway and internal TLS paths share runtime node-authentication routes. Add an
  opt-in certificate node identity allowlist, and exercise CA-trusted outsider credentials
  through the public endpoint as well as TLS peer-subject rejection.
