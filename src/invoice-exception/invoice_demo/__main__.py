# SPDX-License-Identifier: Apache-2.0
"""Container entry points; no provider keys are read by the application."""

import argparse
import json
import os
import platform
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

from .cloud_plan import CLOUD_CASES
from .fixtures import generate, save, verify


def check_report(path: Path, sdk: bool = False) -> None:
    cases = ET.parse(path).findall(".//testcase")
    if not cases:
        raise RuntimeError("Required test suite did not execute")
    for case in cases:
        if case.find("skipped") is not None and not (
            sdk and case.get("name", "").startswith("test_gates_chronology_certain_only[")
        ):
            raise RuntimeError("Unexpected skipped test: " + case.get("name", "unknown"))
    if sdk and sum(case.find("skipped") is None for case in cases) < 175:
        raise RuntimeError("Pinned Python SDK qualification did not execute all required cases")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "command",
        choices=[
            "generate",
            "bootstrap",
            "process",
            "reconcile",
            "unit",
            "test",
            "qualify",
            "quality",
            "cloud-test",
        ],
    )
    parser.add_argument("--inputs", type=Path, default=Path("/inputs"))
    parser.add_argument("--oracle", type=Path, default=Path("/oracle"))
    parser.add_argument("--credentials", type=Path, default=Path("/credentials"))
    parser.add_argument("--work", type=Path, default=Path("/work"))
    parser.add_argument("--seed", type=int, default=111)
    parser.add_argument("--groups", type=int, default=3)
    parser.add_argument(
        "--provider",
        choices=["fixture", "openai", "anthropic", "openrouter"],
        default="fixture",
    )
    parser.add_argument("--model", default="invoice-fixture")
    parser.add_argument("--preferred-model", action="store_true")
    parser.add_argument("--cloud-run", help="Fresh shared run ID for distributed cloud acceptance")
    parser.add_argument("--approve", action="store_true")
    parser.add_argument("--limit", type=int)
    args = parser.parse_args()
    if args.limit is not None and args.limit < 1:
        parser.error("--limit must be positive")
    if args.cloud_run and not re.fullmatch(r"[a-zA-Z0-9-]{1,80}", args.cloud_run):
        parser.error("--cloud-run must contain only letters, digits and hyphens")
    if args.cloud_run and args.limit is not None:
        parser.error("Cloud acceptance runs must execute every assigned case")
    if args.command == "generate":
        selected_profile = os.environ.get("DEMO_PROFILE", "default")
        profiles = json.loads(Path("/app/fixture-profiles.json").read_text())
        if selected_profile not in ("default", "heldout", "stress"):
            parser.error("Unknown fixture profile")
        if not any(token.split("=")[0] in ("--seed", "--groups") for token in sys.argv):
            args.seed = profiles[selected_profile]["seed"]
            args.groups = profiles[selected_profile]["groups"]
        else:
            selected_profile = "custom"
        if (args.inputs / "manifest.json").exists():
            manifest = verify(args.inputs)
            if (
                manifest["seed"] != args.seed
                or manifest["groups"] != args.groups
                or manifest.get("profile") != selected_profile
                or not (args.oracle / "expected.json").exists()
            ):
                raise ValueError("Use a new Compose project for a different fixture seed/profile")
        else:
            manifest = generate(args.inputs, args.oracle, args.seed, args.groups, selected_profile)
        print(f"Verified {manifest['case_count']} synthetic invoice cases.")
    elif args.command == "bootstrap":
        from .server import bootstrap

        if args.preferred_model:
            args.model = os.environ.get(args.provider.upper() + "_MODEL", "").strip()
            if not args.model:
                parser.error("Set the selected provider's preferred model")
        bootstrap(args.inputs, args.credentials, args.work, args.provider, args.model, args.approve)
        print("Verified and activated case indexes; scoped query capability saved privately.")
    elif args.command in ("process", "reconcile"):
        from .batch import process

        config = json.loads((args.credentials / "query.json").read_text())
        output = args.work / config["provider"] / config["namespace"]
        case_ids = None
        if args.cloud_run:
            case_ids = CLOUD_CASES[config["provider"]]
            output = args.work / "cloud" / args.cloud_run / config["provider"]
        summary = process(
            args.inputs,
            args.credentials,
            output,
            args.command == "reconcile",
            args.limit,
            case_ids,
        )
        print(json.dumps(summary))
        if summary["uncertain"] or summary["unverified"] or summary["failed"]:
            raise SystemExit(2)
    elif args.command in ("unit", "test"):
        args.work.mkdir(parents=True, exist_ok=True)
        subprocess.run(["ruff", "check", "invoice_demo", "tests"], check=True)
        testfiles = (
            ["tests/test_unit.py"]
            if args.command == "unit"
            else ["tests/test_unit.py", "tests/test_integration.py"]
        )
        subprocess.run(
            [
                sys.executable,
                "-m",
                "pytest",
                *testfiles,
                "-q",
                f"--junitxml={args.work}/{args.command}.xml",
            ],
            check=True,
        )
        check_report(args.work / f"{args.command}.xml")
    elif args.command == "qualify":
        from .server import ready

        ready()
        args.work.mkdir(parents=True, exist_ok=True)
        subprocess.run([sys.executable, "/opt/munarium/clients/check_compatibility.py"], check=True)
        subprocess.run(
            [
                sys.executable,
                "-m",
                "pytest",
                "/opt/munarium/clients/python/tests",
                "/opt/munarium/clients/python/conformance",
                "-q",
                f"--junitxml={args.work}/sdk.xml",
            ],
            check=True,
            cwd="/opt/munarium/clients/python",
        )
        check_report(args.work / "sdk.xml", sdk=True)
    elif args.command == "cloud-test":
        from .quality import assess

        if not args.cloud_run:
            parser.error("cloud-test requires --cloud-run")
        config = json.loads((args.credentials / "query.json").read_text())
        case_ids = CLOUD_CASES[config["provider"]]
        output = args.work / "cloud" / args.cloud_run / config["provider"]
        output.mkdir(parents=True, exist_ok=True)
        report = assess(output, args.oracle, config, case_ids)
        save(output / "quality.json", report)
        test_env = dict(
            os.environ, INVOICE_CLOUD_OUTPUT=str(output), INVOICE_CLOUD_PROVIDER=config["provider"]
        )
        subprocess.run(
            [
                sys.executable,
                "-m",
                "pytest",
                "tests/test_cloud.py",
                "-q",
                f"--junitxml={output}/tests.xml",
            ],
            check=True,
            env=test_env,
        )
        check_report(output / "tests.xml")
        print(json.dumps(report, indent=2))
    elif args.command == "quality":
        from .quality import assess

        config = json.loads((args.credentials / "query.json").read_text())
        report = assess(args.work / config["provider"] / config["namespace"], args.oracle, config)
        report.update(
            python=platform.python_version(),
            container_platform=platform.platform(),
            architecture=platform.machine(),
        )
        save(args.work / config["provider"] / config["namespace"] / "quality.json", report)
        print(json.dumps(report, indent=2))
        if report["failures"]:
            raise SystemExit(2)


if __name__ == "__main__":
    main()
