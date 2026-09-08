#!/usr/bin/env python3
"""Run the release workflow's actual pkg step with only platform tools replaced.

The macOS job passes /bin/bash so its Bash 3.2 nounset behavior is part of this gate.
Real pkgbuild output is checked separately by run-macos-pkg-artifact-gate.sh.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import plistlib
import shutil
import subprocess
import tempfile
import unittest

import yaml


REPO_ROOT = Path(__file__).resolve().parents[2]
SIGNING_KEY = "Developer ID Installer: Salmon Egg (TEST)"
PACKAGE_BYTES = b"pkgbuild fixture\n"
TOOL_STUB = r'''#!/usr/bin/env python3
import json
import os
from pathlib import Path
import shutil
import sys

name = Path(sys.argv[0]).name
args = sys.argv[1:]
with open(os.environ["PKG_TOOL_CALLS"], "a") as log:
    log.write(json.dumps({"tool": name, "args": args}) + "\n")
if name == "pkgbuild":
    Path(args[-1]).write_bytes(b"pkgbuild fixture\n")
elif name == "productsign":
    shutil.copyfile(args[-2], args[-1])
else:
    sys.exit("Unexpected platform tool: " + name)
'''


class MacosPkgBuildContract(unittest.TestCase):
    bash = "/bin/bash"

    @classmethod
    def setUpClass(cls):
        workflow = yaml.safe_load(
            (REPO_ROOT / ".github/workflows/release-packaging.yml").read_text()
        )
        steps = workflow["jobs"]["package-macos"]["steps"]
        matches = [step for step in steps if step.get("id") == "build-pkg"]
        if len(matches) != 1 or matches[0].get("shell") != "bash":
            raise AssertionError("Expected one Bash build-pkg step in the release workflow")
        cls.workflow_step = matches[0]["run"]
        cls.bash = str(Path(shutil.which(cls.bash) or cls.bash).resolve(strict=True))

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="salmonegg-pkg-contract-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve() / "workspace with spaces"
        scripts = self.root / "scripts/release"
        scripts.mkdir(parents=True)
        for name in ("build-macos-pkg.sh", "macos-pkg-postinstall.sh"):
            shutil.copy2(REPO_ROOT / "scripts/release" / name, scripts / name)

        self.tools = self.root / "tools"
        self.tools.mkdir()
        (self.tools / "bash").symlink_to(self.bash)
        for name in ("pkgbuild", "productsign"):
            tool = self.tools / name
            tool.write_text(TOOL_STUB)
            tool.chmod(0o755)
        dotnet = self.tools / "dotnet"
        dotnet.write_text("#!/bin/sh\nprintf '%s\\n' '1.5.0'\n")
        dotnet.chmod(0o755)

        self.calls = self.root / "tool-calls.jsonl"
        self.output = self.root / "github-output"
        self.env = os.environ.copy()
        self.env.update(
            PATH=str(self.tools) + os.pathsep + self.env["PATH"],
            DOTNET_BIN=str(dotnet),
            PKG_TOOL_CALLS=str(self.calls),
            GITHUB_OUTPUT=str(self.output),
        )
        self.env.pop("MACOS_PKG_CODESIGN_KEY", None)
        self.app = self.root / "publish/macos-bundle/SalmonEgg.app"
        (self.app / "Contents/MacOS").mkdir(parents=True)
        (self.app / "Contents/MacOS/SalmonEgg").write_bytes(b"apphost fixture\n")
        self.write_plist()

    def write_plist(self):
        with (self.app / "Contents/Info.plist").open("wb") as handle:
            plistlib.dump(
                {"CFBundleIdentifier": "com.example.salmonegg", "CFBundleExecutable": "SalmonEgg"},
                handle,
                fmt=plistlib.FMT_BINARY,
            )

    def add_command(self, area, executable=True):
        command = self.app / "Contents" / area / "cli/salmon-egg"
        command.parent.mkdir(parents=True, exist_ok=True)
        command.write_text("#!/bin/sh\nprintf '%s\\n' '1.5.0'\n")
        command.chmod(0o755 if executable else 0o644)
        return command

    def run_step(self):
        return subprocess.run(
            [self.bash, "--noprofile", "--norc", "-e", "-o", "pipefail", "-c", self.workflow_step],
            cwd=self.root,
            env=self.env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=30,
            check=False,
        )

    def tool_calls(self):
        return [json.loads(line) for line in self.calls.read_text().splitlines()] if self.calls.exists() else []

    def assert_package(self, area, signed=False):
        result = self.run_step()
        self.assertEqual(0, result.returncode, result.stdout)
        self.assertIn(f"[macos-pkg] command:    Contents/{area}/cli/salmon-egg", result.stdout)
        package = self.root / "artifacts/macos/SalmonEgg-1.5.0.pkg"
        self.assertEqual(PACKAGE_BYTES, package.read_bytes())
        checksum = package.with_suffix(".pkg.sha256").read_text().strip().split()
        self.assertEqual([hashlib.sha256(PACKAGE_BYTES).hexdigest(), package.name], checksum)
        self.assertIn(f"pkg-path={package}\n", self.output.read_text())
        self.assertIn("display-version=1.5.0\n", self.output.read_text())

        calls = self.tool_calls()
        self.assertEqual(["pkgbuild", "productsign"] if signed else ["pkgbuild"], [c["tool"] for c in calls])
        args = calls[0]["args"]
        staged_root = Path(args[args.index("--root") + 1])
        staged_command = staged_root / "Applications/SalmonEgg.app/Contents" / area / "cli/salmon-egg"
        self.assertEqual((self.app / "Contents" / area / "cli/salmon-egg").read_bytes(), staged_command.read_bytes())
        self.assertTrue(os.access(staged_command, os.X_OK))
        postinstall = Path(args[args.index("--scripts") + 1]) / "postinstall"
        self.assertEqual((REPO_ROOT / "scripts/release/macos-pkg-postinstall.sh").read_bytes(), postinstall.read_bytes())
        self.assertTrue(os.access(postinstall, os.X_OK))
        self.assertEqual("com.example.salmonegg", args[args.index("--identifier") + 1])
        self.assertEqual("1.5.0", args[args.index("--version") + 1])
        if signed:
            self.assertEqual(["--sign", SIGNING_KEY], calls[1]["args"][:2])

    def test_unsigned_macos_command(self):
        self.add_command("MacOS")
        self.assert_package("MacOS")

    def test_unsigned_resources_command(self):
        self.add_command("Resources")
        self.assert_package("Resources")

    def test_empty_signing_key(self):
        self.env["MACOS_PKG_CODESIGN_KEY"] = ""
        self.add_command("Resources")
        self.assert_package("Resources")

    def test_signed_macos_command(self):
        self.env["MACOS_PKG_CODESIGN_KEY"] = SIGNING_KEY
        self.add_command("MacOS")
        self.assert_package("MacOS", signed=True)

    def test_signed_resources_command(self):
        self.env["MACOS_PKG_CODESIGN_KEY"] = SIGNING_KEY
        self.add_command("Resources")
        self.assert_package("Resources", signed=True)

    def test_macos_command_preferred_when_both_exist(self):
        self.add_command("MacOS")
        self.add_command("Resources")
        self.assert_package("MacOS")

    def test_nonexecutable_candidate_does_not_hide_executable_command(self):
        self.add_command("MacOS", executable=False)
        self.add_command("Resources")
        self.assert_package("Resources")

    def test_missing_command_refused_before_packaging(self):
        result = self.run_step()
        self.assertNotEqual(0, result.returncode, result.stdout)
        self.assertIn("no executable bundled CLI", result.stdout)
        self.assertEqual([], self.tool_calls())

    def test_nonexecutable_command_refused_before_packaging(self):
        self.add_command("Resources", executable=False)
        result = self.run_step()
        self.assertNotEqual(0, result.returncode, result.stdout)
        self.assertIn("no executable bundled CLI", result.stdout)
        self.assertEqual([], self.tool_calls())

    def test_apostrophe_in_bundle_path(self):
        original_root = self.root
        self.root = self.root.rename(self.root.with_name("owner's workspace with spaces"))
        self.app = self.root / self.app.relative_to(original_root)
        self.calls = self.root / self.calls.relative_to(original_root)
        self.output = self.root / self.output.relative_to(original_root)
        for key in ("PATH", "DOTNET_BIN", "PKG_TOOL_CALLS", "GITHUB_OUTPUT"):
            self.env[key] = self.env[key].replace(str(original_root), str(self.root))
        self.add_command("MacOS")
        self.assert_package("MacOS")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bash", default="/bin/bash", help="Bash used for the workflow and builder")
    args = parser.parse_args()
    MacosPkgBuildContract.bash = args.bash
    unittest.main(argv=[__file__], verbosity=2)
