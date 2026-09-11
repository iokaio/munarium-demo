# SPDX-License-Identifier: Apache-2.0
import os
from pathlib import Path

import httpx

from digest_demo.fixtures import read
from digest_demo.workflow import process


def test_completed_digest_after_server_restart():
    path = Path(os.environ["DIGEST_REPORT_DIR"]).parent / "controlled/case-001"
    saved = read(path / "digest.json")
    before = httpx.get("http://provider-fixture:11434/state").json()["calls"]
    assert process("/inputs", "/credentials", path, "case-001") == saved
    assert httpx.get("http://provider-fixture:11434/state").json()["calls"] == before
