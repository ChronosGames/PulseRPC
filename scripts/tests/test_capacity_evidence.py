"""Fail-closed regression tests; no deployment, secrets or third-party dependencies."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

SPEC = importlib.util.spec_from_file_location("capacity", Path(__file__).resolve().parents[1] / "run-game-capacity.py")
capacity = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(capacity)


def report():
    profile = dict(capacity.DEFAULTS, durationSeconds=60, requestsPerSecond=10,
                   environmentId="test", slo=dict(p99Ms=100, maxFailureRatio=.01))
    return dict(schemaVersion=1, profile=profile, complete=True, assetIntegrity=True,
                continuousRequirementMet=True, profileSha256="hash", environment=dict(commit="a"*40),
                result=dict(offered=600, dispatched=600, succeeded=600, errors=0,
                            generatorDropped=0, inFlight=0, seconds=60, p99Ms=10, corruption=0))


def metrics(node="node-a"):
    return "\n".join(['game_%s{node="%s"} 1' % (name, node) for name in capacity.METRICS]
                     + ['game_gc_collections_total{node="%s",generation="%d"} 1' % (node, generation)
                        for generation in range(3)]
                     + ['game_%s{node="%s",queue="test-queue"} 1' % (name, node)
                        for name in ("queue_depth", "queue_capacity", "queue_rejected_total")])


def samples():
    return [dict(node_index=node, monotonic=t, **capacity.parse_metrics(metrics("node-"+str(node))))
            for node in range(3) for t in (0, 10, 20)]


class AssessmentTests(unittest.TestCase):
    def strict(self, value=Ellipsis, **kwargs):
        value = report() if value is Ellipsis else value
        return capacity.assess(value, 0, require_evidence=True, **kwargs)

    def test_valid_matching_report(self):
        value = report()
        result = self.strict(value, expected_profile=value["profile"], profile_sha256="hash", commit="a"*40)
        self.assertTrue(result["execution_passed"])
        self.assertTrue(result["slo_passed"])

    def test_smoke_without_slo_still_supported(self):
        value = report()
        value["profile"]["slo"] = None
        self.assertTrue(capacity.assess(value, 0)["execution_passed"])
        self.assertIsNone(capacity.assess(value, 0)["slo_passed"])
        self.assertIn("slo_required", self.strict(value)["failures"])

    def test_malformed_reports_fail_without_exception(self):
        for value in (None, [], 1, "bad", {}, {"profile": []}, {"profile": report()["profile"], "result": []}):
            with self.subTest(value=value):
                self.assertFalse(self.strict(value)["execution_passed"])

    def test_malformed_slo_fails(self):
        for slo in ({}, [], True, "bad", {"p99Ms": 1}, {"p99Ms": 1, "maxFailureRatio": 0, "other": 1}):
            value = report()
            value["profile"]["slo"] = slo
            self.assertFalse(self.strict(value)["execution_passed"])
        for key in ("p99Ms", "maxFailureRatio"):
            for invalid in (None, True, "1", float("inf"), float("nan"), -1, 10**1000):
                value = report()
                value["profile"]["slo"][key] = invalid
                self.assertFalse(self.strict(value)["execution_passed"])

    def test_invalid_profile_bounds(self):
        for key in capacity.BOUNDS:
            for invalid in (None, True, "1", [], float("nan"), -1, 10**1000):
                value = report()
                value["profile"][key] = invalid
                self.assertFalse(self.strict(value)["execution_passed"])

    def test_profile_hash_and_commit_mismatch(self):
        value = report()
        expected = dict(value["profile"], connections=9)
        for kwargs, failure in ((dict(expected_profile=expected), "profile_mismatch"),
                                (dict(profile_sha256="wrong"), "profile_hash_mismatch"),
                                (dict(commit="b"*40), "commit_mismatch")):
            self.assertIn(failure, self.strict(value, **kwargs)["failures"])

    def test_counters_reject_missing_nonfinite_boolean_and_fraction(self):
        for key in report()["result"]:
            for invalid in (None, True, "0", float("inf"), float("nan"), -1, 10**1000):
                value = report()
                value["result"][key] = invalid
                self.assertFalse(self.strict(value)["execution_passed"])
        value = report()
        value["result"]["offered"] = 600.5
        self.assertFalse(self.strict(value)["execution_passed"])

    def test_accounting_and_slo_cannot_trust_reported_pass(self):
        for key, invalid in (("offered", 599), ("dispatched", 599), ("succeeded", 599),
                             ("inFlight", 1), ("corruption", 1), ("seconds", 59), ("p99Ms", 101)):
            value = report()
            value["sloPassed"] = True
            value["result"][key] = invalid
            self.assertFalse(self.strict(value)["execution_passed"])

    def test_complete_flags_must_be_boolean_true(self):
        for key in ("complete", "assetIntegrity", "continuousRequirementMet"):
            for invalid in (False, 1, "true", None):
                value = report()
                value[key] = invalid
                self.assertFalse(self.strict(value)["execution_passed"])

    def test_soak_must_be_one_complete_24_hour_run(self):
        value = report()
        value["profile"].update(durationSeconds=86400, minimumContinuousSeconds=86400)
        value["result"].update(seconds=86400, offered=864000, dispatched=864000, succeeded=864000)
        value["soak24HoursCompleted"] = True
        self.assertTrue(self.strict(value, soak=True)["execution_passed"])
        for key, invalid in (("seconds", 86399), ("offered", 863999)):
            broken = copy.deepcopy(value)
            broken["result"][key] = invalid
            self.assertFalse(self.strict(broken, soak=True)["execution_passed"])
        value["soak24HoursCompleted"] = False
        self.assertFalse(self.strict(value, soak=True)["execution_passed"])
        self.assertFalse(self.strict(report(), soak=True)["execution_passed"])


class MetricsTests(unittest.TestCase):
    def test_complete_three_node_evidence(self):
        summary = capacity.summarize_metrics(samples(), 3, 1, 19)
        self.assertTrue(summary["passed"])
        self.assertEqual(3, len(summary["nodes"]))

    def test_missing_failed_duplicate_or_interrupted_samples(self):
        values = samples()
        broken = copy.deepcopy(values)
        broken[1] = dict(node_index=0, monotonic=10, error_type="TimeoutError")
        duplicate = copy.deepcopy(values)
        for sample in duplicate:
            sample["node"] = "same-node"
        gap = copy.deepcopy(values)
        for sample in gap:
            if sample["monotonic"] == 20:
                sample["monotonic"] = 100
        for value in ([], values[:6], broken, duplicate, gap, values[1:]):
            self.assertFalse(capacity.summarize_metrics(value, 3, 1, 19)["passed"])

    def test_empty_malformed_missing_and_nonfinite_metrics(self):
        for value in ("", "<html>error</html>", metrics().replace("game_storage_up", "game_unknown"),
                      metrics().replace("} 1", "} NaN"), metrics().replace("} 1", "} Inf"),
                      metrics()+"\n"+metrics(), metrics().replace('node="node-a"', 'node=""')):
            with self.assertRaises(ValueError):
                capacity.parse_metrics(value)

    def test_queue_series_must_be_nonempty_complete_and_unique(self):
        full = metrics()
        no_queues = "\n".join(line for line in full.splitlines() if "queue=" not in line)
        queue_line = 'game_queue_depth{node="node-a",queue="test-queue"} 1'
        invalid = [no_queues, full.replace('queue="test-queue"', 'queue=""'),
                   full.replace('queue="test-queue"', 'queue="   "'), full + "\n" + queue_line]
        for name in ("queue_depth", "queue_capacity", "queue_rejected_total"):
            invalid.append("\n".join(line for line in full.splitlines() if not line.startswith("game_" + name)))
            invalid.append(full + '\ngame_%s{node="node-a",queue="partial-second-queue"} 1' % name)
        for body in invalid:
            with self.subTest(body=body), self.assertRaises(ValueError):
                capacity.parse_metrics(body)
        second = "\n".join(line.replace('queue="test-queue"', 'queue="second-queue"')
                           for line in full.splitlines() if "queue=" in line)
        parsed = capacity.parse_metrics(full + "\n" + second)
        queue_series = [series for series in parsed["series"] if "queue" in series]
        self.assertEqual(6, len(queue_series))

    def test_failed_samples_do_not_log_endpoint_or_error_content(self):
        with tempfile.TemporaryDirectory() as directory:
            step = Path(directory) / "step"
            step.mkdir()
            collector = capacity.MetricCollector(["http://private.example"], step)
            with mock.patch.object(capacity.urllib.request, "urlopen", side_effect=RuntimeError("SECRET")):
                collector.capture()
            output = (step / "node-metrics.jsonl").read_text()
            self.assertNotIn("SECRET", output)
            self.assertNotIn("private.example", output)
            self.assertEqual("RuntimeError", json.loads(output)["error_type"])


class DriverTests(unittest.TestCase):
    def test_malformed_json_report_is_failed_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "results.json"
            path.write_text("not JSON")
            self.assertEqual({}, capacity.read_report(path))

    def test_strict_driver_requires_all_nodes_even_when_workload_passes(self):
        for telemetry_present in (True, False):
            with self.subTest(telemetry_present=telemetry_present), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                profile = root / "source.json"
                profile.write_text(json.dumps(report()["profile"]))
                host = root / "host.dll"
                host.touch()
                process = mock.Mock()
                process.wait.return_value = 0
                def run(args, **kwargs):
                    output = Path(args[-1])
                    value = report()
                    value["profile"] = json.loads(Path(args[3]).read_text())
                    value["profileSha256"] = capacity.hashlib.sha256(Path(args[3]).read_bytes()).hexdigest()
                    value["result"].update(seconds=1, offered=10, dispatched=10, succeeded=10)
                    (output / "results.json").write_text(json.dumps(value))
                    return process
                def finish(collector):
                    collector.samples = samples() if telemetry_present else []
                args = ["driver", "--profile", str(profile), "--port", "9000", "--rates", "10",
                        "--duration", "1", "--require-evidence", "--output", str(root / "out")]
                for node in range(3):
                    args.extend(["--admin", "http://node-" + str(node) + ":9001"])
                environment = dict(GAME_CANDIDATE_SHA="a"*40, GAME_REDIS="redacted", GAME_POSTGRES="redacted", GAME_JWT_KEY="redacted")
                with mock.patch("sys.argv", args), mock.patch.object(capacity, "HOST", host), \
                     mock.patch.dict(capacity.os.environ, environment), \
                     mock.patch.object(capacity.subprocess, "check_output", return_value="10.0\n"), \
                     mock.patch.object(capacity.subprocess, "Popen", side_effect=run), \
                     mock.patch.object(capacity.MetricCollector, "start"), \
                     mock.patch.object(capacity.MetricCollector, "finish", finish), \
                     mock.patch.object(capacity.time, "monotonic", side_effect=[1, 3]), \
                     mock.patch("builtins.print"):
                    if telemetry_present:
                        capacity.main()
                    else:
                        with self.assertRaises(SystemExit):
                            capacity.main()
                summary = json.loads(next((root / "out").glob("*/summary.json")).read_text())
                self.assertEqual(telemetry_present, summary["passed"])
                self.assertFalse(summary["capacity_certified"])

    def test_timeout_writes_failing_summary(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            profile = root / "source.json"
            profile.write_text(json.dumps(report()["profile"]))
            host = root / "host.dll"
            host.touch()
            process = mock.Mock()
            process.wait.side_effect = [capacity.subprocess.TimeoutExpired("dotnet", 960), 0]
            process.poll.return_value = None
            args = ["driver", "--profile", str(profile), "--port", "9000", "--rates", "10",
                    "--output", str(root / "out")]
            environment = dict(GAME_CANDIDATE_SHA="a"*40, GAME_REDIS="redacted", GAME_POSTGRES="redacted", GAME_JWT_KEY="redacted")
            with mock.patch("sys.argv", args), mock.patch.object(capacity, "HOST", host), \
                 mock.patch.dict(capacity.os.environ, environment), \
                 mock.patch.object(capacity.subprocess, "check_output", return_value="10.0\n"), \
                 mock.patch.object(capacity.subprocess, "Popen", return_value=process):
                with self.assertRaises(SystemExit):
                    capacity.main()
            summary = json.loads(next((root / "out").glob("*/summary.json")).read_text())
            self.assertFalse(summary["passed"])
            self.assertIn("TimeoutExpired", summary["failures"])
            process.terminate.assert_called_once()


if __name__ == "__main__":
    unittest.main()
