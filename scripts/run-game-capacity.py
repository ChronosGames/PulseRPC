#!/usr/bin/env python3
"""Run bounded open-loop workloads on an existing isolated game-server deployment.

This driver never turns hosted or short-run evidence into production certification.
Each workload is a new process/run; a soak must be continuous within that process.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import subprocess
import threading
import time
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
HOST = ROOT / "samples/GameServer/GameServer.Host/bin/Release/net10.0/GameServer.Host.dll"


def assess(report, exit_code):
    counts = report.get("result", {})
    profile = report.get("profile", {})
    errors = []
    for field in ("offered", "dispatched", "succeeded", "errors", "generatorDropped", "inFlight", "seconds", "p99Ms"):
        value = counts.get(field)
        if not isinstance(value, (int, float)) or isinstance(value, bool) or not math.isfinite(value) or value < 0:
            errors.append("invalid_counter_" + field)
    if errors:
        return {"execution_passed": False, "slo_configured": bool(profile.get("slo")), "slo_passed": False, "failures": errors}
    if exit_code != 0 or not report.get("complete") or not report.get("assetIntegrity"):
        errors.append("execution_or_asset_integrity_failed")
    expected = profile.get("durationSeconds", 0) * profile.get("requestsPerSecond", 0)
    if expected <= 0 or counts.get("offered") != expected:
        errors.append("scheduled_arrivals_incomplete")
    if counts.get("dispatched", 0) + counts.get("generatorDropped", 0) != counts.get("offered"):
        errors.append("arrival_accounting_mismatch")
    if counts.get("succeeded", 0) + counts.get("errors", 0) != counts.get("dispatched") or counts.get("inFlight") != 0:
        errors.append("completion_accounting_mismatch")
    if counts.get("succeeded", 0) <= 0 or counts.get("corruption", 0) != 0:
        errors.append("no_successful_work_or_payload_corruption")
    if counts.get("seconds", 0) < profile.get("durationSeconds", 0):
        errors.append("duration_incomplete")
    # Recompute configured limits; don't trust a stored boolean alone.
    slo = profile.get("slo")
    if slo:
        ratio = (counts.get("errors", 0) + counts.get("generatorDropped", 0)) / max(1, counts.get("offered", 0))
        if ratio > slo["maxFailureRatio"] or counts.get("p99Ms", float("inf")) > slo["p99Ms"]:
            errors.append("slo_exceeded")
    return {"execution_passed": not errors, "slo_configured": bool(slo),
            "slo_passed": not errors if slo else None, "failures": errors}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", required=True, type=Path)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", required=True, type=int)
    parser.add_argument("--rates", default="100,200,400")
    parser.add_argument("--duration", default=60, type=int, help="Seconds per capacity step; does not shorten a soak")
    parser.add_argument("--soak", action="store_true")
    parser.add_argument("--admin", action="append", default=[], help="Forwarded loopback admin base URL; repeat per node")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/game-server/capacity")
    options = parser.parse_args()
    source = options.profile.read_bytes()
    profile = json.loads(source)
    if not 1 <= options.port <= 65535 or not 1 <= options.duration <= 86400:
        parser.error("Invalid endpoint port or step duration")
    if not profile.get("environmentId") or "REPLACE" in profile["environmentId"]:
        parser.error("Name the actual controlled environment in the profile first")
    if options.soak and profile.get("durationSeconds", 0) < 86400:
        parser.error("A soak requires at least 86400 continuous seconds")
    rates = [profile["requestsPerSecond"]] if options.soak else [int(rate) for rate in options.rates.split(",")]
    if not rates or any(rate < 1 or rate > 100000 for rate in rates):
        parser.error("Rates must be positive and at most 100000 per second")
    if not HOST.is_file():
        parser.error("Build GameServer.Host in Release first")
    for name in ("GAME_REDIS", "GAME_POSTGRES", "GAME_JWT_KEY"):
        if not os.environ.get(name):
            parser.error("Set " + name)
    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex[:8]
    output = options.output / run_id
    output.mkdir(parents=True)
    (output / "profile.source.json").write_bytes(source)
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    sdk = subprocess.check_output(["dotnet", "--version"], cwd=ROOT, text=True).strip()
    env = dict(os.environ, GAME_CANDIDATE_SHA=commit)
    summary = {"schema_version": 1, "run_id": run_id, "commit": commit, "sdk": sdk,
               "platform": platform.platform(), "cpu_count": os.cpu_count(),
               "profile_sha256": hashlib.sha256(source).hexdigest(), "steps": [],
               "capacity_certified": False, "soak_24_hours_completed": False,
               "note": "Observed test envelope only; target hardware, business SLO and topology require review."}
    stop_metrics = threading.Event()

    def collect_metrics():
        with (output / "node-metrics.jsonl").open("w", buffering=1) as stream:
            while not stop_metrics.is_set():
                for index, endpoint in enumerate(options.admin):
                    try:
                        with urllib.request.urlopen(endpoint.rstrip("/") + "/metrics", timeout=3) as response:
                            value = {"metrics": response.read(1_000_000).decode(), "status": response.status}
                    except Exception as error:
                        value = {"error_type": type(error).__name__}
                    stream.write(json.dumps({"time": time.time(), "node_index": index, **value}) + "\n")
                stop_metrics.wait(5)

    monitor = threading.Thread(target=collect_metrics, daemon=True)
    monitor.start()
    try:
        for index, rate in enumerate(rates):
            resolved = dict(profile, requestsPerSecond=rate)
            if options.soak:
                resolved["minimumContinuousSeconds"] = max(86400, resolved.get("minimumContinuousSeconds", 0))
            else:
                resolved.update(durationSeconds=options.duration, minimumContinuousSeconds=0)
            directory = output / f"step-{index}-{rate}rps"
            directory.mkdir()
            profile_path = directory / "profile.json"
            profile_path.write_text(json.dumps(resolved, indent=2) + "\n")
            started = time.monotonic()
            with (directory / "client.log").open("w") as log:
                process = subprocess.Popen(["dotnet", str(HOST), "load", str(profile_path), options.host, str(options.port), str(directory)],
                                           cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
                try:
                    exit_code = process.wait(timeout=resolved["durationSeconds"] + 900)
                except BaseException:
                    process.terminate()
                    try:
                        process.wait(timeout=30)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=10)
                    raise
            result_path = directory / "results.json"
            report = json.loads(result_path.read_text()) if result_path.is_file() else {}
            assessment = assess(report, exit_code)
            step = {"rate": rate, "connections": resolved["connections"], "seconds": time.monotonic()-started,
                    "result_file": str(result_path.relative_to(output)), **assessment}
            summary["steps"].append(step)
            if options.soak:
                summary["soak_24_hours_completed"] = (assessment["execution_passed"]
                    and report.get("soak24HoursCompleted") is True and report["result"]["seconds"] >= 86400)
            (output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
            print(json.dumps(step), flush=True)
            if not assessment["execution_passed"]:
                break
    finally:
        stop_metrics.set()
        monitor.join(timeout=5)
        passed_rates = [step["rate"] for step in summary["steps"] if step["slo_passed"] is True]
        summary["highest_tested_slo_passing_rate"] = max(passed_rates) if passed_rates else None
        summary["all_requested_steps_completed"] = len(summary["steps"]) == len(rates)
        (output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    if not summary["all_requested_steps_completed"] or any(not step["execution_passed"] for step in summary["steps"]):
        raise SystemExit("Capacity run failed; inspect " + str(output / "summary.json"))


if __name__ == "__main__":
    main()
