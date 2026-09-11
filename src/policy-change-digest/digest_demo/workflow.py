# SPDX-License-Identifier: Apache-2.0
"""One durable session per revision pair, with explicit transcript recovery."""

import csv
import difflib
import fcntl
import io
import json
import os
import re
from pathlib import Path

from munarium_client.models import TurnResult

from .fixtures import digest, encode, read, save, verify, write
from .server import query_client


def compare(inputs, case):
    manifest = verify(inputs)
    if case not in manifest["cases"]:
        raise ValueError("Unknown revision pair")
    folder = Path(inputs) / case
    before = (folder / "before.md").read_text()
    after = (folder / "after.md").read_text() if (folder / "after.md").exists() else None
    old_rule = re.search(r"^Rule: (.+)$", before, re.M).group(1)
    new_rule = re.search(r"^Rule: (.+)$", after, re.M).group(1) if after else None
    return {
        "case": case,
        **manifest["cases"][case],
        "status": "insufficient_evidence"
        if after is None
        else "unchanged"
        if old_rule == new_rule
        else "candidate_impacts",
        "old_rule": old_rule,
        "new_rule": new_rule,
        "source_hashes": {
            name: sha for name, sha in manifest["files"].items() if name.startswith(case + "/")
        },
        "diff": "".join(
            difflib.unified_diff(
                before.splitlines(True),
                (after or "").splitlines(True),
                fromfile=case + "/r1",
                tofile=case + "/r2",
            )
        ),
    }


def validate(result, comparison, config):
    errors = []
    try:
        completion = result.completion
        if not completion:
            raise ValueError("No completion")
        if completion.model != config["model"] or completion.provider != (
            "ollama" if config["provider"] == "fixture" else config["provider"]
        ):
            errors.append("Unexpected completion identity")
        if completion.verification and completion.verification.violations:
            errors.append("Unresolved verification violations")
        answer = json.loads(completion.text.strip().removeprefix("```json\n").removesuffix("\n```"))
        if (
            set(answer) != {"status", "explanation", "candidates"}
            or not isinstance(answer["explanation"], str)
            or not 1 <= len(answer["explanation"]) <= 3000
        ):
            raise ValueError("Unexpected digest schema")
        if answer["status"] != comparison["status"]:
            errors.append(
                "Answer disagrees with revision availability or deterministic rule comparison"
            )
        if not isinstance(answer["candidates"], list) or len(answer["candidates"]) > 10:
            raise ValueError("Invalid candidate list")
        if answer["status"] != "candidate_impacts" and answer["candidates"]:
            errors.append("Unsupported candidates without a known change")
        if answer["status"] == "candidate_impacts" and not answer["candidates"]:
            errors.append("No supported candidate identified")
        labels = {f"{h.collection}/{h.chunk_id}": h for h in result.hits}
        seen = set()
        for candidate in answer["candidates"]:
            if set(candidate) != {"checklist_id", "explanation", "citations"}:
                raise ValueError("Unexpected candidate schema")
            if (
                not isinstance(candidate["explanation"], str)
                or not 1 <= len(candidate["explanation"]) <= 2000
            ):
                raise ValueError("Invalid candidate explanation")
            ident = candidate["checklist_id"]
            if not isinstance(ident, str) or ident in seen:
                raise ValueError("Duplicate or invalid checklist identity")
            seen.add(ident)
            citations = candidate["citations"]
            if (
                not isinstance(citations, list)
                or not citations
                or not all(isinstance(c, str) and c in labels for c in citations)
            ):
                errors.append("Citations do not resolve to retrieved chunks")
                continue
            hits = [labels[c] for c in citations]
            filenames = {h.source_path.rsplit("/", 1)[-1] for h in hits}
            if not {"before.md", "after.md", "checklist.md"}.issubset(filenames):
                errors.append("Candidate lacks both revisions and the affected checklist")
            if not any(f"Checklist ID: {ident}\n" in h.text for h in hits):
                errors.append("Checklist ID is absent from its cited evidence")
        for hit in result.hits:
            name = comparison["case"] + "/" + hit.source_path.rsplit("/", 1)[-1]
            if (
                comparison["source_hashes"].get(name) != hit.source_content_hash
                or comparison["case"] not in hit.source_path
            ):
                errors.append("Source revision hash or scope mismatch")
        return answer, errors
    except (ValueError, TypeError, KeyError, AttributeError):
        return None, ["Completion is not a supported cited digest"]


def export(work, journal, result, config):
    comparison = journal["comparison"]
    answer, errors = validate(result, comparison, config)
    raw = result.model_dump(mode="json")
    if journal.get("recovered"):
        raw.pop("skipped", None)
    packet = {
        "comparison": comparison,
        "analysis_status": "unverified" if errors else "validated_draft",
        "answer": answer,
        "errors": errors,
        "evidence": raw,
        "exhaustive": False,
        "runbook": journal["runbook"],
        "revision_runbooks": config["revision_runbooks"][comparison["case"]],
        "input_hash": journal["input_hash"],
    }
    save(work / "digest.json", packet)
    text = f"# Candidate policy impacts: {comparison['case']}\n\nAnalysis: {packet['analysis_status']}. Coverage: retrieved candidates only; not exhaustive.\nRevision pair: {comparison['before']} → {comparison['after']}\n\n"
    text += (
        answer["explanation"] if answer and not errors else "Requires review: " + "; ".join(errors)
    ) + "\n\n"
    if answer and not errors:
        for candidate in answer["candidates"]:
            text += f"- Candidate {candidate['checklist_id']}: {candidate['explanation']}\n"
            text += "  Citations: " + ", ".join(candidate["citations"]) + "\n"
    text += (
        "\n## Exact textual diff\n\n```diff\n"
        + comparison["diff"]
        + "```\n\n## Source revisions\n\n"
    )
    text += "".join(
        f"- {name}: SHA-256 {sha}\n" for name, sha in comparison["source_hashes"].items()
    )
    write(work / "digest.md", text.encode())
    output = io.StringIO(newline="")
    writer = csv.writer(output)
    writer.writerow(
        ["case", "before", "after", "candidate_checklist", "analysis_status", "coverage"]
    )
    for candidate in answer["candidates"] if answer and not errors else []:
        writer.writerow(
            [
                comparison["case"],
                "r1",
                "r2",
                candidate["checklist_id"],
                packet["analysis_status"],
                "not exhaustive",
            ]
        )
    write(work / "digest.csv", output.getvalue().encode())
    return packet


def process(inputs, credentials, work, case, recover=False, crash=False):
    work = Path(work)
    work.mkdir(parents=True, exist_ok=True)
    with (work / ".lock").open("a+b") as lease:
        fcntl.flock(lease, fcntl.LOCK_EX | fcntl.LOCK_NB)
        comparison = compare(inputs, case)
        api, config = query_client(Path(credentials))
        with api:
            if (
                config["fixture_revision"]
                != digest((Path(inputs) / "manifest.json").read_bytes())[:12]
            ):
                raise ValueError("Credential fixture revision mismatch")
            runbook = config["runbooks"][case]
            fingerprint = digest(
                encode(
                    {
                        "comparison": comparison,
                        "runbook": runbook,
                        "model": config["model"],
                        "provider": config["provider"],
                        "uid": config["uid"],
                    }
                )
            )
            path = work / "journal.json"
            if path.exists():
                journal = read(path)
                if journal["input_hash"] != fingerprint:
                    raise ValueError(
                        "Changed revision pair or configuration requires a new work directory"
                    )
                if journal.get("result"):
                    return export(
                        work, journal, TurnResult.model_validate(journal["result"]), config
                    )
                if not recover or not journal.get("session_id"):
                    raise RuntimeError(
                        "Uncertain turn requires explicit transcript inspection; no replay"
                    )
                transcript = api.sessions.get(journal["session_id"])
                if transcript.uid != config["uid"] or transcript.runbook_ref != runbook:
                    raise ValueError("Transcript identity mismatch")
                turns = [
                    t for t in transcript.turns if t.query == journal["query"] and t.completion
                ]
                if len(turns) != 1:
                    raise RuntimeError("No uniquely recoverable completion; turn remains uncertain")
                turn = turns[0]
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
                query = (
                    "Compare the policy revisions and identify candidate downstream checklist impacts. REVISION_COMPARISON="
                    + json.dumps(comparison, ensure_ascii=False, sort_keys=True)
                )
                journal = {
                    "state": "creating_session",
                    "input_hash": fingerprint,
                    "comparison": comparison,
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
                    raise RuntimeError("Stream ended without a completion")
                if crash:
                    os._exit(71)
            journal.update(state="response_saved", result=result.model_dump(mode="json"))
            save(path, journal)
            packet = export(work, journal, result, config)
            journal["state"] = packet["analysis_status"]
            save(path, journal)
            return packet
