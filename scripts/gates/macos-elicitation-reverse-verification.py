#!/usr/bin/env python3
"""Prove the native macOS consent gate rejects an actual unconsented browser launch."""

import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys


def main():
    if sys.platform != "darwin":
        raise SystemExit("macOS reverse verification requires the native macOS product.")
    root = Path(__file__).resolve().parents[2]
    output = root / "artifacts/macos-elicitation-reverse"
    output.mkdir(parents=True, exist_ok=True)
    source = root / "src/SalmonEgg.Presentation.Core/ViewModels/Chat/Elicitation/ElicitationInteractionViewModels.cs"
    original = source.read_bytes()
    anchor = b"        _requestState.Changed += OnRequestStateChanged;"
    assert original.count(anchor) == 1
    mutant = original.replace(anchor,
        b"        if (_urlTarget is not null) _ = _uriLauncher.OpenAsync(_urlTarget, CancellationToken.None);\n"
        + anchor)
    build = ["dotnet", "build", "SalmonEgg/SalmonEgg/SalmonEgg.csproj", "-c", "Debug", "-f", "net10.0-desktop",
        "-p:SalmonEggTargetFrameworks=net10.0-desktop", "-p:SalmonEggAllTargetFrameworks=net10.0-desktop",
        "-p:UseSharedCompilation=false", "-m:1", "--disable-build-servers"]
    gate = [sys.executable, "scripts/gates/macos-elicitation-consent-smoke.py", "--output"]

    def run(command, name, timeout):
        with (output / name).open("w") as log:
            process = subprocess.Popen(command, cwd=root, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            try:
                return process.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                # Give the native gate its signal handler/finally before enforcing the outer bound.
                os.killpg(process.pid, signal.SIGTERM)
                try: process.wait(timeout=30)
                except subprocess.TimeoutExpired: pass
                raise
            finally:
                try: os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError: pass
                process.wait(timeout=5)

    red_exit = None
    try:
        source.write_bytes(mutant)
        assert run(build, "mutant-build.log", 600) == 0, "Mutant did not build."
        red_exit = run([*gate, str(output / "red")], "red.log", 240)
        assert red_exit != 0 and "macOS opened URL before consent" in (output / "red.log").read_text(), \
            "The real product gate did not catch the unconsented browser launch."
    finally:
        source.write_bytes(original)
        assert source.read_bytes() == original
        assert run(build, "restored-build.log", 600) == 0, "Restored product did not rebuild."
    assert run([*gate, str(output / "restored")], "restored.log", 240) == 0
    record = {"passed": True, "source": str(source.relative_to(root)),
        "originalSha256": hashlib.sha256(original).hexdigest(), "mutantSha256": hashlib.sha256(mutant).hexdigest(),
        "restoredSha256": hashlib.sha256(source.read_bytes()).hexdigest(), "redExit": red_exit,
        "failure": "macOS opened URL before consent", "restoredGatePassed": True,
        "commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()}
    (output / "result.json").write_text(json.dumps(record, indent=2) + "\n")
    print(json.dumps(record))


if __name__ == "__main__":
    main()
