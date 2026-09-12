#!/usr/bin/env python3
"""Install the current .app on an owned Simulator and run native XCUITest consent flows."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import plistlib
import signal
import socket
import subprocess
import sys
import time
import urllib.request


def run(command, **kwargs):
    print("[ios-gate] " + " ".join(command), flush=True)
    return subprocess.run(command, check=True, text=True, timeout=kwargs.pop("timeout", 180), **kwargs)


def output(command):
    return subprocess.check_output(command, text=True, timeout=90).strip()


def seed(container, endpoint):
    root = container / "Library/Application Support/SalmonEgg"
    (root / "config/servers").mkdir(parents=True)
    (root / "conversations").mkdir()
    (root / "config/app.yaml").write_text("schema_version: 1\ntheme: Light\nlanguage: en\n"
        "last_selected_server_id: native-elicitation-profile\n"
        "last_selected_project_id: remote-directory:native-elicitation-directory\n"
        "agent_remote_directories:\n  - directory_id: native-elicitation-directory\n"
        "    display_name: Native acceptance\n    remote_path: /acceptance\n"
        "navigation_remote_directory_ids:\n  - native-elicitation-directory\n")
    (root / "config/servers/native-elicitation-profile.yaml").write_text("schema_version: 5\n"
        "id: native-elicitation-profile\nname: Native Elicitation Fixture\ntransport: websocket\n"
        f"server_url: {endpoint}\nconnection_timeout_seconds: 30\n"
        "authentication:\n  mode: none\nproxy:\n  mode: none\n")
    conversation = {"conversationId": "native-elicitation-conversation", "displayName": "Native acceptance session",
        "createdAt": "2026-09-12T00:00:00Z", "lastUpdatedAt": "2026-09-12T00:00:00Z",
        "cwd": "/acceptance", "projectId": "remote-directory:native-elicitation-directory",
        "boundProfileId": "native-elicitation-profile", "remoteSessionId": "native-elicitation-session", "messages": []}
    (root / "conversations/conversations.v1.json").write_text(json.dumps(
        {"version": 1, "lastActiveConversationId": None, "conversations": [conversation]}))
    return root


def main(args):
    repo, artifacts = args.repo.resolve(), args.artifacts.resolve()
    artifacts.mkdir(parents=True, exist_ok=True)
    app = args.app.resolve()
    info = plistlib.loads((app / "Info.plist").read_bytes())
    bundle_id = info["CFBundleIdentifier"]
    assert bundle_id == "com.companyname.salmonegg", "Unexpected product package"
    binary = app / info["CFBundleExecutable"]
    assert binary.is_file(), "The current build has no app binary"
    simulator = None
    peer = None
    installed = False
    try:
        listing = json.loads(output(["xcrun", "simctl", "list", "--json"]))
        sdk_version = output(["xcrun", "--sdk", "iphonesimulator", "--show-sdk-version"])
        runtime = next(item["identifier"] for item in listing["runtimes"]
                       if item.get("isAvailable") and item["identifier"].startswith("com.apple.CoreSimulator.SimRuntime.iOS-")
                       and item["version"] == sdk_version)
        device_type = next(item["identifier"] for item in listing["devicetypes"] if "iPad-Pro-13-inch" in item["identifier"])
        simulator = output(["xcrun", "simctl", "create", "SalmonEgg ACP acceptance", device_type, runtime])
        (artifacts / "simulator.json").write_text(json.dumps({"id": simulator, "runtime": runtime,
                                                            "deviceType": device_type}, indent=2) + "\n")
        # bootstatus owns startup; opening Simulator first can already boot this device.
        with (artifacts / "simulator-boot.log").open("w") as boot_log:
            run(["xcrun", "simctl", "bootstatus", simulator, "-b"], timeout=300,
                stdout=boot_log, stderr=subprocess.STDOUT)
        run(["open", "-a", "Simulator", "--args", "-CurrentDeviceUDID", simulator])
        run(["xcrun", "simctl", "install", simulator, str(app)])
        installed = True
        container = Path(output(["xcrun", "simctl", "get_app_container", simulator, bundle_id, "data"]))
        with socket.socket() as reservation:
            reservation.bind(("127.0.0.1", 0))
            port = reservation.getsockname()[1]
        ready = artifacts / "peer-ready.json"
        with (artifacts / "peer.log").open("w") as peer_log:
            peer = subprocess.Popen([sys.executable, str(repo / "scripts/gates/fixtures/mobile-elicitation-peer.py"),
                "--port", str(port), "--ready", str(ready), "--artifacts", str(artifacts)],
                stdout=peer_log, stderr=subprocess.STDOUT, start_new_session=True)
        deadline = time.monotonic() + 15
        while not ready.exists():
            assert peer.poll() is None, "The mobile fixture failed to start"
            if time.monotonic() >= deadline:
                raise TimeoutError("The mobile fixture did not become ready")
            time.sleep(0.05)
        endpoints = json.loads(ready.read_text())
        root = seed(container, endpoints["endpoint"])
        provenance = {"head": output(["git", "-C", str(repo), "rev-parse", "HEAD"]),
                      "app": str(app), "bundleId": bundle_id, "version": info["CFBundleVersion"],
                      "binarySha256": hashlib.sha256(binary.read_bytes()).hexdigest(),
                      "simulator": simulator, "runtime": runtime, "deviceType": device_type,
                      "xcode": output(["xcodebuild", "-version"]), "peer": "bounded deterministic ACP fixture"}
        (artifacts / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
        project = repo / "tests/SalmonEgg.iOS.UiTests"
        run([args.xcodegen, "generate", "--spec", str(project / "project.yml")], cwd=project)
        env = dict(os.environ, SALMONEGG_IOS_CONTROL_URL=endpoints["control"])
        with (artifacts / "xcuitest.log").open("w") as log:
            result = subprocess.run(["xcodebuild", "test", "-project", str(project / "SalmonEggNativeAcceptance.xcodeproj"),
                "-scheme", "SalmonEggNativeAcceptance", "-destination", "platform=iOS Simulator,id=" + simulator,
                "-derivedDataPath", str(artifacts / "test-build"), "-resultBundlePath", str(artifacts / "results.xcresult"),
                "-parallel-testing-enabled", "NO", "CODE_SIGNING_ALLOWED=NO", "SALMONEGG_IOS_CONTROL_URL=" + endpoints["control"]],
                env=env, stdout=log, stderr=subprocess.STDOUT, timeout=420)
        if result.returncode:
            print((artifacts / "xcuitest.log").read_text(errors="replace")[-18000:])
        assert result.returncode == 0, "The installed product XCUITest failed"
        log_text = (artifacts / "xcuitest.log").read_text()
        assert "IOS_ELICITATION_ACCEPTANCE_PASS" in log_text, "No completed native acceptance marker"
        state = json.loads(urllib.request.urlopen(endpoints["control"] + "/state", timeout=5).read())
        assert len(state["visits"]) == 2 and len(state["reports"]) == 2
        assert all("Safari" in visit["userAgent"] and not visit["referrer"] for visit in state["visits"])
        assert all(item["openerNull"] and not item["referrer"] for item in state["reports"])
        wire = json.dumps(state["responses"])
        assert "mobile-page-private-canary" not in wire and state["url"] not in wire
        needles = ["mobile-page-private-canary".encode(), state["url"].encode()]
        for path in root.rglob("*"):
            if path.is_file():
                data = path.read_bytes()
                assert not any(needle in data for needle in needles), "External page data reached product persistence"
        (artifacts / "acceptance.json").write_text(json.dumps({"nativeInput": True, "installedProduct": True,
            "systemSafari": True, "browserVisits": 2, "acceptWithoutContent": True,
            "formAnswer": True, "noExternalDataPersistence": True}, indent=2) + "\n")
        print("iOS installed product: consent, form, Safari isolation and completion passed")
    finally:
        if simulator:
            commands = [["xcrun", "simctl", "terminate", simulator, bundle_id]] if installed else []
            commands += [["xcrun", "simctl", "shutdown", simulator], ["xcrun", "simctl", "delete", simulator]]
            for command in commands:
                try:
                    result = subprocess.run(command, capture_output=True, text=True, timeout=45)
                    print("[ios-gate] cleanup", command[2], result.returncode, flush=True)
                except subprocess.TimeoutExpired:
                    print("[ios-gate] cleanup timed out:", command[2], flush=True)
        if peer is not None:
            if peer.poll() is None:
                os.killpg(peer.pid, signal.SIGTERM)
            try:
                peer.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(peer.pid, signal.SIGKILL)
                peer.wait(timeout=5)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", type=Path, default=Path.cwd())
    parser.add_argument("--app", type=Path, required=True)
    parser.add_argument("--artifacts", type=Path, required=True)
    parser.add_argument("--xcodegen", required=True)
    main(parser.parse_args())
