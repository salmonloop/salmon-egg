#!/usr/bin/env python3
"""Static contract: no Actions job may read a tracked file before a checkout provides it.

Driven by scripts/gates/run-workflow-workspace-contract-gate.sh, which resolves the interpreter and
PyYAML first. See that script's header for why this rule exists and why it is fail-closed.

The model is deliberately small. For each job, walk the steps in order while tracking which workspace
directories a checkout has populated. A step that reads a tracked path is a violation unless some
earlier step in the same job checked out a tree containing it. Two properties matter more than
breadth of coverage:

  * Order-sensitive. `Setup .NET` before `Checkout` is exactly the defect that shipped two empty
    releases, and both steps are individually well formed. Only their sequence is wrong.
  * Fail-closed on the unknown. A reference this analyzer cannot classify is an error, not a pass.
    The `global-json-file` defect survived review precisely because nothing was looking; a rule that
    shrugs at unfamiliar shapes recreates that blind spot.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
import tempfile
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath

import yaml


CHECKOUT_ACTION = "actions/checkout"

# Inputs of third-party actions whose value is a path resolved inside the workspace. Keyed by the
# action's owner/repo (the version pin is stripped before lookup) so a pin bump does not silently
# disable the rule -- which would be its own instance of the defect this gate exists to catch.
#
# Only inputs that are genuinely workspace-relative belong here. `actions/download-artifact`'s `path`,
# for instance, is an output directory the action creates, so it needs no checkout.
ACTION_PATH_INPUTS: dict[str, tuple[str, ...]] = {
    "actions/setup-dotnet": ("global-json-file",),
    "actions/setup-node": ("cache-dependency-path",),
    "actions/setup-python": ("requirements",),
    "actions/setup-java": ("cache-dependency-path",),
}

# Actions that are known not to read tracked files. Anything outside this set and outside
# ACTION_PATH_INPUTS is reported, so a newly introduced action must be classified deliberately.
KNOWN_WORKSPACE_FREE_ACTIONS: frozenset[str] = frozenset(
    {
        "actions/checkout",
        "actions/download-artifact",
        "actions/cache",
        "actions/cache/restore",
        "actions/cache/save",
        "actions/github-script",
        "github/codeql-action/init",
        "github/codeql-action/analyze",
        "github/codeql-action/autobuild",
        "nuget/login",
        "docker/setup-buildx-action",
        "docker/login-action",
    }
)

# `actions/upload-artifact` reads its `path` from the workspace, but that path routinely points at
# build output rather than tracked source, and build output is produced by a step this analyzer
# already validated. Treat it as workspace-free: flagging it would produce noise that trains readers
# to ignore the gate.
KNOWN_WORKSPACE_FREE_ACTIONS = KNOWN_WORKSPACE_FREE_ACTIONS | frozenset({"actions/upload-artifact"})


def action_id(uses: str) -> str:
    """Strips the version pin and lowercases, so `actions/checkout@abc123 # v5` -> `actions/checkout`."""
    head = uses.split("#", 1)[0].strip()
    return head.split("@", 1)[0].strip().lower()


@dataclass
class Violation:
    workflow: str
    job: str
    step: str
    detail: str

    def render(self) -> str:
        return f"{self.workflow} :: job '{self.job}' :: step '{self.step}': {self.detail}"


@dataclass
class CheckedOutTrees:
    """The workspace directories that checkouts have populated so far, in step order.

    A checkout without `path` populates the workspace root; with `path: repo` it populates `repo/`.
    `sparse-checkout` narrows *which* files land there, and the whole point of the fix under test is
    a sparse root checkout that provides exactly `global.json`. Modelling sparseness is therefore not
    optional: treating a sparse checkout as providing everything would let a future sparse pattern
    that omits the needed file pass.
    """

    roots: list[tuple[PurePosixPath, frozenset[str] | None]] = field(default_factory=list)

    def add(self, path: str | None, sparse: frozenset[str] | None) -> None:
        base = PurePosixPath(path) if path else PurePosixPath(".")
        self.roots.append((base, sparse))

    def any_checkout(self) -> bool:
        return bool(self.roots)

    def provides(self, reference: str) -> bool:
        ref = PurePosixPath(reference)
        for base, sparse in self.roots:
            if base == PurePosixPath("."):
                relative: PurePosixPath | None = ref
            else:
                try:
                    relative = ref.relative_to(base)
                except ValueError:
                    continue

            if relative is None:
                continue
            if sparse is None:
                return True
            # A sparse checkout in non-cone mode takes gitignore-style patterns. Compare on the
            # normalized path: the patterns this repository uses are plain file paths, and anything
            # more elaborate is reported rather than guessed at.
            if str(relative) in sparse:
                return True
        return False


def sparse_patterns(step_with: dict) -> frozenset[str] | None:
    raw = step_with.get("sparse-checkout")
    if raw is None:
        return None
    if isinstance(raw, str):
        entries = [line.strip() for line in raw.replace(",", "\n").splitlines()]
    elif isinstance(raw, list):
        entries = [str(item).strip() for item in raw]
    else:
        return frozenset()
    return frozenset(entry.lstrip("./") for entry in entries if entry)


# Matches a script reference to a tracked path: `scripts/...`, `./scripts/...`, `repo/scripts/...`.
# Restricted to directories this repository actually tracks so that output paths such as
# `publish/wasm` or `artifacts/msix` (created by earlier steps) are not mistaken for source.
TRACKED_DIRECTORIES = ("scripts", "src", "tests", "SalmonEgg", "docs", ".github")
TRACKED_FILES = ("global.json", "SalmonEgg.sln", "Directory.Build.props", "NuGet.config")

_TRACKED_DIR_ALTERNATION = "|".join(re.escape(name) for name in TRACKED_DIRECTORIES)
_TRACKED_FILE_ALTERNATION = "|".join(re.escape(name) for name in TRACKED_FILES)

RUN_REFERENCE_PATTERN = re.compile(
    rf"(?<![\w./-])(?P<prefix>(?:[\w.-]+/)*)(?P<tail>(?:{_TRACKED_DIR_ALTERNATION})/[\w./-]+|(?:{_TRACKED_FILE_ALTERNATION}))(?![\w-])"
)


# `cd <dir>` inside a run block re-bases every path after it. release-packaging.yml's CLI asset step
# reads `src/SalmonEgg.Cli/...` from inside `$(cd repo && ...)`, which is provided by the `repo`
# checkout -- reading that as a root-relative path reports a violation that does not exist. A gate that
# cries wolf gets disabled, so the base directory is tracked rather than assumed to be the root.
#
# Two forms appear in this repository and both are handled: a bare `cd dir` line, which changes the
# working directory for every following line in the same block, and `$(cd dir && ...)`, whose effect is
# confined to that substitution. Anything else -- `cd` into a variable, `pushd`, a `cd` inside a loop
# whose bounds this scanner cannot see -- is deliberately *not* interpreted; those paths keep resolving
# against the block's current base, which errs toward reporting rather than silently passing.
_CD_LINE_PATTERN = re.compile(r"^\s*cd\s+(?P<dir>[\w./\-\$\{\}\"']+)\s*$")
_CD_SUBSHELL_PATTERN = re.compile(r"\(\s*cd\s+(?P<dir>[\w./-]+)\s*&&")


def _rebase(base: str, reference: str) -> str:
    """Resolves a reference against a run block's current working directory."""
    if not base:
        return reference
    return str(PurePosixPath(base) / reference)


def run_references(script: str) -> set[str]:
    """Extracts workspace-relative references to tracked paths from a `run:` block.

    Comment lines are excluded: this file, and several workflow steps, discuss `scripts/...` paths in
    prose. Counting those would make the gate fire on documentation.
    """
    found: set[str] = set()
    # The block's working directory relative to the workspace root, updated by bare `cd` lines.
    block_base = ""

    for raw_line in script.splitlines():
        stripped = raw_line.strip()
        if stripped.startswith("#"):
            continue
        # Strip a trailing comment, but only when the '#' starts a word -- '#' appears inside
        # expressions and strings too.
        line = re.sub(r"\s#\s.*$", "", raw_line)

        bare_cd = _CD_LINE_PATTERN.match(line)
        if bare_cd:
            target = bare_cd.group("dir").strip("\"'")
            # An expression-valued or absolute `cd` target cannot be resolved statically. Leave the
            # base untouched so later references are still checked against something real.
            if "${{" not in target and not target.startswith(("/", "$")):
                block_base = str(PurePosixPath(block_base) / target) if block_base else target
            continue

        # A `$(cd dir && ...)` substitution re-bases only what is inside it.
        subshell_base = block_base
        subshell = _CD_SUBSHELL_PATTERN.search(line)
        if subshell:
            target = subshell.group("dir")
            subshell_base = str(PurePosixPath(block_base) / target) if block_base else target

        for match in RUN_REFERENCE_PATTERN.finditer(line):
            reference = (match.group("prefix") or "") + match.group("tail")
            reference = reference.lstrip("./")
            if reference.startswith("${{"):
                continue
            # A reference that already carries a checked-out prefix (`repo/scripts/...`) is written
            # relative to the workspace root, not to the block's base; re-basing it would invent a
            # path like `repo/repo/scripts`. Only re-base when the base is not already the prefix.
            base = subshell_base if subshell else block_base
            if base and not reference.startswith(f"{base}/"):
                reference = _rebase(base, reference)
            found.add(reference)
    return found


def analyze_workflow(path: Path) -> tuple[list[Violation], int]:
    document = yaml.safe_load(path.read_text(encoding="utf-8"))
    violations: list[Violation] = []
    job_count = 0

    jobs = (document or {}).get("jobs") or {}
    for job_name, job in jobs.items():
        if not isinstance(job, dict):
            continue
        job_count += 1
        trees = CheckedOutTrees()

        for index, step in enumerate(job.get("steps") or []):
            if not isinstance(step, dict):
                continue
            step_name = step.get("name") or step.get("uses") or f"step #{index + 1}"
            with_block = step.get("with") if isinstance(step.get("with"), dict) else {}

            uses = step.get("uses")
            if uses:
                identifier = action_id(str(uses))
                if identifier == CHECKOUT_ACTION:
                    trees.add(with_block.get("path"), sparse_patterns(with_block))
                    continue

                for input_name in ACTION_PATH_INPUTS.get(identifier, ()):  # noqa: B007
                    value = with_block.get(input_name)
                    if value is None:
                        continue
                    reference = str(value).strip().lstrip("./")
                    if "${{" in reference:
                        violations.append(
                            Violation(
                                path.name,
                                str(job_name),
                                str(step_name),
                                f"input '{input_name}' is a template expression ('{value}'); this gate "
                                "cannot prove a checkout provides it. Use a literal path.",
                            )
                        )
                        continue
                    if not trees.provides(reference):
                        if trees.any_checkout():
                            detail = (
                                f"reads '{reference}' via '{input_name}', but no preceding checkout in "
                                "this job provides that path (wrong `path:` or too narrow a "
                                "`sparse-checkout:`)."
                            )
                        else:
                            detail = (
                                f"reads '{reference}' via '{input_name}' before any "
                                f"'{CHECKOUT_ACTION}' step in this job, so it resolves against an "
                                "empty workspace."
                            )
                        violations.append(
                            Violation(path.name, str(job_name), str(step_name), detail)
                        )

                if identifier not in ACTION_PATH_INPUTS and identifier not in KNOWN_WORKSPACE_FREE_ACTIONS:
                    violations.append(
                        Violation(
                            path.name,
                            str(job_name),
                            str(step_name),
                            f"uses unclassified action '{identifier}'. Add it to either "
                            "ACTION_PATH_INPUTS (with the inputs it resolves in the workspace) or "
                            "KNOWN_WORKSPACE_FREE_ACTIONS in "
                            "scripts/gates/workflow_workspace_contract.py. This gate fails closed: an "
                            "action nobody classified is an action nobody checked.",
                        )
                    )
                continue

            script = step.get("run")
            if not script:
                continue
            for reference in sorted(run_references(str(script))):
                if trees.provides(reference):
                    continue
                if trees.any_checkout():
                    detail = (
                        f"references '{reference}', but no preceding checkout in this job provides "
                        "that path."
                    )
                else:
                    detail = (
                        f"references '{reference}' before any '{CHECKOUT_ACTION}' step in this job."
                    )
                violations.append(Violation(path.name, str(job_name), str(step_name), detail))

    return violations, job_count


def run_repository_check(workflow_dir: Path) -> int:
    workflows = sorted(
        [p for p in workflow_dir.iterdir() if p.suffix in {".yml", ".yaml"}],
        key=lambda p: p.name,
    )
    if not workflows:
        print(f"[workflow-gate] FAIL no workflow files under {workflow_dir}", file=sys.stderr)
        return 1

    all_violations: list[Violation] = []
    total_jobs = 0
    # Name the files and job counts rather than only a total: a count alone drifts with unrelated
    # additions, and a gate that silently starts covering less looks identical to one that passes.
    for workflow in workflows:
        violations, job_count = analyze_workflow(workflow)
        total_jobs += job_count
        all_violations.extend(violations)
        status = "ok" if not violations else f"{len(violations)} violation(s)"
        print(f"[workflow-gate] {workflow.name}: {job_count} job(s), {status}")

    if all_violations:
        print("", file=sys.stderr)
        print("[workflow-gate] FAIL workspace contract violations:", file=sys.stderr)
        for violation in all_violations:
            print(f"  - {violation.render()}", file=sys.stderr)
        return 1

    print(
        f"[workflow-gate] {len(workflows)} workflow(s), {total_jobs} job(s): every workspace read is "
        "preceded by a checkout that provides it"
    )
    return 0


# --- self-test ----------------------------------------------------------------------------------------

# Every rule above claims to reject a specific shape. Without driving each claim against a synthetic
# workflow, a weakened rule would pass for as long as the real workflows happen to be correct -- which
# is exactly the state this repository was in before v1.4.0.
SELF_TEST_CASES: list[tuple[str, str, str]] = [
    (
        "checkout before the SDK pin",
        "pass",
        """
jobs:
  publish:
    steps:
      - name: Checkout
        uses: actions/checkout@abc # v5.1.0
      - name: Setup .NET
        uses: actions/setup-dotnet@def # v5.4.0
        with:
          global-json-file: global.json
""",
    ),
    (
        "the SDK pin before any checkout (the v1.4.0 defect)",
        "fail",
        """
jobs:
  publish:
    steps:
      - name: Setup .NET
        uses: actions/setup-dotnet@def # v5.4.0
        with:
          global-json-file: global.json
      - name: Checkout
        uses: actions/checkout@abc # v5.1.0
""",
    ),
    (
        "the SDK pin with no checkout anywhere (the release-acp-sdk defect)",
        "fail",
        """
jobs:
  publish:
    steps:
      - name: Setup .NET
        uses: actions/setup-dotnet@def # v5.4.0
        with:
          global-json-file: global.json
      - name: Push
        run: echo push
""",
    ),
    (
        "a sparse checkout that provides exactly the pinned file",
        "pass",
        """
jobs:
  publish:
    steps:
      - name: Checkout for the SDK pin
        uses: actions/checkout@abc # v5.1.0
        with:
          sparse-checkout: global.json
          sparse-checkout-cone-mode: false
      - name: Setup .NET
        uses: actions/setup-dotnet@def # v5.4.0
        with:
          global-json-file: global.json
""",
    ),
    (
        "a sparse checkout that omits the pinned file",
        "fail",
        """
jobs:
  publish:
    steps:
      - name: Checkout
        uses: actions/checkout@abc # v5.1.0
        with:
          sparse-checkout: scripts
          sparse-checkout-cone-mode: false
      - name: Setup .NET
        uses: actions/setup-dotnet@def # v5.4.0
        with:
          global-json-file: global.json
""",
    ),
    (
        "a checkout into a subdirectory does not satisfy a root-relative pin",
        "fail",
        """
jobs:
  publish:
    steps:
      - name: Checkout for release tooling
        uses: actions/checkout@abc # v5.1.0
        with:
          path: repo
      - name: Setup .NET
        uses: actions/setup-dotnet@def # v5.4.0
        with:
          global-json-file: global.json
""",
    ),
    (
        "a subdirectory checkout satisfies a reference under that subdirectory",
        "pass",
        """
jobs:
  publish:
    steps:
      - name: Checkout for release tooling
        uses: actions/checkout@abc # v5.1.0
        with:
          path: repo
      - name: Build formula
        run: repo/scripts/release/build-cli-homebrew-formula.sh --version 1.0.0
""",
    ),
    (
        "a script reference before any checkout",
        "fail",
        """
jobs:
  gate:
    steps:
      - name: Run gate
        run: scripts/gates/run-release-artifact-contract-gate.sh --self-test
      - name: Checkout
        uses: actions/checkout@abc # v5.1.0
""",
    ),
    (
        "a script reference after checkout",
        "pass",
        """
jobs:
  gate:
    steps:
      - name: Checkout
        uses: actions/checkout@abc # v5.1.0
      - name: Run gate
        run: ./scripts/gates/run-release-artifact-contract-gate.sh --self-test
""",
    ),
    (
        "a run step touching only artifact output needs no checkout",
        "pass",
        """
jobs:
  publish:
    steps:
      - name: Download
        uses: actions/download-artifact@abc # v5.0.0
        with:
          path: artifacts/acp-sdk-package
      - name: Push
        run: dotnet nuget push artifacts/acp-sdk-package/*.nupkg
""",
    ),
    (
        "a tracked path mentioned only in a comment is not a reference",
        "pass",
        """
jobs:
  publish:
    steps:
      - name: Push
        run: |
          # The rule itself lives in scripts/release/DesktopMsiContract.ps1 so it can be rehearsed.
          echo push
""",
    ),
    (
        "an unclassified action is reported rather than assumed safe",
        "fail",
        """
jobs:
  publish:
    steps:
      - name: Mystery
        uses: some-org/some-action@abc # v1
""",
    ),
    (
        "a template expression as the pinned path cannot be proven",
        "fail",
        """
jobs:
  publish:
    steps:
      - name: Checkout
        uses: actions/checkout@abc # v5.1.0
      - name: Setup .NET
        uses: actions/setup-dotnet@def # v5.4.0
        with:
          global-json-file: ${{ env.SDK_PIN }}
""",
    ),
    (
        "a version pin bump does not disable the rule",
        "fail",
        """
jobs:
  publish:
    steps:
      - name: Setup .NET
        uses: actions/setup-dotnet@0000000000000000000000000000000000000000 # v9.9.9
        with:
          global-json-file: global.json
""",
    ),
    (
        "one job's checkout does not cover another job",
        "fail",
        """
jobs:
  first:
    steps:
      - name: Checkout
        uses: actions/checkout@abc # v5.1.0
  second:
    steps:
      - name: Setup .NET
        uses: actions/setup-dotnet@def # v5.4.0
        with:
          global-json-file: global.json
""",
    ),
]


def run_self_test() -> int:
    failures = 0
    with tempfile.TemporaryDirectory() as work:
        for description, expected, content in SELF_TEST_CASES:
            path = Path(work) / "case.yml"
            path.write_text(content, encoding="utf-8")
            violations, _ = analyze_workflow(path)
            actual = "fail" if violations else "pass"
            if actual != expected:
                print(
                    f"[workflow-gate] FAIL self-test: {description} expected {expected} but got {actual}",
                    file=sys.stderr,
                )
                for violation in violations:
                    print(f"    reported: {violation.render()}", file=sys.stderr)
                failures += 1
            else:
                print(f"[workflow-gate] self-test: {description} -> {actual} (as intended)")

    if failures:
        print(f"[workflow-gate] self-test failed with {failures} wrong outcome(s)", file=sys.stderr)
        return 1

    print(f"[workflow-gate] self-test passed ({len(SELF_TEST_CASES)} cases)")
    return 0


# --- entry --------------------------------------------------------------------------------------------


def main() -> int:
    parser = argparse.ArgumentParser(add_help=True)
    parser.add_argument("--repo-root", default=".")
    parser.add_argument("--workflow-dir", default=None)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        return run_self_test()

    workflow_dir = (
        Path(args.workflow_dir)
        if args.workflow_dir
        else Path(args.repo_root) / ".github" / "workflows"
    )
    if not workflow_dir.is_dir():
        print(f"[workflow-gate] FAIL workflow directory not found: {workflow_dir}", file=sys.stderr)
        return 1

    return run_repository_check(workflow_dir)


if __name__ == "__main__":
    sys.exit(main())
