# PulseRPC Code Change Skill

Use this skill when changing PulseRPC runtime code outside Source Generator-specific work.

## Steps

1. Read `.agent/repo-map.md` and `.agent/change-playbooks.md`.
2. Identify the owning project under `src/`.
3. Search for existing tests before editing.
4. Keep changes scoped to the requested behavior.
5. Update docs only when user-facing behavior changes.
6. Run the test subset from `.agent/testing-matrix.md`.

## Required checks

- Public API changes follow `.agent/public-api-policy.md`.
- Protocol or transport changes include behavior tests.
- Cluster changes do not mix user authentication and node authentication.
- Unity-facing client changes avoid syntax newer than C# 9.0 in generated code.

