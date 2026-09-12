#!/usr/bin/env python3
"""Drive the actual macOS Skia application and LaunchServices browser using native AX/CGEvent."""

import argparse
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import importlib.util
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import threading
import time


def module(path):
    spec = importlib.util.spec_from_file_location("linux_seed", path)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


def main():
    if sys.platform != "darwin":
        raise SystemExit("macOS native URL acceptance requires macOS.")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path("artifacts/macos-elicitation-consent"))
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    repo = Path(__file__).resolve().parents[2]
    helpers = repo / "scripts/gates"
    seed = module(helpers / "skia-elicitation-consent-smoke.py")
    app_path = repo / "SalmonEgg/SalmonEgg/bin/Debug/net10.0-desktop/SalmonEgg"
    assert app_path.is_file(), "Build this tree's Debug Skia product first."
    ax_tool = output / "native-ui"
    subprocess.run(["xcrun", "swiftc", str(helpers / "macos-native-ui.swift"), "-framework", "AppKit",
                    "-framework", "ApplicationServices", "-o", str(ax_tool)], check=True, timeout=90)
    # LaunchServices may attach to an existing browser; do not close any process already running.
    original_pids = set(json.loads(subprocess.check_output([str(ax_tool), "pids", "all"], text=True)))
    reports, visits = [], []
    private_value = "native-macos-page-private"
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
            self.wfile.write(("<!doctype html><input id=private value=" + private_value + ">"
                "<script>fetch('/report',{method:'POST',body:JSON.stringify({opener:window.opener===null,"
                "referrer:document.referrer,privateValue:document.querySelector('#private').value})});</script>").encode())

        def do_POST(self):
            reports.append(json.loads(self.rfile.read(int(self.headers["Content-Length"]))))
            self.send_response(204)
            self.end_headers()

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    appdata = output / "appdata"
    appdata.mkdir(exist_ok=False)
    scenario, peer_log, control = output / "scenario.json", output / "peer.jsonl", output / "control.json"
    project = seed.seed_profile(appdata, scenario, helpers / "fixtures/native-elicitation-peer.py")
    url = f"http://127.0.0.1:{server.server_port}/authorize?token={nonce}"
    scenario.write_text(json.dumps({"url": url, "log": str(peer_log), "control": str(control), "cwd": str(project)}))
    scenario.chmod(0o600)
    environment = dict(os.environ, SALMONEGG_APPDATA_ROOT=str(appdata), SALMONEGG_GUI="1",
                       SALMONEGG_NATIVE_ELICITATION_PROBE="1", DOTNET_PROCESSOR_COUNT="2")
    stdout = output / "stdout.log"
    app = None
    try:
        with stdout.open("w") as log:
            app = subprocess.Popen([str(app_path)], cwd=app_path.parent, env=environment,
                stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        provenance = {"commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip(),
            "app": str(app_path), "sha256": hashlib.sha256(app_path.with_suffix('.dll').read_bytes()).hexdigest(),
            "pid": app.pid, "peer": "deterministic stdio fixture", "input": "native AX positions and CGEvent clicks"}
        (output / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
        deadline = time.monotonic() + 170

        def wait(predicate, message, timeout=30):
            until = min(deadline, time.monotonic() + timeout)
            while time.monotonic() < until:
                assert app.poll() is None, "The current app exited before acceptance."
                value = predicate()
                if value:
                    return value
                time.sleep(0.1)
            raise AssertionError(message)

        initialized = wait(lambda: next((x for x in seed.read_json_lines(peer_log) if x.get('method') == 'initialize'), None), 'initialize did not complete')
        assert initialized['capabilities'].get('elicitation', {}).get('url') == {}, 'macOS URL capability missing'
        wait(lambda: 'Reason=SessionLoadCompleted' in stdout.read_text(), 'Authoritative session restore did not complete')
        phase = 0

        def instruct(action):
            nonlocal phase
            phase += 1
            temporary = control.with_suffix('.tmp')
            temporary.write_text(json.dumps({'phase': phase, 'action': action}))
            temporary.replace(control)

        def state(stage, completed=None):
            value = stdout.read_text().split('NativeElicitationProbe sample')[-1]
            return f'stage={stage} ' in value and 'visible=True' in value and 'url=True' in value and 'host=True' in value \
                and (completed is None or f'completed={completed}' in value)

        def click(title):
            action = {'Decline': 'decline', 'Cancel': 'cancel', 'Open in browser': 'submit',
                      'Open again': 'reopen', 'Close notice': 'dismiss'}[title]
            previous, count = None, 0

            def locate():
                nonlocal previous, count
                text = stdout.read_text().split('NativeElicitationProbe sample')[-1]
                found = re.search(rf'button seq=\d+ action={action} enabled=True x=(\d+) y=(\d+) width=(\d+) height=(\d+) rootHeight=(\d+) scale=([0-9.]+)', text)
                if not found:
                    return None
                values = found.groups()
                count = count + 1 if values == previous else 1
                previous = values
                return values if count >= 3 else None

            x, y, width, height, root_height, scale = map(float, wait(locate, 'No stable enabled native button'))
            permission = subprocess.run([str(ax_tool), 'allow-local-network', 'python-fixture'], capture_output=True, text=True, timeout=10)
            with (output / 'system-permission.log').open('a') as log:
                log.write(permission.stdout + permission.stderr)
            assert permission.returncode == 0, 'Could not handle the matching temporary fixture permission prompt'
            subprocess.run(['screencapture', '-x', str(output / ('before-' + action + '.png'))], check=True, timeout=5)
            attempt = subprocess.run([str(ax_tool), 'pointer', str(app.pid), str((x + width / 2) / scale),
                                      str((y + height / 2) / scale), str(root_height / scale)], capture_output=True, text=True, timeout=10)
            with (output / 'native-pointer.log').open('a') as log:
                log.write(attempt.stdout + attempt.stderr)
            assert attempt.returncode == 0, attempt.stderr

        def replies(action):
            return [x for x in seed.read_json_lines(peer_log) if x.get('id') == 'native-url-' + action]

        for action, title in [('decline', 'Decline'), ('cancel', 'Cancel')]:
            instruct(action)
            wait(lambda: state(action), 'Consent card not visible')
            assert not visits, 'macOS opened URL before consent'
            click(title)
            response = wait(lambda: replies(action), 'Native action did not reply')
            assert len(response) == 1 and response[0].get('result') == {'action': action}
            assert not visits, 'macOS opened URL before consent'
        instruct('open')
        wait(lambda: state('open'), 'Open card not visible')
        assert not visits, 'macOS opened URL before consent'
        click('Open in browser')
        response = wait(lambda: replies('open'), 'No native consent response')
        assert len(response) == 1 and response[0].get('result') == {'action': 'accept'}
        wait(lambda: len(reports) == 1, 'System browser did not visit', 45)
        assert state('open', 'False')
        click('Open again')
        wait(lambda: len(reports) == 2, 'System browser did not reopen', 45)
        assert len(replies('open')) == 1
        instruct('complete')
        wait(lambda: state('open', 'True'), 'Agent completion not projected')
        click('Close notice')
        instruct('expire')
        wait(lambda: state('expire'), 'Next request could not occupy the native card')
        instruct('disconnect')

        def expired():
            sample = stdout.read_text().split('NativeElicitationProbe sample')[-1]
            return 'stage=none' in sample or ('stage=expire' in sample and 'url=False' in sample
                and 'host=False' in sample and 'action=submit enabled=False' in sample)

        wait(expired, 'Disconnect left the original URL or open action active')
        assert not replies('expire')
        for action in ('decline', 'cancel', 'open'):
            assert len(replies(action)) == 1
        assert reports == [{'opener': True, 'referrer': '', 'privateValue': private_value}] * 2
        assert visits == [None, None]
        assert private_value not in stdout.read_text() + peer_log.read_text() and url not in stdout.read_text()
        persisted = list(appdata.rglob('*'))
        for path in persisted:
            if path.is_file():
                content = path.read_bytes()
                assert private_value.encode() not in content and nonce.encode() not in content, \
                    'Private URL or page data persisted in application data: ' + str(path.relative_to(appdata))
        report = {'passed': True, 'nativePointerActions': 5, 'browserVisits': 2,
                  'singleAccept': True, 'agentCompletion': True, 'disconnectExpiry': True, 'privatePageNotReturned': True}
        (output / 'result.json').write_text(json.dumps(report, indent=2) + '\n')
        print(json.dumps(report))
    finally:
        try:
            (output / 'browser-observation.json').write_text(json.dumps({'visits': len(visits), 'reports': reports}, indent=2))
            subprocess.run(['screencapture', '-x', str(output / 'final-screen.png')], timeout=5)
            if app is not None:
                with (output / 'native-controls.json').open('w') as tree:
                    subprocess.run([str(ax_tool), 'describe', str(app.pid)], stdout=tree, timeout=10)
        except (OSError, subprocess.SubprocessError) as error:
            print('Best-effort native diagnostic failed: ' + type(error).__name__, file=sys.stderr)
        cleanup_errors = []
        if app is not None:
            try:
                try: os.killpg(app.pid, signal.SIGTERM)
                except ProcessLookupError: pass
                try: app.wait(timeout=5)
                except subprocess.TimeoutExpired: pass
            finally:
                try: os.killpg(app.pid, signal.SIGKILL)
                except ProcessLookupError: pass
                try: app.wait(timeout=3)
                except subprocess.TimeoutExpired: cleanup_errors.append('Product process did not exit')
        # Only terminate newly opened browser processes, never unrelated or pre-existing applications.
        try:
            new_pids = set(json.loads(subprocess.check_output([str(ax_tool), 'pids', 'all'], text=True, timeout=5))) - original_pids
            for pid in new_pids:
                try: os.kill(pid, signal.SIGTERM)
                except ProcessLookupError: pass
            until = time.monotonic() + 5
            while time.monotonic() < until:
                remaining = set(json.loads(subprocess.check_output([str(ax_tool), 'pids', 'all'], text=True, timeout=5))) & new_pids
                if not remaining:
                    break
                time.sleep(0.1)
            else:
                for pid in remaining:
                    try: os.kill(pid, signal.SIGKILL)
                    except ProcessLookupError: pass
        finally:
            try: server.shutdown()
            finally:
                server.server_close()
                scenario.unlink(missing_ok=True)
        assert not cleanup_errors, '; '.join(cleanup_errors)


if __name__ == '__main__':
    sys.dont_write_bytecode = True
    def interrupt(_signum, _frame):
        raise KeyboardInterrupt('Native Mac gate interrupted')
    signal.signal(signal.SIGTERM, interrupt)
    main()
