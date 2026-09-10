# SPDX-License-Identifier: Apache-2.0
"""Check local Markdown link targets without requesting external sites."""
from pathlib import Path
import re
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[1]
errors = []
files = list(ROOT.glob("*.md")) + list((ROOT / "docs").rglob("*.md")) + list((ROOT / "data").glob("*.md"))
for path in files:
    for target in re.findall(r"\]\(([^)\s]+)(?:\s+[^)]*)?\)", path.read_text(encoding="utf-8")):
        if "://" in target or target.startswith(("#", "mailto:")):
            continue
        target = unquote(target.split("#")[0].strip("<>"))
        if target and not (path.parent / target).exists():
            errors.append(f"{path.relative_to(ROOT)}: missing link target {target}")
if errors:
    raise SystemExit("\n".join(errors))
print(f"Local links checked in {len(files)} documentation files.")
