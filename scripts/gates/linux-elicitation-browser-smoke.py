#!/usr/bin/env python3
"""Exercise a freshly built launcher through real xdg-open and a separate Chromium process."""

import argparse
import ctypes
import json
import os
from pathlib import Path
import re
import shlex
import signal
import subprocess
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("probe", type=Path, help="Freshly built LinuxElicitationProbe DLL")
    parser.add_argument("--dotnet", required=True)
    parser.add_argument("--chromium", required=True)
    options = parser.parse_args()
    assert options.probe.is_file()
    assert Path(options.chromium).is_file()
    # Reap the opener's browser descendants after the short-lived probe has exited.
    assert ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0) == 0
    received = threading.Event()
    reports = []
    visits = []
    nonce = os.urandom(12).hex()

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_args):
            pass

        def do_GET(self):
            if not self.path.startswith("/authorize?"):
                self.send_error(404)
                return
            visits.append(self.path)
            self.send_response(200)
            self.send_header("Content-Type", "text/html")
            self.end_headers()
            self.wfile.write(b"""<!doctype html><input id='private' value='page-private-canary'>
                <script>fetch('/report', {method:'POST', body: JSON.stringify({
                opener: window.opener === null, referrer: document.referrer,
                privateValue: document.querySelector('#private').value
                })}).finally(() => document.title='External step complete');</script>""")

        def do_POST(self):
            reports.append(json.loads(self.rfile.read(int(self.headers["Content-Length"]))))
            self.send_response(204)
            self.end_headers()
            received.set()

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    worker = threading.Thread(target=server.serve_forever, daemon=True)
    worker.start()
    process = None
    browser_pid = None
    try:
        with tempfile.TemporaryDirectory(prefix="salmon-egg-url-browser-") as directory:
            root = Path(directory)
            pid_file = root / "browser.pid"
            browser_profile = root / "profile"
            browser_log = root / "browser.log"
            browser_log.touch(mode=0o600)
            launcher = root / "browser-handler"
            launcher.write_text("#!/bin/sh\n"
                "printf 'launcher-private-canary\\n'\n"
                "printf 'launcher-private-canary\\n' >&2\n"
                f"{shlex.quote(options.chromium)} --headless=new --disable-gpu --no-first-run "
                f"--no-default-browser-check --user-data-dir={shlex.quote(str(browser_profile))} "
                f'"$1" >{shlex.quote(str(browser_log))} 2>&1 &\n'
                f'printf "%s\\n" "$!" > {shlex.quote(str(pid_file))}\n'
                "exit 0\n")
            launcher.chmod(0o700)
            env = os.environ.copy()
            env.update(PATH="/usr/bin:/bin", BROWSER=str(launcher), XDG_CURRENT_DESKTOP="X-Generic")
            env.pop("DISPLAY", None)
            env.pop("WAYLAND_DISPLAY", None)
            url = f"http://127.0.0.1:{server.server_port}/authorize?url-private-canary={nonce}"
            private_values = (url, nonce, "page-private-canary", "launcher-private-canary")
            process = subprocess.Popen([options.dotnet, str(options.probe)], env=env,
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                text=True, start_new_session=True)
            try:
                output, errors = process.communicate(url + "\n", timeout=25)
                if pid_file.exists():
                    browser_pid = int(pid_file.read_text())
                assert process.returncode == 0, "Launcher failed: " + redact_diagnostic(output + errors, private_values)
                assert output.strip() == "Opened", redact_diagnostic(output, private_values)
                assert errors == "", redact_diagnostic(errors, private_values)
                assert received.wait(15), describe_browser_failure(browser_log, browser_pid, private_values)
                assert len(visits) == 1, f"Expected one browser visit; observed {len(visits)}."
                assert reports == [{"opener": True, "referrer": "", "privateValue": "page-private-canary"}]
                assert browser_pid is not None
                args = Path(f"/proc/{browser_pid}/cmdline").read_bytes().replace(b"\0", b" ")
                assert b"--remote-debugging" not in args, "The product must not receive a browser debugging bridge."
                assert b"--no-sandbox" not in args, "The browser must retain its native sandbox."
                for canary in private_values:
                    assert canary not in output + errors
                print(f"Linux URL launcher passed: artifact={options.probe}; real xdg-open; "
                    "separate sandboxed browser; one visit; no opener/referrer/debug bridge; private output discarded.")
            finally:
                # The probe, opener and browser all belong to this isolated gate's process group.
                # The production launcher itself deliberately never owns or kills the user's browser.
                if process is not None:
                    stop_owned_processes(process)
    finally:
        server.shutdown()
        server.server_close()
        worker.join(timeout=5)


def redact_diagnostic(text, private_values):
    for value in private_values:
        text = text.replace(value, "<redacted>")
    return re.sub(r"\b(?:https?|wss?|file)://\S+", "<redacted-url>", text)


def describe_browser_failure(log_path, browser_pid, private_values):
    # Chrome startup errors must remain observable without publishing the controlled URL or
    # private page/handler output. The raw file lives only in this gate's private temp directory.
    details = log_path.read_text(errors="replace") if log_path.exists() else ""
    diagnostic = redact_diagnostic(details, private_values)[-8000:]
    if browser_pid is None:
        state = "handler-not-invoked"
    else:
        try:
            state = Path(f"/proc/{browser_pid}/stat").read_text().rsplit(")", 1)[1].split()[0]
        except FileNotFoundError:
            state = "exited"
    return "The real browser never visited the controlled external page. " \
        + f"Browser process state={state}; stderr={diagnostic or '<empty>'}"


def stop_owned_processes(process):
    for termination in (signal.SIGTERM, signal.SIGKILL):
        try:
            os.killpg(process.pid, termination)
        except ProcessLookupError:
            pass
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            try:
                child, _status = os.waitpid(-1, os.WNOHANG)
                if child:
                    continue
            except ChildProcessError:
                return
            time.sleep(0.05)
    raise RuntimeError("The gate could not reap its browser descendants.")


if __name__ == "__main__":
    main()
