# SPDX-License-Identifier: Apache-2.0
import os
import time
from pathlib import Path

import pytest
from helpers import assess

from inventory_demo.__main__ import CLOUD_CASES
from inventory_demo.workflow import process

PROVIDER = os.environ["INVENTORY_CLOUD_PROVIDER"]


@pytest.mark.parametrize("number", CLOUD_CASES[PROVIDER])
def test_fresh_online_business_case(number):
    if PROVIDER == "openrouter":
        time.sleep(60)
    case = f"case-{number:03}"
    path = Path(os.environ["INVENTORY_REPORT_DIR"]) / case
    assert not (path / "journal.json").exists(), "Online qualification requires fresh work"
    packet = process("/inputs", "/credentials", path, case)
    assess(packet, case, path)
    assert packet["evidence"]["completion"]["provider"] == PROVIDER
