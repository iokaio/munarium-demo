# SPDX-License-Identifier: Apache-2.0
from digest_demo.fixtures import read, save


def assess(packet, case, path):
    oracle = read("/oracle/expected.json")[case]
    assert packet["analysis_status"] == "validated_draft", packet["errors"]
    assert packet["answer"]["status"] == oracle["status"]
    assert (
        sorted(c["checklist_id"] for c in packet["answer"]["candidates"]) == oracle["candidate_ids"]
    )
    assert packet["comparison"]["old_rule"] == oracle["old_rule"] + "."
    assert packet["comparison"]["new_rule"] == (
        oracle["new_rule"] + "." if oracle["new_rule"] else None
    )
    assert not packet["exhaustive"]
    hits = packet["evidence"]["hits"]
    assert hits and all(case in h["source_path"] for h in hits)
    assert any(h["source_path"].endswith("before.md") for h in hits)
    if oracle["new_rule"]:
        assert any(h["source_path"].endswith("after.md") for h in hits)
    assert packet["evidence"]["envelopes"]
    save(
        path / "quality.json",
        {
            "passed": True,
            "case": case,
            "status": oracle["status"],
            "candidates": oracle["candidate_ids"],
            "model": packet["evidence"]["completion"],
            "exhaustive": False,
        },
    )
