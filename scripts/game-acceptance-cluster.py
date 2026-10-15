#!/usr/bin/env python3
"""Own one isolated three-node acceptance lifecycle inside the Compose runner.

No Docker socket or repository metadata is needed. Certificates and unfiltered
process logs stay outside the evidence mount. One driver invocation supplies one
continuous workload; restarting a failed workload is deliberately unsupported.
"""
import argparse
from datetime import datetime, timezone
import importlib.util
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
HOST = ROOT / "samples/GameServer/GameServer.Host/bin/Release/net10.0/GameServer.Host.dll"
NODES = ("gateway", "game-a", "game-b")


class ChildExited(RuntimeError):
    pass


class Interrupted(RuntimeError):
    pass


def load_helpers():
    spec = importlib.util.spec_from_file_location("verify_game_cluster", ROOT / "scripts/verify-game-cluster.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def validate_profile(profile, mode, duration):
    rate = profile.get("requestsPerSecond")
    if isinstance(rate, bool) or not isinstance(rate, int) or not 1 <= rate <= 100000:
        raise ValueError("Invalid rate")
    if mode == "smoke":
        if not 600 <= duration <= 1800:
            raise ValueError("Smoke duration must be 600 to 1800 seconds")
        return rate, duration
    seconds = profile.get("durationSeconds")
    if isinstance(seconds, bool) or not isinstance(seconds, int) or seconds != 86400:
        raise ValueError("Soak requires exactly 86400 continuous seconds")
    return rate, seconds


def proxy_config(directory, base, index):
    node = NODES[index]
    common = (f"cert = {directory}/{node}.crt\nkey = {directory}/{node}.key\n"
              f"CAfile = {directory}/ca.crt\nverifyChain = yes\nrequireCert = yes\n")
    config = f"foreground = yes\npid = {directory}/{node}.pid\ndebug = warning\n"
    config += f"[internal]\naccept = 127.0.0.1:{base + 10 + index}\nconnect = 127.0.0.1:{base + index}\n" + common
    config += "".join(f"checkHost = {allowed}\n" for allowed in NODES)
    for target, remote in enumerate(NODES):
        config += (f"[to-{remote}]\nclient = yes\naccept = 127.0.0.1:{base + 30 + index * 10 + target}\n"
                   f"connect = 127.0.0.1:{base + 10 + target}\n" + common + f"checkHost = {remote}\n")
    if node == "gateway":
        config += (f"[players]\naccept = 127.0.0.1:{base + 61}\nconnect = 127.0.0.1:{base}\n"
                   f"cert = {directory}/{node}.crt\nkey = {directory}/{node}.key\n")
    return config


def player_config(directory, base):
    return (f"foreground = yes\npid = {directory}/player.pid\ndebug = warning\n[server]\nclient = yes\n"
            f"accept = 127.0.0.1:{base + 60}\nconnect = 127.0.0.1:{base + 61}\n"
            f"CAfile = {directory}/ca.crt\nverifyChain = yes\ncheckHost = gateway\n")


class Children:
    """Each top-level child owns a session; never signal unrelated processes."""
    def __init__(self, directory, env):
        self.directory, self.env = directory, env
        self.processes = []
        self.logs = []

    def start(self, command, name):
        log = (self.directory / (name + ".log")).open("w")
        self.logs.append(log)
        process = subprocess.Popen(command, cwd=ROOT, env=self.env, stdout=log,
                                   stderr=subprocess.STDOUT, start_new_session=True)
        self.processes.append(process)
        return process

    @staticmethod
    def check(processes):
        if any(process.poll() is not None for process in processes):
            raise ChildExited()

    def wait(self, process, timeout, watched=()):
        deadline = time.monotonic() + timeout
        while True:
            self.check(watched)
            code = process.poll()
            if code is not None:
                if code != 0:
                    raise ChildExited()
                return
            if time.monotonic() >= deadline:
                raise TimeoutError()
            time.sleep(0.2)

    def close(self):
        # Signal even exited leaders: a driver can leave a workload descendant.
        for sig in (signal.SIGTERM, signal.SIGKILL):
            for process in reversed(self.processes):
                try:
                    os.killpg(process.pid, sig)
                except ProcessLookupError:
                    pass
            deadline = time.monotonic() + (15 if sig == signal.SIGTERM else 5)
            for process in reversed(self.processes):
                try:
                    process.wait(timeout=max(0.01, deadline - time.monotonic()))
                except subprocess.TimeoutExpired:
                    pass
        for log in self.logs:
            log.close()


def get_admin(base, index, route, timeout=3):
    try:
        with urllib.request.urlopen(f"http://127.0.0.1:{base + 70 + index}{route}", timeout=timeout) as response:
            return {"status": response.status, "body": response.read(1_000_000).decode()}
    except urllib.error.HTTPError as error:
        return {"status": error.code}  # dependency exception bodies may contain credentials
    except Exception as error:
        return {"error_type": type(error).__name__}


def snapshot(base):
    return {node: {route[1:]: get_admin(base, index, route)
                   for route in ("/live", "/ready", "/metrics")}
            for index, node in enumerate(NODES)}


def healthy(snapshot_value):
    return all(all(value.get(route, {}).get("status") == 200 for route in ("live", "ready", "metrics"))
               and "game_queue_capacity" in value["metrics"].get("body", "")
               and "game_outbox_pending" in value["metrics"].get("body", "")
               for value in snapshot_value.values()) and len(snapshot_value) == 3


def wait_ready(base, processes, timeout=90):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        Children.check(processes)
        current = snapshot(base)
        Children.check(processes)
        if healthy(current) and time.monotonic() < deadline:
            return current
        time.sleep(0.25)
    raise TimeoutError()


def redact(text, env):
    # Raw connection strings, their embedded passwords, tokens and PEM blocks
    # are never published. Logs are diagnostic only; no unfiltered stdout.
    secrets = [env.get(name, "") for name in ("GAME_REDIS", "GAME_POSTGRES", "GAME_JWT_KEY")]
    for connection in secrets[:2]:
        secrets.extend(re.findall(r"(?i)(?:password|pwd)\s*=\s*([^;,]+)", connection))
    for secret in sorted(set(secrets), key=len, reverse=True):
        if secret:
            text = text.replace(secret, "[REDACTED]")
    text = re.sub(r"-----BEGIN [^-]+-----.*?-----END [^-]+-----", "[REDACTED PEM]", text, flags=re.S)
    text = re.sub(r"(?i)(password|pwd|authorization|token|secret)\s*[:=]\s*[^\s;,]+", r"\1=[REDACTED]", text)
    text = re.sub(r"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", "[REDACTED JWT]", text)
    return text


def publish_logs(source, destination, env):
    for path in source.glob("*.log"):
        (destination / path.name).write_text(redact(path.read_text(errors="replace"), env))
    # Driver writes its structured reports directly so interrupted runs retain
    # evidence; scrub diagnostic logs and JSON before the artifacts are uploaded.
    for path in destination.rglob("*"):
        if path.is_file() and path.suffix in (".log", ".json", ".jsonl", ".txt"):
            path.write_text(redact(path.read_text(errors="replace"), env))


def driver_command(options, base, rate):
    command = [sys.executable, str(ROOT / "scripts/run-game-capacity.py"), "--profile", str(options.profile.resolve()),
               "--host", "127.0.0.1", "--port", str(base + 60), "--rates", str(rate),
               "--duration", str(options.duration), "--require-evidence", "--expected-node-count", "3",
               "--output", str(options.output / "capacity")]
    if options.mode == "soak":
        command.append("--soak")
    for index in range(3):
        command.extend(("--admin", f"http://127.0.0.1:{base + 70 + index}"))
    return command


def execute(options):
    options.output = options.output.resolve()
    options.output.mkdir(parents=True, exist_ok=True)
    status = {"schema_version": 1, "mode": options.mode, "passed": False,
              "capacity_certified": False, "started_at": datetime.now(timezone.utc).isoformat()}
    env = dict(os.environ)
    children = None
    temporary = tempfile.TemporaryDirectory(prefix="game-acceptance-")
    private = Path(temporary.name)
    base = None
    watched = []
    final_saved = False
    previous_handlers = {}

    def interrupted(signum, frame):
        status["signal"] = signum
        raise Interrupted()

    try:
        for sig in (signal.SIGINT, signal.SIGTERM):
            previous_handlers[sig] = signal.signal(sig, interrupted)
        for name in ("GAME_REDIS", "GAME_POSTGRES", "GAME_JWT_KEY", "GAME_CANDIDATE_SHA"):
            if not env.get(name):
                raise ValueError("Missing required environment")
        if not HOST.is_file():
            raise FileNotFoundError()
        profile = json.loads(options.profile.read_text())
        rate, duration = validate_profile(profile, options.mode, options.duration)
        if not re.fullmatch(r"[0-9a-fA-F]{40}", env["GAME_CANDIDATE_SHA"]):
            raise ValueError("Invalid candidate SHA")
        status.update(candidate_sha=env["GAME_CANDIDATE_SHA"], requests_per_second=rate, duration_seconds=duration)
        helpers = load_helpers()
        base = helpers.available_base()
        env["GAME_ADMIN_BASE_PORT"] = str(base + 70)
        env["GAME_PURCHASE_STREAM"] = "game-acceptance:purchase:" + uuid.uuid4().hex
        env["GAME_SESSION_MODE"] = "required"
        children = Children(private, env)
        # Reuse certificate issuance while keeping OpenSSL output private and
        # applying the same process ownership/timeouts as all other children.
        counter = 0
        def private_run(command, timeout=40):
            nonlocal counter
            counter += 1
            children.wait(children.start(command, "certificate-" + str(counter)), timeout)
            return ""
        helpers.run = private_run
        helpers.certificates(private)
        children.wait(children.start(["dotnet", str(HOST), "init"], "schema-init"), 90)
        for index, node in enumerate(NODES):
            config = private / (node + ".conf")
            config.write_text(proxy_config(private, base, index))
            watched.append(children.start(["stunnel4", str(config)], "tls-" + node))
            watched.append(children.start(["dotnet", str(HOST), "node", node, str(base + index),
                                           str(private), str(base + 30 + index * 10)], node))
        public = private / "player.conf"
        public.write_text(player_config(private, base))
        watched.append(children.start(["stunnel4", str(public)], "tls-player"))
        initial = wait_ready(base, watched)
        (options.output / "initial-readiness-metrics.json").write_text(json.dumps(initial, indent=2) + "\n")
        command = driver_command(options, base, rate)
        command.extend(("--private-log-directory", str(private)))
        driver = children.start(command, "capacity-driver")
        children.wait(driver, duration + 1200, watched)
        Children.check(watched)
        final = snapshot(base)
        (options.output / "final-readiness-metrics.json").write_text(json.dumps(final, indent=2) + "\n")
        final_saved = True
        if not healthy(final):
            raise RuntimeError("Final health failed")
        status["passed"] = True
    except BaseException as error:
        status["error_type"] = type(error).__name__
    finally:
        # A second interrupt must not skip cleanup or the final status artifact.
        for sig in previous_handlers:
            signal.signal(sig, signal.SIG_IGN)
        try:
            if base is not None and watched and not final_saved:
                (options.output / "final-readiness-metrics.json").write_text(json.dumps(snapshot(base), indent=2) + "\n")
        except BaseException as error:
            status.update(passed=False, snapshot_error_type=type(error).__name__)
        try:
            if children:
                children.close()
        except BaseException as error:
            status.update(passed=False, cleanup_error_type=type(error).__name__)
        try:
            publish_logs(private, options.output, env)
        except BaseException as error:
            status.update(passed=False, export_error_type=type(error).__name__)
        try:
            temporary.cleanup()
        except BaseException as error:
            status.update(passed=False, cleanup_error_type=type(error).__name__)
        finally:
            status["finished_at"] = datetime.now(timezone.utc).isoformat()
            (options.output / "cluster-status.json").write_text(json.dumps(status, indent=2) + "\n")
            for sig, handler in previous_handlers.items():
                signal.signal(sig, handler)
    print(json.dumps({"passed": status["passed"], "error_type": status.get("error_type")}))
    return 0 if status["passed"] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", required=True, type=Path)
    parser.add_argument("--mode", choices=("smoke", "soak"), required=True)
    parser.add_argument("--duration", default=600, type=int)
    parser.add_argument("--output", default=Path("/evidence"), type=Path)
    return execute(parser.parse_args())


if __name__ == "__main__":
    sys.exit(main())
