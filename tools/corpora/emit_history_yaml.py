# SPDX-License-Identifier: Apache-2.0
# Modified 2026-09-09 for the standalone demo corpus import.
"""Emit the history-revolution runbook's `collections:` block from layouts.py.

The upload paths and the runbook's collection bindings have to agree exactly
— a mismatch means documents land in blob and bind to nothing, which the run
only reports as an empty collection much later. Generating one from the other
removes that failure mode.

    py emit_history_yaml.py            # print the block
    py emit_history_yaml.py --check    # exit 1 if the committed runbook drifted

The committed runbook is the artifact of record; this just regenerates the
repetitive middle of it.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from layouts import HISTORY_GROUPS, NEWSPAPER_GROUP, _group_prefixes

RUNBOOK = Path(__file__).resolve().parents[2] / "vendor" / "runbooks" / "history-revolution.yaml"


def collection_name(prefix: str) -> str:
    """`rev/founders/gw/b3/` -> `loc-founders-gw-b3`."""
    return "loc-" + prefix.removeprefix("rev/").rstrip("/").replace("/", "-")


def emit() -> str:
    lines = ["  collections:"]
    for group, dirs, shards in [*HISTORY_GROUPS, NEWSPAPER_GROUP]:
        src = ", ".join(dirs)
        # ASCII only: this text is spliced into a committed YAML file and
        # round-trips through shell redirects on Windows.
        lines.append(f"    # {group} <- {src} ({shards} shard(s))")
        for pfx in _group_prefixes(group, shards):
            lines += [
                f"    - name: {collection_name(pfx)}",
                "      shape: archival-documents@1",
                "      accessLevel: 0",
                f'      sources: {{ filenamePrefix: "{pfx}" }}',
            ]
    return "\n".join(lines) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()
    block = emit()
    if not args.check:
        sys.stdout.write(block)
        return 0
    text = RUNBOOK.read_text(encoding="utf-8")
    if block.rstrip() not in text:
        print(
            f"DRIFT: {RUNBOOK} does not contain the collections block layouts.py implies.\n"
            "Regenerate it: py emit_history_yaml.py",
            file=sys.stderr,
        )
        return 1
    n = sum(s for _, _, s in [*HISTORY_GROUPS, NEWSPAPER_GROUP])
    print(f"ok: runbook matches layouts.py ({n} collections)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
