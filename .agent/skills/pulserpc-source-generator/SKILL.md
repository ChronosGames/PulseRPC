# PulseRPC Source Generator Skill

Use this skill when changing `PulseRPC.Client.SourceGenerator` or `PulseRPC.Server.SourceGenerator`.

## Required context

- `docs/concepts/source-generation.md`
- `tests/PulseRPC.SourceGenerator.Tests`
- Client generator output must be Unity-friendly.

## Steps

1. Add or update a SourceGenerator test that captures the desired generated shape.
2. Modify generator helpers or models.
3. Verify generated code avoids runtime reflection when possible.
4. Check protocol ID consistency, especially method overloads.
5. Run `dotnet test tests/PulseRPC.SourceGenerator.Tests/PulseRPC.SourceGenerator.Tests.csproj`.
6. Run Client/Server tests if generated code affects runtime contracts.

## Avoid

- C# syntax newer than 9.0 in client generated code.
- Hidden string-based routing when protocol IDs are expected.
- Large generated blocks with no diagnostics for invalid user contracts.

