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
| 0 | Reproducible baseline and required-suite/Redis execution checks | Run 37494029780 passed: 80 Client, 407 Server, 59 generator, 18 infrastructure, 25 Redis tests; no skips; Debug/Release and benchmark passed |
| 1 | Lease loss, expiry, quiescence, bounded renewal, stale-writer fencing example | Runtime guard and 11 new safety tests passed in runs 37501715307 and 37502950121; durable fencing example pending |
| 2 | Bounded concurrent ingress, Actor ordering, lifecycle/context isolation | Concurrent shard implementation, 4 new tests and existing lifecycle/ordering tests passed in run 37502950121; real multiplexed cluster acceptance pending |
| 3 | Overload response, budgets, deadline/cancellation, resource recovery | Byte/connection budgets, busy/deadline replies, bounded reentrant mailbox and reliable response queue implemented; validation pending |
| 4 | Production Gateway/internal profiles, resource authorization, actual mTLS | Pending |
| 5 | Durable idempotency example, retry contracts, client/protocol compatibility | Pending |
| 6 | Separate-process cluster, real Redis, faults, load and capacity evidence | Pending |

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
