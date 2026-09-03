#!/usr/bin/env python3
"""Regenerate src/SalmonEgg.Acp/SchemaTolerance.Fields.txt from the upstream ACP schemas.

The upstream schema marks fields whose deserialization must not fail:

    x-deserialize-default-on-error    the field falls back to its default when it cannot be read
    x-deserialize-skip-invalid-items  a bad array element is dropped, the rest of the array survives

Those markers are the protocol's own statement of intended leniency, so they are the ground truth
for AGENTS.md "protocol looseness must not be tightened in reverse". This script distils them into a
reviewable manifest that the test suite reads offline; run it by hand when upstream moves and commit
the diff.

    python3 scripts/gates/generate-acp-tolerance-manifest.py > src/SalmonEgg.Acp/SchemaTolerance.Fields.txt
"""

from __future__ import annotations

import json
import signal
import sys
import urllib.request

SCHEMAS = {
    "v1": "https://raw.githubusercontent.com/zed-industries/agent-client-protocol/main/schema/v1/schema.json",
    "v2": "https://raw.githubusercontent.com/zed-industries/agent-client-protocol/main/schema/v2/schema.json",
}

DEFAULT_ON_ERROR = "x-deserialize-default-on-error"
SKIP_INVALID_ITEMS = "x-deserialize-skip-invalid-items"


def collect(node: object, path: str, into: dict[str, set[str]]) -> None:
    """Walk one type definition, recording every field that carries a tolerance marker."""
    if not isinstance(node, dict):
        return

    if path:
        for marker in (DEFAULT_ON_ERROR, SKIP_INVALID_ITEMS):
            if node.get(marker):
                into.setdefault(path, set()).add(marker)

    for field, sub in (node.get("properties") or {}).items():
        collect(sub, f"{path}.{field}" if path else field, into)

    items = node.get("items")
    if isinstance(items, dict):
        collect(items, f"{path}[]", into)

    for union in ("oneOf", "anyOf", "allOf"):
        for branch in node.get(union) or []:
            collect(branch, path, into)


def main() -> int:
    signal.signal(signal.SIGPIPE, signal.SIG_DFL)

    rows: list[tuple[str, str, str, str]] = []
    provenance: list[str] = []

    for version, url in SCHEMAS.items():
        with urllib.request.urlopen(url, timeout=60) as response:  # noqa: S310 - fixed upstream URL
            raw = response.read()
        schema = json.loads(raw)
        defs = schema.get("$defs") or {}
        provenance.append(f"#   {version}  {url}  ({len(raw)} bytes, {len(defs)} $defs)")

        for type_name, type_def in sorted(defs.items()):
            found: dict[str, set[str]] = {}
            collect(type_def, "", found)
            for field, markers in sorted(found.items()):
                tags = "+".join(
                    tag
                    for tag, marker in (("default", DEFAULT_ON_ERROR), ("skip-items", SKIP_INVALID_ITEMS))
                    if marker in markers
                )
                rows.append((version, type_name, field, tags))

    out = sys.stdout
    out.write("# Fields the upstream ACP schema marks as tolerant. One line per version + type + field:\n")
    out.write("#\n")
    out.write("#     <v1|v2> <SchemaType> <fieldPath> <default|skip-items|default+skip-items>\n")
    out.write("#\n")
    out.write("# default     x-deserialize-default-on-error   -- unreadable value falls back to the default\n")
    out.write("# skip-items  x-deserialize-skip-invalid-items -- an unreadable array element is dropped\n")
    out.write("#\n")
    out.write("# These are the protocol's own leniency markers, so a reader that throws on one of them is\n")
    out.write("# stricter than the protocol -- exactly what AGENTS.md forbids. Generated, do not hand-edit:\n")
    out.write("#\n")
    out.write("#     python3 scripts/gates/generate-acp-tolerance-manifest.py > src/SalmonEgg.Acp/SchemaTolerance.Fields.txt\n")
    out.write("#\n")
    out.write("# Source of truth:\n")
    for line in provenance:
        out.write(line + "\n")
    out.write("#\n")
    for version, type_name, field, tags in rows:
        out.write(f"{version} {type_name} {field} {tags}\n")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
