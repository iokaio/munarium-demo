# SPDX-License-Identifier: Apache-2.0
"""Report native Rust test results and rasterize captured terminal output."""
import json
from pathlib import Path
import re
import sys
import textwrap
import xml.etree.ElementTree as ET


def report(log, output, kind):
    text = Path(log).read_text()
    pattern = r"^test (.+?) \.\.\. (ok|FAILED|ignored)$" if kind == "unit" else r"^\s+(PASS|FAIL)\s+(.+)$"
    matches = re.findall(pattern, text, re.M)
    cases = [(name, state) for name, state in matches] if kind == "unit" else [(name, state) for state, name in matches]
    failures = sum(state not in ("ok", "PASS") for _, state in cases)
    if kind == "unit":
        totals = re.findall(r"test result: \w+\. (\d+) passed; (\d+) failed; (\d+) ignored", text)
        if sum(sum(map(int, counts)) for counts in totals) != len(cases):
            raise SystemExit("Native Rust counts do not match parsed test cases")
    root = ET.Element("testsuite", name=kind, tests=str(len(cases)), failures=str(failures), skipped="0")
    for name, state in cases:
        child = ET.SubElement(root, "testcase", name=name)
        if state not in ("ok", "PASS"):
            ET.SubElement(child, "failure").text = state
    ET.ElementTree(root).write(output, encoding="unicode")
    Path(output).with_suffix(".json").write_text(json.dumps({"tests": len(cases), "passed": len(cases)-failures, "failed": failures, "skipped": 0}, indent=2))
    if not cases or failures:
        raise SystemExit("Missing, failed or unexpectedly skipped Rust tests")


def render(source, output):
    from PIL import Image, ImageDraw, ImageFont
    text = Path(source).read_text()
    # Render the first source plus explanation, keeping actual hashes and citations.
    first = text.split("\nSOURCE 2")[0]
    explanation = "\nEXPLANATION" + text.split("\nEXPLANATION", 1)[1] if "\nEXPLANATION" in text else ""
    lines = []
    for line in (first + explanation).splitlines():
        lines.extend(textwrap.wrap(line, width=108, replace_whitespace=False) or [""])
    image = Image.new("RGB", (1420, max(600, 100+len(lines)*25)), "#101b28")
    draw = ImageDraw.Draw(image)
    font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf", 20)
    for n, line in enumerate(lines):
        color = "#6fe3bd" if n < 2 or line.startswith(("SOURCE", "EXPLANATION", "Provider:")) else "#d8e3f0"
        draw.text((32, 36+n*25), line, font=font, fill=color)
    image.save(output)


if __name__ == "__main__":
    {"report": report, "render": render}[sys.argv[1]](*sys.argv[2:])
