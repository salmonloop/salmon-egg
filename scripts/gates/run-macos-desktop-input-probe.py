#!/usr/bin/env python3
"""Build a Cocoa input probe with the selected Xcode and execute it without altering TCC."""

import argparse
import json
import os
from pathlib import Path
import signal
import subprocess
import sys


def main():
    if sys.platform != "darwin":
        raise SystemExit("The Cocoa input probe requires macOS.")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path("artifacts/macos-native-input"))
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    source = Path(__file__).with_name("macos-desktop-input-probe.swift")
    binary = output / "native-input-probe"
    result = output / "result.json"
    provenance = {
        "source": str(source),
        "commit": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(),
        "xcode": subprocess.check_output(["xcodebuild", "-version"], text=True).strip(),
        "developerDirectory": subprocess.check_output(["xcode-select", "-p"], text=True).strip(),
    }
    (output / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
    with (output / "build.log").open("w") as log:
        subprocess.run(["xcrun", "swiftc", str(source), "-framework", "AppKit", "-framework", "CoreGraphics", "-o", str(binary)],
                       stdout=log, stderr=subprocess.STDOUT, check=True, timeout=90)
    process = None
    try:
        with (output / "runtime.log").open("w") as log:
            process = subprocess.Popen([str(binary), str(result)], stdout=log, stderr=subprocess.STDOUT,
                                       start_new_session=True)
            code = process.wait(timeout=30)
        report = json.loads(result.read_text())
        print(json.dumps(report))
        if code != 0 or report.get("passed") is not True or report.get("nativeTextReceived") is not True:
            raise RuntimeError("The hosted Cocoa window did not receive native keyboard input; platform GUI remains unverified.")
    finally:
        if process is not None:
            try:
                os.killpg(process.pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait(timeout=3)


if __name__ == "__main__":
    signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(KeyboardInterrupt("Cocoa probe interrupted")))
    main()
