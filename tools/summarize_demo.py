# SPDX-License-Identifier: Apache-2.0
"""Summarize retained project samples without claiming unobserved peaks."""
import json
import re
import sys
from collections import defaultdict
from pathlib import Path


def size(text):
    match = re.fullmatch(r"([0-9.]+)(B|kB|MB|GB|TB|KiB|MiB|GiB|TiB)", text.strip())
    if not match:
        raise ValueError("Unexpected Docker size unit")
    unit = match[2]
    factors = {"B": 1, "kB": 1000, "MB": 1000**2, "GB": 1000**3, "TB": 1000**4,
               "KiB": 1024, "MiB": 1024**2, "GiB": 1024**3, "TiB": 1024**4}
    return int(float(match[1]) * factors[unit])


def summarize(folder):
    folder = Path(folder)
    run = json.loads((folder / "run.json").read_text(encoding="utf-8-sig"))
    ticks = defaultdict(lambda: {"cpu": 0, "memory": 0, "containers": set()})
    samples = folder / "samples.jsonl"
    for line in samples.read_text(encoding="utf-8-sig").splitlines() if samples.exists() else []:
        item = json.loads(line)
        stats, tick = item["stats"], ticks[item["utc"]]
        if stats["ID"] in tick["containers"]:
            raise ValueError("Duplicate container sample at the same tick")
        tick["containers"].add(stats["ID"])
        tick["cpu"] += float(stats["CPUPerc"].rstrip("%"))
        tick["memory"] += size(stats["MemUsage"].split("/")[0])
    passed = run.get("passed", run.get("exit_code") == 0)
    if passed and not ticks:
        raise ValueError("Successful workflow has no resource samples")
    report = {"demo": run["demo"], "profile": run["profile"], "passed": passed,
              "elapsed_seconds": run["elapsed_seconds"], "sample_ticks": len(ticks),
              "sampled_project_cpu_percent_max": max((v["cpu"] for v in ticks.values()), default=None),
              "sampled_project_memory_bytes_max": max((v["memory"] for v in ticks.values()), default=None),
              "limits": "Sampled peaks exclude host/VM/build-daemon overhead and may miss brief containers; 100% CPU is one core"}
    (folder / "measurement-summary.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))


if __name__ == "__main__":
    summarize(sys.argv[1])
