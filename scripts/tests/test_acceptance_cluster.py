import argparse
import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

PATH = Path(__file__).resolve().parents[1] / 'game-acceptance-cluster.py'
spec = importlib.util.spec_from_file_location('acceptance_cluster', PATH)
cluster = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cluster)


class LifecycleTests(unittest.TestCase):
    def test_duration_and_rate_bounds(self):
        for seconds in (599, 1801):
            with self.assertRaises(ValueError):
                cluster.validate_profile({'requestsPerSecond': 50}, 'smoke', seconds)
        self.assertEqual((50, 600), cluster.validate_profile({'requestsPerSecond': 50}, 'smoke', 600))
        for seconds in (86399, 86401, True):
            with self.assertRaises(ValueError):
                cluster.validate_profile({'requestsPerSecond': 50, 'durationSeconds': seconds}, 'soak', 600)
        self.assertEqual((50, 86400), cluster.validate_profile({'requestsPerSecond': 50, 'durationSeconds': 86400}, 'soak', 600))
        for rate in (True, 0, 1.5, 100001):
            with self.assertRaises(ValueError):
                cluster.validate_profile({'requestsPerSecond': rate}, 'smoke', 600)

    def test_single_driver_and_all_admin_endpoints(self):
        options = argparse.Namespace(profile=Path('/tmp/profile.json'), mode='soak', duration=600, output=Path('/tmp/evidence'))
        command = cluster.driver_command(options, 20000, 50)
        self.assertIn('--soak', command)
        self.assertIn('--require-evidence', command)
        self.assertEqual(3, command.count('--admin'))
        self.assertEqual('50', command[command.index('--rates') + 1])
        self.assertEqual('3', command[command.index('--expected-node-count') + 1])
        self.assertIn('http://127.0.0.1:20072', command)

    def test_stunnel_topology(self):
        gateway = cluster.proxy_config(Path('/private'), 20000, 0)
        self.assertIn('accept = 127.0.0.1:20061\nconnect = 127.0.0.1:20000', gateway)
        for index in range(3):
            config = cluster.proxy_config(Path('/private'), 20000, index)
            self.assertEqual(4, config.count('verifyChain = yes'))
            self.assertEqual(4, config.count('requireCert = yes'))
            for remote in range(3):
                self.assertIn(f'accept = 127.0.0.1:{20030 + index * 10 + remote}', config)
                self.assertIn(f'connect = 127.0.0.1:{20010 + remote}', config)

    def test_redaction(self):
        env = {'GAME_POSTGRES': 'Host=db;Password=private-password', 'GAME_REDIS': 'redis,password=redis-secret', 'GAME_JWT_KEY': 'jwt-secret'}
        raw = '\n'.join(env.values()) + '\nprivate-password jwt-secret redis-secret\n-----BEGIN PRIVATE KEY-----\nmaterial\n-----END PRIVATE KEY-----'
        cleaned = cluster.redact(raw, env)
        for value in ('private-password', 'jwt-secret', 'redis-secret', 'material'):
            self.assertNotIn(value, cleaned)

    def test_health_requires_all_nodes_and_metrics(self):
        node = {route: {'status': 200, 'body': 'game_queue_capacity 1\ngame_outbox_pending 0'} for route in ('live', 'ready', 'metrics')}
        values = {name: node for name in cluster.NODES}
        self.assertTrue(cluster.healthy(values))
        del values['game-b']
        self.assertFalse(cluster.healthy(values))

    def test_child_exit_fails_fast_and_close_owns_only_children(self):
        with tempfile.TemporaryDirectory() as temp:
            children = cluster.Children(Path(temp), dict(os.environ))
            external = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'])
            try:
                watched = children.start([sys.executable, '-c', 'raise SystemExit(1)'], 'failed')
                watched.wait(timeout=5)
                driver = children.start([sys.executable, '-c', 'import time; time.sleep(60)'], 'driver')
                with self.assertRaises(cluster.ChildExited):
                    children.wait(driver, 60, [watched])
                children.close()
                self.assertIsNotNone(driver.poll())
                self.assertIsNone(external.poll())
            finally:
                external.terminate()
                external.wait(timeout=5)
                children.close()

    def test_failure_always_writes_status_without_secrets(self):
        with tempfile.TemporaryDirectory() as temp, patch.dict(os.environ, {}, clear=True):
            options = argparse.Namespace(output=Path(temp), mode='smoke', duration=600, profile=Path('/missing'))
            self.assertEqual(1, cluster.execute(options))
            status = json.loads((Path(temp) / 'cluster-status.json').read_text())
            self.assertFalse(status['passed'])
            self.assertEqual('ValueError', status['error_type'])
            self.assertIn('finished_at', status)


if __name__ == '__main__':
    unittest.main()
