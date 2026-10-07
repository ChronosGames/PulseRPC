#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"
baseline_ref=${1:?Pass the baseline commit or ref.}
mode=${2:-smoke}
case "$mode" in
  smoke) workload=(--smoke --operations 250 --concurrency 8 --repetitions 1); rounds=1 ;;
  full) workload=(--operations 10000 --concurrency 32 --repetitions 1); rounds=5 ;;
  *) echo "Mode must be smoke or full." >&2; exit 1 ;;
esac
result_root="${PULSERPC_BENCHMARK_OUTPUT:-$repo_root/artifacts/game-server/benchmark}"
mkdir -p "$result_root"
baseline_sha=$(git rev-parse --verify "$baseline_ref^{commit}")
candidate_sha=$(git rev-parse HEAD)
temporary_root=$(mktemp -d)
baseline_tree="$temporary_root/baseline"
cleanup() {
  git -C "$repo_root" worktree remove --force "$baseline_tree" 2>/dev/null || true
  rmdir "$temporary_root" 2>/dev/null || true
}
trap cleanup EXIT
git worktree add --detach "$baseline_tree" "$baseline_sha"
dotnet --info > "$result_root/dotnet-info.txt"
python3 - "$baseline_sha" "$candidate_sha" "$mode" "$result_root/environment.json" <<'PY'
import hashlib, json, os, platform, sys
from pathlib import Path
Path(sys.argv[4]).write_text(json.dumps({
    "baseline": sys.argv[1], "candidate": sys.argv[2], "mode": sys.argv[3],
    "platform": platform.platform(), "processor": platform.processor(),
    "cpu_count": os.cpu_count(), "capacity_certified": False,
    "measurement_schema": 2,
    "shared_harness_sha256": hashlib.sha256(Path("perf/BenchmarkApp/PulseRPC.Benchmark/Architecture/ArchitectureBaselineBenchmark.cs").read_bytes()).hexdigest(),
    "cpu_info": Path("/proc/cpuinfo").read_text() if Path("/proc/cpuinfo").exists() else None,
    "note": "Same-runner regression evidence; hosted-runner results are not production capacity."
}, indent=2) + "\n", encoding="utf-8")
PY
project=perf/BenchmarkApp/PulseRPC.Benchmark/PulseRPC.Benchmark.csproj
# Both runtime revisions use the candidate measurement harness. V1 reports are
# deliberately incompatible: their synchronous hot-path worker scheduling was biased.
harness=perf/BenchmarkApp/PulseRPC.Benchmark/Architecture/ArchitectureBaselineBenchmark.cs
cp "$repo_root/$harness" "$baseline_tree/$harness"
dotnet build "$baseline_tree/$project" -c Release
dotnet build "$repo_root/$project" -c Release --warnaserror
binary=perf/BenchmarkApp/PulseRPC.Benchmark/bin/Release/net10.0/PulseRPC.Benchmark.dll
for ((round=0; round<rounds; round++)); do
  if ((round % 2 == 0)); then order=(baseline candidate); else order=(candidate baseline); fi
  for revision in "${order[@]}"; do
    if [[ "$revision" == baseline ]]; then tree="$baseline_tree"; else tree="$repo_root"; fi
    dotnet "$tree/$binary" architecture-baseline "${workload[@]}" \
      --output "$result_root/$revision-$round.json"
  done
done
python3 scripts/compare-game-benchmarks.py "$result_root" "$rounds"

if [[ "${PULSERPC_CAPTURE_TRACE:-0}" == 1 ]]; then
  dotnet tool install dotnet-trace --tool-path "$temporary_root/tools" --version 10.0.745401
  "$temporary_root/tools/dotnet-trace" --version > "$result_root/trace-version.txt"
  for revision in baseline candidate; do
    if [[ "$revision" == baseline ]]; then tree="$baseline_tree"; else tree="$repo_root"; fi
    # Tracing perturbs timings; these runs are separate from all comparison samples.
    "$temporary_root/tools/dotnet-trace" collect --show-child-io \
      --providers 'Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime:0x4001:4' \
      --output "$result_root/$revision.nettrace" -- \
      dotnet "$tree/$binary" architecture-baseline --operations 10000 --concurrency 32 --repetitions 1 \
      --output "$result_root/$revision-traced.json"
    "$temporary_root/tools/dotnet-trace" report "$result_root/$revision.nettrace" topN -n 40 \
      > "$result_root/$revision-top-functions.txt"
  done
fi
