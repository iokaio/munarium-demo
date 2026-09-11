# SPDX-License-Identifier: Apache-2.0
import json
import os
import subprocess
import sys

import pytest
from munarium_client.models import TurnResult

from invoice_demo.accounting import calculate, integer, load_cases
from invoice_demo.batch import export_packet, validate_answer
from invoice_demo.cloud_plan import CLOUD_CASES, select_cases
from invoice_demo.fixtures import generate, verify
from invoice_demo.quality import assess


def test_seeded_generation_is_byte_identical_and_separate(tmp_path):
    one = generate(tmp_path / "one", tmp_path / "oracle1")
    two = generate(tmp_path / "two", tmp_path / "oracle2")
    assert one == two
    for relative in [*one["files"], "manifest.json"]:
        assert (tmp_path / "one" / relative).read_bytes() == (
            tmp_path / "two" / relative
        ).read_bytes()
    assert not list((tmp_path / "one").rglob("expected.json"))
    assert one["case_count"] == 20
    other = generate(tmp_path / "other", tmp_path / "oracle3", seed=112)
    assert one["files"] != other["files"]


def test_accounting_matches_independent_scenario_oracle(tmp_path):
    generate(tmp_path / "inputs", tmp_path / "oracle")
    expected = json.loads((tmp_path / "oracle/expected.json").read_text())
    for case in load_cases(tmp_path / "inputs"):
        result = calculate(case)
        for field in ("exceptions", "computed_total_cents", "stated_total_cents", "receipt_status"):
            assert result[field] == expected[case["case_id"]][field]
        assert result["payment_authorized"] is False


@pytest.mark.parametrize("profile,seed,groups,cases", [("default", 111, 3, 20), ("heldout", 8111, 3, 20), ("stress", 9111, 30, 182)])
def test_named_profiles_reproduce_inputs_and_oracles(tmp_path, profile, seed, groups, cases):
    first = generate(tmp_path / "one", tmp_path / "oracle-one", seed, groups, profile)
    subprocess.run([sys.executable, "-m", "invoice_demo", "generate", "--inputs", str(tmp_path / "two"), "--oracle", str(tmp_path / "oracle-two")], env=os.environ | {"DEMO_PROFILE": profile}, check=True, capture_output=True)
    second = json.loads((tmp_path / "two/manifest.json").read_text())
    assert first == second
    assert first["case_count"] == cases
    assert (tmp_path / "oracle-one/expected.json").read_bytes() == (tmp_path / "oracle-two/expected.json").read_bytes()
    expected = json.loads((tmp_path / "oracle-one/expected.json").read_text())
    for case in load_cases(tmp_path / "one"):
        actual = calculate(case)
        for field in ("exceptions", "computed_total_cents", "stated_total_cents", "receipt_status"):
            assert actual[field] == expected[case["case_id"]][field]


def test_cloud_selection_keeps_duplicate_detection_and_covers_distinct_scenarios(tmp_path):
    generate(tmp_path / "inputs", tmp_path / "oracle")
    all_cases = load_cases(tmp_path / "inputs")
    assigned = [case_id for group in CLOUD_CASES.values() for case_id in group]
    assert all(len(group) >= 2 for group in CLOUD_CASES.values())
    assert len(assigned) == len(set(assigned))
    duplicate = select_cases(all_cases, ("case-019",))[0]
    assert calculate(duplicate)["exceptions"] == ["duplicate_invoice"]
    with pytest.raises(ValueError, match="absent"):
        select_cases(all_cases, ("case-999",))
    with pytest.raises(ValueError, match="nonempty"):
        select_cases(all_cases, ())


def test_partial_cloud_assessment_requires_every_assigned_packet(tmp_path):
    generate(tmp_path / "inputs", tmp_path / "oracle")
    report = assess(
        tmp_path / "outputs",
        tmp_path / "oracle",
        {"provider": "openai", "model": "test"},
        CLOUD_CASES["openai"],
    )
    assert report["cases"] == 3
    assert report["failures"] == [
        "case-001: missing packet", "case-003: missing packet", "case-005: missing packet"
    ]


@pytest.mark.parametrize("provider,model", [("anthropic", "fixture"), ("openai", "wrong-model")])
def test_cloud_assessment_rejects_wrong_provider_or_model(tmp_path, provider, model):
    generate(tmp_path / "inputs", tmp_path / "oracle")
    facts = calculate(load_cases(tmp_path / "inputs")[0])
    result = turn(
        {
            "explanation": "Matched invoice for review.",
            "citations": ["case/policy.md"],
            "recommended_action": "acknowledge",
            "receipt_status": "received",
        }
    )
    result.completion.provider = provider
    result.completion.model = model
    export_packet(tmp_path / "outputs", facts, result)
    report = assess(
        tmp_path / "outputs",
        tmp_path / "oracle",
        {"provider": "openai", "model": "fixture"},
        ("case-001",),
    )
    assert report["failures"] == [
        "case-001: provider/model identity differs from configured selection"
    ]


@pytest.mark.parametrize("value", [True, -1, "1.25", "NaN", "-0", "1e6", 10**13])
def test_reject_invalid_money_and_quantities(value):
    with pytest.raises(ValueError):
        integer(value, "unit_price_cents")


def test_price_tolerance_boundary_and_combined_exceptions(tmp_path):
    generate(tmp_path / "inputs", tmp_path / "oracle")
    case = load_cases(tmp_path / "inputs")[0]
    case["unit_price_cents"] += 50
    case["stated_total_cents"] = case["quantity"] * case["unit_price_cents"]
    assert calculate(case)["exceptions"] == []
    case["unit_price_cents"] += 1
    case["receipt"] = None
    case["duplicate"] = True
    assert calculate(case)["exceptions"] == [
        "duplicate_invoice",
        "missing_receipt",
        "price_variance",
        "total_mismatch",
    ]


def test_fixture_tampering_and_overwrite_are_refused(tmp_path):
    generate(tmp_path / "inputs", tmp_path / "oracle")
    (tmp_path / "inputs/case-001/invoice.txt").write_text("tampered")
    with pytest.raises(ValueError, match="hashes"):
        verify(tmp_path / "inputs")
    with pytest.raises(ValueError, match="empty"):
        generate(tmp_path / "inputs", tmp_path / "oracle")
    with pytest.raises(ValueError, match="separate"):
        generate(tmp_path / "nested", tmp_path / "nested/oracle")


def test_wrong_order_cannot_be_used_as_receipt(tmp_path):
    generate(tmp_path / "inputs", tmp_path / "oracle")
    (tmp_path / "inputs/case-001/receipt.json").write_text(
        '{"fictional":true,"order_id":"OTHER","quantity":10}'
    )
    with pytest.raises(ValueError, match="different order"):
        load_cases(tmp_path / "inputs")


def turn(answer):
    return TurnResult.model_validate(
        {
            "session_id": "s",
            "ordinal": 1,
            "collections_searched": ["case"],
            "hits": [
                {
                    "collection": "case",
                    "chunk_id": "c",
                    "source_id": "source",
                    "source_path": "case/policy.md",
                    "source_content_hash": "a" * 64,
                    "text": "Missing receipt requires review",
                    "score": 0.5,
                }
            ],
            "envelopes": [],
            "completion": {
                "provider": "ollama",
                "model": "fixture",
                "was_override": False,
                "text": json.dumps(answer),
                "input_tokens": 1,
                "output_tokens": 1,
            },
        }
    )


@pytest.mark.parametrize(
    "field,value",
    [
        ("citations", ["unserved/policy.md"]),
        ("receipt_status", "received"),
        ("recommended_action", "pay"),
        ("explanation", ""),
        ("citations", []),
    ],
)
def test_generated_fields_cannot_override_business_evidence(field, value):
    answer = {
        "explanation": "Missing receipt; request evidence.",
        "citations": ["case/policy.md"],
        "recommended_action": "review",
        "receipt_status": "missing",
    }
    answer[field] = value
    _, errors = validate_answer(
        turn(answer), {"exceptions": ["missing_receipt"], "receipt_status": "missing"}
    )
    assert errors


def test_plain_json_schema_not_arbitrary_model_fields():
    _, errors = validate_answer(
        turn({"payment_authorized": True}), {"exceptions": [], "receipt_status": "received"}
    )
    assert errors
