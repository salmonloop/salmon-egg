#!/usr/bin/env python3
"""Run opt-in Pi acceptance against an isolated native Secret Service and Agent directory."""

import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time


def stop(process):
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        pass
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        os.killpg(process.pid, signal.SIGKILL)
        process.wait(timeout=5)
    # The process leader may exit before descendants that inherited its process group.
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass


def run_owned(command, timeout, **kwargs):
    process = subprocess.Popen(command, start_new_session=True, **kwargs)
    try:
        code = process.wait(timeout=timeout)
        if code:
            raise subprocess.CalledProcessError(code, command)
    finally:
        stop(process)


def reap_owned_children():
    # Linux makes grandchildren ours when a leader dies. This process has no unrelated children.
    children = Path(f"/proc/{os.getpid()}/task/{os.getpid()}/children")
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        for value in children.read_text().split():
            try:
                os.kill(int(value), signal.SIGKILL)
            except ProcessLookupError:
                pass
        while True:
            try:
                pid, _ = os.waitpid(-1, os.WNOHANG)
                if not pid:
                    break
            except ChildProcessError:
                return
        # A killed parent can make its independent-session descendants newly adoptable.
        time.sleep(0.02)
    raise RuntimeError("Owned credential processes did not exit within the cleanup deadline")


def run_inside_bus(args):
    root = Path(os.environ["SALMONEGG_CREDENTIAL_SANDBOX"])
    keyring = subprocess.Popen(["gnome-keyring-daemon", "--foreground", "--unlock", "--components=secrets",
        "--control-directory=" + str(root / "keyring-control")], stdin=subprocess.PIPE,
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
    keyring.stdin.write(b"ephemeral-acceptance-keyring\n")
    keyring.stdin.close()
    try:
        deadline = time.monotonic() + 10
        while True:
            owner = subprocess.check_output(["dbus-send", "--session", "--print-reply", "--dest=org.freedesktop.DBus",
                "/org/freedesktop/DBus", "org.freedesktop.DBus.NameHasOwner", "string:org.freedesktop.secrets"], text=True, timeout=5)
            if "boolean true" in owner:
                break
            assert keyring.poll() is None, "The isolated keyring exited"
            if time.monotonic() >= deadline:
                raise TimeoutError("The isolated Secret Service did not start")
            time.sleep(0.05)
        with (args.artifacts / "client.log").open("w") as log:
            test = subprocess.Popen([args.dotnet, "test", "--project",
                "tests/SalmonEgg.Acp.Desktop.Tests/SalmonEgg.Acp.Desktop.Tests.csproj", "--configuration", "Release",
                "--no-build", "--no-restore", "--no-ansi", "--filter-class",
                "SalmonEgg.Acp.Desktop.Tests.IsolatedCredentialAcceptanceTests", "--minimum-expected-tests", "1", "--output", "Detailed"],
                cwd=args.repo, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            try:
                code = test.wait(timeout=155)
            finally:
                stop(test)
        assert code == 0, "The real Agent/keyring test failed; inspect client.log"
        result = (args.artifacts / "client.log").read_text()
        assert "succeeded: 1" in result and "skipped: 0" in result, "The acceptance test did not execute"
    finally:
        stop(keyring)


def main(args):
    if args.inside_bus:
        run_inside_bus(args)
        return
    assert sys.platform == "linux", "This gate requires the real Linux Secret Service"
    for command in ["pi-acp", "pi", "gnome-keyring-daemon", "secret-tool", "dbus-run-session", "dbus-send"]:
        assert shutil.which(command), "Missing prerequisite: " + command
    secret = os.environ.get("SALMONEGG_REAL_AGENT_SECRET")
    assert secret, "Supply SALMONEGG_REAL_AGENT_SECRET through a secure environment"
    assert ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0) == 0
    args.artifacts = args.artifacts.resolve()
    args.artifacts.mkdir(parents=True, exist_ok=True)
    with (args.artifacts / "build.log").open("w") as log:
        run_owned([args.dotnet, "build", "tests/SalmonEgg.Acp.Desktop.Tests/SalmonEgg.Acp.Desktop.Tests.csproj",
                        "--configuration", "Release", "-m:1", "--disable-build-servers", "-p:UseSharedCompilation=false"],
                       cwd=args.repo, stdout=log, stderr=subprocess.STDOUT, timeout=180)
    # Control sockets must fit sun_path. These are ephemeral files, not the durable evidence.
    with tempfile.TemporaryDirectory(prefix="se-credential-") as temporary:
        root = Path(temporary)
        agent = root / "pi-agent"
        agent.mkdir()
        model = {"id": args.model, "reasoning": False, "input": ["text"], "contextWindow": 32768, "maxTokens": 512}
        provider = {"baseUrl": args.endpoint, "api": args.api, "apiKey": "$SALMONEGG_PI_ACCEPTANCE_KEY", "models": [model]}
        (agent / "models.json").write_text(json.dumps({"providers": {"salmon-acceptance": provider}}))
        (agent / "settings.json").write_text(json.dumps({"defaultProvider": "salmon-acceptance", "defaultModel": args.model,
            "defaultThinkingLevel": "off", "quietStartup": True, "defaultProjectTrust": "never",
            "enableAnalytics": False, "enableInstallTelemetry": False}))
        for name in ["data", "runtime", "config", "keyring-control"]:
            (root / name).mkdir(mode=0o700)
        env = dict(os.environ)
        for name in list(env):
            if name.endswith(("_API_KEY", "_AUTH_TOKEN", "_ACCESS_TOKEN")) or name == "SALMONEGG_PI_ACCEPTANCE_KEY":
                env.pop(name)
        env.update(SALMONEGG_REAL_AGENT_SECRET=secret, SALMONEGG_REAL_AGENT_COMMAND=shutil.which("pi-acp"),
            SALMONEGG_CREDENTIAL_SANDBOX=str(root), SALMONEGG_ISOLATED_PI_DIR=str(agent), PI_CODING_AGENT_DIR=str(agent),
            XDG_DATA_HOME=str(root / "data"), XDG_RUNTIME_DIR=str(root / "runtime"), XDG_CONFIG_HOME=str(root / "config"),
            DOTNET_PROCESSOR_COUNT="2", DOTNET_CLI_USE_MSBUILD_SERVER="0", MSBUILDDISABLENODEREUSE="1")
        run_owned(["dbus-run-session", "--", sys.executable, str(Path(__file__).resolve()), "--inside-bus",
            "--repo", str(args.repo), "--artifacts", str(args.artifacts), "--dotnet", args.dotnet,
            "--endpoint", args.endpoint, "--model", args.model, "--api", args.api], env=env, timeout=190)
    (args.artifacts / "acceptance.json").write_text(json.dumps({
        "head": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=args.repo, text=True).strip(),
        "workingTree": subprocess.check_output(["git", "status", "--short"], cwd=args.repo, text=True),
        "piVersion": subprocess.check_output(["pi", "--version"], text=True, timeout=10).strip(),
        "adapter": shutil.which("pi-acp"), "model": args.model,
        "testSourceSha256": hashlib.sha256((args.repo / "tests/SalmonEgg.Acp.Desktop.Tests/IsolatedCredentialAcceptanceTests.cs").read_bytes()).hexdigest(),
        "requiresAuthWithoutKey": True, "nativeKeyringReload": True,
        "realPrompt": True, "clearRejectsReconnect": True,
    }, indent=2) + "\n")
    print("Real Pi acceptance passed: auth required, native keyring reload, bound prompt, cleared credential refusal")


if __name__ == "__main__":
    def interrupt(_signal, _frame):
        raise KeyboardInterrupt("Isolated credential gate interrupted")

    signal.signal(signal.SIGTERM, interrupt)
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", type=Path, default=Path.cwd())
    parser.add_argument("--artifacts", type=Path, required=True)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--endpoint", required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--api", choices=["openai-completions", "openai-responses", "anthropic-messages"], default="openai-completions")
    parser.add_argument("--inside-bus", action="store_true", help=argparse.SUPPRESS)
    arguments = parser.parse_args()
    try:
        main(arguments)
    finally:
        if sys.platform == "linux" and not arguments.inside_bus:
            reap_owned_children()
