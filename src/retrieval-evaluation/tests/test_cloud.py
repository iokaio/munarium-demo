# SPDX-License-Identifier: Apache-2.0
import os
import time
from pathlib import Path

import pytest
from helpers import assess, credentials

from bench_demo.__main__ import CLOUD_CASES
from bench_demo.metrics import report
from bench_demo.workflow import run

PROVIDER = os.environ["BENCH_CLOUD_PROVIDER"]
ROOT = Path(os.environ["BENCH_REPORT_DIR"])


@pytest.mark.parametrize("number", CLOUD_CASES[PROVIDER])
def test_online_repeated_workload(number):
    case = f"case-{number:03}"
    for replicate in range(2):
        if PROVIDER == "openrouter":
            time.sleep(60)
        path = ROOT / "experiments" / case / f"replicate-{replicate}"
        assert not (path / "journal.json").exists(), "Qualification requires fresh sessions"
        record = run("/inputs", credentials(case), path, case, complete=True)
        assess(record, path, True)
        assert record["result"]["completion"]["provider"] == PROVIDER
    report(ROOT / "experiments", "/oracle/labels.json")
