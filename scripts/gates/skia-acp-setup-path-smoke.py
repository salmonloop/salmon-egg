#!/usr/bin/env python3
"""Verify the setup path editor with real XTest keys against this tree's Debug Skia build."""

import argparse
import base64
import ctypes
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import select
import shutil
import signal
import subprocess
import sys
import time


FIRST_INPUT = "/tmp/acp-path-probe"
FINAL_INPUT = "/tmp/acp-path-probe-next"


def stop_process(process):
    if process is None:
        return
    try:
        os.killpg(process.pid, signal.SIGTERM)
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        os.killpg(process.pid, signal.SIGKILL)
        process.wait(timeout=5)
    except ProcessLookupError:
        process.wait(timeout=5)


def build(args, repo, output):
    if args.no_build:
        return
    command = [
        args.dotnet, "build", str(repo / "SalmonEgg/SalmonEgg/SalmonEgg.csproj"),
        "-c", "Debug", "-f", "net10.0-desktop", "-v", "minimal", "-m:1",
        "-p:SalmonEggTargetFrameworks=net10.0-desktop",
        "-p:SalmonEggAllTargetFrameworks=net10.0-desktop", "-p:UseSharedCompilation=false",
    ]
    env = dict(os.environ, DOTNET_PROCESSOR_COUNT="2", DOTNET_CLI_USE_MSBUILD_SERVER="0",
               MSBUILDDISABLENODEREUSE="1")
    with (output / "build.log").open("w") as log:
        process = subprocess.Popen(command, cwd=repo, env=env, stdout=log,
                                   stderr=subprocess.STDOUT, start_new_session=True)
        try:
            if process.wait(timeout=600) != 0:
                raise RuntimeError("Debug Desktop build failed; see build.log")
        finally:
            stop_process(process)


def read_events(stdout, app):
    text = stdout.read_text(errors="replace") if stdout.exists() else ""
    if "AcpSetupPathProbe complete passed=False" in text:
        raise RuntimeError("Native path editor assertion failed; see stdout.log")
    if app.poll() is not None:
        raise RuntimeError(f"App exited during the probe: {app.returncode}")
    return text


def await_event(stdout, app, predicate, deadline, description):
    while time.monotonic() < deadline:
        text = read_events(stdout, app)
        result = predicate(text)
        if result:
            return result
        time.sleep(0.025)
    raise RuntimeError(f"Timed out waiting for {description}")


def run(args):
    repo = Path(args.repo).resolve()
    output = Path(args.artifacts).resolve()
    output.mkdir(parents=True, exist_ok=True)
    appdata = output / "appdata"
    appdata.mkdir(exist_ok=False)
    build(args, repo, output)
    app_path = repo / "SalmonEgg/SalmonEgg/bin/Debug/net10.0-desktop/SalmonEgg"
    assembly = app_path.with_suffix(".dll")
    if not app_path.is_file() or not assembly.is_file():
        raise RuntimeError("This tree has no Debug Desktop executable and assembly")
    provenance = {
        "repo": str(repo), "head": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip(),
        "working_tree": subprocess.check_output(["git", "status", "--short"], cwd=repo, text=True),
        "app": str(app_path), "assembly_sha256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
        "built_by_gate": not args.no_build, "agent": args.agent,
    }
    (output / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
    helper = repo / "scripts/gates/skia-desktop-x11-window-probe.py"
    spec = importlib.util.spec_from_file_location("x11_probe", helper)
    x11_probe = importlib.util.module_from_spec(spec)
    sys.dont_write_bytecode = True
    spec.loader.exec_module(x11_probe)
    x11 = x11_probe.configure_x11()
    xtst = x11_probe.configure_xtst()
    xvfb = app = display = None
    read_fd, write_fd = os.pipe()
    try:
        with (output / "xvfb.log").open("w") as xvfb_log:
            xvfb = subprocess.Popen(
                ["Xvfb", "-displayfd", str(write_fd), "-screen", "0", "1920x1080x24", "-nolisten", "tcp"],
                pass_fds=(write_fd,), stdout=xvfb_log, stderr=subprocess.STDOUT, start_new_session=True)
        os.close(write_fd)
        write_fd = None
        if not select.select([read_fd], [], [], 10)[0]:
            raise RuntimeError("Xvfb did not allocate a display")
        display_name = ":" + os.read(read_fd, 32).decode().strip()
        os.close(read_fd)
        read_fd = None
        display = x11.XOpenDisplay(display_name.encode())
        if not display:
            raise RuntimeError("Cannot connect to the isolated Xvfb display")
        env = dict(os.environ, DISPLAY=display_name, SALMONEGG_GUI="1", SALMONEGG_APPDATA_ROOT=str(appdata),
                   SALMONEGG_ACP_SETUP_PATH_PROBE="1", SALMONEGG_ACP_SETUP_PATH_AGENT=args.agent,
                   DOTNET_PROCESSOR_COUNT="2")
        stdout_path = output / "stdout.log"
        with stdout_path.open("w") as stdout:
            app = subprocess.Popen([str(app_path)], cwd=app_path.parent, env=env, stdout=stdout,
                                   stderr=subprocess.STDOUT, start_new_session=True)
        provenance.update(pid=app.pid, display=display_name)
        (output / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
        deadline = time.monotonic() + 100
        ready = await_event(stdout_path, app,
                            lambda text: re.search(r"AcpSetupPathProbe ready pid=(\d+)[^\n]*", text),
                            deadline, "the real missing-runtime editor")
        if int(ready[1]) != app.pid:
            raise RuntimeError("Ready marker belongs to a different process")
        windows = x11_probe.get_viewable_windows(x11, display, x11.XDefaultRootWindow(display), app.pid, 200, 200)
        own_windows = [candidate for candidate in windows if candidate[4] == app.pid]
        if not own_windows:
            # Uno's X11 host may omit _NET_WM_PID. This fresh display has only the app we launched;
            # accept its sole named window, never a different explicit PID or an ambiguous display.
            named = [value for value in windows if "SalmonEgg" in value[3] or "Salmon Egg" in value[3]]
            if len(named) == 1 and named[0][4] is None and all(value[4] in (None, app.pid) for value in windows):
                own_windows = named
            else:
                details = [{"window": value[1], "title": value[3], "pid": value[4]} for value in windows]
                raise RuntimeError(f"No unique application window on the isolated display: {details}")
        window = own_windows[0][1]
        provenance.update(window=window, window_pid=own_windows[0][4],
                          window_identity="pid-property" if own_windows[0][4] == app.pid else "isolated-display")
        (output / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
        x11.XSetInputFocus(display, window, x11_probe.REVERT_TO_PARENT, x11_probe.CURRENT_TIME)
        x11.XSync(display, 0)

        def key(keysym, pressed):
            code = x11.XKeysymToKeycode(display, keysym)
            if not code or not xtst.XTestFakeKeyEvent(display, code, int(pressed), 0):
                raise RuntimeError(f"XTest rejected keysym {keysym}")

        def press(keysym):
            key(keysym, True)
            key(keysym, False)
            x11.XSync(display, 0)

        sample_count = 0

        def expect_text(expected):
            nonlocal sample_count
            encoded = base64.b64encode(expected.encode()).decode()

            def match(text):
                for found in re.finditer(r"AcpSetupPathProbe sample=(\d+)([^\n]*)", text):
                    details = found[2]
                    if int(found[1]) <= sample_count or not re.search(r"\btext64=" + re.escape(encoded) + r"(?:\s|$)", details):
                        continue
                    if any(f"{field}=True" not in details for field in ["visible", "focused", "binding", "verifyVisible"]):
                        raise RuntimeError(f"Invalid native input sample: {found[0]}")
                    return int(found[1])
                return None

            sample_count = await_event(stdout_path, app, match, min(deadline, time.monotonic() + 5),
                                       f"native text {expected!r}")

        def type_text(value):
            for length, character in enumerate(value, start=1):
                press(ord(character))
                expect_text(value[:length])

        type_text(FIRST_INPUT)
        key(0xFFE3, True)  # Control_L
        press(ord("a"))
        key(0xFFE3, False)
        press(0xFF08)  # BackSpace
        expect_text("")
        type_text(FINAL_INPUT)
        complete = await_event(stdout_path, app,
                               lambda text: re.search(r"AcpSetupPathProbe complete passed=True samples=(\d+)", text),
                               deadline, "native completion")
        expected_samples = len(FIRST_INPUT) + 1 + len(FINAL_INPUT)
        if int(complete[1]) < expected_samples or sample_count < expected_samples:
            raise RuntimeError("Too few unique native input samples")
        result = {"passed": True, "samples": sample_count, "expected_samples": expected_samples,
                  "ready": ready[0], "pid": app.pid}
        (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
        print(json.dumps(result))
    finally:
        if display:
            x11.XCloseDisplay(display)
        stop_process(app)
        stop_process(xvfb)
        for fd in (read_fd, write_fd):
            if fd is not None:
                os.close(fd)


def main():
    def interrupt(signum, _frame):
        raise InterruptedError(f"Interrupted by signal {signum}")

    signal.signal(signal.SIGTERM, interrupt)
    signal.signal(signal.SIGINT, interrupt)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", default=Path(__file__).resolve().parents[2])
    parser.add_argument("--artifacts", required=True, help="Fresh directory for this build and run's evidence")
    parser.add_argument("--dotnet", default=shutil.which("dotnet") or "dotnet")
    parser.add_argument("--no-build", action="store_true", help="Use a Debug Desktop build just produced in this same tree")
    parser.add_argument("--agent", default="qwen-code", help="Real catalog agent whose runtime is absent on this machine")
    args = parser.parse_args()
    try:
        run(args)
    except Exception as exception:
        print(f"ACP setup path smoke failed: {exception}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
