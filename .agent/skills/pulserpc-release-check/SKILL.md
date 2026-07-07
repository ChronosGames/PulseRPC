# PulseRPC Release Check Skill

Use this skill before packaging or publishing PulseRPC packages.

## Checklist

1. `dotnet restore`
2. `dotnet build PulseRPC.sln -c Release`
3. Run core tests from `.agent/testing-matrix.md`.
4. Check `Directory.Build.props` package version.
5. Check PublicAPI shipped/unshipped state.
6. Confirm README and `docs/index.md` point to current docs.
7. Confirm samples marked as historical are not advertised as current templates.

## Notes

- Do not publish with generated `bin/obj`, logs, Unity `Library`, or package caches in the working tree.
- If Redis/Testcontainers tests are skipped, record the reason.

