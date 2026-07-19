#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$repo_root"

documents=(
  README.md
  docs/getting-started/quickstart.md
  docs/nuget-readme.md
)

for document in "${documents[@]}"; do
  grep -q "samples/HelloRPC" "$document"
  if grep -Eq "samples/(ChatApp|JwtAuthentication|HubFactoryExample|ServiceFactoryExample|JsonTranscoding)" "$document"; then
    echo "$document advertises a competing quickstart path."
    exit 1
  fi
done

dotnet build ./samples/HelloRPC/HelloRPC.sln -c Release --warnaserror \
  -p:ShouldUnsetParentConfigurationAndPlatform=false

server_log=$(mktemp)
dotnet run --project ./samples/HelloRPC/HelloRPC.Server/HelloRPC.Server.csproj \
  -c Release --no-build >"$server_log" 2>&1 &
server_pid=$!

cleanup() {
  kill "$server_pid" 2>/dev/null || true
  wait "$server_pid" 2>/dev/null || true
  rm -f "$server_log"
}
trap cleanup EXIT

ready=false
for _ in {1..100}; do
  if ! kill -0 "$server_pid" 2>/dev/null; then
    cat "$server_log"
    exit 1
  fi
  if grep -q "HelloRPC server ready" "$server_log"; then
    ready=true
    break
  fi
  sleep 0.1
done

if [ "$ready" != true ]; then
  cat "$server_log"
  exit 1
fi

client_output=$(timeout 30s dotnet run \
  --project ./samples/HelloRPC/HelloRPC.Client/HelloRPC.Client.csproj \
  -c Release --no-build)
printf '%s\n' "$client_output"
grep -Fxq "Hello, PulseRPC!" <<<"$client_output"
