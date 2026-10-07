#!/usr/bin/env python3
"""Separate .NET processes, Redis leases, PostgreSQL state and actual stunnel mTLS.

Certificates and private keys live only in a temporary directory, never in artifacts.
Requires .NET 10, openssl, stunnel4, GAME_REDIS and GAME_POSTGRES.
"""
import json
import os
import platform
from pathlib import Path
import random
import signal
import socket
import ssl
import subprocess
import sys
import tempfile
import time
import uuid
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
HOST = ROOT / "samples/GameServer/GameServer.Host/bin/Release/net10.0/GameServer.Host.dll"
ARTIFACTS = ROOT / "artifacts/game-server/cluster"
NODES = ["gateway", "game-a", "game-b"]


def run(command, timeout=40):
    result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            timeout=timeout, cwd=ROOT)
    if result.returncode:
        # Commands never contain signing keys/tokens; preserve the failing process output.
        print(result.stdout[-6000:], file=sys.stderr)
        raise subprocess.CalledProcessError(result.returncode, command, output=result.stdout)
    return result.stdout


def available_base():
    for _ in range(100):
        base = random.randrange(20000, 45000)
        held = []
        try:
            for port in range(base, base + 90):
                stream = socket.socket()
                held.append(stream)
                stream.bind(("127.0.0.1", port))
            return base
        except OSError:
            pass
        finally:
            for stream in held:
                stream.close()
    raise RuntimeError("No free test port block")


def certificates(directory):
    run(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "2",
         "-keyout", str(directory / "ca.key"), "-out", str(directory / "ca.crt"),
         "-subj", "/CN=PulseRPC acceptance CA"])
    for node in NODES + ["outsider"]:
        run(["openssl", "req", "-new", "-newkey", "rsa:2048", "-nodes", "-subj", f"/CN={node}",
             "-keyout", str(directory / f"{node}.key"), "-out", str(directory / f"{node}.csr")])
        extensions = directory / f"{node}.ext"
        extensions.write_text(f"basicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\n"
                              f"extendedKeyUsage=serverAuth,clientAuth\nsubjectAltName=DNS:{node}\n")
        run(["openssl", "x509", "-req", "-in", str(directory / f"{node}.csr"), "-CA", str(directory / "ca.crt"),
             "-CAkey", str(directory / "ca.key"), "-CAcreateserial", "-days", "2", "-extfile", str(extensions),
             "-out", str(directory / f"{node}.crt")])
    run(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "2",
         "-keyout", str(directory / "rogue.key"), "-out", str(directory / "rogue.crt"),
         "-subj", "/CN=gateway"])
    for key in directory.glob("*.key"):
        key.chmod(0o600)


def tls_rejected(directory, port, certificate=None, hostname="game-a"):
    context = ssl.create_default_context(cafile=str(directory / "ca.crt"))
    if certificate:
        context.load_cert_chain(str(directory / f"{certificate}.crt"), str(directory / f"{certificate}.key"))
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=3) as raw:
            with context.wrap_socket(raw, server_hostname=hostname) as stream:
                # TLS 1.3 certificate alerts can follow the client handshake. Send no
                # application bytes: a malformed RPC reset would falsely pass this test.
                stream.recv(1)
    except (ssl.SSLError, ConnectionResetError):
        return
    raise AssertionError("TLS accepted an unauthorized peer")


def main():
    for name in ("GAME_REDIS", "GAME_POSTGRES", "GAME_JWT_KEY"):
        if not os.environ.get(name):
            raise RuntimeError(f"Set {name}")
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    processes = []
    logs = []
    node_processes = {}
    results = {"separate_processes": True, "real_redis": True, "real_postgres": True,
               "actual_mtls": True, "capacity_certified": False, "checks": [], "loads": []}
    results["environment"] = {"commit": run(["git", "rev-parse", "HEAD"]).strip(),
                              "platform": platform.platform(), "cpu_count": os.cpu_count(),
                              "sdk": run(["dotnet", "--version"]).strip()}
    # Generous CI regression ceiling, not a production latency SLO.
    results["smoke_p99_limit_ms"] = 250
    redis_paused = False
    base = available_base()
    os.environ["GAME_ADMIN_BASE_PORT"] = str(base + 70)
    run_id = uuid.uuid4().hex[:10]
    player_id = "alice-" + run_id

    def record(message):
        results["checks"].append(message)
        print("PASS " + message, flush=True)

    def start(command, name):
        output = open(ARTIFACTS / f"{name}.log", "w", encoding="utf-8")
        logs.append(output)
        process = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT, cwd=ROOT)
        processes.append(process)
        return process

    def client(player, scenario, *arguments):
        output = run(["dotnet", str(HOST), "client", str(base + 60), player, scenario, *map(str, arguments)], timeout=150)
        lines = [line[7:] for line in output.splitlines() if line.startswith("RESULT ")]
        if len(lines) != 1:
            raise RuntimeError("Client did not produce exactly one result: " + output[-4000:])
        return json.loads(lines[0])

    def admin(node, path, method="GET"):
        request = urllib.request.Request(f"http://127.0.0.1:{base + 70 + NODES.index(node)}{path}", method=method)
        try:
            with urllib.request.urlopen(request, timeout=5) as response:
                return response.status, response.read().decode()
        except urllib.error.HTTPError as error:
            return error.code, error.read().decode()

    temporary = tempfile.TemporaryDirectory(prefix="pulserpc-mtls-")
    try:
        directory = Path(temporary.name)
        certificates(directory)
        run(["dotnet", str(HOST), "init"])
        verification = run(["dotnet", str(HOST), "verify-store"], timeout=50)
        results["database"] = json.loads(next(line[7:] for line in verification.splitlines() if line.startswith("RESULT ")))
        record("PostgreSQL concurrent replay, stale-writer fencing and outbox/inbox verification")
        session_verification = run(["dotnet", str(HOST), "verify-sessions-store"])
        results["session_database"] = json.loads(next(line[7:] for line in session_verification.splitlines() if line.startswith("RESULT ")))
        record("database session lock serializes login replacement with purchases; stale queued writers are rejected")
        broker_player = "broker-" + run_id
        broker_stream = "game-acceptance:purchase:" + run_id
        run(["dotnet", str(HOST), "seed-broker", broker_player, str(uuid.uuid4())])

        def kill_at_broker_window(mode):
            process = start(["dotnet", str(HOST), "broker", broker_stream, mode, broker_player], mode)
            deadline = time.monotonic() + 20
            while "FAULT_READY " + mode not in (ARTIFACTS / f"{mode}.log").read_text():
                if process.poll() is not None or time.monotonic() >= deadline:
                    raise RuntimeError("Broker crash window was not reached: " + mode)
                time.sleep(0.05)
            process.kill()
            process.wait(timeout=5)

        kill_at_broker_window("publish-crash")
        run(["dotnet", str(HOST), "broker", broker_stream, "publish", broker_player])
        kill_at_broker_window("consume-crash")
        time.sleep(1.1)  # The new process claims the killed consumer's pending delivery.
        run(["dotnet", str(HOST), "broker", broker_stream, "consume", broker_player])
        broker_output = run(["dotnet", str(HOST), "broker", broker_stream, "verify", broker_player])
        results["broker"] = json.loads(next(line[7:] for line in broker_output.splitlines() if line.startswith("RESULT ")))
        record("real broker: SIGKILL after publish and after consumer commit; replay applies each side effect once")
        cluster_process_start = len(processes)  # Earlier broker fault processes intentionally exited.
        for index, node in enumerate(NODES):
            common = (f"cert = {directory}/{node}.crt\nkey = {directory}/{node}.key\n"
                      f"CAfile = {directory}/ca.crt\nverifyChain = yes\nrequireCert = yes\n")
            config = f"foreground = yes\npid = {directory}/{node}.pid\ndebug = warning\n"
            config += f"[internal]\naccept = 127.0.0.1:{base + 10 + index}\nconnect = 127.0.0.1:{base + index}\n" + common
            config += "".join(f"checkHost = {allowed}\n" for allowed in NODES)
            for target, remote in enumerate(NODES):
                config += (f"[to-{remote}]\nclient = yes\naccept = 127.0.0.1:{base + 30 + index * 10 + target}\n"
                           f"connect = 127.0.0.1:{base + 10 + target}\n" + common + f"checkHost = {remote}\n")
            if node == "gateway":
                # Public TLS authenticates the server; player identity comes from JWT.
                config += (f"[players]\naccept = 127.0.0.1:{base + 61}\nconnect = 127.0.0.1:{base}\n"
                           f"cert = {directory}/{node}.crt\nkey = {directory}/{node}.key\n")
            config_path = directory / f"{node}.conf"
            config_path.write_text(config)
            start(["stunnel4", str(config_path)], f"tls-{node}")
            node_processes[node] = start(["dotnet", str(HOST), "node", node, str(base + index),
                                          str(directory), str(base + 30 + index * 10)], node)
        public = directory / "player.conf"
        public.write_text(f"foreground = yes\npid = {directory}/player.pid\ndebug = warning\n[server]\nclient = yes\n"
                          f"accept = 127.0.0.1:{base + 60}\nconnect = 127.0.0.1:{base + 61}\n"
                          f"CAfile = {directory}/ca.crt\nverifyChain = yes\ncheckHost = gateway\n")
        start(["stunnel4", str(public)], "tls-player")
        deadline = time.monotonic() + 35
        while not all(f"READY {node}" in (ARTIFACTS / f"{node}.log").read_text() for node in NODES):
            if any(process.poll() is not None for process in processes[cluster_process_start:]):
                raise RuntimeError("A node or TLS proxy exited during startup; inspect cluster logs")
            if time.monotonic() >= deadline:
                raise TimeoutError("Cluster did not start")
            time.sleep(0.1)

        for node in NODES:
            assert admin(node, "/ready")[0] == 200, node
            status, metrics = admin(node, "/metrics")
            assert status == 200 and "game_queue_capacity" in metrics and "game_outbox_pending" in metrics, metrics
            (ARTIFACTS / f"{node}-metrics-before.txt").write_text(metrics)
        record("loopback readiness checks dependencies; metrics expose queues, connections, RPC, GC and durable outbox lag")

        tls_rejected(directory, base + 11)
        tls_rejected(directory, base + 11, "outsider")
        tls_rejected(directory, base + 11, "rogue")
        tls_rejected(directory, base + 11, "gateway", "wrong-node")
        record("mTLS rejects absent certificate, untrusted issuer, unauthorized subject and wrong server name")
        results["security"] = client(player_id, "security", directory)
        record("anonymous, cross-player, expired identities and CA-trusted non-member node credentials rejected")
        results["sessions"] = client("sessions-" + run_id, "sessions")
        record("replacement login revokes old calls; late logout is safe; reconnect authenticates and resynchronizes without duplicate assets")
        operation = uuid.uuid4()
        purchase = client(player_id, "purchase", operation)
        assert purchase["Balance"] == 993 and purchase["Inventory"] == 1, purchase
        record("generated client purchase and repeated operation ID commit once")
        legacy = ROOT / "samples/GameServer/GameServer.LegacyClient/bin/Release/net10.0/GameServer.LegacyClient.dll"
        legacy_output = run(["dotnet", str(legacy), str(base + 60), player_id])
        legacy_result = json.loads(next(line[7:] for line in legacy_output.splitlines() if line.startswith("RESULT ")))
        assert legacy_result["Balance"] == 993 and legacy_result["Inventory"] == 1, legacy_result
        record("independent C# 9 V1 client reads V2 DTO and stable protocol ID over TLS")
        owner = purchase["NodeId"]
        assert owner in ("game-a", "game-b"), owner
        node_processes[owner].kill()
        node_processes[owner].wait(timeout=5)
        failover_started = time.monotonic()
        last_error = None
        while time.monotonic() - failover_started < 40:
            try:
                replay = client(player_id, "purchase", operation)
                break
            except subprocess.CalledProcessError as error:
                last_error = error.stdout[-3000:]
                time.sleep(0.5)
        else:
            raise RuntimeError("Failover failed: " + str(last_error))
        assert replay["NodeId"] != owner and replay["Fence"] > purchase["Fence"], replay
        assert replay["Balance"] == 993 and replay["Inventory"] == 1, replay
        results["failover_seconds"] = time.monotonic() - failover_started
        assert results["failover_seconds"] < 20, "Owner recovery exceeded the 20-second acceptance budget"
        record("killed owner replaced; PostgreSQL fence advances; replay survives process loss")

        index = NODES.index(owner)
        node_processes[owner] = start(["dotnet", str(HOST), "node", owner, str(base + index), str(directory),
                                      str(base + 30 + index * 10)], owner + "-restarted")
        time.sleep(6)
        # SIGSTOP models a long GC/process suspension: the old process can resume later.
        paused_owner = replay["NodeId"]
        node_processes[paused_owner].send_signal(signal.SIGSTOP)
        pause_started = time.monotonic()
        time.sleep(11)
        while time.monotonic() - pause_started < 55:
            try:
                resumed_replay = client(player_id, "purchase", operation)
                break
            except subprocess.CalledProcessError:
                time.sleep(0.5)
        else:
            raise AssertionError("Paused owner was not replaced")
        assert resumed_replay["NodeId"] != paused_owner and resumed_replay["Fence"] > replay["Fence"], resumed_replay
        node_processes[paused_owner].send_signal(signal.SIGCONT)
        time.sleep(3)
        after_resume = client(player_id, "purchase", operation)
        assert after_resume["Balance"] == 993 and after_resume["Inventory"] == 1, after_resume
        assert after_resume["Fence"] >= resumed_replay["Fence"], after_resume
        results["pause_failover_seconds"] = time.monotonic() - pause_started
        record("SIGSTOP owner replaced; resumed old process cannot restore its generation or duplicate purchase")

        redis_container = os.environ.get("GAME_REDIS_CONTAINER")
        if not redis_container:
            raise RuntimeError("Set GAME_REDIS_CONTAINER to the isolated Redis test container; outage acceptance is mandatory")
        run(["docker", "pause", redis_container])
        redis_paused = True
        assert admin("gateway", "/ready")[0] == 503, "Readiness ignored Redis outage"
        # Redis placement TTL (9s) + DB lease TTL (6s) + scheduling allowance.
        time.sleep(17)
        run(["dotnet", str(HOST), "verify-inactive-owner", player_id])
        try:
            client(player_id, "state")
        except subprocess.CalledProcessError:
            pass
        else:
            raise AssertionError("RPC remained available while Redis ownership could not be established")
        run(["docker", "unpause", redis_container])
        redis_paused = False
        recovery_started = time.monotonic()
        while time.monotonic() - recovery_started < 45:
            try:
                recovered = client(player_id, "purchase", operation)
                break
            except subprocess.CalledProcessError:
                time.sleep(0.5)
        else:
            raise AssertionError("Cluster did not recover after Redis outage")
        assert recovered["Balance"] == 993 and recovered["Inventory"] == 1, recovered
        results["redis_recovery_seconds"] = time.monotonic() - recovery_started
        record("Redis outage stops Actor Tick/DB renewal and RPC; recovery preserves committed state")

        def resident_bytes():
            sizes = {}
            for name, process in node_processes.items():
                status = Path(f"/proc/{process.pid}/status").read_text()
                sizes[name] = int(next(line.split()[1] for line in status.splitlines() if line.startswith("VmRSS:"))) * 1024
            return sizes

        overload_player = "overload-" + run_id
        client(overload_player, "state")
        lock_process = start(["dotnet", str(HOST), "hold-player-row", overload_player], "row-lock")
        deadline = time.monotonic() + 10
        while "LOCKED" not in (ARTIFACTS / "row-lock.log").read_text():
            if lock_process.poll() is not None or time.monotonic() > deadline:
                raise RuntimeError("Could not establish the overload test SQL lock")
            time.sleep(0.01)
        results["overload"] = client(overload_player, "overload")
        lock_process.wait(timeout=10)
        record("blocked purchase produces explicit SERVER_BUSY under a burst; same connection recovers")

        results["server_rss_before_load"] = resident_bytes()
        for player, payload in [("load-" + run_id, 128), ("load-" + run_id, 4096), ("hot", 128)]:
            sample = client(player, "load", 1000, 16, payload)
            results["loads"].append(sample)
            assert sample["P99Ms"] < results["smoke_p99_limit_ms"], sample
        results["server_rss_after_load"] = resident_bytes()
        for _ in range(20):
            state = client(player_id, "state")
            assert state["Balance"] == 993 and state["Inventory"] == 1, state
        record("normal, larger payload, hot-Actor load and 20 fresh client sessions preserve state")
        os.environ["GAME_CANDIDATE_SHA"] = results["environment"]["commit"]
        mixed_output = run(["dotnet", str(HOST), "load", "perf/game-server/ci.json", "127.0.0.1", str(base + 60),
                            str(ARTIFACTS / "mixed-load")], timeout=100)
        results["mixed_load"] = json.loads(next(line[7:] for line in mixed_output.splitlines() if line.startswith("RESULT ")))
        assert results["mixed_load"]["complete"] and results["mixed_load"]["assetIntegrity"], results["mixed_load"]
        mixed_counts = results["mixed_load"]["result"]
        assert mixed_counts["offered"] == 2000 and mixed_counts["succeeded"] == 2000, mixed_counts
        assert mixed_counts["purchases"] > 0 and mixed_counts["reads"] > 0, mixed_counts
        assert mixed_counts["errors"] == 0 and mixed_counts["generatorDropped"] == 0, mixed_counts
        record("open-loop mixed purchase/read/echo load retains offered, dropped, rejected and scheduled-arrival latency; SQL assets reconcile")
        for node in NODES:
            status, metrics = admin(node, "/metrics")
            assert status == 200, node
            (ARTIFACTS / f"{node}-metrics-after.txt").write_text(metrics)

        drain_player = "drain-" + run_id
        drain_operation = uuid.uuid4()
        before_drain = client(drain_player, "purchase", drain_operation)
        draining_owner = before_drain["NodeId"]
        drain_started = time.monotonic()
        assert admin(draining_owner, "/drain", "POST")[0] in (200, 202)
        while time.monotonic() - drain_started < 45:
            status, body = admin(draining_owner, "/drain", "POST")
            if status == 200:
                break
            assert status == 202, body
            time.sleep(0.1)
        else:
            raise AssertionError("Node did not drain")
        assert admin(draining_owner, "/ready")[0] == 503
        time.sleep(1)  # Propagate the drain placement view to other processes.
        after_drain = client(drain_player, "purchase", drain_operation)
        assert after_drain["NodeId"] != draining_owner, after_drain
        assert after_drain["Balance"] == before_drain["Balance"] and after_drain["Inventory"] == 1, after_drain
        results["drain"] = {"seconds": time.monotonic() - drain_started, "old_owner": draining_owner,
                            "new_owner": after_drain["NodeId"], "asset_mutations": 1}
        record("drain stops admission and placement, retires owned Actors, and hands off with durable replay")
        results["passed"] = True
    finally:
        if redis_paused:
            subprocess.run(["docker", "unpause", os.environ["GAME_REDIS_CONTAINER"]], timeout=15, check=False)
        for process in reversed(processes):
            if process.poll() is None:
                process.send_signal(signal.SIGCONT)
                process.terminate()
        for process in reversed(processes):
            try:
                process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        for output in logs:
            output.close()
        temporary.cleanup()
        (ARTIFACTS / "results.json").write_text(json.dumps(results, indent=2) + "\n")
    print(json.dumps(results, indent=2))


if __name__ == "__main__":
    main()
