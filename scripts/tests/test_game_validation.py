import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPTS = Path(__file__).resolve().parents[1]


def module(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), SCRIPTS / (name + ".py"))
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


capacity = module("run-game-capacity")
benchmarks = module("compare-game-benchmarks")


class CapacityEvidenceTests(unittest.TestCase):
    def report(self):
        return {"complete": True, "assetIntegrity": True,
                "profile": {"durationSeconds": 2, "requestsPerSecond": 10, "slo": {"p99Ms": 100, "maxFailureRatio": .01}},
                "result": {"offered": 20, "dispatched": 20, "succeeded": 20, "errors": 0,
                           "generatorDropped": 0, "inFlight": 0, "seconds": 2, "p99Ms": 10, "corruption": 0}}

    def test_complete_consistent_execution_meets_configured_slo(self):
        self.assertTrue(capacity.assess(self.report(), 0)["slo_passed"])

    def test_generator_drops_count_against_slo_even_when_server_reports_no_errors(self):
        report = self.report()
        report["sloPassed"] = True
        report["result"].update(generatorDropped=10, dispatched=10, succeeded=10)
        self.assertIn("slo_exceeded", capacity.assess(report, 0)["failures"])

    def test_interrupted_run_or_partial_arrivals_cannot_pass(self):
        for change in ({"complete": False}, {"assetIntegrity": False}):
            report = self.report()
            report.update(change)
            self.assertFalse(capacity.assess(report, 0)["execution_passed"])
        report = self.report()
        report["result"].update(offered=10, dispatched=10, succeeded=10)
        self.assertIn("scheduled_arrivals_incomplete", capacity.assess(report, 0)["failures"])

    def test_short_run_cannot_satisfy_a_soak_profile_even_with_a_true_boolean(self):
        report = self.report()
        report["profile"]["durationSeconds"] = 86400
        report["soak24HoursCompleted"] = True
        self.assertFalse(capacity.assess(report, 0)["execution_passed"])

    def test_missing_slo_does_not_imply_slo_acceptance(self):
        report = self.report()
        report["profile"]["slo"] = None
        self.assertIsNone(capacity.assess(report, 0)["slo_passed"])

    def test_nan_and_missing_counters_are_rejected(self):
        for value in (float("nan"), float("inf"), -1, None):
            report = self.report()
            report["result"]["p99Ms"] = value
            self.assertFalse(capacity.assess(report, 0)["execution_passed"])


class BenchmarkEvidenceTests(unittest.TestCase):
    def report(self):
        scenario = {"name": "actor-hot-lookup", "workerOperations": [10, 10], "concurrency": 2, "operations": 20,
                    "operationsPerSecond": 10, "allocatedBytesPerOperation": 5, "cpuMs": 3, "lockContentions": 0,
                    "latency": {"p95Ms": 2}}
        return {"schemaVersion": 2, "configuration": {"repetitions": 1}, "runtime": "test",
                "operatingSystem": "test", "architecture": "test", "processorCount": 2, "serverGc": False,
                "scenarios": [scenario], "samples": [copy.deepcopy(scenario)]}

    def compare(self, baseline, candidate):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "baseline-0.json").write_text(json.dumps(baseline))
            (root / "candidate-0.json").write_text(json.dumps(candidate))
            return benchmarks.compare(root, 1)

    def test_shared_harness_preserves_paired_samples(self):
        result = self.compare(self.report(), self.report())
        self.assertEqual(0, result["scenarios"][0]["metrics"]["operationsPerSecond"]["change_percent"])

    def test_synchronous_worker_starvation_is_rejected(self):
        report = self.report()
        report["samples"][0]["workerOperations"] = [20, 0]
        with self.assertRaisesRegex(ValueError, "worker"):
            self.compare(self.report(), report)

    def test_previous_biased_harness_and_mismatched_environment_are_rejected(self):
        for field, value in (("schemaVersion", 1), ("processorCount", 4)):
            report = self.report()
            report[field] = value
            with self.assertRaises(ValueError):
                self.compare(self.report(), report)


if __name__ == "__main__":
    unittest.main()
