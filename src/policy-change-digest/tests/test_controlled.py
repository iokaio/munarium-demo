# SPDX-License-Identifier: Apache-2.0
import os
import subprocess
import sys
import time
from pathlib import Path

import httpx
import pytest
from helpers import assess
from munarium_client import ForbiddenError, MunariumError, UnauthenticatedError

from digest_demo.fixtures import read, save
from digest_demo.server import client, query_client
from digest_demo.workflow import process

ROOT = Path(os.environ["DIGEST_REPORT_DIR"])


def calls():
    return httpx.get("http://provider-fixture:11434/state").json()["calls"]


@pytest.mark.parametrize(
    "number", sorted(int(case.split("-")[1]) for case in read("/oracle/expected.json"))
)
def test_independent_business_case(number):
    case = f"case-{number:03}"
    path = ROOT / case
    assess(process("/inputs", "/credentials", path, case), case, path)


def test_completed_pair_is_reused():
    path = ROOT / "case-001"
    before = calls()
    journal = read(path / "journal.json")
    process("/inputs", "/credentials", path, "case-001")
    assert calls() == before
    assert read(path / "journal.json") == journal
    subprocess.run([sys.executable, "-m", "digest_demo", "batch", "--work", str(ROOT)], check=True)
    assert set(read(ROOT / "batch.json").values()) == {"validated_draft"}
    assert calls() == before


def test_revision_specific_retrieval():
    api, config = query_client(Path("/credentials"))
    before = calls()
    with api:
        for revision, suffix in (("r1", "before.md"), ("r2", "after.md")):
            session = api.sessions.create(config["revision_runbooks"]["case-001"][revision])
            result = api.sessions.turn(
                session.session_id, query="purchasing approval Rule", complete=False
            )
            assert result.hits and all(h.source_path.endswith(suffix) for h in result.hits)
            assert result.completion is None
            save(ROOT / (revision + "-retrieval.json"), result.model_dump(mode="json"))
    assert calls() == before


def test_access_and_expiry():
    config = read("/credentials/query.json")
    allowed = config["runbooks"]["case-001"]
    with client(os.environ["MUNARIUM_MGMT_TOKEN"], "issuer") as issuer:
        grant = issuer.tokens.mint(
            uid="bounded", scopes=["query"], runbook_refs=[allowed.rsplit("@", 1)[0]], ttl_secs=1
        )
    with client(grant.token, "bounded") as api:
        with pytest.raises(ForbiddenError):
            api.sessions.create(config["runbooks"]["case-002"])
        with pytest.raises(ForbiddenError):
            api.runbooks.apply_shape("invalid")
        session = api.sessions.create(allowed)
        time.sleep(33)
        with pytest.raises(UnauthenticatedError):
            api.sessions.get(session.session_id)


def test_lost_stream_and_explicit_recovery(monkeypatch):
    path = ROOT / "lost-stream"
    httpx.get("http://faults:11435/control/drop").raise_for_status()
    try:
        with monkeypatch.context() as patch:
            patch.setenv("MUNARIUM_REST_URL", "http://faults:11435")
            with pytest.raises(MunariumError):
                process("/inputs", "/credentials", path, "case-002")
    finally:
        httpx.get("http://faults:11435/control/normal").raise_for_status()
    assert read(path / "journal.json")["state"] == "uncertain"
    before = calls()
    with pytest.raises(RuntimeError, match="Uncertain"):
        process("/inputs", "/credentials", path, "case-002")
    assess(process("/inputs", "/credentials", path, "case-002", recover=True), "case-002", path)
    assert calls() == before and read(path / "journal.json")["recovered"]


def test_provider_outage_never_blindly_replays():
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


def test_crash_after_server_response():
    path = ROOT / "crashed"
    result = subprocess.run(
        [sys.executable, "-m", "digest_demo", "crash", "--work", str(path), "--case", "case-004"],
        check=False,
    )
    assert result.returncode == 71
    before = calls()
    assess(process("/inputs", "/credentials", path, "case-004", recover=True), "case-004", path)
    assert calls() == before


def test_fluent_irrelevant_citation_fails():
    path = ROOT / "irrelevant"
    httpx.post("http://provider-fixture:11434/mode", json={"mode": "irrelevant"}).raise_for_status()
    try:
        packet = process("/inputs", "/credentials", path, "case-005")
        assert packet["analysis_status"] == "unverified" and packet["errors"]
        assert "Candidate checklist-005" not in (path / "digest.md").read_text()
    finally:
        httpx.post("http://provider-fixture:11434/mode", json={"mode": "ok"}).raise_for_status()


def test_changed_pair_cannot_reuse_work():
    with pytest.raises(ValueError, match="Changed revision pair"):
        process("/inputs", "/credentials", ROOT / "case-001", "case-002")
