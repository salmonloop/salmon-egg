#!/usr/bin/env python3
"""Prove URL consent on actual Windows MSIX packages, then restore and retest the full product."""

import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys


def main():
    if sys.platform != "win32":
        raise SystemExit("The reverse gate requires the actual Windows MSIX toolchain and desktop.")
    root = Path(__file__).resolve().parents[2]
    output = root / "artifacts/windows-elicitation-reverse"
    output.mkdir(parents=True, exist_ok=True)
    source = root / "src/SalmonEgg.Presentation.Core/ViewModels/Chat/Elicitation/ElicitationInteractionViewModels.cs"
    original = source.read_bytes()
    anchor = b"        _requestState.Changed += OnRequestStateChanged;"
    assert original.count(anchor) == 1
    mutant = original.replace(anchor,
        b"        if (_urlTarget is not null) _ = _uriLauncher.OpenAsync(_urlTarget, CancellationToken.None);\n" + anchor)
    package = ["pwsh", "-NoProfile", "-File", ".tools/run-winui3-msix.ps1", "-Configuration", "Debug"]

    def run(command, name, timeout, environment=None):
        with (output / name).open("w") as log:
            process = subprocess.Popen(command, cwd=root, env=environment, stdout=log, stderr=subprocess.STDOUT,
                creationflags=subprocess.CREATE_NEW_PROCESS_GROUP)
            try:
                return process.wait(timeout=timeout)
            finally:
                if process.poll() is None:
                    process.send_signal(signal.CTRL_BREAK_EVENT)
                    try: process.wait(timeout=15)
                    except subprocess.TimeoutExpired:
                        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                            stdout=log, stderr=subprocess.STDOUT, timeout=10, check=False)
                        process.wait(timeout=5)

    def record_install(name):
        marker = root / "artifacts/msix/current-install.json"
        (output / name).write_bytes(marker.read_bytes())

    try:
        source.write_bytes(mutant)
        assert run(package, "mutant-package.log", 1500) == 0, "The real mutant MSIX did not build/install."
        record_install("mutant-install.json")
        red_output = output / "red"
        red_output.mkdir(exist_ok=False)
        env = dict(os.environ, SALMONEGG_GUI="1", SALMONEGG_GUI_ACCEPTANCE_ARTIFACTS=str(red_output),
            SALMONEGG_APPDATA_ROOT=str(red_output / "appdata"), SALMONEGG_GUI_PYTHON=sys.executable)
        test = ["dotnet", "test", "--project", "tests/SalmonEgg.GuiTests.Windows/SalmonEgg.GuiTests.Windows.csproj",
            "--configuration", "Debug", "--no-build", "--no-ansi", "--filter-class",
            "SalmonEgg.GuiTests.Windows.UrlElicitationSmokeTests", "--minimum-expected-tests", "1", "--timeout", "3m",
            "--output", "Detailed"]
        red_exit = run(test, "red.log", 210, env)
        assert red_exit != 0 and "The product opened a URL without consent." in (output / "red.log").read_text(), \
            "The current installed mutant was not rejected for unconsented URL navigation."
    finally:
        source.write_bytes(original)
        assert source.read_bytes() == original
        assert run(package, "restored-package.log", 1500) == 0, "The restored MSIX did not rebuild/install."
        record_install("restored-install.json")
    assert run(["pwsh", "-NoProfile", "-File", "scripts/gates/run-windows-packaged-gui-acceptance.ps1",
        "-ArtifactsDirectory", str(output / "restored")], "restored.log", 550) == 0, "The full restored installed-product gate failed."
    record = {"passed": True, "head": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip(),
        "originalSha256": hashlib.sha256(original).hexdigest(), "mutantSha256": hashlib.sha256(mutant).hexdigest(),
        "restoredSha256": hashlib.sha256(source.read_bytes()).hexdigest(), "redExit": red_exit,
        "restoredCompleteProductGate": True}
    (output / "result.json").write_text(json.dumps(record, indent=2) + "\n")
    print(json.dumps(record))


if __name__ == "__main__":
    def interrupt(_signum, _frame):
        raise KeyboardInterrupt("Windows installed-product reverse gate interrupted")
    signal.signal(signal.SIGTERM, interrupt)
    if hasattr(signal, "SIGBREAK"):
        signal.signal(signal.SIGBREAK, interrupt)
    main()
