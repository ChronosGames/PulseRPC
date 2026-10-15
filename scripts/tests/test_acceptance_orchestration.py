import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('orchestration', ROOT / 'scripts/run-game-acceptance.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class OrchestrationTests(unittest.TestCase):
    def test_only_owned_project_names_are_accepted(self):
        for project in ('default', 'production', 'pulserpc-acceptance-', 'a;docker system prune'):
            with self.assertRaises(ValueError):
                module.command(project, 'down')
        cmd = module.command('pulserpc-acceptance-' + 'a' * 20, 'down')
        self.assertIn('--project-name', cmd)
        self.assertNotIn('--all', cmd)

    def test_profile_cannot_escape_committed_profile_directory(self):
        for profile in ('/etc/passwd', '../outside.json', 'scripts/run-game-capacity.py'):
            with self.assertRaises(ValueError):
                module.profile_path(profile)
        self.assertEqual(module.profile_path('perf/game-server/acceptance-smoke.json'),
                         'perf/game-server/acceptance-smoke.json')

    def test_compose_does_not_publish_ports_or_mount_docker_socket(self):
        compose = (ROOT / 'scripts/game-acceptance.compose.yml').read_text()
        self.assertNotIn('ports:', compose)
        self.assertNotIn('docker.sock', compose)
        self.assertIn('internal: true', compose)
        self.assertNotIn('container_name:', compose)

    def test_self_hosted_is_manual_main_only(self):
        workflow = (ROOT / '.github/workflows/game-server-capacity.yml').read_text()
        self.assertIn("if: github.ref == 'refs/heads/main'", workflow)
        self.assertIn('workflow_dispatch:', workflow)
        self.assertNotIn('pull_request:', workflow)
        self.assertNotIn('secrets.', workflow)
        self.assertIn('if-no-files-found: error', workflow)

    def test_log_collection_failure_still_attempts_cleanup(self):
        import json
        import subprocess
        import tempfile
        from unittest import mock
        calls = []
        def run(command, **kwargs):
            calls.append(command)
            if 'logs' in command:
                raise subprocess.TimeoutExpired(command, 30)
            return mock.Mock(returncode=0)
        process = mock.Mock()
        process.wait.return_value = 1
        process.poll.return_value = 1
        with tempfile.TemporaryDirectory() as root:
            with mock.patch('sys.argv', ['run', '--output-root', root]), \
                 mock.patch.object(module.subprocess, 'check_output', return_value='a' * 40), \
                 mock.patch.object(module.subprocess, 'Popen', return_value=process), \
                 mock.patch.object(module.subprocess, 'run', side_effect=run), \
                 mock.patch.dict(module.os.environ, {}, clear=True):
                self.assertEqual(module.main(), 1)
            result = json.loads(next(Path(root).glob('*/orchestration.json')).read_text())
            self.assertTrue(result['cleanup_completed'])
            self.assertFalse(result['passed'])
            self.assertEqual(result['logs_error_type'], 'TimeoutExpired')
        self.assertTrue(any('down' in command for command in calls))

    def test_independent_cleanup_rejects_wrong_ownership_and_uses_no_secret(self):
        import json
        import tempfile
        from unittest import mock
        spec = importlib.util.spec_from_file_location('cleanup', ROOT / 'scripts/cleanup-game-acceptance.py')
        cleanup = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cleanup)
        with tempfile.TemporaryDirectory() as parent:
            directory = Path(parent) / ('pulserpc-acceptance-' + 'a' * 20)
            directory.mkdir()
            marker = directory / 'ownership.json'
            marker.write_text(json.dumps(dict(project='production', commit='b' * 40)))
            with self.assertRaises(ValueError):
                cleanup.cleanup(directory)
            marker.write_text(json.dumps(dict(project=directory.name, commit='b' * 40)))
            with mock.patch.object(cleanup.subprocess, 'run', return_value=mock.Mock(returncode=0)) as run:
                self.assertEqual(cleanup.cleanup(directory), 0)
                args, kwargs = run.call_args
                self.assertIn('down', args[0])
                self.assertNotIn('up', args[0])
                self.assertEqual(kwargs['env']['ACCEPTANCE_DB_PASSWORD'], 'cleanup-unused')
            self.assertTrue(json.loads((directory / 'cleanup-retry.json').read_text())['passed'])
