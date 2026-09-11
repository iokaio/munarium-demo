# SPDX-License-Identifier: Apache-2.0
import dataclasses
import fcntl
import os
import subprocess
import sys
import time
from pathlib import Path

import httpx
import pytest
import yaml
from helpers import assess
from munarium_client import ForbiddenError, MunariumError, UnauthenticatedError

from inventory_demo.fixtures import read, save
from inventory_demo.server import client, matrix, query_client
from inventory_demo.workflow import process, resolve

ROOT = Path(os.environ["INVENTORY_REPORT_DIR"])


def calls():
    return httpx.get("http://provider-fixture:11434/state").json()["calls"]


@pytest.mark.parametrize("number", range(1, 9))
def test_independent_business_case(number):
    case = f"case-{number:03}"
    assess(process("/inputs", "/credentials", ROOT / case, case), case, ROOT / case)


def test_completed_work_and_cli_reuse():
    path = ROOT / "case-001"
    before = calls()
    saved = read(path / "inventory.json")
    assert process("/inputs", "/credentials", path, "case-001") == saved
    subprocess.run(
        [sys.executable, "-m", "inventory_demo", "process", "--work", str(path)], check=True
    )
    assert calls() == before
    with pytest.raises(ValueError, match="Changed"):
        process("/inputs", "/credentials", path, "case-002")


@pytest.mark.parametrize("variant", ["limited", "stale"])
def test_incomplete_inventory_cannot_claim_exact_count(variant):
    packet = process("/inputs", "/credentials", ROOT / variant, "case-001", variant=variant)
    assert packet["inventory"]["status"] == ("truncated" if variant == "limited" else "stale"), (
        packet
    )
    assert packet["inventory"]["exact_count"] is None
    assert packet["inventory"]["rows"]
    assert "actionable advice withheld" in (ROOT / variant / "inventory.md").read_text()


def test_matrix_verified_question_rejects_wrong_oracle():
    contract = yaml.safe_load(Path("/app/matrix/contract.yaml").read_text())
    contract["metadata"]["name"] = "deliberately-wrong-count"
    contract["spec"]["verifiedQuestions"][0]["expect"]["rows"] = 999
    with matrix() as mx:
        mx.apply(yaml.safe_dump(contract))
        result = mx.verify("deliberately-wrong-count")
        save(ROOT / "wrong-count.json", dataclasses.asdict(result))
        assert result.failed == 1 and result.passed == 0


def test_query_scope_and_real_expiry():
    config = read("/credentials/query.json")
    with client(os.environ["MUNARIUM_MGMT_TOKEN"], "issuer") as issuer:
        grant = issuer.tokens.mint(
            uid="bounded",
            scopes=["query"],
            runbook_refs=[config["runbooks"]["case-001"].rsplit("@", 1)[0]],
            ttl_secs=1,
        )
    with client(grant.token, "bounded") as api:
        with pytest.raises(ForbiddenError):
            api.sessions.create(config["variants"]["privileged"])
        with pytest.raises(ForbiddenError):
            api.runbooks.apply_shape("invalid")
        session = api.sessions.create(config["runbooks"]["case-001"])
        time.sleep(33)
        with pytest.raises(UnauthenticatedError):
            api.sessions.get(session.session_id)


def test_evidence_clearance():
    config = read("/credentials/query.json")
    with client(os.environ["MUNARIUM_MGMT_TOKEN"], "issuer") as issuer:
        grant = issuer.tokens.mint(
            uid="privileged-planner",
            access_level=2,
            scopes=["query", "evidence"],
            runbook_refs=[config["variants"]["privileged"].rsplit("@", 1)[0]],
            ttl_secs=120,
        )
    with client(grant.token, "privileged-planner") as api:
        session = api.sessions.create(config["variants"]["privileged"])
        result = api.sessions.turn(
            session.session_id, query="below threshold stock", complete=False
        )
        save(ROOT / "privileged-retrieval.json", result.model_dump(mode="json"))
        evidence_id = next(
            layer.evidence_id for layer in result.hierarchy.layers if layer.layer == "register"
        )
        assert api.evidence.get(evidence_id)["authorization_class"]["access_level"] == 2
    api, _ = query_client("/credentials")
    with api, pytest.raises(MunariumError) as caught:
        api.evidence.get(evidence_id)
    assert getattr(caught.value, "status", None) == 403


def test_expired_artifact_is_explicitly_unresolved():
    path = ROOT / "expiry"
    packet = process("/inputs", "/credentials", path, "case-001", variant="expiry")
    ident = packet["inventory"]["evidence_id"]
    # The SDK intentionally has no destructive evidence operation. The test
    # operator purges only this newly sealed disposable artifact via REST.
    response = httpx.delete(
        "http://server:8080/v1/evidence/" + ident,
        headers={
            "Authorization": "Bearer " + os.environ["MUNARIUM_MGMT_TOKEN"],
            "x-munarium-uid": "retention-test",
        },
    )
    response.raise_for_status()
    api, _ = query_client("/credentials")
    with api:
        unresolved = resolve(
            api, ident, read("/credentials/query.json")["contracts"]["expiry"], "2026-09-11"
        )
    assert unresolved["status"] == "unresolved_citation" and unresolved["exact_count"] is None
    assert unresolved["http_status"] == 410
    repeated = process("/inputs", "/credentials", path, "case-001", variant="expiry")
    assert repeated["inventory"]["status"] == "unresolved_citation"
    save(ROOT / "expired-citation.json", unresolved)


def test_lost_stream_recovers_without_new_turn(monkeypatch):
    path = ROOT / "lost-stream"
    httpx.get("http://faults:11435/control/drop").raise_for_status()
    try:
        with monkeypatch.context() as patch:
            patch.setenv("MUNARIUM_REST_URL", "http://faults:11435")
            with pytest.raises(MunariumError):
                process("/inputs", "/credentials", path, "case-002")
    finally:
        httpx.get("http://faults:11435/control/normal").raise_for_status()
    before = calls()
    with pytest.raises(RuntimeError, match="Uncertain"):
        process("/inputs", "/credentials", path, "case-002")
    packet = process("/inputs", "/credentials", path, "case-002", recover=True)
    assess(packet, "case-002", path)
    assert (
        packet["recovered"]
        and "hierarchy" not in packet["evidence"]
        and "skipped" not in packet["evidence"]
    )
    assert calls() == before


def test_provider_outage_leaves_uncertain_work():
    path = ROOT / "provider-outage"
    httpx.post(
        "http://provider-fixture:11434/mode", json={"mode": "unavailable"}
    ).raise_for_status()
    try:
        with pytest.raises(MunariumError):
            process("/inputs", "/credentials", path, "case-003")
    finally:
        httpx.post("http://provider-fixture:11434/mode", json={"mode": "ok"}).raise_for_status()
    before = calls()
    with pytest.raises(RuntimeError):
        process("/inputs", "/credentials", path, "case-003", recover=True)
    assert calls() == before


def test_crash_checkpoint_and_work_lease():
    path = ROOT / "crashed"
    result = subprocess.run(
        [
            sys.executable,
            "-m",
            "inventory_demo",
            "crash",
            "--work",
            str(path),
            "--case",
            "case-004",
        ],
        check=False,
    )
    assert result.returncode == 71
    save(ROOT / "restart.json", {"work": str(path), "case": "case-004", "calls": calls()})
    with (ROOT / "case-001/.lock").open("a+b") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        with pytest.raises(BlockingIOError):
            process("/inputs", "/credentials", ROOT / "case-001", "case-001")


def test_fabricated_row_citation_is_rejected():
    httpx.post("http://provider-fixture:11434/mode", json={"mode": "irrelevant"}).raise_for_status()
    try:
        packet = process("/inputs", "/credentials", ROOT / "irrelevant", "case-005")
        assert packet["analysis_status"] == "unverified" and packet["errors"]
    finally:
        httpx.post("http://provider-fixture:11434/mode", json={"mode": "ok"}).raise_for_status()


def test_sealed_parameter_binding_cannot_cross_warehouses():
    packet = read(ROOT / "case-001/inventory.json")
    api, _ = query_client("/credentials")
    with api, pytest.raises(ValueError, match="parameter binding"):
        resolve(
            api, packet["inventory"]["evidence_id"], "below-threshold@2", "2026-09-11", "case-002"
        )


def test_empty_complete_result_is_zero():
    packet = process("/inputs", "/credentials", ROOT / "empty", "case-001", variant="empty")
    assert packet["inventory"]["status"] == "complete"
    assert packet["inventory"]["exact_count"] == 0 and packet["inventory"]["rows"] == []
    assert packet["analysis_status"] == "validated_draft" and packet["answer"]["actions"] == []
