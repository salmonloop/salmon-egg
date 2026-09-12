#!/usr/bin/env python3
"""Install this build on an owned Android emulator and exercise native ACP consent."""

import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import signal
import socket
import subprocess
import sys
import tarfile
import tempfile
import time
import urllib.request
import xml.etree.ElementTree as ET


PACKAGE = "com.companyname.salmonegg"
CHROME = "com.android.chrome"
SESSION = "MainNav.Session.native-elicitation-conversation"
PROJECT = "MainNav.Project.remote-directory:native-elicitation-directory"


def eventually(description, condition, timeout=30):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        result = condition()
        if isinstance(result, ET.Element) or result:
            return result
        time.sleep(0.2)
    raise TimeoutError(description)


def command(arguments, **kwargs):
    try:
        return subprocess.run(arguments, check=kwargs.pop("check", True), timeout=kwargs.pop("timeout", 90), **kwargs)
    except subprocess.CalledProcessError as error:
        for message in (error.stdout, error.stderr):
            if message:
                print(message.decode(errors="replace") if isinstance(message, bytes) else message, file=sys.stderr)
        raise


def output(arguments):
    return command(arguments, text=True, capture_output=True).stdout.strip()


def has_label(node, label):
    return label in (node.get("text"), node.get("content-desc"), node.get("resource-id")) \
        or node.get("resource-id", "").endswith(":id/" + label)


def bounds(node):
    values = [int(value) for value in re.findall(r"-?\d+", node.get("bounds", ""))]
    return values if len(values) == 4 and values[2] > values[0] and values[3] > values[1] else None


class AndroidDevice:
    def __init__(self, serial, artifacts):
        self.serial = serial
        self.artifacts = artifacts
        self.activity = ""

    def adb(self, *arguments, **kwargs):
        return command(["adb", "-s", self.serial, *arguments], **kwargs)

    def text(self, *arguments):
        return self.adb(*arguments, text=True, capture_output=True).stdout.strip()

    def tree(self):
        self.text("shell", "rm", "-f", "/sdcard/salmonegg-acceptance.xml")
        status = self.text("shell", "uiautomator", "dump", "/sdcard/salmonegg-acceptance.xml")
        exists = self.adb("shell", "test", "-s", "/sdcard/salmonegg-acceptance.xml",
                          text=True, capture_output=True, check=False)
        if exists.returncode:
            # Android can report no root during a splash/transition. Never reuse a previous tree;
            # the caller's bounded wait will require the actual control once it is exposed.
            with (self.artifacts / "ui-sampling.log").open("a") as log:
                log.write(status + "\n")
            return ET.Element("hierarchy")
        xml = self.text("shell", "cat", "/sdcard/salmonegg-acceptance.xml")
        (self.artifacts / "last-ui.xml").write_text(xml)
        return ET.fromstring(xml)

    def find(self, label, package=PACKAGE, enabled=False, editable=False):
        labels = (label,) if isinstance(label, str) else label
        for node in self.tree().iter("node"):
            if node.get("package") != package or not any(has_label(node, item) for item in labels) or not bounds(node):
                continue
            if editable and (not node.get("class", "").endswith(("EditText", "TextBox"))
                             and not any(child.get("class", "").endswith("EditText") for child in node.iter("node"))):
                continue
            if not enabled or node.get("enabled") == "true":
                return node
        return None

    def wait(self, label, package=PACKAGE, timeout=30):
        return eventually("The native control is absent or disabled: " + str(label),
                          lambda: self.find(label, package, enabled=True), timeout)

    def tap_node(self, node):
        rectangle = bounds(node)
        assert rectangle is not None and node.get("enabled") == "true", "The native control is not interactable"
        left, top, right, bottom = rectangle
        self.text("shell", "input", "tap", str((left + right) // 2), str((top + bottom) // 2))

    def tap(self, label, package=PACKAGE):
        self.tap_node(self.wait(label, package))

    def capture(self, name):
        snapshot = self.tree()
        (self.artifacts / (name + ".xml")).write_bytes(ET.tostring(snapshot))
        with (self.artifacts / (name + ".png")).open("wb") as image:
            self.adb("exec-out", "screencap", "-p", stdout=image)

    def activate_product(self):
        self.text("shell", "am", "start", "-W", "-a", "android.intent.action.MAIN",
                  "-c", "android.intent.category.LAUNCHER", "-f", "0x10200000", "-n", self.activity)

    def foreground(self, package):
        activity = self.text("shell", "dumpsys", "activity", "activities")
        return any(package + "/" in line and ("mResumedActivity" in line or "topResumedActivity" in line)
                   for line in activity.splitlines())


def seed_product(device, endpoint):
    # run-as is available only for this Debug APK. Seed the same persisted profile and conversation
    # that production startup reads; all activation and consent still go through native controls.
    files = {
        "config/app.yaml": "schema_version: 1\ntheme: Light\nlanguage: en\n"
            "last_selected_server_id: native-elicitation-profile\n"
            "last_selected_project_id: remote-directory:native-elicitation-directory\n"
            "agent_remote_directories:\n  - directory_id: native-elicitation-directory\n"
            "    display_name: Native acceptance\n    remote_path: /acceptance\n"
            "navigation_remote_directory_ids:\n  - native-elicitation-directory\n",
        "config/servers/native-elicitation-profile.yaml": "schema_version: 5\n"
            "id: native-elicitation-profile\nname: Native Elicitation Fixture\ntransport: websocket\n"
            f"server_url: {endpoint}\nconnection_timeout_seconds: 30\n"
            "authentication:\n  mode: none\nproxy:\n  mode: none\n",
        "conversations/conversations.v1.json": json.dumps({"version": 1, "lastActiveConversationId": None,
            "conversations": [{"conversationId": "native-elicitation-conversation",
                "displayName": "Native acceptance session", "createdAt": "2026-09-12T00:00:00Z",
                "lastUpdatedAt": "2026-09-12T00:00:00Z", "cwd": "/acceptance",
                "projectId": "remote-directory:native-elicitation-directory",
                "boundProfileId": "native-elicitation-profile", "remoteSessionId": "native-elicitation-session",
                "sessionInfo": {"title": "Native acceptance session", "hasTitle": True, "cwd": "/acceptance"},
                "messages": []}]}),
    }
    archive = io.BytesIO()
    with tarfile.open(fileobj=archive, mode="w") as tar:
        for relative, content in files.items():
            data = content.encode()
            entry = tarfile.TarInfo("files/SalmonEgg/" + relative)
            entry.size, entry.mode = len(data), 0o600
            tar.addfile(entry, io.BytesIO(data))
    device.adb("shell", "-T", "run-as", PACKAGE, "tar", "-xf", "-", input=archive.getvalue(), capture_output=True)
    stored = device.text("shell", "run-as", PACKAGE, "cat", "files/SalmonEgg/config/app.yaml")
    assert stored == files["config/app.yaml"].strip(), "The installed product did not receive its configuration"


def wait_for_boot_work(device):
    # boot_completed precedes Google image first-run dexopt and service setup. Starting Chrome
    # during that CPU storm caused a measured OS ANR; require actual idle CPU before user input.
    previous = None
    calm_samples = 0

    def calm():
        nonlocal previous, calm_samples
        line = device.text("shell", "cat", "/proc/stat").splitlines()[0].split()
        values = [int(value) for value in line[1:]]
        current = sum(values[:8]), values[3] + values[4]
        if previous is None:
            previous = current
            return False
        elapsed, idle = current[0] - previous[0], current[1] - previous[1]
        previous = current
        fraction = idle / elapsed if elapsed else 0
        with (device.artifacts / "boot-load.jsonl").open("a") as log:
            log.write(json.dumps({"idleFraction": fraction, "load": device.text("shell", "cat", "/proc/loadavg")}) + "\n")
        calm_samples = calm_samples + 1 if fraction >= 0.4 else 0
        return calm_samples >= 3

    eventually("The Android system first-run work did not settle", calm, timeout=120)


def prepare_browser(device):
    # Fresh Google APIs images unpack Chrome after boot_completed. Its stub package can exist
    # before the actual launcher activity; wait for PackageManager's authoritative resolution.
    def installed_browser():
        activity = device.text("shell", "cmd", "package", "resolve-activity", "--components",
                               "-a", "android.intent.action.MAIN", "-c", "android.intent.category.LAUNCHER", CHROME)
        return activity if activity.startswith(CHROME + "/") else None

    browser_activity = eventually("The system Chrome package has no launcher activity", installed_browser, timeout=90)
    device.text("shell", "cmd", "role", "add-role-holder", "android.app.role.BROWSER", CHROME)
    device.text("shell", "am", "start", "-W", "-a", "android.intent.action.MAIN",
                "-c", "android.intent.category.LAUNCHER", "-n", browser_activity)
    allowed = ("Use without an account", "Continue without an account", "Accept & continue", "No thanks",
               "Not now", "Got it", "com.android.chrome:id/signin_fre_dismiss_button",
               "com.android.chrome:id/terms_accept", "com.android.chrome:id/negative_button")

    def ready():
        nodes = list(device.tree().iter("node"))
        toolbar = ("com.android.chrome:id/url_bar", "com.android.chrome:id/location_bar",
                   "com.android.chrome:id/search_box_text")
        if any(node.get("package") == CHROME and node.get("resource-id") in toolbar for node in nodes):
            return True
        for node in nodes:
            if node.get("package") == CHROME and node.get("enabled") == "true" and bounds(node) \
                    and any(has_label(node, label) for label in allowed):
                device.tap_node(node)
                break
        return False

    eventually("The real Chrome first-run UI did not finish", ready, timeout=90)
    device.capture("chrome-ready")


class ConsentFlow:
    def __init__(self, device, control):
        self.device = device
        self.control = control

    def state(self):
        with urllib.request.urlopen(self.control + "/state", timeout=5) as response:
            return json.load(response)

    def instruct(self, action, request_id=None):
        data = {"action": action}
        if request_id is not None:
            data["id"] = request_id
        request = urllib.request.Request(self.control + "/control", data=json.dumps(data).encode(),
                                         headers={"Content-Type": "application/json"}, method="POST")
        with urllib.request.urlopen(request, timeout=5) as response:
            assert response.status == 200

    def response(self, request_id, action):
        replies = eventually("No matching ACP reply for " + request_id,
            lambda: [item for item in self.state()["responses"] if item.get("id") == request_id])
        assert len(replies) == 1 and replies[0].get("result", {}).get("action") == action, replies
        if request_id.startswith("native-url-") or action != "accept":
            assert "content" not in replies[0]["result"], "Consent reply included unrelated form content"
        return replies[0]["result"]

    def url_card(self, request):
        self.device.wait(request)
        self.device.wait(self.state()["url"])
        self.device.wait("127.0.0.1")

    def no_navigation(self):
        assert self.state()["visits"] == [], "The app opened a URL before explicit consent"

    def activate_conversation(self):
        self.device.activate_product()
        eventually("The installed app is not foreground", lambda: self.device.foreground(PACKAGE))
        session_labels = (SESSION, "Native acceptance session")
        project_labels = (PROJECT, "Native acceptance")
        session = self.device.find(session_labels, enabled=True)
        if session is None:
            project = self.device.find(project_labels, enabled=True)
            if project is None:
                self.device.tap(("TitleBar.ToggleSidebar", "Toggle sidebar"))
                self.device.wait(project_labels)
            if self.device.find(session_labels, enabled=True) is None:
                self.device.tap(project_labels)
        self.device.tap(session_labels)
        eventually("The product did not load the authoritative session", lambda: self.state()["loaded"], timeout=45)
        capabilities = self.state()["capabilities"].get("elicitation", {})
        assert "form" in capabilities and "url" in capabilities, "The installed app did not advertise its UI capabilities"
        self.device.capture("session-loaded")

    def run(self):
        self.activate_conversation()
        for action, button, reply in (("url-decline", "Decline", "decline"), ("url-cancel", "Cancel", "cancel")):
            self.instruct(action)
            self.url_card("native-" + action)
            self.no_navigation()
            self.device.capture(action)
            self.device.tap(button)
            self.response("native-" + action, reply)
            self.no_navigation()

        self.instruct("url-open")
        self.url_card("native-url-open")
        self.no_navigation()
        self.device.tap("Open in browser")
        self.response("native-url-open", "accept")
        eventually("Consent did not open the system browser", lambda: self.device.foreground(CHROME))
        eventually("The actual external page did not report isolation", lambda: len(self.state()["reports"]) == 1)
        self.device.capture("external-browser")
        self.device.activate_product()
        self.device.tap("Open again")
        eventually("The second explicit open did not reach the external page", lambda: len(self.state()["reports"]) == 2)
        self.device.activate_product()
        self.device.wait("Open again")
        self.response("native-url-open", "accept")
        self.instruct("complete", "unknown-id")
        self.device.wait("Open again")
        self.instruct("complete")
        eventually("Completion left the URL card active", lambda: self.device.find("Open again") is None)
        self.instruct("complete")

        self.instruct("form-accept")
        self.device.wait("native-form-accept")
        field = eventually("The accessible form input is absent", lambda:
            self.device.find("Acceptance answer", enabled=True, editable=True))
        self.device.tap_node(field)
        self.device.text("shell", "input", "text", "native-form-answer")
        self.device.text("shell", "input", "keyevent", "KEYCODE_TAB")
        self.device.capture("form-answer")
        self.device.tap("Submit")
        assert self.response("native-form-accept", "accept").get("content") == {"answer": "native-form-answer"}
        for action, button, reply in (("form-decline", "Decline", "decline"), ("form-cancel", "Cancel", "cancel")):
            self.instruct(action)
            self.device.wait("native-" + action)
            self.device.tap(button)
            self.response("native-" + action, reply)

        self.instruct("url-expire")
        self.url_card("native-url-expire")
        self.instruct("disconnect")
        eventually("Disconnect retained the private URL", lambda: self.device.find(self.state()["url"]) is None)
        self.device.capture("connection-expired")
        final = self.state()
        assert len(final["responses"]) == 6, "Duplicate or unscoped response"
        assert len(final["visits"]) == 2 and len(final["reports"]) == 2
        assert all("Chrome/" in visit["userAgent"] and not visit["referrer"] for visit in final["visits"])
        assert all(report["openerNull"] and not report["referrer"]
                   and report["privateValue"] == "mobile-page-private-canary" for report in final["reports"])
        wire = json.dumps(final["responses"])
        assert "mobile-page-private-canary" not in wire and final["url"] not in wire
        return final


def assert_private_data_absent(device, url):
    device.text("shell", "am", "force-stop", PACKAGE)
    needles = [url.encode(), b"mobile-page-private-canary"]
    with tempfile.TemporaryFile() as archive:
        device.adb("exec-out", "run-as", PACKAGE, "tar", "-cf", "-", "files/SalmonEgg", stdout=archive)
        archive.seek(0)
        with tarfile.open(fileobj=archive) as tar:
            for member in tar:
                if member.isfile():
                    content = tar.extractfile(member).read()
                    assert not any(needle in content for needle in needles), \
                        "External page data reached product persistence: " + member.name


def main(args):
    repo, artifacts = args.repo.resolve(), args.artifacts.resolve()
    artifacts.mkdir(parents=True, exist_ok=True)
    candidates = [args.apk.resolve()] if args.apk else list((repo / "SalmonEgg/SalmonEgg/bin/Debug/net10.0-android36.0")
                                                            .rglob("*-Signed.apk"))
    assert len(candidates) == 1 and candidates[0].is_file(), "Exactly one current signed Debug APK is required"
    apk = candidates[0]
    devices = [line.split()[0] for line in output(["adb", "devices"]).splitlines()[1:] if line.endswith("\tdevice")]
    assert devices == [args.serial] and args.serial.startswith("emulator-"), "Use one dedicated emulator, never a personal device"
    device = AndroidDevice(args.serial, artifacts)
    assert device.text("shell", "getprop", "ro.kernel.qemu") == "1", "The target is not an emulator"
    peer = None
    reverse = None
    try:
        wait_for_boot_work(device)
        prepare_browser(device)
        device.adb("install", "--no-streaming", "-r", "-g", str(apk), timeout=180)
        device.text("shell", "am", "force-stop", PACKAGE)
        device.text("shell", "pm", "clear", PACKAGE)
        device.text("shell", "pm", "grant", PACKAGE, "android.permission.POST_NOTIFICATIONS")
        device.activity = device.text("shell", "cmd", "package", "resolve-activity", "--components",
                                      "-a", "android.intent.action.MAIN", "-c", "android.intent.category.LAUNCHER", PACKAGE)
        assert device.activity.startswith(PACKAGE + "/"), "The installed APK has no launcher activity"
        installed = device.text("shell", "pm", "path", PACKAGE).removeprefix("package:")
        assert "\n" not in installed, "Unexpected split installation"
        with apk.open("rb") as package_file:
            apk_hash = hashlib.file_digest(package_file, "sha256").hexdigest()
        installed_hash = device.text("shell", "sha256sum", installed).split()[0]
        assert installed_hash == apk_hash, "The installed APK differs from this build"
        provenance = {"head": output(["git", "-C", str(repo), "rev-parse", "HEAD"]), "apk": str(apk),
            "apkSha256": apk_hash, "installedApkSha256": installed_hash, "package": PACKAGE,
            "activity": device.activity, "serial": args.serial, "android": device.text("shell", "getprop", "ro.build.fingerprint"),
            "abi": device.text("shell", "getprop", "ro.product.cpu.abi"),
            "chrome": device.text("shell", "dumpsys", "package", CHROME),
            "configuration": "Debug with assemblies embedded; production startup and native views",
            "peer": "bounded deterministic ACP fixture; independent Agent acceptance is separate"}
        (artifacts / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
        with socket.socket() as reservation:
            reservation.bind(("127.0.0.1", 0))
            port = reservation.getsockname()[1]
        ready = artifacts / "peer-ready.json"
        ready.unlink(missing_ok=True)
        with (artifacts / "peer.log").open("w") as log:
            peer = subprocess.Popen([sys.executable, str(repo / "scripts/gates/fixtures/mobile-elicitation-peer.py"),
                "--port", str(port), "--ready", str(ready), "--artifacts", str(artifacts)],
                stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        eventually("The mobile ACP fixture did not start", lambda: ready.exists() if peer.poll() is None else False, timeout=15)
        endpoints = json.loads(ready.read_text())
        reverse = "tcp:" + str(port)
        device.text("reverse", reverse, reverse)
        seed_product(device, endpoints["endpoint"])
        final = ConsentFlow(device, endpoints["control"]).run()
        assert_private_data_absent(device, final["url"])
        (artifacts / "acceptance.json").write_text(json.dumps({"installedProduct": True, "nativeInput": True,
            "systemChrome": True, "browserVisits": 2, "urlConsentResponses": 3, "formResponses": 3,
            "noExternalDataPersistence": True, "connectionExpiry": True}, indent=2) + "\n")
        print("ANDROID_ELICITATION_ACCEPTANCE_PASS installed_apk=true native_input=true system_chrome=true browser_visits=2")
    finally:
        if peer is not None:
            if peer.poll() is None:
                os.killpg(peer.pid, signal.SIGTERM)
            try:
                peer.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(peer.pid, signal.SIGKILL)
                peer.wait(timeout=5)
        cleanup = [("shell", "am", "force-stop", PACKAGE), ("shell", "am", "force-stop", CHROME)]
        if reverse is not None:
            cleanup.append(("reverse", "--remove", reverse))
        try:
            with (artifacts / "final-screen.png").open("wb") as image:
                device.adb("exec-out", "screencap", "-p", stdout=image, check=False, timeout=20)
            with (artifacts / "logcat.log").open("wb") as log:
                device.adb("logcat", "-d", stdout=log, check=False, timeout=20)
        except (OSError, subprocess.TimeoutExpired) as error:
            print("Log collection failed:", error, file=sys.stderr)
        for arguments in cleanup:
            try:
                device.adb(*arguments, capture_output=True, check=False, timeout=20)
            except (OSError, subprocess.TimeoutExpired) as error:
                print("Device cleanup failed:", error, file=sys.stderr)
        try:
            with (artifacts / "product-files.tar").open("wb") as archive:
                device.adb("exec-out", "run-as", PACKAGE, "tar", "-cf", "-", "files/SalmonEgg",
                           stdout=archive, stderr=subprocess.DEVNULL, check=False, timeout=20)
        except (OSError, subprocess.TimeoutExpired) as error:
            print("Product file collection failed:", error, file=sys.stderr)


if __name__ == "__main__":
    def terminate(_signal, _frame):
        raise SystemExit("Android acceptance terminated; cleaning owned processes")

    signal.signal(signal.SIGTERM, terminate)
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", type=Path, default=Path.cwd())
    parser.add_argument("--apk", type=Path)
    parser.add_argument("--serial", default="emulator-5554")
    parser.add_argument("--artifacts", type=Path, required=True)
    main(parser.parse_args())
