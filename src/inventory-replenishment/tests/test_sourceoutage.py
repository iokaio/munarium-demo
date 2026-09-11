# SPDX-License-Identifier: Apache-2.0
import os
from pathlib import Path

from inventory_demo.workflow import process


def test_real_inventory_database_outage():
    path = Path(os.environ["INVENTORY_REPORT_DIR"]) / "missing-source"
    packet = process("/inputs", "/credentials", path, "case-001")
    assert packet["inventory"]["status"] == "unavailable"
    assert packet["inventory"]["exact_count"] is None
    assert packet["evidence"]["hits"], "Procedures remain available during inventory outage"
    register = next(
        layer for layer in packet["evidence"]["hierarchy"]["layers"] if layer["layer"] == "register"
    )
    assert register["block"] == "refusal" and register["refusal_code"]
    assert not register["supports_completeness"]
