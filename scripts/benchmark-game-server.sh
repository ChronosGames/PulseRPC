#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"
baseline_ref=${1:?Pass the baseline commit or ref.}
mode=${2:-smoke}
case "$mode" in
  smoke) workload=(--smoke --operations 250 --concurrency 8 --repetitions 1) ;;
  full) workload=(--operations 2000 --concurrency 32 --repetitions 5) ;;
  *) echo "Mode must be smoke or full." >&2; exit 1 ;;
esac
result_root="$repo_root/artifacts/game-server/benchmark"
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
import json, os, platform, sys
from pathlib import Path
Path(sys.argv[4]).write_text(json.dumps({
    "baseline": sys.argv[1], "candidate": sys.argv[2], "mode": sys.argv[3],
    "platform": platform.platform(), "processor": platform.processor(),
    "cpu_count": os.cpu_count(), "capacity_certified": False,
    "note": "Same-runner regression evidence; hosted-runner results are not production capacity."
}, indent=2) + "\n", encoding="utf-8")
PY
project=perf/BenchmarkApp/PulseRPC.Benchmark/PulseRPC.Benchmark.csproj
(
  cd "$baseline_tree"
  dotnet run --project "$project" -c Release -- architecture-baseline \
    "${workload[@]}" --output "$result_root/baseline.json"
)
dotnet run --project "$project" -c Release -- architecture-baseline \
  "${workload[@]}" --output "$result_root/candidate.json" \
  --compare "$result_root/baseline.json" --max-regression-percent 0
