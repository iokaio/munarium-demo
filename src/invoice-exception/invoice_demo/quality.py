# SPDX-License-Identifier: Apache-2.0
"""Independent oracle checks and a limited, explicit prose rubric."""

import json
from pathlib import Path


def assess(
    output: Path, oracle: Path, config: dict, case_ids: tuple[str, ...] | None = None
) -> dict:
    expected = json.loads((oracle / "expected.json").read_text())
    if case_ids is not None:
        if not case_ids or set(case_ids) - expected.keys():
            raise ValueError("Requested acceptance cases are missing from the oracle")
        expected = {case_id: expected[case_id] for case_id in case_ids}
    failures = []
    usage = {"input_tokens": 0, "output_tokens": 0}
    for case_id, truth in expected.items():
        path = output / (case_id + ".json")
        if not path.exists():
            failures.append(case_id + ": missing packet")
            continue
        packet = json.loads(path.read_text())
        for field in ("exceptions", "computed_total_cents", "stated_total_cents", "receipt_status"):
            if packet[field] != truth[field]:
                failures.append(case_id + ": incorrect " + field)
        if packet["analysis_status"] != "validated_draft" or packet["payment_authorized"]:
            failures.append(case_id + ": unverified or unauthorized outcome")
        completion = packet["evidence"]["completion"]
        expected_provider = "ollama" if config["provider"] == "fixture" else config["provider"]
        if completion["provider"] != expected_provider or completion["model"] != config["model"]:
            failures.append(case_id + ": provider/model identity differs from configured selection")
        for name in usage:
            usage[name] += completion[name]
        answer = packet.get("answer") or {}
        prose = answer.get("explanation", "").lower().replace("_", " ")
        if truth["receipt_status"] == "missing" and not (
            "receipt" in prose
            and any(
                term in prose for term in ("missing", "insufficient", "no receipt", "unavailable")
            )
        ):
            failures.append(case_id + ": missing-receipt abstention not explained")
        served = {hit["source_path"] for hit in packet["evidence"]["hits"]}
        if not answer.get("citations") or any(path not in served for path in answer["citations"]):
            failures.append(case_id + ": unresolved citation")
        if not any(
            path.endswith(truth["required_source_suffix"]) for path in answer.get("citations", [])
        ):
            failures.append(case_id + ": purchasing policy not cited")
    return {
        "provider": config["provider"],
        "model": config["model"],
        "cases": len(expected),
        "case_ids": list(expected),
        "failures": failures,
        "usage": usage,
        "real_model": config["provider"] != "fixture",
        "rubric_limit": "Checks arithmetic, evidence status, source resolution, and missing-receipt wording; human review must judge full semantic support.",
    }
