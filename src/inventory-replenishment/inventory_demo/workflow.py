# SPDX-License-Identifier: Apache-2.0
"""Persist the paid turn once; resolve immutable Matrix artifacts via Server."""

import csv
import fcntl
import io
import json
import os
import re
from datetime import date
from pathlib import Path

from munarium_client import MunariumError
from munarium_client.models import TurnResult

from .fixtures import REV, digest, encode, read, save, verify, write
from .server import query_client

QUOTE = "Obtain supplier confirmation before requesting replenishment."
COLUMNS = ["sku", "on_hand", "reorder_level", "constraint_code", "observed_on"]


def resolve(api, evidence_id, contract, as_of, warehouse=None):
    """Read capped pages; no table or total is inferred from document retrieval."""
    try:
        manifest = api.evidence.get(evidence_id)
        if hasattr(manifest, "model_dump"):
            manifest = manifest.model_dump(mode="json")
        if warehouse is not None:
            # Matrix canon@1 parameter identity: names in sorted order, field
            # separator, canonical typed value, record separator. These two
            # declared parameters contain no separator bytes.
            bound_hash = "sha256:" + digest(
                ("as_of\x1f" + as_of + "\x1e" + "warehouse\x1f" + warehouse + "\x1e").encode()
            )
            if manifest["plan"]["bound_parameters_hash"] != bound_hash:
                raise ValueError("Sealed parameter binding does not match this warehouse/date")
        if (
            manifest["source"]["source_id"] != "inventory"
            or manifest["versions"]["query_contract"] != contract
            or manifest["kind"] != "table"
        ):
            raise ValueError("Unexpected sealed source or contract")
        if [c["name"] for c in manifest["schema"]["columns"]] != COLUMNS or manifest["identity"][
            "row_id_rule"
        ] != "keys":
            raise ValueError("Unexpected sealed schema or row identity")
        rows = []
        for _ in range(50):
            page = api.evidence.rows(evidence_id, from_=len(rows), limit=2)
            if page.evidence_id != evidence_id or page.from_ != len(rows):
                raise ValueError("Evidence pagination mismatch")
            rows.extend(page.rows)
            if not page.has_more:
                if page.total != len(rows) or manifest["identity"]["rows"] != len(rows):
                    raise ValueError("Incomplete row paging")
                break
            if not page.rows:
                raise ValueError("Empty intermediate page")
        else:
            raise ValueError("Evidence exceeds application row bound")
        if len({r["sku"] for r in rows}) != len(rows):
            raise ValueError("Duplicate sealed row keys")
        status = "truncated" if manifest["completeness"]["truncated"] else "complete"
        if any(
            not 0 <= (date.fromisoformat(as_of) - date.fromisoformat(r["observed_on"])).days <= 2
            for r in rows
        ):
            status = "stale"
        return {
            "status": status,
            "manifest": manifest,
            "rows": rows,
            "exact_count": len(rows) if status == "complete" else None,
            "evidence_id": evidence_id,
        }
    except MunariumError as error:
        return {
            "status": "unresolved_citation",
            "error_category": type(error).__name__,
            "error_slug": error.slug,
            "http_status": getattr(error, "status", None),
            "evidence_id": evidence_id,
            "rows": [],
            "exact_count": None,
        }


def export(api, inputs, work, journal, result, config):
    raw = result.model_dump(mode="json")
    errors = []
    answer = None
    try:
        completion = result.completion
        if (
            not completion
            or completion.provider
            != ("ollama" if config["provider"] == "fixture" else config["provider"])
            or completion.model != config["model"]
        ):
            raise ValueError("Completion unavailable or identity mismatch")
        answer = json.loads(completion.text.strip().removeprefix("```json\n").removesuffix("\n```"))
        if (
            set(answer) != {"actions", "procedure_quote"}
            or answer["procedure_quote"] != QUOTE
            or not isinstance(answer["actions"], list)
        ):
            raise ValueError("Unsupported answer schema or rule")
        if completion.verification and completion.verification.violations:
            errors.append("Server verification violations")
    except (ValueError, TypeError, AttributeError):
        errors.append("Analysis unavailable or unverified")
    ids = []
    hierarchy = raw.get("hierarchy")
    if hierarchy:
        ids = [
            layer["evidence_id"]
            for layer in hierarchy["layers"]
            if layer["layer"] == "register" and layer.get("evidence_id")
        ]
    elif journal.get("recovered"):
        # The SDK transcript does not contain live hierarchy decisions. Recover
        # only manifest-backed rows explicitly cited by that recorded answer.
        ids = sorted(
            set(
                re.findall(
                    r"evidence/(ev-[A-Za-z0-9-]+)#",
                    result.completion.text if result.completion else "",
                )
            )
        )
        raw.pop("hierarchy", None)
        raw.pop("skipped", None)
    contract = config["contracts"][journal["variant"]]
    stock = (
        resolve(api, ids[0], contract, journal["as_of"], journal["case"])
        if len(ids) == 1
        else {"status": "unavailable", "rows": [], "exact_count": None}
    )
    if (
        hierarchy
        and stock["status"] == "complete"
        and not any(
            layer["layer"] == "register" and layer["supports_completeness"]
            for layer in hierarchy["layers"]
        )
    ):
        stock["status"], stock["exact_count"] = "incomplete", None
    labels = {f"procedures/{hit.chunk_id}": hit for hit in result.hits}
    expected_sha = digest((Path(inputs) / journal["case"] / "procedure.md").read_bytes())
    if any(
        hit.source_content_hash != expected_sha
        or not hit.source_path.startswith(journal["runbook"].rsplit("@", 1)[0] + "/")
        for hit in result.hits
    ):
        errors.append("Procedure source hash or scope mismatch")
    try:
        actions = answer["actions"] if answer else []
        rows = {row["sku"]: row for row in stock["rows"]}
        if len(actions) != len(rows) or {a["sku"] for a in actions} != set(rows):
            raise ValueError("Actions do not match sealed rows")
        for action in actions:
            if (
                set(action) != {"sku", "constraint_code", "citations"}
                or action["constraint_code"] != rows[action["sku"]]["constraint_code"]
            ):
                raise ValueError("Action contradicts sealed constraint")
            citations = action["citations"]
            row_label = f"evidence/{stock['evidence_id']}#{action['sku']}"
            if not isinstance(citations, list) or len(citations) != 2 or row_label not in citations:
                raise ValueError("Missing row citation")
            supporting = [labels[c] for c in citations if c in labels]
            if len(supporting) != 1 or QUOTE not in supporting[0].text:
                raise ValueError("Missing grounded procedure citation")
    except (KeyError, ValueError, TypeError):
        errors.append("Actions or citations do not resolve to governed evidence")
    packet = {
        "case": journal["case"],
        "analysis_status": "unverified" if errors else "validated_draft",
        "inventory": stock,
        "answer": answer,
        "errors": errors,
        "evidence": raw,
        "runbook": journal["runbook"],
        "as_of": journal["as_of"],
        "max_business_age_days": 2,
        "recovered": journal.get("recovered", False),
        "client_revision": REV,
    }
    save(work / "inventory.json", packet)
    text = f"# Inventory planning brief: {journal['case']}\n\nInventory: {stock['status']}. Analysis: {packet['analysis_status']}.\n"
    text += (
        f"Exact governed count: {stock['exact_count']}.\n"
        if stock["exact_count"] is not None
        else "Exact count unavailable; do not infer it from document hits.\n"
    )
    text += f"As of: {journal['as_of']}. Business freshness limit: 2 days.\nRunbook: {journal['runbook']}\n\n"
    if not errors and stock["status"] == "complete":
        text += QUOTE + "\n\n"
        for action in answer["actions"]:
            text += (
                f"- {action['sku']}: {action['constraint_code']}\n  "
                + ", ".join(action["citations"])
                + "\n"
            )
    else:
        text += "Planner review required; actionable advice withheld.\n"
    text += "\nRows are scoped to the governed reader. No purchase order is submitted.\n"
    if "manifest" in stock:
        text += "Sealed result: " + stock["manifest"]["logical_result_hash"] + "\n"
    write(work / "inventory.md", text.encode())
    output = io.StringIO(newline="")
    writer = csv.DictWriter(output, fieldnames=[*COLUMNS, "evidence_citation", "coverage"])
    writer.writeheader()
    for row in stock["rows"]:
        writer.writerow(
            {
                **row,
                "evidence_citation": f"evidence/{stock['evidence_id']}#{row['sku']}",
                "coverage": stock["status"],
            }
        )
    write(work / "inventory.csv", output.getvalue().encode())
    return packet


def process(inputs, credentials, work, case, recover=False, crash=False, variant="normal"):
    work = Path(work)
    work.mkdir(parents=True, exist_ok=True)
    with (work / ".lock").open("a+b") as lease:
        fcntl.flock(lease, fcntl.LOCK_EX | fcntl.LOCK_NB)
        manifest = verify(inputs)
        if case not in manifest["cases"] or variant not in (
            "normal",
            "limited",
            "stale",
            "empty",
            "expiry",
        ):
            raise ValueError("Unknown trusted selection")
        api, config = query_client(credentials)
        runbook = config["runbooks"][case] if variant == "normal" else config["variants"][variant]
        if variant != "normal" and case != "case-001":
            raise ValueError("Variant is scoped to first warehouse")
        as_of = (
            "2026-09-20"
            if variant == "stale"
            else "2026-09-10"
            if variant == "empty"
            else manifest["cases"][case]["as_of"]
        )
        identity = digest(
            encode(
                {
                    "runbook": runbook,
                    "uid": config["uid"],
                    "model": config["model"],
                    "provider": config["provider"],
                    "manifest": manifest,
                    "case": case,
                    "variant": variant,
                }
            )
        )
        path = work / "journal.json"
        with api:
            if path.exists():
                journal = read(path)
                if journal["input_hash"] != identity:
                    raise ValueError("Changed selection requires new work directory")
                if journal.get("result"):
                    return export(
                        api,
                        inputs,
                        work,
                        journal,
                        TurnResult.model_validate(journal["result"]),
                        config,
                    )
                if not recover or not journal.get("session_id"):
                    raise RuntimeError("Uncertain turn requires explicit recovery; no replay")
                transcript = api.sessions.get(journal["session_id"])
                matches = [
                    turn
                    for turn in transcript.turns
                    if turn.query == journal["query"] and turn.completion
                ]
                if (
                    transcript.uid != config["uid"]
                    or transcript.runbook_ref != runbook
                    or len(matches) != 1
                    or len(transcript.turns) != 1
                ):
                    raise RuntimeError("No uniquely recoverable completion")
                turn = matches[0]
                completion = dict(turn.completion)
                completion["was_override"] = completion["resolved"]["was_override"]
                result = TurnResult(
                    session_id=transcript.session_id,
                    ordinal=turn.ordinal,
                    collections_searched=turn.collections_searched,
                    hits=turn.hits or [],
                    envelopes=turn.envelope or [],
                    completion=completion,
                )
                journal["recovered"] = True
            else:
                query = "List the below-threshold inventory rows and their constraint codes. Cite every register row and the replenishment procedure. Do not submit an order."
                journal = {
                    "state": "creating_session",
                    "input_hash": identity,
                    "case": case,
                    "variant": variant,
                    "as_of": as_of,
                    "runbook": runbook,
                    "query": query,
                    "progress": [],
                }
                save(path, journal)
                session = api.sessions.create(runbook)
                journal.update(state="uncertain", session_id=session.session_id)
                save(path, journal)
                result = None
                try:
                    for event in api.sessions.turn_stream(
                        session.session_id, query=query, complete=True
                    ):
                        if isinstance(event, TurnResult):
                            result = event
                        else:
                            journal["progress"].append(event.model_dump(mode="json"))
                            save(path, journal)
                except Exception as error:
                    journal["error_category"] = type(error).__name__
                    save(path, journal)
                    raise
                if result is None:
                    raise RuntimeError("Stream ended without result")
                if crash:
                    os._exit(71)
            journal.update(state="response_saved", result=result.model_dump(mode="json"))
            save(path, journal)
            return export(api, inputs, work, journal, result, config)
