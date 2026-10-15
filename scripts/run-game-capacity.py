#!/usr/bin/env python3
"""Run bounded open-loop workloads; strict evidence is observational, not certification."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import re
import subprocess
import threading
import time
import urllib.parse
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
HOST = ROOT / "samples/GameServer/GameServer.Host/bin/Release/net10.0/GameServer.Host.dll"
DEFAULTS = dict(durationSeconds=20, connections=8, requestsPerSecond=100,
                queuePerConnection=2, payloadBytes=128, readPercent=15, purchasePercent=5,
                requestTimeoutSeconds=5, sampleSeconds=5, minimumContinuousSeconds=0,
                environmentId="unspecified", slo=None)
BOUNDS = dict(durationSeconds=(1, 172800), connections=(1, 50000), requestsPerSecond=(1, 100000),
              queuePerConnection=(1, 32), payloadBytes=(1, 65536), readPercent=(0, 100),
              purchasePercent=(0, 100), requestTimeoutSeconds=(1, 60), sampleSeconds=(1, 300),
              minimumContinuousSeconds=(0, 172800))
METRICS = {"working_set_bytes", "cpu_seconds_total", "managed_heap_bytes", "connections", "actors",
           "requests_active", "requests_total", "requests_failed_total", "draining", "drain_rejections_total",
           "rpc_p99_milliseconds", "outbox_pending", "outbox_oldest_seconds", "storage_up"}


def finite(value):
    try:
        return type(value) in (int, float) and math.isfinite(value)
    except OverflowError:
        return False


def validate_profile(profile, require_slo=False):
    if not isinstance(profile, dict) or set(profile) - set(DEFAULTS):
        return ["invalid_profile"]
    resolved = dict(DEFAULTS, **profile)
    errors = []
    for key, (low, high) in BOUNDS.items():
        value = resolved[key]
        if type(value) is not int or not low <= value <= high:
            errors.append("invalid_profile_" + key)
    environment = resolved["environmentId"]
    if not isinstance(environment, str) or not environment.strip() or "REPLACE" in environment:
        errors.append("invalid_profile_environmentId")
    if errors:
        return errors
    if resolved["readPercent"] + resolved["purchasePercent"] > 100:
        errors.append("invalid_profile_workload_mix")
    if resolved["minimumContinuousSeconds"] > resolved["durationSeconds"]:
        errors.append("invalid_profile_continuous_duration")
    slo = resolved["slo"]
    if slo is None:
        if require_slo:
            errors.append("slo_required")
    elif (not isinstance(slo, dict) or set(slo) != {"p99Ms", "maxFailureRatio"}
          or not finite(slo.get("p99Ms")) or slo["p99Ms"] <= 0
          or not finite(slo.get("maxFailureRatio")) or not 0 <= slo["maxFailureRatio"] <= 1):
        errors.append("invalid_slo")
    return errors


def assess(report, exit_code, expected_profile=None, require_evidence=False, soak=False,
           profile_sha256=None, commit=None):
    errors = []
    if not isinstance(report, dict):
        report = {}
    profile = report.get("profile")
    errors.extend(validate_profile(profile, require_evidence))
    if require_evidence and isinstance(profile, dict) and set(profile) != set(DEFAULTS):
        errors.append("incomplete_report_profile")
    counts = report.get("result")
    if not isinstance(counts, dict):
        counts = {}
    fields = ("offered", "dispatched", "succeeded", "errors", "generatorDropped", "inFlight", "seconds", "p99Ms")
    if require_evidence:
        fields += ("corruption",)
    for field in fields:
        value = counts.get(field)
        if not finite(value) or value < 0 or (field not in ("seconds", "p99Ms") and int(value) != value):
            errors.append("invalid_counter_" + field)
    configured = isinstance(profile, dict) and isinstance(profile.get("slo"), dict) and bool(profile["slo"])
    if errors:
        return dict(execution_passed=False, slo_configured=configured, slo_passed=False, failures=errors)
    profile = dict(DEFAULTS, **profile)
    if expected_profile is not None and (validate_profile(expected_profile, require_evidence)
            or profile != dict(DEFAULTS, **expected_profile)):
        errors.append("profile_mismatch")
    if exit_code != 0 or report.get("complete") is not True or report.get("assetIntegrity") is not True:
        errors.append("execution_or_asset_integrity_failed")
    if require_evidence:
        if report.get("schemaVersion") != 1 or type(report.get("schemaVersion")) is not int:
            errors.append("invalid_report_schema")
        if report.get("continuousRequirementMet") is not True:
            errors.append("continuous_requirement_not_met")
        if profile_sha256 is not None and report.get("profileSha256") != profile_sha256:
            errors.append("profile_hash_mismatch")
        environment = report.get("environment")
        if commit is not None and (not isinstance(environment, dict) or environment.get("commit") != commit):
            errors.append("commit_mismatch")
    expected = profile["durationSeconds"] * profile["requestsPerSecond"]
    if counts["offered"] != expected:
        errors.append("scheduled_arrivals_incomplete")
    if counts["dispatched"] + counts["generatorDropped"] != counts["offered"]:
        errors.append("arrival_accounting_mismatch")
    if counts["succeeded"] + counts["errors"] != counts["dispatched"] or counts["inFlight"] != 0:
        errors.append("completion_accounting_mismatch")
    if counts["succeeded"] <= 0 or counts.get("corruption", 0) != 0:
        errors.append("no_successful_work_or_payload_corruption")
    if counts["seconds"] < max(profile["durationSeconds"], profile["minimumContinuousSeconds"]):
        errors.append("duration_incomplete")
    if soak and (profile["durationSeconds"] < 86400 or profile["minimumContinuousSeconds"] < 86400
                 or counts["seconds"] < 86400 or report.get("soak24HoursCompleted") is not True):
        errors.append("soak_incomplete")
    slo = profile["slo"]
    if slo:
        ratio = (counts["errors"] + counts["generatorDropped"]) / max(1, counts["offered"])
        if ratio > slo["maxFailureRatio"] or counts["p99Ms"] > slo["p99Ms"]:
            errors.append("slo_exceeded")
    return dict(execution_passed=not errors, slo_configured=bool(slo),
                slo_passed=not errors if slo else None, failures=errors)


def parse_metrics(body):
    """Keep only known numeric metrics, never arbitrary response text or URLs."""
    values, identities, series, generations, queues = {}, set(), [], set(), {}
    queue_metrics = {"queue_depth", "queue_capacity", "queue_rejected_total"}
    for line in body.splitlines():
        if not line or line.startswith("#"):
            continue
        match = re.fullmatch(r'game_([a-z0-9_]+)\{node="([^"\\]+)"([^}]*)\} ([^ ]+)', line)
        if not match:
            raise ValueError("invalid_metrics")
        name, node, labels, raw = match.groups()
        value = float(raw)
        if not math.isfinite(value) or value < 0:
            raise ValueError("invalid_metric_value")
        identities.add(node)
        if name in METRICS:
            if labels or name in values:
                raise ValueError("duplicate_or_labeled_metric")
            values[name] = value
        elif name == "gc_collections_total":
            generation = re.fullmatch(r',generation="([012])"', labels)
            if not generation or generation[1] in generations:
                raise ValueError("invalid_gc_metric")
            generations.add(generation[1])
            series.append(dict(name=name, generation=int(generation[1]), value=value))
        elif name in queue_metrics:
            queue = re.fullmatch(r',queue="((?:[^"\\]|\\.)*)"', labels)
            if not queue or not queue[1].strip():
                raise ValueError("invalid_queue_metric")
            seen = queues.setdefault(queue[1], set())
            if name in seen:
                raise ValueError("duplicate_queue_metric")
            seen.add(name)
            series.append(dict(name=name, queue=queue[1], value=value))
    if not queues or any(seen != queue_metrics for seen in queues.values()):
        raise ValueError("missing_queue_metrics")
    if generations != {"0", "1", "2"}:
        raise ValueError("missing_gc_metrics")
    if len(identities) != 1 or not METRICS <= values.keys() or values["storage_up"] != 1:
        raise ValueError("missing_metrics_or_storage_unavailable")
    return dict(node=next(iter(identities)), values=values, series=series)


def summarize_metrics(samples, expected_nodes, started, ended, max_gap=30):
    nodes, failures, identities = [], [], []
    for index in range(expected_nodes):
        records = [s for s in samples if s.get("node_index") == index]
        valid = [s for s in records if "values" in s and "node" in s]
        names = sorted({s["node"] for s in valid})
        times = sorted(s["monotonic"] for s in valid)
        gaps = [b-a for a, b in zip(times, times[1:])]
        covered = bool(times) and times[0] <= started and times[-1] >= ended and max(gaps, default=0) <= max_gap
        passed = len(valid) == len(records) and len(valid) >= 2 and len(names) == 1 and covered
        if not passed:
            failures.append("node_metrics_incomplete_" + str(index))
        identities.extend(names)
        nodes.append(dict(node_index=index, identities=names, samples=len(records), successful_samples=len(valid),
                          failed_samples=len(records)-len(valid), coverage_complete=covered, passed=passed,
                          max_sample_gap_seconds=max(gaps, default=None)))
    if len(set(identities)) != expected_nodes or len(identities) != expected_nodes:
        failures.append("node_identity_count_mismatch")
    if any(s.get("node_index") not in range(expected_nodes) for s in samples):
        failures.append("unexpected_node_metrics")
    return dict(passed=not failures, expected_node_count=expected_nodes, nodes=nodes, failures=failures)


class MetricCollector:
    def __init__(self, endpoints, directory):
        self.endpoints = endpoints
        self.directory = directory
        self.samples = []
        self.stop = threading.Event()
        self.thread = None

    def capture(self):
        for index, endpoint in enumerate(self.endpoints):
            sample = dict(time=time.time(), node_index=index)
            try:
                with urllib.request.urlopen(endpoint.rstrip("/") + "/metrics", timeout=3) as response:
                    body = response.read(1_000_001)
                    if response.status != 200 or len(body) > 1_000_000:
                        raise ValueError("invalid_metrics_response")
                    sample.update(parse_metrics(body.decode("utf-8")))
            except Exception as error:
                sample["error_type"] = type(error).__name__
            sample["monotonic"] = time.monotonic()
            self.samples.append(sample)
            with (self.directory / "node-metrics.jsonl").open("a") as stream:
                stream.write(json.dumps(sample, allow_nan=False) + "\n")
            with (self.directory.parent / "node-metrics.jsonl").open("a") as stream:
                stream.write(json.dumps(dict(sample, step=self.directory.name), allow_nan=False) + "\n")
            with (self.directory / ("node-" + str(index) + "-metrics.jsonl")).open("a") as stream:
                stream.write(json.dumps(sample, allow_nan=False) + "\n")

    def start(self):
        self.capture()
        def loop():
            while not self.stop.wait(5):
                self.capture()
        self.thread = threading.Thread(target=loop, daemon=True)
        self.thread.start()

    def finish(self):
        self.stop.set()
        if self.thread:
            self.thread.join()  # Each request is bounded; never assess a still-running collector.
        self.capture()


def read_report(path):
    try:
        return json.loads(path.read_text())
    except (OSError, ValueError):
        return {}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", required=True, type=Path)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", required=True, type=int)
    parser.add_argument("--rates", default="100,200,400")
    parser.add_argument("--duration", default=60, type=int)
    parser.add_argument("--soak", action="store_true")
    parser.add_argument("--admin", action="append", default=[], help="Admin base URL; repeat per node")
    parser.add_argument("--require-evidence", action="store_true", help="Require SLO, matching reports and complete all-node telemetry")
    parser.add_argument("--expected-node-count", type=int, default=3)
    parser.add_argument("--private-log-directory", type=Path, help="Keep raw client logs outside exported evidence")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/game-server/capacity")
    options = parser.parse_args()
    try:
        source = options.profile.read_bytes()
        profile = json.loads(source)
    except (OSError, ValueError):
        parser.error("Profile must be a readable JSON object")
    errors = validate_profile(profile, options.require_evidence)
    if errors:
        parser.error(", ".join(errors))
    profile = dict(DEFAULTS, **profile)
    if not 1 <= options.port <= 65535 or not 1 <= options.duration <= 86400:
        parser.error("Invalid endpoint port or step duration")
    if options.expected_node_count < 1:
        parser.error("Expected node count must be positive")
    if options.soak and profile["durationSeconds"] < 86400:
        parser.error("A soak requires at least 86400 continuous seconds")
    try:
        rates = [profile["requestsPerSecond"]] if options.soak else [int(rate) for rate in options.rates.split(",")]
    except ValueError:
        parser.error("Rates must be integers")
    if not rates or any(rate < 1 or rate > 100000 for rate in rates):
        parser.error("Rates must be positive and at most 100000 per second")
    for endpoint in options.admin:
        parsed = urllib.parse.urlsplit(endpoint)
        if parsed.scheme not in ("http", "https") or not parsed.hostname or parsed.username or parsed.password or parsed.query or parsed.fragment:
            parser.error("Admin URLs must be HTTP(S) base URLs without credentials, queries or fragments")
    if options.require_evidence and (len(options.admin) != options.expected_node_count
                                    or len({url.rstrip('/') for url in options.admin}) != options.expected_node_count):
        parser.error("Strict evidence requires distinct admin URLs for every expected node")
    if not HOST.is_file():
        parser.error("Build GameServer.Host in Release first")
    for name in ("GAME_REDIS", "GAME_POSTGRES", "GAME_JWT_KEY"):
        if not os.environ.get(name):
            parser.error("Set " + name)
    run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ-") + uuid.uuid4().hex[:8]
    output = options.output / run_id
    output.mkdir(parents=True)
    (output / "profile.source.json").write_bytes(source)
    summary = dict(schema_version=1, run_id=run_id, steps=[], capacity_certified=False,
                   soak_24_hours_completed=False, require_evidence=options.require_evidence,
                   expected_node_count=options.expected_node_count, failures=[],
                   profile_sha256=hashlib.sha256(source).hexdigest(), platform=platform.platform(),
                   cpu_count=os.cpu_count(), note="Observed test envelope only; not production certification.")
    try:
        commit = os.environ.get("GAME_CANDIDATE_SHA") or subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
        if not re.fullmatch(r"[0-9a-fA-F]{40}", commit):
            raise ValueError("invalid_candidate_sha")
        summary.update(commit=commit, sdk=subprocess.check_output(["dotnet", "--version"], cwd=ROOT, text=True).strip())
        env = dict(os.environ, GAME_CANDIDATE_SHA=commit)
        for index, rate in enumerate(rates):
            resolved = dict(profile, requestsPerSecond=rate)
            if options.soak:
                resolved["minimumContinuousSeconds"] = max(86400, resolved["minimumContinuousSeconds"])
            else:
                resolved.update(durationSeconds=options.duration, minimumContinuousSeconds=0)
            directory = output / f"step-{index}-{rate}rps"
            directory.mkdir()
            profile_path = directory / "profile.json"
            profile_path.write_text(json.dumps(resolved, indent=2, allow_nan=False) + "\n")
            monitor = MetricCollector(options.admin, directory)
            exit_code, process = -1, None
            monitor.start()
            started = time.monotonic()
            try:
                log_path = directory / "client.log"
                if options.private_log_directory is not None:
                    options.private_log_directory.mkdir(parents=True, exist_ok=True)
                    log_path = options.private_log_directory / f"client-{index}-{rate}rps.log"
                with log_path.open("w") as log:
                    process = subprocess.Popen(["dotnet", str(HOST), "load", str(profile_path), options.host, str(options.port), str(directory)],
                                               cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
                    exit_code = process.wait(timeout=resolved["durationSeconds"] + 900)
            except BaseException:
                if process is not None and process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=30)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=10)
                raise
            finally:
                ended = time.monotonic()
                monitor.finish()
                result_path = directory / "results.json"
                report = read_report(result_path)
                assessment = assess(report, exit_code, resolved, options.require_evidence, options.soak,
                                    hashlib.sha256(profile_path.read_bytes()).hexdigest(), commit)
                if options.require_evidence and ended - started < resolved["durationSeconds"]:
                    assessment["failures"].append("observed_duration_incomplete")
                    assessment.update(execution_passed=False, slo_passed=False)
                metrics = summarize_metrics(monitor.samples, options.expected_node_count, started, ended)
                (directory / "metrics-summary.json").write_text(json.dumps(metrics, indent=2) + "\n")
                if options.require_evidence and not metrics["passed"]:
                    assessment["failures"].extend(metrics["failures"])
                    assessment.update(execution_passed=False, slo_passed=False)
                step = dict(rate=rate, connections=resolved["connections"], seconds=ended-started,
                            result_file=str(result_path.relative_to(output)), metrics=metrics, **assessment)
                summary["steps"].append(step)
                if options.soak:
                    summary["soak_24_hours_completed"] = assessment["execution_passed"] and report.get("soak24HoursCompleted") is True
                print(json.dumps(step, allow_nan=False), flush=True)
            if not assessment["execution_passed"]:
                break
    except BaseException as error:
        summary["failures"].append(type(error).__name__)
    finally:
        passed_rates = [step["rate"] for step in summary["steps"] if step["slo_passed"] is True]
        summary["highest_tested_slo_passing_rate"] = max(passed_rates) if passed_rates else None
        summary["all_requested_steps_completed"] = len(summary["steps"]) == len(rates)
        summary["passed"] = (not summary["failures"] and summary["all_requested_steps_completed"]
                             and all(step["execution_passed"] for step in summary["steps"])
                             and (not options.soak or summary["soak_24_hours_completed"]))
        (output / "summary.json").write_text(json.dumps(summary, indent=2, allow_nan=False) + "\n")
    if not summary["passed"]:
        raise SystemExit("Capacity run failed; inspect " + str(output / "summary.json"))


if __name__ == "__main__":
    main()
