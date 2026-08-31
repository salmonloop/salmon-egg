#!/usr/bin/env bash
#
# Asserts that no GitHub Actions job reads a tracked file before `actions/checkout` has put one there.
#
# This rule exists because the defect it catches is invisible everywhere a defect is normally caught. The
# v1.4.0 and v1.4.1 releases both died in `Publish Release Assets` on
#
#   The specified global.json file 'global.json' does not exist
#
# because that job's first step was `Setup .NET` with `global-json-file: global.json`, and its checkout
# came nine steps later. The YAML is valid, actionlint has nothing to say about it, and all seven
# packaging jobs go green first -- so the tag exists, the release exists, and it has zero assets. The
# identical defect sat unnoticed in release-acp-sdk.yml's publish job, which had no checkout at all; it
# had not fired only because the last SDK publish predates the switch to `global.json`.
#
# What makes this class special is that the broken jobs are the ones no pull request ever runs. A job
# gated on `if: startsWith(github.ref, 'refs/tags/v')` is unreachable from every branch build, so its
# first real execution is the release you were trying to ship. A static rule is the only thing that can
# observe it beforehand.
#
# Fail-closed by construction: the checks below are driven by what a job *does*, and an unrecognized
# workspace reference is reported rather than skipped. A rule that silently ignores shapes it does not
# understand is the reason the original defect reached two releases.
#
# Usage:
#   run-workflow-workspace-contract-gate.sh [--self-test] [--workflow-dir DIR]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

PYTHON_BIN=""
for candidate in python3 python; do
  if command -v "$candidate" >/dev/null 2>&1; then
    PYTHON_BIN="$candidate"
    break
  fi
done

if [ -z "$PYTHON_BIN" ]; then
  echo "[workflow-gate] FAIL no python3/python on PATH; this gate parses YAML" >&2
  exit 1
fi

# PyYAML is present on every GitHub-hosted runner. Check rather than assume: an ImportError deep inside
# the analyzer reads like a bug in the rule instead of a missing dependency.
if ! "$PYTHON_BIN" -c 'import yaml' >/dev/null 2>&1; then
  echo "[workflow-gate] FAIL PyYAML is not importable; install it (pip install pyyaml) to run this gate" >&2
  exit 1
fi

exec "$PYTHON_BIN" "$SCRIPT_DIR/workflow_workspace_contract.py" --repo-root "$REPO_ROOT" "$@"
