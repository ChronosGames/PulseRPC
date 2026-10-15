#!/usr/bin/env python3
"""创建本次运行独占的 Docker 集群，验收后删除本次资源并保留证据。"""
import argparse
import base64
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import secrets
import signal
import subprocess
import uuid

ROOT = Path(__file__).resolve().parents[1]
COMPOSE = ROOT / 'scripts/game-acceptance.compose.yml'


def profile_path(value):
    path = (ROOT / value).resolve()
    if not path.is_relative_to(ROOT / 'perf/game-server') or path.suffix != '.json' or not path.is_file():
        raise ValueError('Profile must be a tracked JSON file under perf/game-server')
    return path.relative_to(ROOT).as_posix()


def command(project, *args):
    if not re.fullmatch(r'pulserpc-acceptance-[a-f0-9]{20}', project):
        raise ValueError('Invalid owned project name')
    return ['docker', 'compose', '--project-name', project, '--file', str(COMPOSE), *args]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--profile', default='perf/game-server/acceptance-smoke.json')
    parser.add_argument('--mode', choices=['smoke', 'soak'], default='smoke')
    parser.add_argument('--duration', type=int, default=600)
    parser.add_argument('--output-root', type=Path, default=ROOT / 'artifacts/game-server/acceptance')
    options = parser.parse_args()
    if not 600 <= options.duration <= 1800:
        parser.error('Smoke duration must be 600–1800 seconds')
    profile = profile_path(options.profile)
    project = 'pulserpc-acceptance-' + uuid.uuid4().hex[:20]
    output = options.output_root.resolve() / project
    output.mkdir(parents=True, mode=0o755)
    # Written before any Docker operation so the always() upload can retain failures.
    if os.environ.get('GITHUB_OUTPUT'):
        with open(os.environ['GITHUB_OUTPUT'], 'a') as stream:
            stream.write(f'artifact_path={output}\n')
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    if not re.fullmatch(r'[a-f0-9]{40}', commit):
        raise ValueError('Invalid candidate commit')
    (output / 'ownership.json').write_text(json.dumps({'project': project, 'commit': commit}) + '\n')
    env = dict(os.environ, ACCEPTANCE_DB_PASSWORD=secrets.token_hex(24),
               ACCEPTANCE_JWT_KEY=base64.b64encode(secrets.token_bytes(48)).decode('ascii'), GAME_CANDIDATE_SHA=commit,
               ACCEPTANCE_OUTPUT=str(output), ACCEPTANCE_PROFILE=profile,
               ACCEPTANCE_MODE=options.mode, ACCEPTANCE_DURATION=str(options.duration))
    summary = {'schema_version': 1, 'commit': commit, 'project': project,
               'mode': options.mode, 'started_utc': datetime.now(timezone.utc).isoformat(),
               'passed': False, 'cleanup_completed': False, 'capacity_certified': False}
    process = None

    def interrupted(signum, frame):
        raise InterruptedError('Acceptance interrupted')

    for sig in (signal.SIGTERM, signal.SIGINT):
        signal.signal(sig, interrupted)
    try:
        subprocess.run(['docker', 'compose', 'version'], check=True, timeout=30)
        process = subprocess.Popen(command(project, 'up', '--build', '--abort-on-container-exit',
                                            '--exit-code-from', 'acceptance'), cwd=ROOT, env=env)
        # Build, bootstrap and result reconciliation allowance, separate from measured load duration.
        timeout = (86400 if options.mode == 'soak' else options.duration) + 3600
        summary['exit_code'] = process.wait(timeout=timeout)
        status_file = output / 'cluster-status.json'
        if status_file.is_file():
            summary['cluster'] = json.loads(status_file.read_text())
        summary['passed'] = summary['exit_code'] == 0 and summary.get('cluster', {}).get('passed') is True
    except BaseException as error:
        summary['error_type'] = type(error).__name__
    finally:
        # A second cancellation must not interrupt the bounded cleanup attempt.
        signal.signal(signal.SIGTERM, signal.SIG_IGN)
        signal.signal(signal.SIGINT, signal.SIG_IGN)
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
        try:
            with (output / 'compose.log').open('w') as log:
                subprocess.run(command(project, 'logs', '--no-color', '--tail', '2000'), cwd=ROOT,
                               env=env, stdout=log, stderr=subprocess.STDOUT, timeout=30, check=False)
        except (OSError, subprocess.TimeoutExpired) as error:
            summary['logs_error_type'] = type(error).__name__
        try:
            result = subprocess.run(command(project, 'down', '--volumes', '--remove-orphans', '--rmi', 'local',
                                             '--timeout', '90'), cwd=ROOT, env=env, timeout=150, check=False)
            summary['cleanup_completed'] = result.returncode == 0
        except (OSError, subprocess.TimeoutExpired) as error:
            summary['cleanup_error_type'] = type(error).__name__
        summary['passed'] = summary['passed'] and summary['cleanup_completed']
        summary['finished_utc'] = datetime.now(timezone.utc).isoformat()
        (output / 'orchestration.json').write_text(json.dumps(summary, indent=2) + '\n')
        print(json.dumps(summary), flush=True)
        print('Evidence: ' + str(output), flush=True)
    return 0 if summary['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
