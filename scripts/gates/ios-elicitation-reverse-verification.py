#!/usr/bin/env python3
"""Rebuild and install an unconsented Safari launch, then restore and recheck the real app."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys


def run(command, root, log_path, timeout):
    with log_path.open("w") as log:
        process = subprocess.Popen(command, cwd=root, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return process.wait(timeout=timeout)
        finally:
            # The child gate handles SIGTERM by deleting its own Simulator and reaping its peer.
            try:
                os.killpg(process.pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
            try:
                process.wait(timeout=120)
            except subprocess.TimeoutExpired:
                pass
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait(timeout=5)


def main(args):
    root = Path(__file__).resolve().parents[2]
    output = root / "artifacts/ios-acp-acceptance/reverse"
    output.mkdir(parents=True, exist_ok=False)
    source = root / "src/SalmonEgg.Presentation.Core/ViewModels/Chat/Elicitation/ElicitationInteractionViewModels.cs"
    original = source.read_bytes()
    anchor = b"        _requestState.Changed += OnRequestStateChanged;"
    assert original.count(anchor) == 1, "The consent mutation anchor changed"
    mutant = original.replace(anchor,
        b"        if (_urlTarget is not null) _ = _uriLauncher.OpenAsync(_urlTarget, CancellationToken.None);\n" + anchor)
    build = ["dotnet", "build", "SalmonEgg/SalmonEgg/SalmonEgg.csproj", "--configuration", "Debug",
        "--framework", "net10.0-ios", "--runtime", "iossimulator-arm64", "--no-restore",
        "-p:SalmonEggTargetFrameworks=net10.0-ios", "-p:SalmonEggSupportsDesktopProcessHost=false",
        "-p:UseInterpreter=true", "-p:TrimMode=copy", "-p:UseSharedCompilation=false", "-m:1"]
    gate = [sys.executable, "scripts/gates/run-ios-elicitation-gate.py", "--app", str(args.app.resolve()),
            "--xcodegen", args.xcodegen, "--artifacts"]
    red_exit = None
    try:
        source.write_bytes(mutant)
        assert run(build, root, output / "mutant-build.log", 600) == 0, "The mutated app did not build"
        red_exit = run([*gate, str(output / "red")], root, output / "red.log", 900)
        assert red_exit != 0 and "The product opened a URL without consent." in (output / "red.log").read_text(), \
            "The installed mutant was not rejected for its actual unconsented Safari visit"
        red = json.loads((output / "red/peer-state.json").read_text())
        assert red["visits"] and not red["responses"], "No real unconsented navigation was observed"
    finally:
        source.write_bytes(original)
        assert source.read_bytes() == original
        assert run(build, root, output / "restored-build.log", 600) == 0, "The restored app did not rebuild"
    assert run([*gate, str(output / "restored")], root, output / "restored.log", 900) == 0
    baseline = json.loads((output.parent / "provenance.json").read_text())
    red = json.loads((output / "red/provenance.json").read_text())
    restored = json.loads((output / "restored/provenance.json").read_text())
    assert red["appSha256"] != baseline["appSha256"], "The mutant reused the original app"
    assert red["appSha256"] != restored["appSha256"], "The restored gate reused the mutant app"
    (output / "result.json").write_text(json.dumps({
        "passed": True, "originalSourceSha256": hashlib.sha256(original).hexdigest(),
        "mutantSourceSha256": hashlib.sha256(mutant).hexdigest(),
        "restoredSourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(), "redExit": red_exit,
        "failure": "The product opened a URL without consent.", "restoredFullProductGate": True,
        "originalAppSha256": baseline["appSha256"], "mutantAppSha256": red["appSha256"],
        "restoredAppSha256": restored["appSha256"],
        "head": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    }, indent=2) + "\n")
    print("iOS installed-product reverse verification passed; original source and app rebuilt and accepted")


if __name__ == "__main__":
    def interrupt(_signal, _frame):
        raise KeyboardInterrupt("iOS reverse verification interrupted; restoring source and cleaning owned children")

    signal.signal(signal.SIGTERM, interrupt)
    parser = argparse.ArgumentParser()
    parser.add_argument("--app", type=Path, required=True)
    parser.add_argument("--xcodegen", required=True)
    main(parser.parse_args())
