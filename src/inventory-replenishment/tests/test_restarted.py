# SPDX-License-Identifier: Apache-2.0
import os
from pathlib import Path

import httpx

from inventory_demo.fixtures import read
from inventory_demo.workflow import process


def test_completed_inventory_after_server_restart():
    path = Path(os.environ["INVENTORY_REPORT_DIR"]).parent / "controlled/case-001"
    saved = read(path / "inventory.json")
    before = httpx.get("http://provider-fixture:11434/state").json()["calls"]
    assert process("/inputs", "/credentials", path, "case-001") == saved
    assert httpx.get("http://provider-fixture:11434/state").json()["calls"] == before


def test_crashed_turn_after_dependency_restart():
    from helpers import assess

    data = read(Path(os.environ["INVENTORY_REPORT_DIR"]).parent / "controlled/restart.json")
    before = httpx.get("http://provider-fixture:11434/state").json()["calls"]
    packet = process("/inputs", "/credentials", Path(data["work"]), data["case"], recover=True)
    assess(packet, data["case"], Path(data["work"]))
    assert packet["recovered"]
    assert httpx.get("http://provider-fixture:11434/state").json()["calls"] == before
