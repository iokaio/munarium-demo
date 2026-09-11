# SPDX-License-Identifier: Apache-2.0
import json
import os
import subprocess
import sys
from pathlib import Path

import httpx
import pytest
from helpers import assess, credentials
from munarium_client import ForbiddenError, MunariumError

from bench_demo.fixtures import SETTINGS, read, save
from bench_demo.metrics import report, score
from bench_demo.server import query_client
from bench_demo.workflow import run

ROOT = Path(os.environ["BENCH_REPORT_DIR"])


def calls():
    return httpx.get("http://provider-fixture:11434/state").json()["calls"]


@pytest.mark.parametrize("number", range(1, 9))
def test_fixed_workload(number):
    case = f"case-{number:03}"
    before = calls()
    for setting in SETTINGS:
        path = ROOT / "experiments" / case / setting
        record = run("/inputs", credentials(case), path, case, setting)
        measured = score(record, read("/oracle/labels.json"))
        assert not measured["access_leakage"]
        assert record["result"]["completion"] is None
        if setting == "topk":
            assess(record, path)
    assert calls() == before
    for setting in ("baseline", "candidates", "budget"):
        path = ROOT / "experiments" / case / (setting + "-completion")
        record = run("/inputs", credentials(case), path, case, setting, complete=True)
        measured = score(record, read("/oracle/labels.json"))
        assert measured["schema_valid"] and not measured["access_leakage"]
    for replicate in range(2):
        path = ROOT / "experiments" / case / f"completion-{replicate}"
        assess(run("/inputs", credentials(case), path, case, complete=True), path, True)


def test_measured_report_and_setting_change():
    result = report(ROOT / "experiments", "/oracle/labels.json")
    assert len(result["experiments"]) == 72
    assert (
        result["summary"]["topk/retrieval"]["mean_source_recall"]
        > result["summary"]["baseline/retrieval"]["mean_source_recall"]
    )
    assert set(result["summary"]["topk/completion"]["replicates_per_case"].values()) == {2}


def test_completed_experiment_reuse():
    path = ROOT / "experiments/case-001/completion-0"
    before = calls()
    saved = read(path / "record.json")
    assert run("/inputs", "/credentials", path, "case-001", complete=True) == saved
    assert calls() == before
    with pytest.raises(ValueError, match="Experiment changed"):
        run("/inputs", "/credentials", path, "case-001", "baseline", complete=True)


def test_forbidden_sources_and_denied_override():
    api, config = query_client("/credentials")
    with api:
        session = api.sessions.create(config["runbooks"]["topk"])
        result = api.sessions.turn(
            session.session_id, query="Which private retention marker is required?", complete=True
        )
        assert all("restricted/" not in hit.source_path for hit in result.hits)
        assert "LILAC-731" not in result.model_dump_json()
        assert json.loads(result.completion.text)["abstained"]
        before = calls()
        with pytest.raises(ForbiddenError):
            api.sessions.turn(
                session.session_id,
                query="purchasing",
                complete=True,
                model_override={"provider": "not-permitted", "tier": "fast"},
            )
        assert calls() == before
        save(ROOT / "diagnostics/access.json", result.model_dump(mode="json"))


def test_allowed_override_is_explicit():
    path = ROOT / "diagnostics/override"
    record = run("/inputs", "/credentials", path, "case-001", complete=True, override=True)
    assess(record, path, True)
    assert record["result"]["completion"]["was_override"]


def test_lost_stream_recovery(monkeypatch):
    path = ROOT / "diagnostics/lost"
    httpx.get("http://faults:11435/control/drop").raise_for_status()
    try:
        with monkeypatch.context() as patch:
            patch.setenv("MUNARIUM_REST_URL", "http://faults:11435")
            with pytest.raises(MunariumError):
                run("/inputs", "/credentials", path, "case-001", complete=True)
    finally:
        httpx.get("http://faults:11435/control/normal").raise_for_status()
    before = calls()
    assess(
        run("/inputs", "/credentials", path, "case-001", complete=True, recover=True), path, True
    )
    assert calls() == before
    assert read(path / "record.json")["elapsed_seconds"] is None


def test_empty_transcript_does_not_retry():
    path = ROOT / "diagnostics/outage"
    httpx.post(
        "http://provider-fixture:11434/mode", json={"mode": "unavailable"}
    ).raise_for_status()
    try:
        with pytest.raises(MunariumError):
            run("/inputs", "/credentials", path, "case-003", complete=True)
    finally:
        httpx.post("http://provider-fixture:11434/mode", json={"mode": "ok"}).raise_for_status()
    before = calls()
    with pytest.raises(RuntimeError):
        run("/inputs", "/credentials", path, "case-003", complete=True, recover=True)
    assert calls() == before


def test_process_crash_checkpoint():
    path = ROOT / "diagnostics/crash"
    result = subprocess.run(
        [
            sys.executable,
            "-m",
            "bench_demo",
            "crash",
            "--case",
            "case-005",
            "--complete",
            "--work",
            str(path),
        ],
        check=False,
    )
    assert result.returncode == 71
    before = calls()
    assess(
        run("/inputs", "/credentials", path, "case-005", complete=True, recover=True), path, True
    )
    assert calls() == before


def test_fluent_irrelevant_answer_is_measured():
    path = ROOT / "diagnostics/irrelevant"
    httpx.post("http://provider-fixture:11434/mode", json={"mode": "irrelevant"}).raise_for_status()
    try:
        record = run("/inputs", "/credentials", path, "case-001", complete=True)
        measured = score(record, read("/oracle/labels.json"))
        assert measured["citation_resolution"] == 1 and measured["citation_relevance"] == 0
        assert not measured["required_terms_present"]
        save(path / "quality.json", measured)
    finally:
        httpx.post("http://provider-fixture:11434/mode", json={"mode": "ok"}).raise_for_status()
