# SPDX-License-Identifier: Apache-2.0
"""Durable turn intent, transcript reconciliation, and review packet exports."""

from __future__ import annotations

import csv
import io
import json
import os
import sqlite3
import time
from pathlib import Path
from uuid import uuid4

from munarium_client import MunariumError
from munarium_client.models import TurnResult

from .accounting import calculate, load_cases
from .cloud_plan import select_cases
from .fixtures import digest, encode, verify
from .server import query_client


def atomic_write(path: Path, raw: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + uuid4().hex + ".tmp")
    with temporary.open("wb") as stream:
        stream.write(raw)
        stream.flush()
        os.fsync(stream.fileno())
    temporary.replace(path)


def validate_answer(result: TurnResult, facts: dict) -> tuple[dict | None, list[str]]:
    errors = []
    if not result.completion:
        return None, ["No completion returned"]
    verification = result.completion.verification
    if verification and verification.violations:
        errors.append("Server completion verification has unresolved violations")
    try:
        text = result.completion.text.strip()
        if text.startswith("```json\n") and text.endswith("\n```"):
            text = text[8:-4]
        answer = json.loads(text)
        if not isinstance(answer, dict) or set(answer) != {
            "explanation",
            "citations",
            "recommended_action",
            "receipt_status",
        }:
            raise ValueError("Unexpected fields")
        if (
            not isinstance(answer["explanation"], str)
            or not 1 <= len(answer["explanation"]) <= 3000
        ):
            raise ValueError("Invalid explanation")
        citations = answer["citations"]
        if (
            not isinstance(citations, list)
            or not citations
            or not all(isinstance(item, str) for item in citations)
        ):
            raise ValueError("Missing citations")
        labels = {f"{hit.collection}/{hit.chunk_id}": hit.source_path for hit in result.hits}
        # Server sends collection/chunk labels to the model, not filenames.
        citations = [labels.get(citation, citation) for citation in citations]
        answer["citations"] = citations
        served = {hit.source_path for hit in result.hits}
        if any(citation not in served for citation in citations) or not any(
            citation.endswith("/policy.md") for citation in citations
        ):
            errors.append(
                "Citations must resolve to retrieved sources and include the purchasing policy"
            )
        if answer["recommended_action"] != ("review" if facts["exceptions"] else "acknowledge"):
            errors.append("Proposed action disagrees with deterministic checks")
        if answer["receipt_status"] != facts["receipt_status"]:
            errors.append("Generated delivery status disagrees with receipt evidence")
        return answer, errors
    except (ValueError, TypeError, KeyError):
        return None, ["Completion is not the required review-packet JSON"]


def export_packet(output: Path, facts: dict, result: TurnResult) -> dict:
    answer, errors = validate_answer(result, facts)
    evidence = result.model_dump(mode="json")
    if evidence.get("recovered_from_transcript"):
        # 1.1.1 stores hits/envelopes but not the live response's skipped list.
        evidence.pop("skipped", None)
    packet = {
        **facts,
        "session_id": result.session_id,
        "analysis_status": "validated_draft" if not errors else "unverified",
        "answer": answer,
        "validation_errors": errors,
        "evidence": evidence,
    }
    atomic_write(output / (facts["case_id"] + ".json"), encode(packet))
    explanation = (
        answer["explanation"]
        if answer and not errors
        else "Analysis requires review: " + "; ".join(errors)
    )
    markdown = (
        f"# Fictional invoice review: {facts['invoice_number']}\n\nBusiness status: {facts['business_status']}. Analysis: {packet['analysis_status']}. Payment authorized: false.\n\nComputed total: {facts['computed_total_cents']} cents; stated total: {facts['stated_total_cents']} cents.\n\n{explanation}\n\nSources:\n\n"
        + "".join(
            f"- {hit.source_path} (SHA-256 {hit.source_content_hash})\n" for hit in result.hits
        )
    )
    atomic_write(output / (facts["case_id"] + ".md"), markdown.encode())
    return packet


def process(
    inputs: Path,
    credentials: Path,
    output: Path,
    reconcile: bool = False,
    limit: int | None = None,
    case_ids: tuple[str, ...] | None = None,
) -> dict:
    verify(inputs)
    output.mkdir(parents=True, exist_ok=True)
    cases = load_cases(inputs)
    selected = select_cases(cases, case_ids)
    manifest_hash = digest((inputs / "manifest.json").read_bytes())
    connection, config = query_client(credentials)
    if config["fixture_revision"] != manifest_hash[:12]:
        connection.close()
        raise ValueError("Bootstrap credentials belong to a different fixture revision")
    summary = {"completed": 0, "reused": 0, "uncertain": 0, "unverified": 0, "failed": 0}
    with connection as api, sqlite3.connect(output / "journal.sqlite", timeout=30) as db:
        db.execute("PRAGMA synchronous=FULL")
        db.execute(
            "CREATE TABLE IF NOT EXISTS jobs (job TEXT PRIMARY KEY, case_id TEXT, state TEXT, session_id TEXT, query TEXT, result TEXT, error TEXT)"
        )
        for case in selected[:limit]:
            facts = calculate(case)
            runbook = config["runbooks"][case["case_id"]]
            job = digest(
                encode(
                    {
                        "manifest": manifest_hash,
                        "case": case,
                        "runbook": runbook,
                        "provider": config["provider"],
                        "model": config["model"],
                    }
                )
            )
            query = (
                "Explain the fictional invoice exception using the purchasing policy. DETERMINISTIC_FACTS="
                + json.dumps(facts, sort_keys=True)
            )
            with db:
                inserted = db.execute(
                    "INSERT OR IGNORE INTO jobs(job,case_id,state,query) VALUES(?,?,?,?)",
                    (job, case["case_id"], "creating_session", query),
                ).rowcount
            row = db.execute(
                "SELECT state,session_id,query,result FROM jobs WHERE job=?", (job,)
            ).fetchone()
            state, session_id, saved_query, saved_result = row
            result = None
            if saved_result:
                result = TurnResult.model_validate_json(saved_result)
                summary["reused"] += 1
            elif not inserted:
                if reconcile and session_id:
                    try:
                        transcript = api.sessions.get(session_id)
                        turns = [turn for turn in transcript.turns if turn.query == saved_query]
                        if len(turns) == 1 and turns[0].completion:
                            turn = turns[0]
                            completion = dict(turn.completion)
                            completion["was_override"] = completion["resolved"]["was_override"]
                            result = TurnResult(
                                session_id=session_id,
                                ordinal=turn.ordinal,
                                collections_searched=turn.collections_searched,
                                hits=turn.hits or [],
                                envelopes=turn.envelope or [],
                                completion=completion,
                                recovered_from_transcript=True,
                            )
                    except (MunariumError, ValueError, OSError, KeyError) as error:
                        with db:
                            db.execute(
                                "UPDATE jobs SET error=? WHERE job=?", (type(error).__name__, job)
                            )
                if result is None:
                    summary["uncertain"] += 1
                    continue  # An empty transcript is never permission to resubmit.
            else:
                try:
                    if config["provider"] == "openrouter":
                        time.sleep(60)
                    session = api.sessions.create(runbook)
                    session_id = session.session_id
                    with db:
                        db.execute(
                            "UPDATE jobs SET state='turn_submitted',session_id=? WHERE job=?",
                            (session_id, job),
                        )
                    result = api.sessions.turn(session_id, query=query, complete=True)
                except (MunariumError, ValueError, OSError) as error:
                    # Store the category, not an upstream message that could contain secrets.
                    with db:
                        db.execute(
                            "UPDATE jobs SET error=? WHERE job=?", (type(error).__name__, job)
                        )
                    summary["uncertain"] += 1
                    continue
            with db:
                db.execute(
                    "UPDATE jobs SET state='response_saved',result=? WHERE job=?",
                    (result.model_dump_json(), job),
                )
            packet = export_packet(output, facts, result)
            with db:
                db.execute("UPDATE jobs SET state='done' WHERE job=?", (job,))
            summary["completed"] += 1
            summary["unverified"] += bool(packet["validation_errors"])
        rows = []
        for _, case_id, state, session_id in db.execute(
            "SELECT job,case_id,state,session_id FROM jobs ORDER BY case_id"
        ):
            facts = calculate(next(case for case in cases if case["case_id"] == case_id))
            rows.append(
                {
                    "case_id": case_id,
                    "invoice_number": facts["invoice_number"],
                    "state": state,
                    "business_status": facts["business_status"],
                    "computed_total_cents": facts["computed_total_cents"],
                    "stated_total_cents": facts["stated_total_cents"],
                    "exceptions": ";".join(facts["exceptions"]),
                    "session_id": session_id or "",
                }
            )
        csv_text = io.StringIO(newline="")
        writer = csv.DictWriter(
            csv_text,
            fieldnames=[
                "case_id",
                "invoice_number",
                "state",
                "business_status",
                "computed_total_cents",
                "stated_total_cents",
                "exceptions",
                "session_id",
            ],
            lineterminator="\n",
        )
        writer.writeheader()
        writer.writerows(rows)
        atomic_write(output / "summary.csv", csv_text.getvalue().encode())
        atomic_write(output / "summary.json", encode(summary))
    return summary
