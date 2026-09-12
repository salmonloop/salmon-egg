#!/usr/bin/env python3
"""Verify an actual unconsented native browser launch fails, then rebuild and retest the original APK."""

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
            # The child gate owns an independent peer session. Let its finally finish first.
            try:
                os.killpg(process.pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
            try:
                process.wait(timeout=30)
            except subprocess.TimeoutExpired:
                pass
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait(timeout=5)


def main():
    root = Path(__file__).resolve().parents[2]
    output = root / "artifacts/android-acp-acceptance/reverse"
    output.mkdir(parents=True, exist_ok=False)
    source = root / "src/SalmonEgg.Presentation.Core/ViewModels/Chat/Elicitation/ElicitationInteractionViewModels.cs"
    original = source.read_bytes()
    anchor = b"        _requestState.Changed += OnRequestStateChanged;"
    assert original.count(anchor) == 1, "The consent mutation anchor changed"
    mutant = original.replace(anchor,
        b"        if (_urlTarget is not null) _ = _uriLauncher.OpenAsync(_urlTarget, CancellationToken.None);\n" + anchor)
    sdk = os.environ["ANDROID_SDK_ROOT"]
    build = ["dotnet", "build", "SalmonEgg/SalmonEgg/SalmonEgg.csproj", "--configuration", "Debug",
        "--framework", "net10.0-android36.0", "--runtime", "android-x64", "--no-restore",
        "-t:SignAndroidPackage", "-p:AndroidPackageFormats=apk", "-p:EmbedAssembliesIntoApk=true",
        "-p:SalmonEggTargetFrameworks=net10.0-android36.0", "-p:SalmonEggSupportsDesktopProcessHost=false",
        "-p:AndroidSdkDirectory=" + sdk, "-p:UseSharedCompilation=false", "-m:1", "--disable-build-servers"]
    gate = [sys.executable, "scripts/gates/run-android-elicitation-gate.py", "--artifacts"]
    red_exit = None
    try:
        source.write_bytes(mutant)
        assert run(build, root, output / "mutant-build.log", 600) == 0, "The mutated APK did not build"
        red_exit = run([*gate, str(output / "red")], root, output / "red.log", 300)
        assert red_exit != 0 and "The product opened a URL without consent." in (output / "red.log").read_text(), \
            "The installed mutant was not rejected for its actual unconsented browser visit"
        red = json.loads((output / "red/peer-state.json").read_text())
        assert len(red["visits"]) > 0 and not red["responses"], "The failed guard did not observe real unconsented navigation"
    finally:
        source.write_bytes(original)
        assert source.read_bytes() == original
        assert run(build, root, output / "restored-build.log", 600) == 0, "The restored APK did not rebuild"
    assert run([*gate, str(output / "restored")], root, output / "restored.log", 600) == 0
    original_provenance = json.loads((output.parent / "provenance.json").read_text())
    red_provenance = json.loads((output / "red/provenance.json").read_text())
    restored_provenance = json.loads((output / "restored/provenance.json").read_text())
    assert red_provenance["apkSha256"] != original_provenance["apkSha256"], "The mutant reused the original APK"
    assert red_provenance["apkSha256"] != restored_provenance["apkSha256"], "The restored gate reused the mutant APK"
    (output / "result.json").write_text(json.dumps({
        "passed": True, "originalSourceSha256": hashlib.sha256(original).hexdigest(),
        "mutantSourceSha256": hashlib.sha256(mutant).hexdigest(),
        "restoredSourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(), "redExit": red_exit,
        "failure": "The product opened a URL without consent.", "restoredFullProductGate": True,
        "originalApkSha256": original_provenance["apkSha256"], "mutantApkSha256": red_provenance["apkSha256"],
        "restoredApkSha256": restored_provenance["apkSha256"],
        "head": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    }, indent=2) + "\n")
    print("Android installed-product reverse verification passed; original source and APK rebuilt and accepted")


if __name__ == "__main__":
    def interrupt(_signal, _frame):
        raise KeyboardInterrupt("Android reverse verification interrupted; restoring source and cleaning owned children")

    signal.signal(signal.SIGTERM, interrupt)
    main()
