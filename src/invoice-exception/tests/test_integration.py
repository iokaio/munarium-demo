# SPDX-License-Identifier: Apache-2.0
import json
import os
import sqlite3
from pathlib import Path
from uuid import uuid4

import httpx
import pytest
from munarium_client import ForbiddenError

from invoice_demo.batch import process
from invoice_demo.quality import assess
from invoice_demo.server import client, query_client

pytestmark = pytest.mark.integration
INPUTS = Path("/inputs")
CREDENTIALS = Path("/credentials")
FIXTURE = "http://provider-fixture:11434"


def count():
    response = httpx.get(FIXTURE + "/state")
    response.raise_for_status()
    return response.json()["calls"]


@pytest.fixture(scope="module")
def packets():
    output = Path("/work/integration") / uuid4().hex[:12]
    summary = process(INPUTS, CREDENTIALS, output)
    assert summary["completed"] == 20, summary
    assert not summary["uncertain"] and not summary["unverified"], summary
    return output


def test_end_to_end_arithmetic_grounding_and_abstention(packets):
    config = json.loads((CREDENTIALS / "query.json").read_text())
    report = assess(packets, Path("/oracle"), config)
    assert report["failures"] == []
    assert report["real_model"] is False
    packets_list = [json.loads(path.read_text()) for path in packets.glob("case-*.json")]
    assert len({packet["session_id"] for packet in packets_list}) == 20
    for packet in packets_list:
        assert packet["evidence"]["envelopes"]
        assert packet["evidence"]["skipped"] == []
        assert all(packet["case_id"] in hit["source_path"] for hit in packet["evidence"]["hits"])
        assert all(len(hit["source_content_hash"]) == 64 for hit in packet["evidence"]["hits"])


def test_restart_reuses_completed_work_without_provider_calls(packets):
    before = count()
    summary = process(INPUTS, CREDENTIALS, packets)
    assert summary["reused"] == 20
    assert count() == before
    assert len(list(packets.glob("case-*.json"))) == 20


def test_lost_response_reconciles_saved_server_transcript(packets):
    with sqlite3.connect(packets / "journal.sqlite") as db:
        db.execute("UPDATE jobs SET state='turn_submitted',result=NULL WHERE case_id='case-001'")
    before = count()
    summary = process(INPUTS, CREDENTIALS, packets, reconcile=True)
    assert summary["uncertain"] == 0, summary
    assert summary["completed"] == 20
    assert count() == before


def test_pending_empty_session_never_retries(tmp_path):
    # Simulate a crash just before dispatch; an empty transcript is ambiguous.
    from invoice_demo import batch

    original = batch.query_client

    class StopBeforeTurn:
        def __init__(self, api):
            self.api = api
            self.sessions = self

        def __enter__(self):
            self.api.__enter__()
            return self

        def __exit__(self, *args):
            return self.api.__exit__(*args)

        def create(self, runbook):
            return self.api.sessions.create(runbook)

        def turn(self, *args, **kwargs):
            raise ConnectionError("simulated lost connection before response")

    def wrapped(path):
        api, config = original(path)
        return StopBeforeTurn(api), config

    before = count()
    with pytest.MonkeyPatch.context() as patch:
        patch.setattr(batch, "query_client", wrapped)
        assert process(INPUTS, CREDENTIALS, tmp_path, limit=1)["uncertain"] == 1
    assert process(INPUTS, CREDENTIALS, tmp_path, reconcile=True, limit=1)["uncertain"] == 1
    assert count() == before


def test_scoped_capability_cannot_administer_or_select_other_runbook():
    api, config = query_client(CREDENTIALS)
    with api:
        with pytest.raises(ForbiddenError):
            api.runbooks.apply_shape("apiVersion: invalid")
    names = list(config["runbooks"].values())
    with client(os.environ["MUNARIUM_MGMT_TOKEN"], "test-issuer") as issuer:
        grant = issuer.tokens.mint(
            uid="restricted",
            scopes=["query"],
            runbook_refs=[names[0].rsplit("@", 1)[0]],
            ttl_secs=60,
        )
    with client(grant.token, "restricted") as restricted:
        assert restricted.sessions.create(names[0]).runbook_ref == names[0]
        with pytest.raises(ForbiddenError):
            restricted.sessions.create(names[1])


def test_provider_outage_retains_intent_without_blind_retry(tmp_path):
    try:
        httpx.post(FIXTURE + "/mode", json={"mode": "unavailable"}).raise_for_status()
        summary = process(INPUTS, CREDENTIALS, tmp_path, limit=1)
        assert summary["uncertain"] == 1 or summary["unverified"] == 1
    finally:
        httpx.post(FIXTURE + "/mode", json={"mode": "ok"}).raise_for_status()
    before = count()
    process(INPUTS, CREDENTIALS, tmp_path, reconcile=True, limit=1)
    assert count() == before


def test_unserved_citation_is_not_published_as_validated(tmp_path):
    try:
        httpx.post(FIXTURE + "/mode", json={"mode": "bad_citation"}).raise_for_status()
        summary = process(INPUTS, CREDENTIALS, tmp_path, limit=1)
        assert summary["unverified"] == 1
        packet = json.loads((tmp_path / "case-001.json").read_text())
        assert packet["analysis_status"] == "unverified" and packet["payment_authorized"] is False
    finally:
        httpx.post(FIXTURE + "/mode", json={"mode": "ok"}).raise_for_status()
