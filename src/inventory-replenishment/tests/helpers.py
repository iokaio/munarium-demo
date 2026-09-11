# SPDX-License-Identifier: Apache-2.0
from inventory_demo.fixtures import encode, read, save


def assess(packet, case, path):
    expected = read("/oracle/expected.json")[case]
    assert packet["analysis_status"] == "validated_draft", packet["errors"]
    assert packet["inventory"]["status"] == "complete", packet["inventory"]
    assert packet["inventory"]["exact_count"] == expected["count"]
    assert packet["inventory"]["rows"] == expected["rows"]
    assert b"RESTRICTED_STOCK_SENTINEL" not in encode(packet)
    assert packet["inventory"]["manifest"]["logical_result_hash"].startswith("sha256:")
    assert packet["inventory"]["manifest"]["artifact_hash"].startswith("sha256:")
    assert packet["evidence"]["hits"]
    save(
        path / "quality.json",
        {
            "passed": True,
            "case": case,
            "exact_count": expected["count"],
            "model": packet["evidence"]["completion"],
            "logical_result_hash": packet["inventory"]["manifest"]["logical_result_hash"],
        },
    )
