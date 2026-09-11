# SPDX-License-Identifier: Apache-2.0
"""Container CLI and report coordinator."""

import argparse
import os
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

from .fixtures import generate, read, save

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
        raise RuntimeError("Missing, failed or unexpectedly skipped tests")
    if sdk and len(cases) - len(skipped) != 175:
        raise RuntimeError("Unexpected pinned SDK test count")
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
            "process",
            "batch",
            "recover",
            "crash",
            "unit",
            "controlled",
            "restarted",
            "sdk",
            "matrixsdk",
            "sourceoutage",
            "cloud",
            "usage",
        ],
    )
    parser.add_argument("--work", type=Path, default=Path("/work/manual/case-001"))
    parser.add_argument("--case", default="case-001")
    parser.add_argument("--provider", choices=["fixture", *CLOUD_CASES], default="fixture")
    parser.add_argument("--inputs", type=Path, default=Path("/inputs"))
    parser.add_argument("--oracle", type=Path, default=Path("/oracle"))
    parser.add_argument("--seed", type=Path, default=Path("/seed"))
    parser.add_argument("--variant", choices=["normal", "limited", "stale"], default="normal")
    args = parser.parse_args()
    args.work.mkdir(parents=True, exist_ok=True)
    if args.command == "generate":
        generate(args.inputs, args.oracle, args.seed)
    elif args.command == "bootstrap":
        from .server import bootstrap

        model = (
            "inventory-fixture"
            if args.provider == "fixture"
            else os.environ[args.provider.upper() + "_MODEL"]
        )
        bootstrap(args.inputs, Path("/credentials"), args.work, args.provider, model)
    elif args.command == "batch":
        from .fixtures import verify
        from .workflow import process

        outcomes = {}
        for case in verify(args.inputs)["cases"]:
            try:
                packet = process(args.inputs, Path("/credentials"), args.work / case, case)
                outcomes[case] = packet["analysis_status"]
            except Exception as error:
                outcomes[case] = type(error).__name__
        save(args.work / "batch.json", outcomes)
        if any(status != "validated_draft" for status in outcomes.values()):
            raise SystemExit(2)
    elif args.command in ("process", "recover", "crash"):
        from .workflow import process

        packet = process(
            args.inputs,
            Path("/credentials"),
            args.work,
            args.case,
            args.command == "recover",
            args.command == "crash",
            args.variant,
        )
        print((args.work / "inventory.md").read_text())
        if (
            packet["analysis_status"] != "validated_draft"
            or packet["inventory"]["status"] != "complete"
        ):
            raise SystemExit(2)
    elif args.command == "usage":
        from .server import client

        with client(os.environ["MUNARIUM_MGMT_TOKEN"], "inventory-reporter") as api:
            save(args.work / "usage.json", api.reports.usage().model_dump(mode="json"))
    else:
        from .server import ready

        env = dict(os.environ, INVENTORY_REPORT_DIR=str(args.work))
        if args.command == "unit":
            subprocess.run(["ruff", "check", "inventory_demo", "tests"], check=True)
            subprocess.run(["ruff", "format", "--check", "inventory_demo", "tests"], check=True)
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
        elif args.command == "matrixsdk":
            ready()
            env["MUNARIUM_MATRIX_TEST_URL"] = "http://matrix:8180"
            env["MUNARIUM_MATRIX_TEST_TOKEN"] = "inventory-matrix-rw"
            tests, sdk = ["/opt/munarium/clients/matrix-python/tests"], False
        else:
            ready()
            if args.command == "cloud":
                config = read("/credentials/query.json")
                env["INVENTORY_CLOUD_PROVIDER"] = config["provider"]
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
