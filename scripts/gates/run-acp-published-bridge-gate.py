#!/usr/bin/env python3
"""Verify an installed acpremote release against the production cancellation test."""

import argparse
import contextlib
import ctypes
import json
import os
from pathlib import Path
import re
import shutil
import signal
import socket
import subprocess
import sys
import time
import urllib.request


def main():
    if not sys.platform.startswith("linux"):
        raise SystemExit("This process-ownership gate requires Linux.")
    # Reap descendants even when a bridge exits before a child it started.
    if ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0) != 0:
        raise OSError(ctypes.get_errno(), "Could not enable child-process ownership for this gate.")
    signal.signal(signal.SIGTERM, interrupt)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bridge", default="acpremote")
    parser.add_argument("--output", type=Path, default=Path("artifacts/acp-published-bridge"))
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[2]
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    bridge_path = shutil.which(args.bridge)
    if bridge_path is None:
        raise SystemExit("Install acpremote==1.9.0 in an isolated environment before running this gate.")
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        port = listener.getsockname()[1]
    peer_log = output / ("peer-" + str(time.time_ns()) + ".ndjson")
    command = [bridge_path, "expose", "--host", "127.0.0.1", "--port", str(port),
               "--stderr-mode", "discard", "--", "python3",
               str(repo / "scripts/gates/fixtures/cancellation-peer.py"), str(peer_log)]
    with (output / "bridge.log").open("w") as log:
        bridge = subprocess.Popen(command, cwd=repo, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            wait_for_bridge(bridge, port)
            env = os.environ.copy()
            env["SALMONEGG_ACP_CANCELLATION_BRIDGE_URL"] = f"ws://127.0.0.1:{port}/acp/ws"
            env["SALMONEGG_ACP_CANCELLATION_PEER_LOG"] = str(peer_log)
            with (output / "test.log").open("w") as result_log:
                test = subprocess.Popen([
                    env.get("DOTNET_BIN", "dotnet"), "test", "--project",
                    "tests/SalmonEgg.Infrastructure.Tests/SalmonEgg.Infrastructure.Tests.csproj",
                    "--configuration", "Release", "--no-ansi", "-p:UseSharedCompilation=false",
                    "--filter-class", "SalmonEgg.Infrastructure.Tests.Transport.ProductionBridgeCancellationTests",
                    "--minimum-expected-tests", "1", "--output", "Detailed",
                ], cwd=repo, env=env, stdout=result_log, stderr=subprocess.STDOUT, start_new_session=True)
                try:
                    return_code = test.wait(timeout=180)
                finally:
                    stop_process_group(test)
            text = (output / "test.log").read_text()
            if return_code or not re.search(r"^\s+succeeded:\s+1\s*$", text, re.M) or not re.search(r"^\s+skipped:\s+0\s*$", text, re.M):
                raise SystemExit("Published bridge gate failed; see test.log. Missing execution is not a pass.")
            print(json.dumps({"passed": True, "bridge": "acpremote", "peer": "controlled stdio fixture",
                              "verified": ["cancel forwarding", "terminal response", "subsequent request", "disconnect"]}))
        finally:
            stop_process_group(bridge)


def stop_process_group(process):
    with contextlib.suppress(ProcessLookupError):
        os.killpg(process.pid, signal.SIGTERM)
    try:
        process.wait(timeout=6)
    except subprocess.TimeoutExpired:
        with contextlib.suppress(ProcessLookupError):
            os.killpg(process.pid, signal.SIGKILL)
        process.wait(timeout=3)
    # The parent can exit promptly while a descendant ignores TERM. Groups are unique because
    # every owned root is started with a new session; never inspect or kill unrelated processes.
    with contextlib.suppress(ProcessLookupError):
        os.killpg(process.pid, signal.SIGKILL)
    deadline = time.monotonic() + 3
    while time.monotonic() < deadline:
        try:
            pid, _ = os.waitpid(-process.pid, os.WNOHANG)
            if pid == 0:
                time.sleep(0.02)
                continue
        except ChildProcessError:
            return
    raise RuntimeError("An owned bridge process group could not be reaped within its cleanup budget.")


def interrupt(_signal, _frame):
    raise KeyboardInterrupt("Bridge gate interrupted; reclaiming its owned processes.")


def wait_for_bridge(bridge, port):
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        if bridge.poll() is not None:
            raise SystemExit("Published bridge exited before readiness; see bridge.log.")
        try:
            with urllib.request.urlopen(f"http://127.0.0.1:{port}/healthz", timeout=1) as response:
                if response.status == 200:
                    return
        except OSError:
            time.sleep(0.1)
    raise SystemExit("Published bridge did not become ready within its startup budget.")


if __name__ == "__main__":
    main()
