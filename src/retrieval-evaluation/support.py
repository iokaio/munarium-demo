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


def plot(source, output):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    measured = json.loads(Path(source).read_text())
    settings = ["baseline", "topk", "candidates", "budget"]
    values = [measured["summary"][name + "/retrieval"]["mean_source_recall"] for name in settings]
    figure, axes = plt.subplots(1, 2, figsize=(13, 6), layout="constrained")
    axes[0].bar(settings, values, color=["#6a7b8d", "#17876e", "#6a7b8d", "#6a7b8d"])
    axes[0].set(ylabel="Mean source recall", ylim=(0, 1.12), title="Independent source labels")
    for i, value in enumerate(values):
        axes[0].text(i, value + 0.025, f"{value:.3f}", ha="center")
    for i, setting in enumerate(settings):
        latency = [row["latency_seconds"] for row in measured["experiments"] if row["setting"] == setting and row["mode"] == "retrieval" and row["latency_seconds"] is not None]
        axes[1].scatter([i] * len(latency), latency, color="#17876e", alpha=0.65)
    axes[1].set(xticks=range(4), xticklabels=settings, ylabel="Observed seconds per turn", title="Fixed workload latency observations")
    for axis in axes:
        axis.spines[["top", "right"]].set_visible(False)
    figure.suptitle("Retrieval evaluation bench | 8 fictional questions, 2 access profiles\nOffline score of captured Server responses", fontsize=15)
    figure.supxlabel(f"Forbidden-source exposures: {measured['access_leakage']} | Unanswerable questions excluded from recall; raw rows retain abstention and citation metrics.\nOne setting changes from baseline at a time. Synthetic observations do not establish production confidence.", fontsize=10)
    figure.savefig(output, dpi=130)
    figure.savefig(str(Path(output).with_suffix(".svg")))
    plt.close(figure)


if __name__ == "__main__":
    {"report": report, "render": render, "plot": plot}[sys.argv[1]](*sys.argv[2:])
