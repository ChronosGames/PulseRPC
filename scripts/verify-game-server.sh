#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"
result_root="$repo_root/artifacts/game-server"
mkdir -p "$result_root/tests"
: "${PULSERPC_TEST_REDIS:?Set PULSERPC_TEST_REDIS to run the real Redis acceptance tests.}"
sdk_version=$(dotnet --version)
case "$sdk_version" in
  10.*) ;;
  *) echo "Expected .NET 10 SDK, got $sdk_version" >&2; exit 1 ;;
esac
dotnet --info > "$result_root/dotnet-info.txt"
git rev-parse HEAD > "$result_root/commit.txt"
dotnet restore PulseRPC.sln
dotnet build PulseRPC.sln -c Debug --no-restore --warnaserror
dotnet build PulseRPC.sln -c Release --no-restore --warnaserror

test_status=0
for project in Client Server SourceGenerator Infrastructure Backplane.Redis; do
  dotnet test "tests/PulseRPC.$project.Tests/PulseRPC.$project.Tests.csproj" \
    -c Release --no-restore \
    --logger "trx;LogFileName=$project.trx" \
    --results-directory "$result_root/tests" || test_status=1
done

python3 scripts/summarize-game-server-tests.py "$result_root/tests" || test_status=1
exit "$test_status"
