#!/usr/bin/env python3
"""Separate .NET processes, Redis leases, PostgreSQL state and actual stunnel mTLS.

Certificates and private keys live only in a temporary directory, never in artifacts.
Requires .NET 10, openssl, stunnel4, GAME_REDIS and GAME_POSTGRES.
"""
import json
import os
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

ROOT = Path(__file__).resolve().parents[1]
HOST = ROOT / "samples/GameServer/GameServer.Host/bin/Release/net10.0/GameServer.Host.dll"
ARTIFACTS = ROOT / "artifacts/game-server/cluster"
NODES = ["gateway", "game-a", "game-b"]


def run(command, timeout=40):
    return subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                          check=True, timeout=timeout, cwd=ROOT).stdout


def available_base():
    for _ in range(100):
        base = random.randrange(20000, 45000)
        held = []
        try:
            for port in range(base, base + 70):
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
    for key in directory.glob("*.key"):
        key.chmod(0o600)


def tls_rejected(directory, port, certificate=None, hostname="game-a"):
    context = ssl.create_default_context(cafile=str(directory / "ca.crt"))
    if certificate:
        context.load_cert_chain(str(directory / f"{certificate}.crt"), str(directory / f"{certificate}.key"))
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=3) as raw:
            with context.wrap_socket(raw, server_hostname=hostname) as stream:
                # TLS 1.3 client-certificate rejection can arrive after client handshake returns.
                stream.sendall(b"unauthorized-probe")
                data = stream.recv(1)
                if not data:
                    return
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
    base = available_base()
    run_id = uuid.uuid4().hex[:10]
    player_id = "alice-" + run_id

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

    try:
        with tempfile.TemporaryDirectory(prefix="pulserpc-mtls-") as temporary:
            directory = Path(temporary)
            certificates(directory)
            run(["dotnet", str(HOST), "init"])
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
                if any(process.poll() is not None for process in processes):
                    raise RuntimeError("A node or TLS proxy exited during startup; inspect cluster logs")
                if time.monotonic() >= deadline:
                    raise TimeoutError("Cluster did not start")
                time.sleep(0.1)

            tls_rejected(directory, base + 11)
            tls_rejected(directory, base + 11, "outsider")
            tls_rejected(directory, base + 11, "gateway", "wrong-node")
            results["checks"].append("mTLS rejects absent certificate, unauthorized subject and wrong server name")
            results["security"] = client(player_id, "security")
            operation = uuid.uuid4()
            purchase = client(player_id, "purchase", operation)
            assert purchase["Balance"] == 993 and purchase["Inventory"] == 1, purchase
            results["checks"].append("generated client purchase and repeated operation ID commit once")
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
            results["checks"].append("killed owner replaced; PostgreSQL fence advances; replay survives process loss")

            index = NODES.index(owner)
            node_processes[owner] = start(["dotnet", str(HOST), "node", owner, str(base + index), str(directory),
                                          str(base + 30 + index * 10)], owner + "-restarted")
            time.sleep(6)
            for player, payload in [("load-" + run_id, 128), ("load-" + run_id, 4096), ("hot", 128)]:
                results["loads"].append(client(player, "load", 1000, 16, payload))
            results["checks"].append("normal, larger payload and hot-Actor load completed without corruption")
            results["passed"] = True
    finally:
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
        (ARTIFACTS / "results.json").write_text(json.dumps(results, indent=2) + "\n")
    print(json.dumps(results, indent=2))


if __name__ == "__main__":
    main()
