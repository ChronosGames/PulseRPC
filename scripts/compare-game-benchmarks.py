#!/usr/bin/env python3
"""Compare paired schema-v2 samples, refusing incomplete or mismatched measurements."""
import json
from pathlib import Path
import statistics
import sys


def compare(directory, rounds):
    if rounds < 1:
        raise ValueError("At least one round is required")
    reports = {name: [json.loads((directory / f"{name}-{index}.json").read_text())
                      for index in range(rounds)] for name in ("baseline", "candidate")}
    reference = reports["baseline"][0]
    scenario_names = {item["name"] for item in reference["scenarios"]}
    for group in reports.values():
        for report in group:
            if report["schemaVersion"] != 2 or report["configuration"] != reference["configuration"]:
                raise ValueError("Incompatible measurement harness or workload")
            for field in ("runtime", "operatingSystem", "architecture", "processorCount", "serverGc"):
                if report[field] != reference[field]:
                    raise ValueError(f"Environment mismatch: {field}")
            if {item["name"] for item in report["scenarios"]} != scenario_names:
                raise ValueError("Missing benchmark scenarios")
            if len(report["samples"]) != len(scenario_names) * report["configuration"]["repetitions"]:
                raise ValueError("Raw samples missing")
            for sample in report["samples"]:
                workers = sample["workerOperations"]
                if len(workers) != sample["concurrency"] or min(workers) <= 0 or sum(workers) != sample["operations"]:
                    raise ValueError("Not every worker executed its workload")
    result = {"schema_version": 2, "rounds": rounds, "capacity_certified": False,
              "order": "AB/BA alternating", "scenarios": []}
    for name in sorted(scenario_names):
        groups = {revision: [next(item for item in report["scenarios"] if item["name"] == name)
                             for report in reports[revision]] for revision in reports}
        metrics = {}
        for metric in ("operationsPerSecond", "allocatedBytesPerOperation", "cpuMs", "lockContentions", "p95Ms"):
            values = {revision: [sample["latency"][metric] if metric == "p95Ms" else sample[metric]
                                 for sample in groups[revision]] for revision in groups}
            baseline = statistics.median(values["baseline"])
            candidate = statistics.median(values["candidate"])
            metrics[metric] = {"baseline_median": baseline, "candidate_median": candidate,
                               "change_percent": (candidate / baseline - 1) * 100 if baseline else None,
                               "baseline_samples": values["baseline"], "candidate_samples": values["candidate"]}
        result["scenarios"].append({"name": name, "metrics": metrics})
    return result


if __name__ == "__main__":
    directory = Path(sys.argv[1])
    result = compare(directory, int(sys.argv[2]))
    (directory / "comparison.json").write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps(result, indent=2))
