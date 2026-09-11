# SPDX-License-Identifier: Apache-2.0
"""Evaluation entry points, keeping collection and offline scoring separate."""

import argparse
import os
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

from .fixtures import SETTINGS, generate, read, save

CLOUD_CASES = {"openai": (1, 3, 5), "anthropic": (2, 4, 6), "openrouter": (7, 8)}


def check_report(path, sdk=False):
    cases = ET.parse(path).findall(".//testcase")
    skipped = [c for c in cases if c.find("skipped") is not None]
    failed = [c for c in cases if c.find("failure") is not None or c.find("error") is not None]
    if (
        not cases
        or failed
        or any(
            not sdk or not c.get("name", "").startswith("test_gates_chronology_certain_only[")
            for c in skipped
        )
    ):
        raise RuntimeError("Missing, failed or unexpected skipped tests")
    if sdk and len(cases) - len(skipped) != 175:
        raise RuntimeError("Unexpected SDK test count")
    save(
        path.with_suffix(".json"),
        {
            "tests": len(cases),
            "passed": len(cases) - len(skipped),
            "failed": 0,
            "skipped": len(skipped),
        },
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "command",
        choices=[
            "generate",
            "bootstrap",
            "run",
            "recover",
            "crash",
            "score",
            "unit",
            "controlled",
            "restarted",
            "sdk",
            "cloud",
            "usage",
        ],
    )
    parser.add_argument("--work", type=Path, default=Path("/work/manual/topk-case-001"))
    parser.add_argument("--case", default="case-001")
    parser.add_argument("--setting", choices=list(SETTINGS), default="topk")
    parser.add_argument("--complete", action="store_true")
    parser.add_argument("--override", action="store_true")
    parser.add_argument("--provider", choices=["fixture", *CLOUD_CASES], default="fixture")
    parser.add_argument("--inputs", type=Path, default=Path("/inputs"))
    parser.add_argument("--oracle", type=Path, default=Path("/oracle"))
    args = parser.parse_args()
    args.work.mkdir(parents=True, exist_ok=True)
    if args.command == "generate":
        generate(args.inputs, args.oracle)
    elif args.command == "bootstrap":
        from .server import bootstrap

        model = (
            "bench-fixture"
            if args.provider == "fixture"
            else os.environ[args.provider.upper() + "_MODEL"]
        )
        bootstrap(args.inputs, args.work, args.provider, model)
    elif args.command in ("run", "recover", "crash"):
        from .workflow import run

        record = run(
            args.inputs,
            "/credentials",
            args.work,
            args.case,
            args.setting,
            args.complete,
            args.command == "recover",
            args.command == "crash",
            args.override,
        )
        print(
            f"{args.case} | {args.setting} | {record['profile']} | hits={len(record['result']['hits'])} | complete={args.complete}"
        )
    elif args.command == "score":
        from .metrics import report

        report(args.work, args.oracle / "labels.json")
        print((args.work / "metrics.md").read_text())
    elif args.command == "usage":
        from .server import client

        with client(os.environ["MUNARIUM_MGMT_TOKEN"], "bench-reporter") as api:
            save(args.work / "usage.json", api.reports.usage().model_dump(mode="json"))
    else:
        from .server import ready

        env = dict(os.environ, BENCH_REPORT_DIR=str(args.work))
        if args.command == "unit":
            subprocess.run(["ruff", "check", "bench_demo", "tests"], check=True)
            subprocess.run(["ruff", "format", "--check", "bench_demo", "tests"], check=True)
            tests, sdk = ["tests/test_unit.py"], False
        elif args.command == "sdk":
            ready()
            subprocess.run(
                [sys.executable, "/opt/munarium/clients/check_compatibility.py"], check=True
            )
            tests, sdk = (
                ["/opt/munarium/clients/python/tests", "/opt/munarium/clients/python/conformance"],
                True,
            )
        else:
            ready()
            if args.command == "cloud":
                env["BENCH_CLOUD_PROVIDER"] = read("/credentials/query.json")["provider"]
            tests, sdk = ["tests/test_" + args.command + ".py"], False
        output = args.work / "tests.xml"
        subprocess.run(
            [sys.executable, "-m", "pytest", *tests, "-q", "--junitxml=" + str(output)],
            env=env,
            check=True,
        )
        check_report(output, sdk)


if __name__ == "__main__":
    main()
