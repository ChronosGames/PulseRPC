#!/usr/bin/env python3
"""取消后的独立清理入口，只接受本次输出目录的所有权标记。"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('acceptance_owner', ROOT / 'scripts/run-game-acceptance.py')
owner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(owner)


def cleanup(output):
    output = output.resolve()
    marker = json.loads((output / 'ownership.json').read_text())
    project = marker['project']
    if output.name != project:
        raise ValueError('Ownership marker does not match output directory')
    command = owner.command(project, 'down', '--volumes', '--remove-orphans', '--rmi', 'local', '--timeout', '90')
    # Compose parses all interpolations even for down. These are placeholders,
    # never used to start services, and no persisted credential is needed.
    env = dict(os.environ, ACCEPTANCE_DB_PASSWORD='cleanup-unused', ACCEPTANCE_JWT_KEY='cleanup-unused',
               GAME_CANDIDATE_SHA=marker['commit'], ACCEPTANCE_OUTPUT=str(output),
               ACCEPTANCE_PROFILE='perf/game-server/acceptance-smoke.json',
               ACCEPTANCE_MODE='smoke', ACCEPTANCE_DURATION='600')
    passed = False
    try:
        passed = subprocess.run(command, cwd=ROOT, env=env, timeout=150, check=False).returncode == 0
    finally:
        (output / 'cleanup-retry.json').write_text(json.dumps({'project': project, 'passed': passed}) + '\n')
    return 0 if passed else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    raise SystemExit(cleanup(parser.parse_args().output))
