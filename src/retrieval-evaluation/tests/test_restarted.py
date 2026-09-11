# SPDX-License-Identifier: Apache-2.0
import os
from pathlib import Path

import httpx

from bench_demo.fixtures import read
from bench_demo.workflow import run


def test_saved_experiment_after_server_restart():
    path = (
        Path(os.environ["BENCH_REPORT_DIR"]).parent / "controlled/experiments/case-001/completion-0"
    )
    before = httpx.get("http://provider-fixture:11434/state").json()["calls"]
    assert run("/inputs", "/credentials", path, "case-001", complete=True) == read(
        path / "record.json"
    )
    assert httpx.get("http://provider-fixture:11434/state").json()["calls"] == before
