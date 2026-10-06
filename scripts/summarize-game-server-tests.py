#!/usr/bin/env python3
"""Reject missing suites or silently skipped real-Redis acceptance tests."""
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def summarize(root):
    expected = {"Client", "Server", "SourceGenerator", "Infrastructure", "Backplane.Redis"}
    suites = {}
    failures = []
    redis_results = []
    for suite in sorted(expected):
        path = root / (suite + ".trx")
        if not path.is_file():
            failures.append("Missing suite: " + suite)
            continue
        document = ET.parse(path)
        results = document.findall(".//{*}UnitTestResult")
        outcomes = {}
        skipped = []
        for result in results:
            outcome = result.get("outcome", "Unknown")
            outcomes[outcome] = outcomes.get(outcome, 0) + 1
            name = result.get("testName", "")
            if outcome == "NotExecuted":
                skipped.append(name)
            elif outcome != "Passed":
                failures.append(suite + ": " + name + " => " + outcome)
            if "RedisActorLeaseStoreIntegrationTests." in name:
                redis_results.append(result)
        if not results or outcomes.get("Passed", 0) == 0:
            failures.append("No passed tests: " + suite)
        suites[suite] = {"outcomes": outcomes, "skipped": skipped}
    if not redis_results or any(result.get("outcome") != "Passed" for result in redis_results):
        failures.append("Real Redis lease integration tests must execute and pass.")
    return {"suites": suites, "failures": failures}


if __name__ == "__main__":
    root = Path(sys.argv[1])
    summary = summarize(root)
    root.mkdir(parents=True, exist_ok=True)
    output = json.dumps(summary, indent=2, ensure_ascii=False)
    (root / "summary.json").write_text(output + "\n", encoding="utf-8")
    print(output)
    sys.exit(bool(summary["failures"]))
