#!/usr/bin/env python3
"""Drive the current Linux desktop's real URL card and system browser with XTest input."""

import argparse
import ctypes
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
import os
from pathlib import Path
import re
import select
import shlex
import shutil
import signal
import subprocess
import sys
import threading
import time


def load_module(name, path):
    sys.dont_write_bytecode = True
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def seed_profile(root, scenario, peer):
    config = root / "config"
    (config / "servers").mkdir(parents=True)
    (root / "conversations").mkdir()
    project = root / "project"
    project.mkdir()
    (config / "app.yaml").write_text("schema_version: 1\ntheme: Light\nlanguage: en\n"
        "last_selected_server_id: native-elicitation-profile\nlast_selected_project_id: native-elicitation-project\n"
        f"projects:\n  - project_id: native-elicitation-project\n    name: Native acceptance\n    root_path: {json.dumps(str(project))}\n")
    (config / "servers/native-elicitation-profile.yaml").write_text("schema_version: 5\n"
        "id: native-elicitation-profile\nname: Native Elicitation Fixture\ntransport: stdio\n"
        f"stdio_command: {json.dumps(sys.executable)}\nstdio_arguments:\n  - {json.dumps(str(peer))}\n  - {json.dumps(str(scenario))}\n"
        "connection_timeout_seconds: 30\nauthentication:\n  mode: none\nproxy:\n  mode: none\n")
    conversation = {"conversationId": "native-elicitation-conversation", "displayName": "Native acceptance",
        "createdAt": "2026-09-12T00:00:00Z", "lastUpdatedAt": "2026-09-12T00:00:00Z",
        "cwd": str(project), "boundProfileId": "native-elicitation-profile",
        "remoteSessionId": "native-elicitation-session", "messages": []}
    (root / "conversations/conversations.v1.json").write_text(json.dumps(
        {"version": 1, "lastActiveConversationId": None, "conversations": [conversation]}))
    return project


def read_json_lines(path):
    if not path.exists():
        return []
    return [json.loads(line) for line in path.read_text().splitlines() if line]


def run(args):
    repo = args.repo.resolve()
    output = args.artifacts.resolve()
    output.mkdir(parents=True, exist_ok=True)
    appdata = output / "appdata"
    appdata.mkdir(exist_ok=False)
    helpers = repo / "scripts/gates"
    setup = load_module("setup_path_gate", helpers / "skia-acp-setup-path-smoke.py")
    x11_helper = load_module("x11_gate", helpers / "skia-desktop-x11-window-probe.py")
    browser_helper = load_module("browser_gate", helpers / "linux-elicitation-browser-smoke.py")
    setup.build(args, repo, output)
    app_path = repo / "SalmonEgg/SalmonEgg/bin/Debug/net10.0-desktop/SalmonEgg"
    assembly = app_path.with_suffix(".dll")
    assert app_path.is_file() and assembly.is_file(), "No current Debug desktop product"
    provenance = {"repo": str(repo), "head": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip(),
        "working_tree": subprocess.check_output(["git", "status", "--short"], cwd=repo, text=True),
        "app": str(app_path), "assembly_sha256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
        "built_by_gate": not args.no_build, "peer": "deterministic stdio fixture"}
    visits, reports = [], []
    private_page = "native-page-private-canary"
    nonce = os.urandom(16).hex()

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_args):
            pass

        def do_GET(self):
            if self.path != "/authorize?token=" + nonce:
                self.send_error(404)
                return
            visits.append(self.headers.get("Referer"))
            self.send_response(200)
            self.send_header("Content-Type", "text/html")
            self.end_headers()
            self.wfile.write(("<!doctype html><input id='private' value='" + private_page + "'>"
                "<script>fetch('/report',{method:'POST',body:JSON.stringify({opener:window.opener===null,"
                "referrer:document.referrer,privateValue:document.querySelector('#private').value})});</script>").encode())

        def do_POST(self):
            assert self.path == "/report"
            reports.append(json.loads(self.rfile.read(int(self.headers["Content-Length"]))))
            self.send_response(204)
            self.end_headers()

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    worker = threading.Thread(target=server.serve_forever, daemon=True)
    worker.start()
    url = f"http://127.0.0.1:{server.server_port}/authorize?token={nonce}"
    scenario, peer_log, control = output / "scenario.json", output / "peer.jsonl", output / "control.json"
    project = seed_profile(appdata, scenario, helpers / "fixtures/native-elicitation-peer.py")
    scenario.write_text(json.dumps({"url": url, "log": str(peer_log), "control": str(control), "cwd": str(project)}))
    scenario.chmod(0o600)
    browser_pid_file, browser_log = output / "browser.pid", output / "browser.log"
    consent_dispatch, unauthorized_open = output / "consent-dispatched", output / "unconsented-browser-start"
    browser_log.touch(mode=0o600)
    launcher = output / "browser-handler"
    launcher.write_text("#!/bin/sh\n"
        f"test -f {shlex.quote(str(consent_dispatch))} || : > {shlex.quote(str(unauthorized_open))}\n"
        # Chromium headless uses an exclusive profile, unlike the user's interactive browser.
        # Isolate each real handler invocation so reopening is measured without profile locking.
        f"profile=$(mktemp -d {shlex.quote(str(output / 'browser-profile.XXXXXX'))})\n"
        f"{shlex.quote(args.chromium)} --headless=new --disable-gpu --no-first-run --no-default-browser-check "
        f"--user-data-dir=\"$profile\" \"$1\" >>{shlex.quote(str(browser_log))} 2>&1 &\n"
        f"printf '%s\\n' \"$!\" >> {shlex.quote(str(browser_pid_file))}\nexit 0\n")
    launcher.chmod(0o700)
    applications = output / "xdg-data/applications"
    applications.mkdir(parents=True)
    # xdg-utils' generic desktop parser resolves Exec's first word through PATH; an isolated
    # command name avoids its quoted-path limitations while preserving the real system opener.
    (applications / "native-elicitation-browser.desktop").write_text(
        "[Desktop Entry]\nType=Application\nName=Native Elicitation Browser\n"
        "Exec=browser-handler %u\nMimeType=x-scheme-handler/http;x-scheme-handler/https;\n")
    xdg_config = output / "xdg-config"
    xdg_config.mkdir()
    (xdg_config / "mimeapps.list").write_text("[Default Applications]\n"
        "x-scheme-handler/http=native-elicitation-browser.desktop;\n"
        "x-scheme-handler/https=native-elicitation-browser.desktop;\n")
    x11, xtst = x11_helper.configure_x11(), x11_helper.configure_xtst()
    xtst.XTestFakeMotionEvent.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_ulong]
    xtst.XTestFakeButtonEvent.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
    x11.XTranslateCoordinates.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_ulong, ctypes.c_int, ctypes.c_int,
        ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_ulong)]
    assert ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0) == 0
    xvfb = app = display = None
    read_fd, write_fd = os.pipe()
    stdout_path = output / "stdout.log"
    try:
        with (output / "xvfb.log").open("w") as log:
            xvfb = subprocess.Popen(["Xvfb", "-displayfd", str(write_fd), "-screen", "0", "1600x1000x24", "-nolisten", "tcp"],
                pass_fds=(write_fd,), stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        os.close(write_fd)
        write_fd = None
        assert select.select([read_fd], [], [], 10)[0], "Xvfb did not allocate a display"
        display_name = ":" + os.read(read_fd, 32).decode().strip()
        display = x11.XOpenDisplay(display_name.encode())
        assert display
        env = dict(os.environ, DISPLAY=display_name, SALMONEGG_APPDATA_ROOT=str(appdata),
            SALMONEGG_NATIVE_ELICITATION_PROBE="1", SALMONEGG_GUI="1", BROWSER=str(launcher),
            XDG_CURRENT_DESKTOP="X-Generic", PATH=str(output) + ":/usr/bin:/bin", DOTNET_PROCESSOR_COUNT="2",
            XDG_DATA_HOME=str(applications.parent), XDG_CONFIG_HOME=str(xdg_config))
        env.pop("WAYLAND_DISPLAY", None)
        with stdout_path.open("w") as log:
            app = subprocess.Popen([str(app_path)], cwd=app_path.parent, env=env,
                stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        provenance.update(pid=app.pid, display=display_name)
        (output / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
        deadline = time.monotonic() + 160

        def wait(check, message, seconds=30):
            until = min(deadline, time.monotonic() + seconds)
            while time.monotonic() < until:
                assert app.poll() is None, "Desktop exited before acceptance completed"
                assert not unauthorized_open.exists(), "The product opened a URL without consent"
                assert "NativeElicitationProbe failed" not in stdout_path.read_text(errors="replace"), "Native probe failed"
                value = check()
                if value:
                    return value
                time.sleep(0.05)
            raise AssertionError(message)

        initialized = wait(lambda: next((line for line in read_json_lines(peer_log) if line.get("method") == "initialize"), None),
            "The configured Agent was not initialized")
        assert initialized["capabilities"].get("elicitation", {}).get("url") == {}, "Native URL capability not advertised"
        wait(lambda: "NativeElicitationProbe sample" in stdout_path.read_text(), "Native chat did not mount")
        wait(lambda: any(entry.get("method") == "session/load" for entry in read_json_lines(peer_log)),
            "The authoritative session load did not begin")
        wait(lambda: "Reason=SessionLoadCompleted" in stdout_path.read_text(),
            "The configured conversation did not complete authoritative restoration")
        windows = x11_helper.get_viewable_windows(x11, display, x11.XDefaultRootWindow(display), app.pid, 200, 200)
        own = [entry for entry in windows if entry[4] == app.pid]
        if not own:
            own = [entry for entry in windows if entry[4] is None and ("SalmonEgg" in entry[3] or "Salmon Egg" in entry[3])]
        assert len(own) == 1, "The isolated display has no unique product window"
        window = own[0][1]
        x11.XSetInputFocus(display, window, x11_helper.REVERT_TO_PARENT, x11_helper.CURRENT_TIME)
        phase = 0

        def instruct(action):
            nonlocal phase
            phase += 1
            temporary = control.with_suffix(".tmp")
            temporary.write_text(json.dumps({"action": action, "phase": phase}))
            temporary.replace(control)

        def state(stage, completed=None):
            samples = re.findall(r"NativeElicitationProbe sample[^\n]*", stdout_path.read_text(errors="replace"))
            if not samples:
                return None
            sample = samples[-1]
            if f"stage={stage} " not in sample or "visible=True" not in sample or "url=True" not in sample or "host=True" not in sample:
                return None
            if completed is not None and f"completed={completed}" not in sample:
                return None
            return int(re.search(r"seq=(\d+)", sample)[1])

        def click(action, stage):
            previous_bounds, stable_samples, previous_sequence = None, 0, None

            def find():
                nonlocal previous_bounds, stable_samples, previous_sequence
                sequence = state(stage)
                if sequence is None:
                    return None
                button = re.search(rf"NativeElicitationProbe button seq={sequence} action={action} enabled=True x=(\d+) y=(\d+) width=(\d+) height=(\d+)", stdout_path.read_text())
                if not button or sequence == previous_sequence:
                    return None
                bounds = button.groups()
                stable_samples = stable_samples + 1 if bounds == previous_bounds else 1
                previous_bounds, previous_sequence = bounds, sequence
                return button if stable_samples >= 3 else None

            button = wait(find, f"No enabled {action} button on the {stage} card")
            dx, dy, child = ctypes.c_int(), ctypes.c_int(), ctypes.c_ulong()
            assert x11.XTranslateCoordinates(display, window, x11.XDefaultRootWindow(display), 0, 0,
                ctypes.byref(dx), ctypes.byref(dy), ctypes.byref(child))
            x = dx.value + int(button[1]) + int(button[3]) // 2
            y = dy.value + int(button[2]) + int(button[4]) // 2
            x11.XSetInputFocus(display, window, x11_helper.REVERT_TO_PARENT, x11_helper.CURRENT_TIME)
            assert xtst.XTestFakeMotionEvent(display, -1, x, y, 0)
            assert xtst.XTestFakeButtonEvent(display, 1, 1, 0)
            assert xtst.XTestFakeButtonEvent(display, 1, 0, 0)
            x11.XSync(display, 0)

        def replies(label):
            return [line for line in read_json_lines(peer_log) if line.get("id") == "native-url-" + label]

        for action in ("decline", "cancel"):
            instruct(action)
            wait(lambda: state(action), f"No visible {action} card")
            assert not visits, "The product opened a URL without consent"
            click(action, action)
            reply = wait(lambda: replies(action), f"No {action} response")
            assert len(reply) == 1 and reply[0].get("result") == {"action": action}
            assert not visits, "Decline or cancel opened the external site"
        instruct("open")
        wait(lambda: state("open"), "No visible consent card")
        assert not visits
        consent_dispatch.touch()
        click("submit", "open")
        reply = wait(lambda: replies("open"), "No accept response")
        assert len(reply) == 1 and reply[0].get("result") == {"action": "accept"}
        wait(lambda: len(reports) == 1, "First browser page did not report", 45)
        assert state("open", completed="False"), "Browser launch was misreported as Agent completion"
        click("reopen", "open")
        wait(lambda: len(reports) == 2, "Reopened browser page did not report", 45)
        assert len(replies("open")) == 1, "Reopening repeated the ACP acceptance"
        instruct("complete")
        wait(lambda: state("open", completed="True"), "Agent completion did not update the card")
        click("dismiss", "open")
        instruct("expire")
        wait(lambda: state("expire"), "Next URL request could not be displayed")
        instruct("disconnect")

        def expired_card():
            sample = stdout_path.read_text().split("NativeElicitationProbe sample")[-1]
            if "stage=none" in sample:
                return True
            return "stage=expire" in sample and "url=False" in sample and "host=False" in sample \
                and re.search(r"action=submit enabled=False", sample) is not None

        wait(expired_card, "Disconnect retained the old private URL or an enabled open command")
        assert not replies("expire"), "The client fabricated consent while the Agent was disconnected"
        for action in ("decline", "cancel", "open"):
            assert len(replies(action)) == 1, "A completed elicitation received a duplicate response"
        assert visits == [None, None] and reports == [{"opener": True, "referrer": "", "privateValue": private_page}] * 2
        for pid in browser_pid_file.read_text().splitlines():
            cmdline = Path(f"/proc/{pid}/cmdline")
            if cmdline.exists():
                command = cmdline.read_bytes()
                assert b"--no-sandbox" not in command and b"--remote-debugging" not in command
        assert private_page not in peer_log.read_text() + stdout_path.read_text()
        assert url not in stdout_path.read_text(), "A private URL leaked into product diagnostics"
        result = {"passed": True, "visits": len(visits), "responses": 3, "native_pointer_actions": 5,
            "browser_sandbox": True, "peer": "deterministic fixture"}
        (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
        print(json.dumps(result))
    finally:
        if display:
            x11.XCloseDisplay(display)
        setup.stop_process(app)
        setup.stop_process(xvfb)
        if app is not None:
            browser_helper.stop_owned_processes(app)
        for fd in (read_fd, write_fd):
            if fd is not None:
                os.close(fd)
        server.shutdown()
        server.server_close()
        worker.join(timeout=5)
        scenario.unlink(missing_ok=True)
        browser_log.unlink(missing_ok=True)


def main():
    def interrupt(_signum, _frame):
        raise InterruptedError("Native acceptance interrupted")

    signal.signal(signal.SIGTERM, interrupt)
    signal.signal(signal.SIGINT, interrupt)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--artifacts", type=Path, required=True)
    parser.add_argument("--dotnet", default=shutil.which("dotnet") or "dotnet")
    parser.add_argument("--chromium", required=True)
    parser.add_argument("--no-build", action="store_true")
    args = parser.parse_args()
    try:
        run(args)
    except Exception as error:
        print(f"Native elicitation acceptance failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
