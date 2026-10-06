# Game server hardening execution record

This is an agent execution record, not a statement of supported production guarantees.

- Baseline: `5ea04df00e1648eb53effdb0aba762f50cc01fca`.
- Working branch: `codex/game-server-hardening`.
- Local constraint: .NET 10 cannot be installed; Docker daemon is unavailable.
- Validation: GitHub Actions via the connected GitHub app. Repository write permission verified.
- Do not mark tests, capacity, or stages complete without execution evidence.

## Acceptance stages

| Stage | Scope | Status |
| --- | --- | --- |
| 0 | Reproducible baseline and required-suite/Redis execution checks | Workflow prepared; execution pending |
| 1 | Lease loss, expiry, quiescence, bounded renewal, stale-writer fencing example | Pending |
| 2 | Bounded concurrent ingress, Actor ordering, lifecycle/context isolation | Pending |
| 3 | Overload response, budgets, deadline/cancellation, resource recovery | Pending |
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
