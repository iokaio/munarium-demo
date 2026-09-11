# SPDX-License-Identifier: Apache-2.0
"""Read-only Linux-container capacity checks; no Docker socket or provider secrets."""
import argparse
import datetime
import json
import os
import platform
import shutil
from pathlib import Path

GIB = 1024 ** 3


def capacity(profile, demo, memory_available, docker_free, output_free, cpus):
    # Conservative operational guardrails, not experimentally established minima.
    limits = {
        "cpus": 2,
        "memory_available_bytes": (8 if profile == "stress" or demo == "inventory-replenishment" else 4) * GIB,
        "docker_free_bytes": (25 if profile == "stress" else 15) * GIB,
        "output_free_bytes": (2 if profile == "stress" else 1) * GIB,
    }
    observed = {"cpus": cpus, "memory_available_bytes": memory_available,
                "docker_free_bytes": docker_free, "output_free_bytes": output_free}
    failures = [name for name in limits if observed[name] < limits[name]]
    return {"limits": limits, "observed": observed, "failures": failures, "passed": not failures}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("demo")
    parser.add_argument("profile", choices=["default", "heldout", "stress"])
    parser.add_argument("report", type=Path)
    args = parser.parse_args()
    memory = {line.split(":")[0]: int(line.split()[1]) * 1024
              for line in Path("/proc/meminfo").read_text().splitlines()}
    available = memory["MemAvailable"]
    cgroup_max, cgroup_current = Path("/sys/fs/cgroup/memory.max"), Path("/sys/fs/cgroup/memory.current")
    if cgroup_max.exists() and cgroup_max.read_text().strip() != "max":
        available = min(available, max(0, int(cgroup_max.read_text()) - int(cgroup_current.read_text())))
    args.report.parent.mkdir(parents=True, exist_ok=True)
    report = capacity(args.profile, args.demo, available, shutil.disk_usage("/").free,
                      shutil.disk_usage(args.report.parent).free, os.cpu_count() or 1)
    report.update({"demo": args.demo, "profile": args.profile, "platform": platform.platform(),
                   "architecture": platform.machine(), "utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                   "requirements_basis": "Conservative guardrails, not measured minimum hardware",
                   "ports": "No published service ports in the automated demo stacks"})
    args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    if not report["passed"]:
        raise SystemExit("Insufficient capacity: " + ", ".join(report["failures"]) + "; see " + str(args.report))
    print("Capacity preflight passed: " + args.demo + " / " + args.profile)


if __name__ == "__main__":
    main()
